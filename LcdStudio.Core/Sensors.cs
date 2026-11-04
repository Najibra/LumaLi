using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace LcdStudio.Core;

public sealed class Sensors : IDisposable
{
	private struct MemoryStatus
	{
		public uint Length;

		public uint Load;

		public ulong Total;

		public ulong Avail;

		public ulong TotalPage;

		public ulong AvailPage;

		public ulong TotalVirtual;

		public ulong AvailVirtual;

		public ulong Extended;
	}

	public static readonly SensorDef[] Catalog = new SensorDef[]
	{
		new SensorDef("cpu.load", "CPU load", "%", 0f, 100f),
		new SensorDef("cpu.temp", "CPU temperature", "°C", 20f, 95f),
		new SensorDef("cpu.power", "CPU power", "W", 0f, 200f),
		new SensorDef("cpu.clock", "CPU clock", "MHz", 0f, 6000f),
		new SensorDef("cpu.busiest", "Busiest CPU thread", "%", 0f, 100f),
		new SensorDef("gpu.load", "GPU load", "%", 0f, 100f),
		new SensorDef("gpu.temp", "GPU temperature", "°C", 20f, 95f),
		new SensorDef("gpu.hotspot", "GPU hot spot", "°C", 20f, 110f),
		new SensorDef("gpu.power", "GPU power", "W", 0f, 600f),
		new SensorDef("gpu.clock", "GPU clock", "MHz", 0f, 3500f),
		new SensorDef("gpu.memclock", "GPU memory clock", "MHz", 0f, 16000f),
		new SensorDef("gpu.vram", "GPU memory used", "GB", 0f, 32f),
		new SensorDef("gpu.fan", "GPU fan", "%", 0f, 100f),
		new SensorDef("ram.load", "RAM used", "%", 0f, 100f),
		new SensorDef("ram.used", "RAM used", "GB", 0f, 128f),
		new SensorDef("net.down", "Download", "MB/s", 0f, 100f),
		new SensorDef("net.up", "Upload", "MB/s", 0f, 50f),
		new SensorDef("fans.rpm", "Case fan speed", "RPM", 0f, 3000f),
		new SensorDef("fans.duty", "Case fan duty", "%", 0f, 100f),
		new SensorDef("game.fps", "Game frame rate", "FPS", 0f, 240f),
		new SensorDef("top.cpu", "Busiest app's CPU share", "%", 0f, 100f),
		new SensorDef("media.progress", "Song progress", "%", 0f, 100f),
		new SensorDef("weather.temp", "Outdoor temperature", "°C", -10f, 50f),
		new SensorDef("weather.humidity", "Outdoor humidity", "%", 0f, 100f),
		new SensorDef("weather.wind", "Wind speed", "km/h", 0f, 80f)
	};

	private const int HistoryLength = 120;

	private readonly ConcurrentDictionary<string, float> _values = new ConcurrentDictionary<string, float>();

	private readonly ConcurrentDictionary<string, float[]> _history = new ConcurrentDictionary<string, float[]>();

	private readonly ConcurrentDictionary<string, string> _text = new ConcurrentDictionary<string, string>();

	private Computer? _pc;

	private GameFps? _fps;

	/// <summary>Where the game frame rate comes from: "Windows", or "none" (then only RTSS, when it runs).</summary>
	public string GameFpsSource => _fps?.Source ?? "none";

	private long _lastRx;

	private long _lastTx;

	private DateTime _lastNet;

	private int _version;

	private readonly ConcurrentDictionary<string, DateTime> _setAt = new ConcurrentDictionary<string, DateTime>();

	public int CoreCount { get; private set; }

	public bool CpuTempAvailable => _values.ContainsKey("cpu.temp");

	/// <summary>The processor's temperature sensors as LibreHardwareMonitor sees them (name=value), for the device report.</summary>
	public string CpuTempSensors { get; private set; } = "(processor not read yet)";

	/// <summary>
	/// Whether the PawnIO driver is installed. LibreHardwareMonitor 0.9.5+ reads every processor's temperature (Intel and
	/// AMD alike) through PawnIO instead of WinRing0, so without it there is no CPU temperature even as administrator.
	/// </summary>
	public static bool PawnIoInstalled
	{
		get
		{
			try
			{
				using Microsoft.Win32.RegistryKey? k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\PawnIO");
				if (k != null)
				{
					return true;
				}
			}
			catch
			{
			}
			return System.IO.File.Exists(System.IO.Path.Combine(Environment.SystemDirectory, "drivers", "PawnIO.sys"));
		}
	}

