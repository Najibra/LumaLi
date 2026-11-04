using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LcdStudio.Native;

/// <summary>
/// Which programs hold a USB device open. WinUSB lets only one program open a device, so when Windows answers "access
/// denied" this looks through every process's open handles for the device's own object name (\Device\USBPDO-n).
/// Runs in the background, at most once a minute per device; a lookup takes a second or two.
/// </summary>
public static class DeviceHolders
{
	private sealed class Entry
	{
		public string Text = "";

		public DateTime At = DateTime.MinValue;

		public bool Busy;
	}

	private static readonly ConcurrentDictionary<string, Entry> _cache = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

	/// <summary>The programs last found holding this device ("SignalRgb, OpenRGB"), or "" (none found yet); starts a new
	/// lookup in the background when the last one is over a minute old.</summary>
	public static string Describe(string devicePath)
	{
		Entry e = _cache.GetOrAdd(devicePath, (string _) => new Entry());
		lock (e)
		{
			if (!e.Busy && (DateTime.UtcNow - e.At).TotalSeconds > 60.0)
			{
				e.Busy = true;
				Task.Run(delegate
				{
					string text = "";
					try
					{
						string? pdo = PdoName(WinUsb.InstanceId(devicePath));
						if (pdo != null)
						{
							text = string.Join(", ", Find(pdo, device: true));
						}
					}
					catch
					{
					}
					lock (e)
					{
						e.Text = text;
						e.At = DateTime.UtcNow;
						e.Busy = false;
					}
				});
			}
			return e.Text;
		}
	}

	/// <summary>The device's object name, e.g. \Device\USBPDO-12, from its instance id.</summary>
	public static string? PdoName(string instanceId)
	{
		if (CM_Locate_DevNodeW(out uint node, instanceId, 0u) != 0)
		{
			return null;
		}
		char[] buf = new char[260];
		uint size = (uint)(buf.Length * 2);
		if (CM_Get_DevNode_Registry_PropertyW(node, 15u /* CM_DRP_PHYSICAL_DEVICE_OBJECT_NAME */, out _, buf, ref size, 0u) != 0)
		{
			return null;
		}
		return new string(buf).TrimEnd('\0');
	}

	/// <summary>Names of the processes (other than this one) with a handle open on the object with this NT name.</summary>
	public static List<string> Find(string objectName, bool device)
	{
		HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int me = Environment.ProcessId;
		// the "File" object type's index: the one of a file handle this process holds while the table is taken
		using System.IO.FileStream probe = new System.IO.FileStream(Environment.ProcessPath ?? typeof(DeviceHolders).Assembly.Location, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
		long probeHandle = probe.SafeFileHandle.DangerousGetHandle();
		byte[]? table = HandleTable();
		if (table == null)
		{
			return found.ToList();
		}
		long count = BitConverter.ToInt64(table, 0);
		int fileType = -1;
		for (long i = 0; i < count; i++)
		{
			int at = 16 + (int)(i * 40);
			if (BitConverter.ToInt64(table, at + 8) == me && BitConverter.ToInt64(table, at + 16) == probeHandle)
			{
				fileType = BitConverter.ToUInt16(table, at + 30);
				break;
			}
		}
		if (fileType < 0)
		{
			return found.ToList();
		}
		Dictionary<long, IntPtr> processes = new Dictionary<long, IntPtr>();
		try
		{
			for (long i = 0; i < count; i++)
			{
				int at = 16 + (int)(i * 40);
				long pid = BitConverter.ToInt64(table, at + 8);
				if (pid == me || pid <= 4 || BitConverter.ToUInt16(table, at + 30) != fileType)
				{
					continue;
				}
				if (!processes.TryGetValue(pid, out IntPtr process))
				{
					process = OpenProcess(0x40u /* PROCESS_DUP_HANDLE */, false, (uint)pid);
					processes[pid] = process;
				}
				if (process == IntPtr.Zero)
				{
					continue;
				}
				IntPtr handle = new IntPtr(BitConverter.ToInt64(table, at + 16));
				if (!DuplicateHandle(process, handle, GetCurrentProcess(), out IntPtr copy, 0u, false, 2u /* DUPLICATE_SAME_ACCESS */))
				{
					continue;
				}
				bool close = true;
				try
				{
					// pipes can hang a name query; disk files (type 1) are never a USB device
					uint kind = GetFileType(copy);
					if (kind == 3u || (device && kind == 1u))
					{
						continue;
					}
					string? name = ObjectName(copy, out bool answered);
					close = answered;
					if (name != null && name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
					{
						try
						{
							using Process p = Process.GetProcessById((int)pid);
							found.Add(p.ProcessName);
						}
						catch
						{
							found.Add("process " + pid);
						}
					}
				}
				finally
				{
					// a query still running keeps its handle (closing it under the query could hit a reused handle)
					if (close)
					{
						CloseHandle(copy);
					}
				}
			}
		}
		finally
		{
			foreach (IntPtr p in processes.Values)
			{
				if (p != IntPtr.Zero)
				{
					CloseHandle(p);
				}
			}
		}
		return found.ToList();
	}

	/// <summary>The NT name of a file or device, e.g. \Device\HarddiskVolume3\x.txt, for tests.</summary>
	public static string? NtNameOfFile(string path)
	{
		using System.IO.FileStream f = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
		return ObjectName(f.SafeFileHandle.DangerousGetHandle(), out _);
	}

	private static byte[]? HandleTable()
	{
		int size = 1 << 22;
		for (int tries = 0; tries < 8; tries++)
		{
			byte[] buf = new byte[size];
			int status = NtQuerySystemInformation(64 /* SystemExtendedHandleInformation */, buf, size, out int needed);
			if (status == 0)
			{
				return buf;
			}
			if (status != unchecked((int)0xC0000004) /* STATUS_INFO_LENGTH_MISMATCH */)
			{
				return null;
			}
			size = Math.Max(size * 2, needed + (1 << 20));
		}
		return null;
	}

	private static string? ObjectName(IntPtr handle, out bool answered)
	{
		string? result = null;
		// a query can still block on an odd handle, so it runs on its own thread and is abandoned after 150 ms
		Thread t = new Thread(delegate()
		{
			byte[] buf = new byte[2048];
			if (NtQueryObject(handle, 1 /* ObjectNameInformation */, buf, buf.Length, out _) == 0)
			{
				int len = BitConverter.ToUInt16(buf, 0);
				if (len > 0)
				{
					result = System.Text.Encoding.Unicode.GetString(buf, 16, len);
				}
			}
		})
		{
			IsBackground = true
		};
		t.Start();
		answered = t.Join(150);
		return answered ? result : null;
	}

	[DllImport("ntdll.dll")]
	private static extern int NtQuerySystemInformation(int infoClass, byte[] info, int length, out int returned);

	[DllImport("ntdll.dll")]
	private static extern int NtQueryObject(IntPtr handle, int infoClass, byte[] info, int length, out int returned);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr target, uint access, bool inherit, uint options);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("kernel32.dll")]
	private static extern uint GetFileType(IntPtr handle);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Locate_DevNodeW(out uint node, string id, uint flags);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern int CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, char[] buffer, ref uint length, uint flags);
}
