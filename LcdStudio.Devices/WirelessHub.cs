using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LcdStudio.Native;

namespace LcdStudio.Devices;

public sealed class WirelessHub : IDisposable
{
	private const int RfSize = 240;

	private readonly WinUsb _tx;

	private readonly WinUsb _rx;

	private readonly object _io = new object();

	private readonly Dictionary<string, int> _misses = new Dictionary<string, int>();

	public byte[] Master { get; private set; } = new byte[6];


	public byte Channel { get; private set; }

	public List<FanGroup> Groups { get; private set; } = new List<FanGroup>();


	public DateTime LastRefresh { get; private set; }

	public WirelessHub(UsbDeviceInfo tx, UsbDeviceInfo rx)
	{
		_tx = new WinUsb(tx);
		try
		{
			_rx = new WinUsb(rx);
			FindMaster();
			Refresh();
			int i = 0;
			int count = Groups.Count;
			for (; i < 3; i++)
			{
				Thread.Sleep(150);
				Refresh();
				if (Groups.Count != count)
				{
					count = Groups.Count;
					continue;
				}
				break;
			}
		}
		catch
		{
			_tx.Dispose();
			_rx?.Dispose();
			throw;
		}
	}

	private void FindMaster()
	{
		byte[] array = new byte[39]
		{
			8, 2, 4, 6, 10, 12, 14, 16, 18, 20,
			22, 24, 26, 28, 30, 32, 34, 36, 38, 1,
			3, 5, 7, 9, 11, 13, 15, 17, 19, 21,
			23, 25, 27, 29, 31, 33, 35, 37, 39
		};
		foreach (byte b in array)
		{
			byte[] array2 = new byte[64];
			array2[0] = 17;
			array2[1] = b;
			_tx.Write(array2);
			byte[] array3 = _tx.Read(100);
			if (array3.Length >= 13 && array3[0] == 17 && array3.Skip(1).Take(6).Any((byte x) => x != 0) && array3.Skip(7).Take(4).Any((byte x) => x != 0))
			{
				Master = array3.Skip(1).Take(6).ToArray();
				Channel = b;
				return;
			}
		}
		throw new IOException("The wireless transmitter did not answer. Close L-Connect and try again.");
	}

	public void Refresh()
	{
		lock (_io)
		{
			byte[] array = Array.Empty<byte>();
			for (int i = 0; i < 3; i++)
			{
				_rx.Flush();
				byte[] array2 = new byte[64];
				array2[0] = 16;
				array2[1] = 3;
				_rx.Write(array2);
				array = _rx.ReadMany(2048, 500);
				if (array.Length >= 4 && array[0] == 16)
				{
					break;
				}
				Thread.Sleep(60);
			}
			if (array.Length < 4 || array[0] != 16)
			{
				throw new IOException("No wireless device list.");
			}
			int num = Math.Min(30, (int)array[1]);
			List<FanGroup> list = new List<FanGroup>();
			byte b2 = 1;
			for (int j = 0; j < num && 4 + j * 42 + 42 <= array.Length; j++)
			{
				int num2 = 4 + j * 42;
				if (array[num2 + 41] == 28 && array[num2 + 18] != byte.MaxValue && array.Skip(num2 + 6).Take(6).SequenceEqual(Master))
				{
					FanGroup fanGroup = new FanGroup
					{
						Mac = array.Skip(num2).Take(6).ToArray(),
						Channel = array[num2 + 12],
						RxType = array[num2 + 13],
						DeviceType = array[num2 + 18],
						FanCount = (byte)Math.Min(4, (array[num2 + 19] >= 10) ? (array[num2 + 19] - 10) : array[num2 + 19]),
						EffectIndex = array.Skip(num2 + 20).Take(4).ToArray(),
						FanTypeByte = array.Skip(num2 + 24).Take(4).FirstOrDefault((byte b) => b != 0),
						Pwm = array.Skip(num2 + 36).Take(4).ToArray(),
						Slot = b2++,
						Record = array.Skip(num2).Take(42).ToArray()
					};
					for (int k = 0; k < 4; k++)
					{
						fanGroup.Rpm[k] = ((array[num2 + 28 + k * 2] & 0xF) << 8) | array[num2 + 29 + k * 2];
					}
					if (fanGroup.FanCount != 0)
					{
						list.Add(fanGroup);
					}
				}
			}
			foreach (FanGroup old in Groups)
			{
				if (list.Any((FanGroup g) => g.Id == old.Id))
				{
					_misses.Remove(old.Id);
					continue;
				}
				int num3 = _misses.GetValueOrDefault(old.Id) + 1;
				_misses[old.Id] = num3;
				if (num3 <= 3)
				{
					list.Add(old);
				}
			}
			Groups = list.OrderBy((FanGroup g) => g.Id).ToList();
			LastRefresh = DateTime.Now;
		}
	}

	private byte TrafficChannel(FanGroup g)
	{
		byte channel = g.Channel;
		if (channel < 1 || channel > 39)
		{
			return Channel;
		}
		return g.Channel;
	}