	public DateTime Started { get; } = DateTime.Now;


	public int Version => Volatile.Read(ref _version);

	public static SensorDef Def(string key)
	{
		string key2 = key;
		return Catalog.FirstOrDefault((SensorDef d) => d.Key == key2) ?? new SensorDef(key2, key2, "", 0f, 100f);
	}

	public void Bump()
	{
		Interlocked.Increment(ref _version);
	}

	public void Open()
	{
		_pc = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true
		};
		try
		{
			_pc.Open();
		}
		catch
		{
		}
		foreach (IHardware item in _pc.Hardware)
		{
			if (item.HardwareType == HardwareType.Cpu)
			{
				_text["cpu.name"] = Shorten(item.Name);
			}
			HardwareType hardwareType = item.HardwareType;
			if ((uint)(hardwareType - 4) <= 1u)
			{
				_text["gpu.name"] = Shorten(item.Name);
			}
		}
		_text.TryAdd("gpu.name", "Graphics");
		_text.TryAdd("cpu.name", "Processor");
	}

	/// <summary>Starts counting game frames. Only the full app does this, so a render or test run never takes over its trace.</summary>
	public void StartGameFps()
	{
		_fps ??= new GameFps();
		_fps.Start();
	}

	private void GameFrameRate()
	{
		float? fps = null;
		try
		{
			fps = (_fps ?? (_fps = new GameFps())).Read();
		}
		catch
		{
		}
		if (fps.HasValue && fps.Value > 0f && fps.Value < 2000f)
		{
			Set("game.fps", fps.Value);
		}
		else if (Age("game.fps") > 3.0)
		{
			// a short gap keeps the last reading; it goes only once the game has stopped drawing for a few seconds
			Remove("game.fps");
		}
	}

	private static string Shorten(string s)
	{
		return s.Replace("AMD ", "").Replace("NVIDIA ", "").Replace("Intel(R) ", "")
			.Replace("Core(TM) ", "")
			.Replace(" Processor", "")
			.Replace("-Core", "")
			.Trim();
	}

	public float? Get(string key)
	{
		if (!_values.TryGetValue(key, out var value))
		{
			return null;
		}
		return value;
	}

	public string? Text(string key)
	{
		if (!_text.TryGetValue(key, out string value))
		{
			return null;
		}
		return value;
	}

	public float[] History(string key)
	{
		if (!_history.TryGetValue(key, out float[] value))
		{
			return Array.Empty<float>();
		}
		return value;
	}

	internal void Backdate(string key, double seconds)
	{
		_setAt[key] = DateTime.UtcNow.AddSeconds(0.0 - seconds);
	}

	public double Age(string key)
	{
		if (!_setAt.TryGetValue(key, out var value))
		{
			return double.PositiveInfinity;
		}
		return (DateTime.UtcNow - value).TotalSeconds;
	}

	public void Set(string key, float value)
	{
		_values[key] = value;
		_setAt[key] = DateTime.UtcNow;
		float[] orAdd = _history.GetOrAdd(key, (string _) => new float[120]);
		lock (orAdd)
		{
			Array.Copy(orAdd, 1, orAdd, 0, orAdd.Length - 1);
			orAdd[^1] = value;
		}
	}

	public void Remove(string key)
	{
		_values.TryRemove(key, out var _);
	}

	public void SetText(string key, string value)
	{
		_text[key] = value;
	}

	public IEnumerable<SensorDef> Available()
	{
		return Catalog.Where((SensorDef d) => _values.ContainsKey(d.Key));
	}

	public void Update()
	{
		if (_pc != null)
		{
			IHardware hardware = null;
			foreach (IHardware item in _pc.Hardware)
			{
				try
				{
					item.Update();
					IHardware[] subHardware = item.SubHardware;
					for (int i = 0; i < subHardware.Length; i++)
					{
						subHardware[i].Update();
					}
				}
				catch
				{
					continue;
				}
				switch (item.HardwareType)
				{
				case HardwareType.Cpu:
					Cpu(item);
					break;
				case HardwareType.Memory:
					if (!item.Name.Contains("Virtual"))
					{
						Memory(item);
					}
					break;
				case HardwareType.GpuNvidia:
					hardware = item;
					break;
				case HardwareType.GpuAmd:
					if ((hardware == null || hardware.HardwareType != HardwareType.GpuNvidia) && (hardware == null || !item.Name.Contains("Radeon(TM) Graphics")))
					{
						hardware = item;
					}
					break;
				case HardwareType.GpuIntel:
					if (hardware == null)
					{
						hardware = item;
					}
					break;
				}
			}
			if (hardware != null)
			{
				Gpu(hardware);
			}
		}
		if (!_values.ContainsKey("ram.load"))
		{
			WindowsMemory();
		}
		Network();
		GameFrameRate();
		Media();
		WeatherReadings();
		_text["uptime"] = Uptime();
	}

	/// <summary>
	/// What's playing as readings: {media.title}, {media.artist}, {media.album}, {media.app}, {media.state} (Playing /
	/// Paused), {media.elapsed} and {media.length} ("1:23"), and media.progress (%). Gone while nothing plays.
	/// </summary>
	private void Media()
	{
		MediaInfo? m = NowPlaying.Current;
		if (m == null)
		{
			Remove("media.progress");
			foreach (string k in new[] { "media.title", "media.artist", "media.album", "media.app", "media.state", "media.elapsed", "media.length" })
			{
				_text.TryRemove(k, out _);
			}
			return;
		}
		_text["media.title"] = m.Title;
		_text["media.artist"] = m.Artist;
		_text["media.album"] = m.Album;
		_text["media.app"] = m.App;
		_text["media.state"] = m.Playing ? "Playing" : "Paused";
		_text["media.elapsed"] = MediaInfo.Clock(m.PositionNow);
		_text["media.length"] = m.Length > 0.0 ? MediaInfo.Clock(m.Length) : "";
		if (m.Length > 0.0)
		{
			Set("media.progress", m.Progress * 100f);
		}
		else
		{
			Remove("media.progress");
		}
	}

	/// <summary>
	/// The weather as readings: weather.temp (°C), weather.humidity, weather.wind, and the text {weather.degrees}
	/// (in the chosen unit, "31°"), {weather.text} ("Partly cloudy"), {weather.place}, {weather.high}, {weather.low}.
	/// </summary>
	private void WeatherReadings()
	{
		WeatherNow? w = Weather.Current;
		if (w == null)
		{
			Remove("weather.temp");
			Remove("weather.humidity");
			Remove("weather.wind");
			foreach (string k in new[] { "weather.degrees", "weather.text", "weather.place", "weather.high", "weather.low" })
			{
				_text.TryRemove(k, out _);
			}
			return;
		}
		Set("weather.temp", (float)w.Temp);
		Set("weather.humidity", (float)w.Humidity);
		Set("weather.wind", (float)w.Wind);
		_text["weather.degrees"] = Weather.Degrees(w.Temp);
		_text["weather.text"] = w.Text;
		_text["weather.place"] = w.Place.Split(',')[0];
		_text["weather.high"] = Weather.Degrees(w.High);
		_text["weather.low"] = Weather.Degrees(w.Low);
	}

	private static string Uptime()
	{
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(Environment.TickCount64);
		if (timeSpan.TotalDays >= 1.0)
		{
			return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h";
		}
		return $"{timeSpan.Hours}h {timeSpan.Minutes:00}m";
	}

	private static ISensor? Find(IHardware hw, SensorType type, string name)
	{
		string name2 = name;
		return hw.Sensors.FirstOrDefault((ISensor s) => s.SensorType == type && s.Name.Contains(name2) && s.Value.HasValue);
	}

	private void Put(string key, ISensor? s, Func<float, float>? map = null)
	{
		float? num = s?.Value;
		if (num.HasValue)
		{
			float valueOrDefault = num.GetValueOrDefault();
			if (!float.IsNaN(valueOrDefault))
			{
				Set(key, map?.Invoke(valueOrDefault) ?? valueOrDefault);
			}
		}
	}

	private void Cpu(IHardware hw)
	{
		List<ISensor> temps = hw.Sensors.Where((ISensor s) => s.SensorType == SensorType.Temperature).ToList();
		CpuTempSensors = temps.Count == 0 ? "(none)" : string.Join(", ", temps.Take(12).Select((ISensor s) => s.Name + "=" + (s.Value.HasValue ? s.Value.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "none")));
		List<ISensor> source = temps.Where((ISensor s) => s.Value > 0f).ToList();
		ISensor sensor = source.FirstOrDefault((ISensor s) => s.Name.Contains("Tctl")) ?? source.FirstOrDefault((ISensor s) => s.Name.Contains("Package")) ?? source.FirstOrDefault((ISensor s) => s.Name.Contains("CCD")) ?? source.FirstOrDefault((ISensor s) => s.Name.Contains("Core"));
		if (sensor != null)
		{
			Put("cpu.temp", sensor);
		}
		else
		{
			Remove("cpu.temp");
		}
		Put("cpu.load", Find(hw, SensorType.Load, "CPU Total"));
		ISensor sensor2 = Find(hw, SensorType.Power, "Package");
		if (sensor2 != null && sensor2.Value > 0f)
		{
			Put("cpu.power", sensor2);
		}
		List<ISensor> list = hw.Sensors.Where((ISensor s) => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core") && s.Value > 0f).ToList();
		if (list.Count > 0)
		{
			Set("cpu.clock", list.Max((ISensor s) => s.Value.Value));
		}
		List<ISensor> list2 = (from s in hw.Sensors
			where s.SensorType == SensorType.Load && s.Name.StartsWith("CPU Core #") && s.Value.HasValue
			orderby s.Index
			select s).ToList();
		CoreCount = list2.Count;
		for (int i = 0; i < list2.Count; i++)
		{
			Set($"cpu.core{i + 1}", list2[i].Value.Value);
		}
		ISensor sensor3 = Find(hw, SensorType.Load, "CPU Core Max");
		if (sensor3 != null)
		{
			Put("cpu.busiest", sensor3);
		}
		else if (list2.Count > 0)
		{
			Set("cpu.busiest", list2.Max((ISensor s) => s.Value.Value));
		}
	}

	private void Gpu(IHardware hw)
	{
		Put("gpu.temp", Find(hw, SensorType.Temperature, "GPU Core"));
		Put("gpu.hotspot", Find(hw, SensorType.Temperature, "Hot Spot"));
		Put("gpu.load", Find(hw, SensorType.Load, "GPU Core"));
		Put("gpu.power", Find(hw, SensorType.Power, "Package") ?? Find(hw, SensorType.Power, ""));
		Put("gpu.clock", Find(hw, SensorType.Clock, "GPU Core"));
		Put("gpu.memclock", Find(hw, SensorType.Clock, "GPU Memory"));
		Put("gpu.vram", Find(hw, SensorType.SmallData, "GPU Memory Used") ?? Find(hw, SensorType.SmallData, "Memory Used"), (float v) => v / 1024f);
		Put("gpu.fan", Find(hw, SensorType.Control, "GPU Fan"));
	}

	private void Memory(IHardware hw)
	{
		Put("ram.load", Find(hw, SensorType.Load, "Memory"));
		Put("ram.used", Find(hw, SensorType.Data, "Memory Used"));
		float? num = Find(hw, SensorType.Data, "Memory Available")?.Value;
		if (num.HasValue)
		{
			float valueOrDefault = num.GetValueOrDefault();
			num = Get("ram.used");
			if (num.HasValue)
			{
				float valueOrDefault2 = num.GetValueOrDefault();
				Set("ram.total", valueOrDefault + valueOrDefault2);
			}
		}
	}

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus m);

	private void WindowsMemory()
	{
		MemoryStatus memoryStatus = default(MemoryStatus);
		memoryStatus.Length = (uint)Marshal.SizeOf<MemoryStatus>();
		MemoryStatus m = memoryStatus;
		if (GlobalMemoryStatusEx(ref m))
		{
			Set("ram.load", m.Load);
			Set("ram.used", (float)(m.Total - m.Avail) / 1.0737418E+09f);
			Set("ram.total", (float)m.Total / 1.0737418E+09f);
		}
	}

	private void Network()
	{
		long num = 0L;
		long num2 = 0L;
		try
		{
			NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface networkInterface in allNetworkInterfaces)
			{
				if (networkInterface.OperationalStatus == OperationalStatus.Up && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
				{
					IPv4InterfaceStatistics iPv4Statistics = networkInterface.GetIPv4Statistics();
					num += iPv4Statistics.BytesReceived;
					num2 += iPv4Statistics.BytesSent;
				}
			}
		}
		catch
		{
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		if (_lastNet != default(DateTime))
		{
			double num3 = Math.Max(0.1, (utcNow - _lastNet).TotalSeconds);
			Set("net.down", (float)Math.Max(0.0, (double)(num - _lastRx) / num3 / 1048576.0));
			Set("net.up", (float)Math.Max(0.0, (double)(num2 - _lastTx) / num3 / 1048576.0));
		}
		_lastRx = num;
		_lastTx = num2;
		_lastNet = utcNow;
	}

	public void Dispose()
	{
		_fps?.Dispose();
		try
		{
			_pc?.Close();
		}
		catch
		{
		}
	}
}
