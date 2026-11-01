using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LcdStudio.Core;

/// <summary>
/// The frame rate of the game (or any 3D app) in front. Two sources:
/// - Windows' own present events (the DXGI and Direct3D 9 providers, the same ones PresentMon reads), counted per
///   process from a real-time trace. Windows only lets an administrator (or a member of "Performance Log Users")
///   start one, so this works when LumaLi runs as administrator.
/// - RivaTuner Statistics Server's shared memory, when RTSS is running (it comes with MSI Afterburner); no rights needed.
/// The reading is the foreground process's frames in the last second; nothing is shown when no game is in front.
/// </summary>
public sealed class GameFps : IDisposable
{
	private const string SessionName = "LumaLi frame rate";

	private static readonly Guid Dxgi = new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
	private static readonly Guid D3D9 = new Guid("783aca0a-790e-4d7f-8451-aa850511c6b9");

	// present-start events: DXGI Present_Start (42), D3D9 Present_Start (1)
	private const ushort DxgiPresent = 42, D3D9Present = 1;

	private static GameFps? _live;

	private readonly Dictionary<uint, Queue<long>> _presents = new Dictionary<uint, Queue<long>>();

	private readonly long _qpcFrequency;

	private ulong _session;

	private ulong _trace;

	private Thread? _thread;

	public string Source { get; private set; } = "none";

	public GameFps()
	{
		QueryPerformanceFrequency(out _qpcFrequency);
	}