	private void SendRf(FanGroup g, byte[] rf)
	{
		for (int i = 0; i < 4; i++)
		{
			byte[] array = new byte[64];
			array[0] = 16;
			array[1] = (byte)i;
			array[2] = TrafficChannel(g);
			array[3] = g.RxType;
			Array.Copy(rf, i * 60, array, 4, 60);
			_tx.Write(array);
			Pause(1.0);
		}
	}

	public void SetSpeed(FanGroup g, int percent)
	{
		SendPwm(g, PwmFrame(g, Master, TrafficChannel(g), percent));
	}

	public void SetRawPwm(FanGroup g, byte[] pwm)
	{
		byte[] array = PwmFrame(g, Master, TrafficChannel(g), 0);
		for (int i = 0; i < 4; i++)
		{
			array[17 + i] = (byte)((i < g.FanCount) ? pwm[i] : 0);
		}
		SendPwm(g, array);
	}

	private void SendPwm(FanGroup g, byte[] rf)
	{
		lock (_io)
		{
			SendRf(g, rf);
			Thread.Sleep(5);
		}
	}

	internal static byte[] PwmFrame(FanGroup g, byte[] master, byte channel, int percent)
	{
		byte b = (byte)Math.Round((double)(Math.Clamp(percent, 0, 100) * 255) / 100.0);
		if (b > 0 && b < 35)
		{
			b = 35;
		}
		byte[] array = new byte[240];
		array[0] = 18;
		array[1] = 16;
		g.Mac.CopyTo(array, 2);
		master.CopyTo(array, 8);
		array[14] = g.RxType;
		array[15] = channel;
		array[16] = g.Slot;
		for (int i = 0; i < 4; i++)
		{
			array[17 + i] = (byte)((i < g.FanCount) ? b : 0);
		}
		return array;
	}

	public byte[] UploadLighting(FanGroup g, IReadOnlyList<(byte R, byte G, byte B)[]> frames, int frameTicks)
	{
		if (!g.RgbSupported)
		{
			throw new InvalidOperationException("This fan type's LED layout is unknown; no RGB data sent.");
		}
		List<byte> list = new List<byte>(frames.Count * g.Leds * 3);
		foreach ((byte, byte, byte)[] frame in frames)
		{
			for (int i = 0; i < g.Leds; i++)
			{
				(byte, byte, byte) tuple = ((i < frame.Length) ? frame[i] : default((byte, byte, byte)));
				list.Add(tuple.Item1);
				list.Add(tuple.Item2);
				list.Add(tuple.Item3);
			}
		}
		byte[] array = TinyUz.Encode(list.ToArray());
		if (list.Count > 8192 || array.Length > 12288)
		{
			throw new InvalidOperationException("Lighting loop is too long for the fan memory.");
		}
		int num = Math.Clamp(frameTicks, 16, 8000);
		uint num2 = 2166136261u;
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			num2 = (num2 ^ b) * 16777619;
		}
		num2 = (num2 ^ (uint)num) * 16777619;
		byte[] array3 = PacketBuilder.BigEndian((int)num2);
		array3[0] |= 128;
		int num3 = (array.Length + 219) / 220;
		lock (_io)
		{
			for (int k = 0; k <= num3; k++)
			{
				byte[] array4 = new byte[240];
				array4[0] = 18;
				array4[1] = 32;
				g.Mac.CopyTo(array4, 2);
				Master.CopyTo(array4, 8);
				array3.CopyTo(array4, 14);
				array4[18] = (byte)k;
				array4[19] = (byte)(num3 + 1);
				if (k == 0)
				{
					PacketBuilder.BigEndian(array.Length).CopyTo(array4, 20);
					array4[25] = (byte)(frames.Count >> 8);
					array4[26] = (byte)frames.Count;
					array4[27] = (byte)g.Leds;
					array4[32] = (byte)(num >> 8);
					array4[33] = (byte)num;
					Thread.Sleep(10);
					for (int l = 0; l < 4; l++)
					{
						if (l > 0)
						{
							Pause(20.0);
						}
						SendRf(g, array4);
					}
				}
				else
				{
					int num4 = (k - 1) * 220;
					Array.Copy(array, num4, array4, 20, Math.Min(220, array.Length - num4));
					SendRf(g, array4);
				}
			}
			return array3;
		}
	}

	public bool Applied(FanGroup g, byte[] index)
	{
		FanGroup g2 = g;
		return Groups.FirstOrDefault((FanGroup x) => x.Id == g2.Id)?.EffectIndex.SequenceEqual(index) ?? false;
	}

	private static void Pause(double ms)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (ms > 3.0)
		{
			Thread.Sleep((int)ms - 2);
		}
		while (stopwatch.Elapsed.TotalMilliseconds < ms)
		{
			Thread.SpinWait(50);
		}
	}

	public void Dispose()
	{
		_tx.Dispose();
		_rx.Dispose();
	}
}
