using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using LcdStudio.Native;

namespace LcdStudio.Devices;

public sealed class LcdLink : IDisposable
{
	public const int FanPacket = 102400;

	public const int ScreenMaxJpeg = 512000;

	private readonly PacketBuilder _packets;

	private WinUsb _usb;

	private bool _initialized;

	private int _brightness = -1;

	private int _failures;

	public UsbDeviceInfo Info { get; }

	public bool IsScreen => Info.Kind == DeviceKind.Screen;

	public string? Firmware { get; private set; }

	public int BufferLevel { get; private set; }

	public string LastAck { get; private set; } = "";


	public LcdLink(UsbDeviceInfo info)
	{
		Info = info;
		_packets = new PacketBuilder(info.Kind == DeviceKind.FanLcd);
		_usb = new WinUsb(info);
	}

	private byte[] Command(byte cmd, int timeout, params byte[] p)
	{
		_usb.Write(_packets.Build(cmd, p));
		byte[] result = _usb.Read(timeout);
		_usb.Flush();
		return result;
	}

	private void Initialize()
	{
		_usb.Flush();
		if (IsScreen)
		{
			Command(123, 2000);
			Thread.Sleep(150);
			byte[] r = Command(10, 2000);
			Firmware = ParseVersion(r);
			Command(15, 2000, 120);
			Command(17, 2000);
			DateTime now = DateTime.Now;
			Command(51, 2000, (byte)(now.Year >> 8), (byte)now.Year, (byte)now.Month, (byte)now.Day, (byte)now.Hour, (byte)now.Minute, (byte)now.Second, 2);
			Command(52, 2000, default(byte));
			using (Bitmap bmp = new Bitmap(480, 1920, PixelFormat.Format32bppArgb))
			{
				byte[] data = Imaging.Png(bmp);
				SendPayload(102, data, 2000);
			}
			using Bitmap bitmap = new Bitmap(480, 1920);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.Black);
			}
			SendPayload(101, Imaging.Jpeg(bitmap, 80), 2000);
		}
		else
		{
			Command(15, 300, 120);
			byte[] r2 = Command(10, 300);
			Firmware = ParseVersion(r2);
		}
		_initialized = true;
		_brightness = -1;
	}

	private static string? ParseVersion(byte[] r)
	{
		if (r.Length <= 8)
		{
			return null;
		}
		int num = Array.IndexOf(r, (byte)0, 8);
		if (num < 0)
		{
			num = Math.Min(r.Length, 40);
		}
		string text = Encoding.ASCII.GetString(r, 8, Math.Max(0, num - 8)).Trim();
		if (text.Length <= 0 || !text.All((char c) => c >= ' ' && c < '\u007f'))
		{
			return null;
		}
		return text;
	}

	private byte[] SendPayload(byte cmd, byte[] data, int ackTimeout)
	{
		byte[] array = _packets.Build(cmd, PacketBuilder.BigEndian(data.Length));
		byte[] array2 = new byte[IsScreen ? (512 + data.Length) : 102400];
		array.CopyTo(array2, 0);
		Array.Copy(data, 0, array2, 512, data.Length);
		_usb.Write(array2);
		byte[] result = _usb.Read(ackTimeout);
		_usb.Flush();
		return result;
	}

	public void SetBrightness(int percent)
	{
		percent = Math.Clamp(percent, 0, 100);
		if (_initialized && percent != _brightness)
		{
			Command(14, IsScreen ? 1000 : 300, (byte)(IsScreen ? percent : FanBrightness(percent)));
			_brightness = percent;
		}
	}

	public void SendFrame(byte[] jpeg, int brightness)
	{
		int num = (IsScreen ? 512000 : 101888);
		if (jpeg.Length > num)
		{
			throw new InvalidOperationException($"Frame is {jpeg.Length / 1024} KB; limit {num / 1024} KB. Lower picture quality.");
		}
		try
		{
			if (!_initialized)
			{
				Initialize();
			}
			SetBrightness(brightness);
			byte[] array = SendPayload(101, jpeg, IsScreen ? 1500 : 250);
			LastAck = ((array.Length != 0) ? BitConverter.ToString(array, 0, Math.Min(12, array.Length)) : "none");
			if (IsScreen)
			{
				BufferLevel = ((array.Length > 8) ? array[8] : 0);
				if (BufferLevel > 3)
				{
					WaitBuffer(2);
				}
			}
			_failures = 0;
		}
		catch (Win32Exception) when (_failures < 3)
		{
			_failures++;
			Reopen();
			throw;
		}
	}

	private void WaitBuffer(int threshold)
	{
		DateTime dateTime = DateTime.UtcNow.AddSeconds(3.0);
		while (DateTime.UtcNow < dateTime)
		{
			Thread.Sleep(50);
			byte[] array = Command(122, 500);
			if (array.Length <= 8)
			{
				break;
			}
			BufferLevel = array[8];
			if (BufferLevel <= threshold)
			{
				break;
			}
		}
	}

	private void Reopen()
	{
		try
		{
			_usb.Dispose();
		}
		catch
		{
		}
		Thread.Sleep(250);
		_usb = new WinUsb(Info);
		_initialized = false;
	}

	/// <summary>
	/// Sets the screen's own clock to now and turns its built-in clock display on or off (firmware commands 0x33 and
	/// 0x34). style is the trailing byte of 0x33, which L-Connect sends as 2. Returns the two replies as hex, for testing.
	/// </summary>
	public string Clock(bool on, byte style = 2)
	{
		if (!_initialized)
		{
			Initialize();
		}
		DateTime now = DateTime.Now;
		byte[] set = Command(51, 2000, (byte)(now.Year >> 8), (byte)now.Year, (byte)now.Month, (byte)now.Day, (byte)now.Hour, (byte)now.Minute, (byte)now.Second, style);
		byte[] run = Command(52, 2000, (byte)(on ? 1 : 0));
		string Hex(byte[] r) => r.Length == 0 ? "none" : BitConverter.ToString(r, 0, Math.Min(16, r.Length));
		return Hex(set) + " | " + Hex(run);
	}

	public void Release()
	{
		try
		{
			if (IsScreen && _initialized)
			{
				Command(123, 500);
			}
		}
		catch
		{
		}
	}

	public static int FanBrightness(int value)
	{
		int[] array = new int[5] { 0, 25, 50, 75, 100 };
		int[] array2 = new int[5] { 0, 10, 30, 40, 100 };
		value = Math.Clamp(value, 0, 100);
		for (int i = 1; i < 5; i++)
		{
			if (value <= array[i])
			{
				return array2[i - 1] + ((value - array[i - 1]) * (array2[i] - array2[i - 1]) + 12) / 25;
			}
		}
		return 100;
	}

	public void Dispose()
	{
		Release();
		_usb.Dispose();
	}

	/// <summary>Lets go of the device without telling it to stop, so the last frame (or its own clock) stays up.</summary>
	public void Leave()
	{
		_usb.Dispose();
	}
}
