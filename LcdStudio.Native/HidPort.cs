using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LcdStudio.Native;

/// <summary>
/// A HID device opened for whole output and input reports, with timed reads: the wired TL LCD fans speak HID rather
/// than WinUSB.
/// </summary>
public sealed class HidPort : IDisposable
{
	[StructLayout(LayoutKind.Sequential)]
	private struct SpInterface
	{
		public int Size;

		public Guid Guid;

		public int Flags;

		public nint Reserved;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct HidCaps
	{
		public ushort Usage;

		public ushort UsagePage;

		public ushort InputReportByteLength;

		public ushort OutputReportByteLength;

		public ushort FeatureReportByteLength;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
		public ushort[] Reserved;

		public ushort NumberLinkCollectionNodes;

		public ushort NumberInputButtonCaps;

		public ushort NumberInputValueCaps;

		public ushort NumberInputDataIndices;

		public ushort NumberOutputButtonCaps;

		public ushort NumberOutputValueCaps;

		public ushort NumberOutputDataIndices;

		public ushort NumberFeatureButtonCaps;

		public ushort NumberFeatureValueCaps;

		public ushort NumberFeatureDataIndices;
	}

	[DllImport("hid.dll")]
	private static extern void HidD_GetHidGuid(out Guid g);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out nint data);

	[DllImport("hid.dll")]
	private static extern bool HidD_FreePreparsedData(nint data);

	[DllImport("hid.dll")]
	private static extern int HidP_GetCaps(nint data, out HidCaps caps);

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

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern unsafe bool WriteFile(SafeFileHandle h, byte* buf, int n, out int written, NativeOverlapped* o);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern unsafe bool ReadFile(SafeFileHandle h, byte* buf, int n, out int read, NativeOverlapped* o);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern unsafe bool GetOverlappedResult(SafeFileHandle h, NativeOverlapped* o, out int n, bool wait);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern unsafe bool CancelIoEx(SafeFileHandle h, NativeOverlapped* o);

	private const uint GenericRead = 0x80000000u, GenericWrite = 0x40000000u, ShareRw = 3u, OpenExisting = 3u, Overlapped = 0x40000000u;

	private const int ErrorIoPending = 997;

	private readonly SafeFileHandle _file;

	private readonly ManualResetEvent _done = new ManualResetEvent(false);

	public int OutputLength { get; }

	public int InputLength { get; }

	/// <summary>HID devices of this vendor and product whose output reports are at least the given length (the one collection that takes the data).</summary>
	public static List<string> Find(string vidPid, int outputLength)
	{
		var found = new List<string>();
		HidD_GetHidGuid(out Guid g);
		nint set = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 0x12u);
		if (set == new IntPtr(-1))
		{
			return found;
		}
		try
		{
			for (uint i = 0; ; i++)
			{
				var f = new SpInterface { Size = Marshal.SizeOf<SpInterface>() };
				if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref f))
				{
					break;
				}
				SetupDiGetDeviceInterfaceDetail(set, ref f, IntPtr.Zero, 0u, out uint needed, IntPtr.Zero);
				nint detail = Marshal.AllocHGlobal((int)needed);
				try
				{
					Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
					if (!SetupDiGetDeviceInterfaceDetail(set, ref f, detail, needed, out _, IntPtr.Zero))
					{
						continue;
					}
					string path = Marshal.PtrToStringUni(detail + 4) ?? "";
					if (!path.ToLowerInvariant().Contains(vidPid))
					{
						continue;
					}
					// opened with no access, only to read its report sizes (works even while another program has it)
					using SafeFileHandle probe = CreateFile(path, 0u, ShareRw, IntPtr.Zero, OpenExisting, 0u, IntPtr.Zero);
					if (!probe.IsInvalid && Caps(probe) is { } caps && caps.OutputReportByteLength >= outputLength)
					{
						found.Add(path);
					}
				}
				finally
				{
					Marshal.FreeHGlobal(detail);
				}
			}
		}
		finally
		{
			SetupDiDestroyDeviceInfoList(set);
		}
		return found;
	}

	private static HidCaps? Caps(SafeFileHandle h)
	{
		if (!HidD_GetPreparsedData(h, out nint data))
		{
			return null;
		}
		try
		{
			return HidP_GetCaps(data, out HidCaps caps) == 0x110000 ? caps : null;
		}
		finally
		{
			HidD_FreePreparsedData(data);
		}
	}

	public HidPort(string path)
	{
		_file = CreateFile(path, GenericRead | GenericWrite, ShareRw, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
		if (_file.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "open " + path);
		}
		HidCaps caps = Caps(_file) ?? throw new System.IO.IOException("could not read the device's report sizes");
		OutputLength = caps.OutputReportByteLength;
		InputLength = caps.InputReportByteLength;
	}


	/// <summary>Sends one output report (its first byte the report ID), padded to the device's report length.</summary>
	public unsafe void Write(byte[] report, int timeoutMs = 1000)
	{
		byte[] buf = report.Length == OutputLength ? report : Pad(report, OutputLength);
		fixed (byte* p = buf)
		{
			Transfer(p, buf.Length, write: true, timeoutMs);
		}
	}

	/// <summary>Reads one input report, or returns an empty array when none arrives in time.</summary>
	public unsafe byte[] Read(int timeoutMs)
	{
		byte[] buf = new byte[Math.Max(1, InputLength)];
		int n;
		fixed (byte* p = buf)
		{
			n = Transfer(p, buf.Length, write: false, timeoutMs);
		}
		return n <= 0 ? Array.Empty<byte>() : buf[..n];
	}

	/// <summary>Drops any reports already waiting.</summary>
	public void Flush()
	{
		for (int i = 0; i < 16 && Read(1).Length > 0; i++)
		{
		}
	}

	private static byte[] Pad(byte[] b, int n)
	{
		byte[] r = new byte[n];
		Array.Copy(b, r, Math.Min(b.Length, n));
		return r;
	}

	private unsafe int Transfer(byte* p, int n, bool write, int timeoutMs)
	{
		_done.Reset();
		var o = new NativeOverlapped { EventHandle = _done.SafeWaitHandle.DangerousGetHandle() };
		bool ok = write ? WriteFile(_file, p, n, out int moved, &o) : ReadFile(_file, p, n, out moved, &o);
		if (ok)
		{
			return moved;
		}
		int err = Marshal.GetLastWin32Error();
		if (err != ErrorIoPending)
		{
			throw new Win32Exception(err, write ? "HID write" : "HID read");
		}
		if (!_done.WaitOne(timeoutMs))
		{
			CancelIoEx(_file, &o);
			GetOverlappedResult(_file, &o, out _, wait: true);
			if (write)
			{
				throw new TimeoutException("HID write timed out");
			}
			return 0;
		}
		if (!GetOverlappedResult(_file, &o, out moved, wait: false))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), write ? "HID write" : "HID read");
		}
		return moved;
	}

	public void Dispose()
	{
		_file.Dispose();
		_done.Dispose();
	}
}
