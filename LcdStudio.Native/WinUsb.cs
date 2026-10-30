using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LcdStudio.Native;

public sealed class WinUsb : IDisposable
{
	private struct SpInterface
	{
		public int Size;

		public Guid Guid;

		public int Flags;

		public nint Reserved;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct InterfaceDescriptor
	{
		public byte Length;

		public byte Type;

		public byte Number;

		public byte Alternate;

		public byte Endpoints;

		public byte Class;

		public byte SubClass;

		public byte Protocol;

		public byte Str;
	}

	private struct PipeInfo
	{
		public int Type;

		public byte Id;

		public ushort MaxPacket;

		public byte Interval;
	}

	private static readonly (string Id, DeviceKind Kind)[] Known = new(string, DeviceKind)[5]
	{
		("vid_1cbe&pid_a088", DeviceKind.Screen),
		("vid_0416&pid_8050", DeviceKind.ScreenLeds),
		("vid_1cbe&pid_0006", DeviceKind.FanLcd),
		("vid_0416&pid_8040", DeviceKind.WirelessTx),
		("vid_0416&pid_8041", DeviceKind.WirelessRx)
	};

	private const uint PipeTransferTimeout = 3u;

	private const int ErrorSemTimeout = 121;

	private SafeFileHandle? _file;

	private nint _handle;

	private byte _in;

	private byte _out;

	private int _inMax = 512;

	private uint _readTimeout = uint.MaxValue;

	public UsbDeviceInfo Info { get; }

	public string Pipes => $"in=0x{_in:X2}/{_inMax} out=0x{_out:X2}";

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SetupDiGetClassDevs(ref Guid g, string? e, nint w, uint f);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(nint d, nint i, ref Guid g, uint n, ref SpInterface f);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetail(nint d, ref SpInterface f, nint p, uint s, out uint needed, nint dev);

	[DllImport("setupapi.dll")]
	private static extern bool SetupDiDestroyDeviceInfoList(nint d);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string p, uint a, uint s, nint sec, uint c, uint f, nint t);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_Initialize(SafeFileHandle h, out nint i);

	[DllImport("winusb.dll")]
	private static extern bool WinUsb_Free(nint i);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_ResetPipe(nint h, byte pipe);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_AbortPipe(nint h, byte pipe);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_QueryInterfaceSettings(nint h, byte a, out InterfaceDescriptor d);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_QueryPipe(nint h, byte a, byte n, out PipeInfo p);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_WritePipe(nint h, byte pipe, byte[] data, uint len, out uint done, nint ov);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_ReadPipe(nint h, byte pipe, byte[] data, uint len, out uint done, nint ov);

	[DllImport("winusb.dll", SetLastError = true)]
	private static extern bool WinUsb_SetPipePolicy(nint h, byte pipe, uint policy, uint len, ref uint value);