	/// <summary>Starts the trace if Windows allows it; otherwise RTSS is the only source.</summary>
	public unsafe void Start()
	{
		try
		{
			StopSession();
			int size = sizeof(TraceProperties) + 2 * (SessionName.Length + 1);
			IntPtr buf = Marshal.AllocHGlobal(size);
			try
			{
				new Span<byte>((void*)buf, size).Clear();
				var p = (TraceProperties*)buf;
				p->Wnode.BufferSize = (uint)size;
				p->Wnode.Flags = 0x20000; // WNODE_FLAG_TRACED_GUID
				p->Wnode.ClientContext = 1; // QPC timestamps
				p->LogFileMode = 0x100; // EVENT_TRACE_REAL_TIME_MODE
				p->BufferSize = 64;
				p->MinimumBuffers = 4;
				p->MaximumBuffers = 16;
				p->FlushTimer = 1;
				p->LoggerNameOffset = (uint)sizeof(TraceProperties);
				int err = StartTraceW(out _session, SessionName, buf);
				if (err != 0)
				{
					Log.Write(err == 5 ? "game frame rate: the trace needs administrator rights; using RTSS if it runs" : $"game frame rate: trace not started ({err})");
					_session = 0;
					return;
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buf);
			}
			Guid g1 = Dxgi, g2 = D3D9;
			EnableTraceEx2(_session, ref g1, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
			EnableTraceEx2(_session, ref g2, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
			_live = this;
			var log = new TraceLogfile
			{
				LoggerName = Marshal.StringToHGlobalUni(SessionName),
				ProcessTraceMode = 0x100 | 0x1000 | 0x10000000, // real time, raw (QPC) timestamps, event records
				EventRecordCallback = (IntPtr)(delegate* unmanaged[Stdcall]<EventRecord*, void>)&OnEvent
			};
			_trace = OpenTraceW(ref log);
			if (_trace == ulong.MaxValue || _trace == 0xFFFFFFFF)
			{
				Log.Write("game frame rate: trace not opened");
				StopSession();
				return;
			}
			_thread = new Thread(() =>
			{
				ulong h = _trace;
				ProcessTrace(ref h, 1, IntPtr.Zero, IntPtr.Zero);
			})
			{
				IsBackground = true,
				Name = "Game frame rate"
			};
			_thread.Start();
			Source = "Windows";
			Log.Write("game frame rate: counting presents");
		}
		catch (Exception ex)
		{
			Log.Write("game frame rate: " + ex.Message);
		}
	}

	[UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
	private static unsafe void OnEvent(EventRecord* r)
	{
		GameFps? me = _live;
		if (me == null)
		{
			return;
		}
		ushort id = r->Id;
		bool present = (id == DxgiPresent && r->ProviderId == Dxgi) || (id == D3D9Present && r->ProviderId == D3D9);
		if (!present)
		{
			return;
		}
		lock (me._presents)
		{
			if (!me._presents.TryGetValue(r->ProcessId, out Queue<long>? q))
			{
				me._presents[r->ProcessId] = q = new Queue<long>();
			}
			q.Enqueue(r->TimeStamp);
			long old = r->TimeStamp - me._qpcFrequency * 3;
			while (q.Count > 0 && q.Peek() < old)
			{
				q.Dequeue();
			}
		}
	}

	/// <summary>The frame rate of the foreground process, or null when it isn't drawing frames.</summary>
	public float? Read()
	{
		uint front = ForegroundProcess();
		// the game seen in front last keeps its reading while another window is in front, as long as it keeps drawing
		foreach (uint pid in new[] { front, _lastGame })
		{
			if (pid == 0 || pid == (uint)Environment.ProcessId)
			{
				continue;
			}
			float? rate = FromRtss(pid) ?? FromTrace(pid);
			if (rate.HasValue)
			{
				if (pid == front && pid != _lastGame)
				{
					_lastGame = pid;
					try
					{
						using var p = System.Diagnostics.Process.GetProcessById((int)pid);
						Log.Write($"game frame rate: following {p.ProcessName} ({rate:0} fps)");
					}
					catch
					{
					}
				}
				return rate;
			}
		}
		return null;
	}

	private uint _lastGame;

	private float? FromTrace(uint pid)
	{
		if (_session == 0)
		{
			return null;
		}
		QueryPerformanceCounter(out long now);
		lock (_presents)
		{
			if (!_presents.TryGetValue(pid, out Queue<long>? q) || q.Count < 3)
			{
				return null;
			}
			// Windows hands the events over in batches, up to about a second late, so the rate is measured over the
			// second before the newest event received rather than the second before now
			long newest = 0;
			foreach (long t in q)
			{
				newest = Math.Max(newest, t);
			}
			if (now - newest > _qpcFrequency * 5 / 2)
			{
				return null;
			}
			long since = newest - _qpcFrequency;
			int n = 0;
			long first = 0;
			foreach (long t in q)
			{
				if (t >= since)
				{
					if (n == 0 || t < first)
					{
						first = t;
					}
					n++;
				}
			}
			if (n < 3 || newest <= first)
			{
				return null;
			}
			return (float)((n - 1) * (double)_qpcFrequency / (newest - first));
		}
	}

	/// <summary>Every process that presented frames in the last second, with its frame rate (for --fps-probe).</summary>
	public Dictionary<uint, float> Rates()
	{
		var result = new Dictionary<uint, float>();
		QueryPerformanceCounter(out long now);
		lock (_presents)
		{
			foreach (var (pid, q) in _presents)
			{
				var recent = new List<long>();
				foreach (long t in q)
				{
					if (t >= now - _qpcFrequency)
					{
						recent.Add(t);
					}
				}
				if (recent.Count >= 3)
				{
					result[pid] = (float)((recent.Count - 1) * (double)_qpcFrequency / (recent[^1] - recent[0]));
				}
			}
		}
		return result;
	}

	/// <summary>Frame rate from RivaTuner Statistics Server for the process, if RTSS is running and watching it.</summary>
	private static float? FromRtss(uint pid)
	{
		try
		{
			using var map = MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2", MemoryMappedFileRights.Read);
			using var v = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
			if (v.ReadUInt32(0) != 0x52545353) // "RTSS"
			{
				return null;
			}
			uint entrySize = v.ReadUInt32(8), arrOffset = v.ReadUInt32(12), count = v.ReadUInt32(16);
			for (uint i = 0; i < count && i < 256; i++)
			{
				long e = arrOffset + (long)i * entrySize;
				if (v.ReadUInt32(e) != pid)
				{
					continue;
				}
				uint t0 = v.ReadUInt32(e + 268), t1 = v.ReadUInt32(e + 272), frames = v.ReadUInt32(e + 276);
				if (t1 > t0 && frames > 0)
				{
					return 1000f * frames / (t1 - t0);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>Browsers and desktop apps draw frames too; in a window they aren't a game, but anything full screen counts.</summary>
	private static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "claude", "discord", "code", "explorer", "steam",
		"steamwebhelper", "epicgameslauncher", "slack", "ms-teams", "teams", "spotify", "whatsapp", "telegram", "obs64",
		"applicationframehost", "searchhost", "startmenuexperiencehost", "textinputhost", "shellexperiencehost", "msedgewebview2",
		"lumali", "gpu tweak iii", "krakenlite", "nzxt cam", "monitor", "msiafterburner", "rtss", "nvidia app", "nvcontainer",
		"windowsterminal", "openconsole", "conhost", "powershell", "pwsh", "cmd", "notepad", "devenv", "rider64"
	};

	private static uint ForegroundProcess()
	{
		IntPtr w = GetForegroundWindow();
		if (w == IntPtr.Zero)
		{
			return 0;
		}
		GetWindowThreadProcessId(w, out uint pid);
		if (FillsScreen(w))
		{
			return pid;
		}
		try
		{
			using var p = System.Diagnostics.Process.GetProcessById((int)pid);
			return NotGames.Contains(p.ProcessName) ? 0u : pid;
		}
		catch
		{
			return 0;
		}
	}

	private static bool FillsScreen(IntPtr w)
	{
		if (!GetWindowRect(w, out Rect r))
		{
			return false;
		}
		var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
		if (!GetMonitorInfoW(MonitorFromWindow(w, 2), ref info))
		{
			return false;
		}
		return r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MonitorInfo
	{
		public int Size;
		public Rect Monitor, Work;
		public uint Flags;
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out Rect r);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

	private unsafe void StopSession()
	{
		int size = sizeof(TraceProperties) + 2 * (SessionName.Length + 1);
		IntPtr buf = Marshal.AllocHGlobal(size);
		try
		{
			new Span<byte>((void*)buf, size).Clear();
			var p = (TraceProperties*)buf;
			p->Wnode.BufferSize = (uint)size;
			p->LoggerNameOffset = (uint)sizeof(TraceProperties);
			ControlTraceW(0, SessionName, buf, 1); // EVENT_TRACE_CONTROL_STOP, by name (also clears one left by a crash)
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	public void Dispose()
	{
		_live = null;
		if (_session != 0)
		{
			StopSession();
			_session = 0;
		}
		if (_trace != 0)
		{
			CloseTrace(_trace);
			_trace = 0;
		}
	}

	// ---- ETW structures (x64) ----

	[StructLayout(LayoutKind.Sequential)]
	private struct WnodeHeader
	{
		public uint BufferSize, ProviderId;
		public ulong HistoricalContext;
		public long TimeStamp;
		public Guid Guid;
		public uint ClientContext, Flags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TraceProperties
	{
		public WnodeHeader Wnode;
		public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
		public int AgeLimit;
		public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
		public IntPtr LoggerThreadId;
		public uint LogFileNameOffset, LoggerNameOffset;
	}

	/// <summary>EVENT_TRACE_LOGFILEW: 448 bytes on x64; only the fields used are named.</summary>
	[StructLayout(LayoutKind.Explicit, Size = 448)]
	private struct TraceLogfile
	{
		[FieldOffset(0)] public IntPtr LogFileName;
		[FieldOffset(8)] public IntPtr LoggerName;
		[FieldOffset(28)] public uint ProcessTraceMode;
		[FieldOffset(424)] public IntPtr EventRecordCallback;
	}

	/// <summary>The start of EVENT_RECORD (its EVENT_HEADER).</summary>
	[StructLayout(LayoutKind.Explicit)]
	private struct EventRecord
	{
		[FieldOffset(12)] public uint ProcessId;
		[FieldOffset(16)] public long TimeStamp;
		[FieldOffset(24)] public Guid ProviderId;
		[FieldOffset(40)] public ushort Id;
	}

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
	private static extern int StartTraceW(out ulong handle, string name, IntPtr properties);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
	private static extern int ControlTraceW(ulong handle, string name, IntPtr properties, uint code);

	[DllImport("advapi32.dll")]
	private static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
	private static extern ulong OpenTraceW(ref TraceLogfile logfile);

	[DllImport("advapi32.dll")]
	private static extern int ProcessTrace(ref ulong handles, uint count, IntPtr start, IntPtr end);

	[DllImport("advapi32.dll")]
	private static extern int CloseTrace(ulong handle);

	[DllImport("kernel32.dll")]
	private static extern bool QueryPerformanceFrequency(out long f);

	[DllImport("kernel32.dll")]
	private static extern bool QueryPerformanceCounter(out long c);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