	public static List<UsbDeviceInfo> Enumerate()
	{
		List<UsbDeviceInfo> list = new List<UsbDeviceInfo>();
		HashSet<Guid> hashSet = new HashSet<Guid>();
		using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\USB"))
		{
			if (registryKey == null)
			{
				return list;
			}
			string[] subKeyNames = registryKey.GetSubKeyNames();
			foreach (string id in subKeyNames)
			{
				if (!Known.Any(((string Id, DeviceKind Kind) k) => k.Id == id.ToLowerInvariant()))
				{
					continue;
				}
				using RegistryKey registryKey2 = registryKey.OpenSubKey(id);
				if (registryKey2 == null)
				{
					continue;
				}
				string[] subKeyNames2 = registryKey2.GetSubKeyNames();
				foreach (string text in subKeyNames2)
				{
					using RegistryKey registryKey3 = registryKey2.OpenSubKey(text + "\\Device Parameters");
					object obj = registryKey3?.GetValue("DeviceInterfaceGUIDs") ?? registryKey3?.GetValue("DeviceInterfaceGUID");
					string[] array = (obj as string[]) ?? ((!(obj is string text2)) ? null : new string[1] { text2 });
					if (array == null)
					{
						continue;
					}
					string[] array2 = array;
					for (int l = 0; l < array2.Length; l++)
					{
						if (Guid.TryParse(array2[l], out var result))
						{
							hashSet.Add(result);
						}
					}
				}
			}
		}
		foreach (Guid item in hashSet)
		{
			Guid g = item;
			nint num = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 18u);
			if (num == new IntPtr(-1))
			{
				continue;
			}
			try
			{
				uint num2 = 0u;
				while (true)
				{
					SpInterface spInterface = default(SpInterface);
					spInterface.Size = Marshal.SizeOf<SpInterface>();
					SpInterface f = spInterface;
					if (!SetupDiEnumDeviceInterfaces(num, IntPtr.Zero, ref g, num2, ref f))
					{
						break;
					}
					SetupDiGetDeviceInterfaceDetail(num, ref f, IntPtr.Zero, 0u, out var needed, IntPtr.Zero);
					nint num3 = Marshal.AllocHGlobal((int)needed);
					try
					{
						Marshal.WriteInt32(num3, (IntPtr.Size == 8) ? 8 : 6);
						if (SetupDiGetDeviceInterfaceDetail(num, ref f, num3, needed, out var _, IntPtr.Zero))
						{
							string path = Marshal.PtrToStringUni(num3 + 4);
							string low = path.ToLowerInvariant();
							(string, DeviceKind) tuple = Known.FirstOrDefault(((string Id, DeviceKind Kind) k) => low.Contains(k.Id));
							if (tuple.Item1 != null && !list.Any((UsbDeviceInfo x) => x.Path == path))
							{
								list.Add(new UsbDeviceInfo(tuple.Item2, path, SerialOf(path), ""));
							}
						}
					}
					finally
					{
						Marshal.FreeHGlobal(num3);
					}
					num2++;
				}
			}
			finally
			{
				SetupDiDestroyDeviceInfoList(num);
			}
		}
		List<UsbDeviceInfo> source = list.OrderBy((UsbDeviceInfo d) => d.Kind).ThenBy<UsbDeviceInfo, string>((UsbDeviceInfo d) => d.Serial, StringComparer.OrdinalIgnoreCase).ToList();
		int fan = 0;
		// fans are named by serial order, so the same fan keeps the same name between runs
		return source.Select((UsbDeviceInfo d) => d with
		{
			Name = d.Kind switch
			{
				DeviceKind.Screen => "Universal Screen 8.8\"",
				DeviceKind.ScreenLeds => "Screen light strip",
				DeviceKind.FanLcd => $"Fan {++fan}",
				DeviceKind.WirelessTx => "Wireless transmitter",
				_ => "Wireless receiver",
			}
		}).ToList();
	}

	public static string InstanceId(string path)
	{
		string[] array = path.Replace("\\\\?\\", "").Split('#');
		if (array.Length < 3)
		{
			return path;
		}
		return $"{array[0]}\\{array[1]}\\{array[2]}".ToUpperInvariant();
	}

	private static string SerialOf(string path)
	{
		string[] array = path.Split('#');
		if (array.Length < 3)
		{
			return path;
		}
		return array[2];
	}

	public WinUsb(UsbDeviceInfo info, bool resetPipes = true)
	{
		Info = info;
		try
		{
			_file = CreateFile(info.Path, 3221225472u, 3u, IntPtr.Zero, 3u, 1073741952u, IntPtr.Zero);
			if (_file.IsInvalid)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), info.Name + " is in use by another program (close L-Connect).");
			}
			if (!WinUsb_Initialize(_file, out _handle))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUSB could not open " + info.Name + ".");
			}
			if (!WinUsb_QueryInterfaceSettings(_handle, 0, out var d))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			for (byte b = 0; b < d.Endpoints; b++)
			{
				if (WinUsb_QueryPipe(_handle, 0, b, out var p) && p.Type == 2)
				{
					if ((p.Id & 0x80u) != 0)
					{
						_in = p.Id;
						_inMax = Math.Max(8, (int)p.MaxPacket);
					}
					else
					{
						_out = p.Id;
					}
				}
			}
			if (_in == 0 || _out == 0)
			{
				throw new IOException(info.Name + ": bulk endpoints not found.");
			}
			uint value = 2500u;
			WinUsb_SetPipePolicy(_handle, _out, 3u, 4u, ref value);
			if (resetPipes)
			{
				ClearStall();
			}
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public void Write(byte[] data, int length = -1)
	{
		uint num = (uint)((length < 0) ? data.Length : length);
		if (!WinUsb_WritePipe(_handle, _out, data, num, out var done, IntPtr.Zero))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			throw new Win32Exception(lastWin32Error, $"{Info.Name}: USB write failed ({lastWin32Error}, {done}/{num} bytes)");
		}
		if (done != num)
		{
			throw new IOException($"{Info.Name}: short USB write {done}/{num}");
		}
	}

	public byte[] Read(int timeoutMs)
	{
		byte[] array = new byte[_inMax];
		long num = Environment.TickCount64 + timeoutMs;
		uint done;
		do
		{
			long num2 = num - Environment.TickCount64;
			if (num2 <= 0)
			{
				return Array.Empty<byte>();
			}
			SetReadTimeout((uint)Math.Max(1L, num2));
			if (!WinUsb_ReadPipe(_handle, _in, array, (uint)array.Length, out done, IntPtr.Zero))
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				if (lastWin32Error == 121)
				{
					return Array.Empty<byte>();
				}
				throw new Win32Exception(lastWin32Error, $"{Info.Name}: USB read failed ({lastWin32Error})");
			}
		}
		while (done == 0);
		Array.Resize(ref array, (int)done);
		return array;
	}

	public byte[] ReadMany(int max, int firstTimeoutMs, int gapMs = 20)
	{
		List<byte> list = new List<byte>();
		while (list.Count < max)
		{
			byte[] array = Read((list.Count == 0) ? firstTimeoutMs : gapMs);
			if (array.Length == 0)
			{
				break;
			}
			list.AddRange(array);
		}
		return list.ToArray();
	}

	public void Flush()
	{
		for (int i = 0; i < 16; i++)
		{
			try
			{
				if (Read(5).Length == 0)
				{
					break;
				}
			}
			catch
			{
				break;
			}
		}
	}

	private void SetReadTimeout(uint ms)
	{
		if (ms != _readTimeout)
		{
			WinUsb_SetPipePolicy(_handle, _in, 3u, 4u, ref ms);
			_readTimeout = ms;
		}
	}

	public void ClearStall()
	{
		WinUsb_AbortPipe(_handle, _out);
		WinUsb_ResetPipe(_handle, _out);
		WinUsb_ResetPipe(_handle, _in);
	}

	public void Dispose()
	{
		if (_handle != IntPtr.Zero)
		{
			WinUsb_Free(_handle);
			_handle = IntPtr.Zero;
		}
		_file?.Dispose();
		_file = null;
	}
}
