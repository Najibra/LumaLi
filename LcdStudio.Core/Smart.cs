using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LcdStudio.Core;

/// <summary>The smart features' settings (Settings → Smart).</summary>
public sealed class SmartSettings
{
	/// <summary>Switches the screen and fan lighting by what the PC is doing.</summary>
	public bool AutoMode { get; set; }

	// per activity: a layout and a fan lighting effect ("" keeps the chosen one)
	public string GamingLayout { get; set; } = "";
	public string WatchingLayout { get; set; } = "";
	public string WorkingLayout { get; set; } = "";
	public string GamingEffect { get; set; } = "";
	public string WatchingEffect { get; set; } = "";
	public string WorkingEffect { get; set; } = "";

	/// <summary>The light strip flows through the main colours on the monitor.</summary>
	public bool StripFollowsMonitor { get; set; }

	/// <summary>The fan lights take the main colours on the monitor (re-sent at most every 30 s).</summary>
	public bool FansFollowMonitor { get; set; }

	/// <summary>Keeps a daily record of temperatures and fan speeds and warns about dust or a failing fan.</summary>
	public bool HealthWatch { get; set; } = true;

	public string LayoutFor(string activity) => activity switch { Activity.Gaming => GamingLayout, Activity.Watching => WatchingLayout, _ => WorkingLayout };

	public string EffectFor(string activity) => activity switch { Activity.Gaming => GamingEffect, Activity.Watching => WatchingEffect, _ => WorkingEffect };
}

/// <summary>What the PC is doing, from the window in front: a game, a video full screen, or anything else.</summary>
public static class Activity
{
	public const string Gaming = "Gaming", Watching = "Watching", Working = "Working";

	public static readonly string[] All = { Gaming, Watching, Working };

	/// <summary>Players and browsers: full screen, they mean a video is on.</summary>
	private static readonly HashSet<string> Players = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"vlc", "mpc-hc64", "mpc-hc", "mpc-be64", "mpc-be", "potplayermini64", "potplayermini", "potplayer", "mpv", "wmplayer", "video.ui",
		"microsoft.media.player", "kodi", "plex", "plexamp", "netflix", "ffplay", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "applicationframehost"
	};

	/// <summary>The activity now. A game is something drawing frames in front (the frame-rate sensor) that isn't a player.</summary>
	public static string Detect(Sensors sensors)
	{
		IntPtr w = GetForegroundWindow();
		string name = "";
		bool full = false;
		if (w != IntPtr.Zero)
		{
			GetWindowThreadProcessId(w, out uint pid);
			full = FillsScreen(w);
			try
			{
				using Process p = Process.GetProcessById((int)pid);
				name = p.ProcessName;
			}
			catch
			{
			}
		}
		if (full && Players.Contains(name))
		{
			return Watching;
		}
		if (sensors.Get("game.fps").HasValue && !Players.Contains(name))
		{
			return Gaming;
		}
		return Working;
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
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out Rect r);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}

/// <summary>
/// The main colours on the monitor: the screen shrunk to 64 × 36 (Windows averages it as it shrinks), then the
/// pixels sorted into 24 hue bins weighted by how vivid they are. The two strongest bins (well apart) are the palette,
/// brightened to full value so the lights stay rich; a grey screen gives a soft white.
/// </summary>
public static class MonitorColors
{
	public static Color[]? Sample()
	{
		const int w = 64, h = 36;
		IntPtr screen = GetDC(IntPtr.Zero);
		if (screen == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
			using Bitmap small = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
			using (Graphics g = Graphics.FromImage(small))
			{
				IntPtr dc = g.GetHdc();
				try
				{
					SetStretchBltMode(dc, 4); // HALFTONE: averages each block of pixels
					if (!StretchBlt(dc, 0, 0, w, h, screen, 0, 0, sw, sh, 0x00CC0020))
					{
						return null;
					}
				}
				finally
				{
					g.ReleaseHdc(dc);
				}
			}
			return Palette(small);
		}
		catch
		{
			return null;
		}
		finally
		{
			ReleaseDC(IntPtr.Zero, screen);
		}
	}

	/// <summary>Two vivid colours from a picture (public for the self-test).</summary>
	public static Color[] Palette(Bitmap bmp)
	{
		double[] weight = new double[24];
		double[] sx = new double[24], sy = new double[24];
		for (int y = 0; y < bmp.Height; y++)
		{
			for (int x = 0; x < bmp.Width; x++)
			{
				Color c = bmp.GetPixel(x, y);
				float s = c.GetSaturation(), v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f;
				if (v < 0.12f || s < 0.18f)
				{
					continue;
				}
				double hue = c.GetHue();
				int bin = (int)(hue / 15.0) % 24;
				double wgt = s * s * v;
				weight[bin] += wgt;
				// the bin's hue as a mean on the circle, so it isn't stuck to the bin's edge
				sx[bin] += Math.Cos(hue * Math.PI / 180.0) * wgt;
				sy[bin] += Math.Sin(hue * Math.PI / 180.0) * wgt;
			}
		}
		double total = weight.Sum();
		if (total < bmp.Width * bmp.Height * 0.01)
		{
			return new[] { Color.FromArgb(255, 236, 214), Color.FromArgb(200, 214, 255) };
		}
		int first = Array.IndexOf(weight, weight.Max());
		int second = -1;
		for (int i = 0; i < 24; i++)
		{
			int apart = Math.Min(Math.Abs(i - first), 24 - Math.Abs(i - first));
			if (apart >= 3 && (second < 0 || weight[i] > weight[second]))
			{
				second = i;
			}
		}
		// a screen of one colour gets a neighbour of it as the second, a little along the wheel
		double h1 = Hue(sx[first], sy[first]);
		double h2 = second >= 0 && weight[second] > weight[first] * 0.12 ? Hue(sx[second], sy[second]) : (h1 + 40.0) % 360.0;
		return new[] { FromHue(h1), FromHue(h2) };
	}

	private static double Hue(double x, double y) => (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;

	/// <summary>Full value, high saturation: lights look washed out otherwise.</summary>
	public static Color FromHue(double hue, double sat = 0.85)
	{
		double c = sat, hh = hue / 60.0, x = c * (1 - Math.Abs(hh % 2 - 1)), m = 1 - c;
		(double r, double g, double b) = (int)hh switch
		{
			0 => (c, x, 0.0),
			1 => (x, c, 0.0),
			2 => (0.0, c, x),
			3 => (0.0, x, c),
			4 => (x, 0.0, c),
			_ => (c, 0.0, x)
		};
		return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
	}

	/// <summary>How far apart two palettes are, in degrees of hue (the bigger of the two colours' moves).</summary>
	public static double Distance(Color[] a, Color[] b)
	{
		double d = 0;
		for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
		{
			double x = Math.Abs(a[i].GetHue() - b[i].GetHue());
			d = Math.Max(d, Math.Min(x, 360 - x));
		}
		return d;
	}

	public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

	[DllImport("user32.dll")]
	private static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);

	[DllImport("gdi32.dll")]
	private static extern int SetStretchBltMode(IntPtr dc, int mode);

	[DllImport("gdi32.dll")]
	private static extern bool StretchBlt(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
}

/// <summary>The app using the most processor time, sampled every few seconds: the {top.app} text and top.cpu sensor.</summary>
public sealed class TopApp
{
	private Dictionary<int, TimeSpan> _last = new Dictionary<int, TimeSpan>();

	private DateTime _at;

	private static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Idle", "System", "Registry", "Memory Compression", "Secure System" };

	/// <summary>Returns the busiest app's name and its share of the whole processor (0–100), or null on the first call.</summary>
	public (string Name, float Percent)? Sample()
	{
		DateTime now = DateTime.UtcNow;
		var times = new Dictionary<int, TimeSpan>();
		var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		double span = (now - _at).TotalSeconds;
		foreach (Process p in Process.GetProcesses())
		{
			try
			{
				if (Skip.Contains(p.ProcessName) || p.Id == 0)
				{
					continue;
				}
				TimeSpan t = p.TotalProcessorTime;
				times[p.Id] = t;
				if (_last.TryGetValue(p.Id, out TimeSpan before) && t >= before)
				{
					byName.TryGetValue(p.ProcessName, out double sum);
					byName[p.ProcessName] = sum + (t - before).TotalSeconds;
				}
			}
			catch
			{
			}
			finally
			{
				p.Dispose();
			}
		}
		bool first = _last.Count == 0;
		_last = times;
		_at = now;
		if (first || span <= 0.2 || byName.Count == 0)
		{
			return null;
		}
		var top = byName.OrderByDescending((KeyValuePair<string, double> kv) => kv.Value).First();
		float percent = (float)Math.Clamp(top.Value / span / Environment.ProcessorCount * 100.0, 0.0, 100.0);
		return (Pretty(top.Key), percent);
	}

	private static string Pretty(string name)
	{
		name = name.Replace("-Win64-Shipping", "").Replace("_", " ");
		return name.Length > 22 ? name.Substring(0, 22) : name;
	}
}

/// <summary>
/// A daily record of how warm the PC runs and how fast the fans turn, kept in health.json. Readings are taken once a
/// minute and filed by load: graphics and processor temperature at rest (load under 15 %) and under load (over 60 %),
/// and each fan group's speed at its duty. The first two weeks with enough readings are the baseline; after that the
/// last three days are compared with it, so dust shows as the same work running warmer, and a tiring fan as the same
/// duty giving fewer turns.
/// </summary>
public sealed class HealthWatch
{
	public sealed class Day
	{
		public string Date { get; set; } = "";
		public Dictionary<string, double> Sum { get; set; } = new Dictionary<string, double>();
		public Dictionary<string, int> Count { get; set; } = new Dictionary<string, int>();

		public double? Mean(string key, int least = 20) => Count.TryGetValue(key, out int n) && n >= least ? Sum[key] / n : null;
	}

	public List<Day> Days { get; set; } = new List<Day>();

	private DateTime _lastSample = DateTime.MinValue;

	private DateTime _lastSave = DateTime.UtcNow;

	private static string FilePath => Path.Combine(Store.Folder, "health.json");

	public static HealthWatch Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return JsonSerializer.Deserialize<HealthWatch>(File.ReadAllText(FilePath)) ?? new HealthWatch();
			}
		}
		catch (Exception e)
		{
			Log.Write("health record unreadable: " + e.Message);
		}
		return new HealthWatch();
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory(Store.Folder);
			string tmp = FilePath + ".tmp";
			File.WriteAllText(tmp, JsonSerializer.Serialize(this));
			File.Move(tmp, FilePath, overwrite: true);
		}
		catch
		{
		}
	}

	/// <summary>Once a minute: files the readings under today and keeps 120 days. Saved every 15 minutes.</summary>
	public void Tick(Sensors s, DateTime now)
	{
		if ((now - _lastSample).TotalSeconds < 60.0)
		{
			return;
		}
		_lastSample = now;
		Add(s, now);
		if ((DateTime.UtcNow - _lastSave).TotalMinutes >= 15.0)
		{
			_lastSave = DateTime.UtcNow;
			Save();
		}
	}

	public void Add(Sensors s, DateTime now)
	{
		string date = now.ToString("yyyy-MM-dd");
		Day? day = Days.LastOrDefault();
		if (day == null || day.Date != date)
		{
			day = new Day { Date = date };
			Days.Add(day);
			if (Days.Count > 120)
			{
				Days.RemoveAt(0);
			}
		}
		void File1(string key, float? v)
		{
			if (v.HasValue && !float.IsNaN(v.Value))
			{
				day.Sum[key] = (day.Sum.TryGetValue(key, out double x) ? x : 0) + v.Value;
				day.Count[key] = (day.Count.TryGetValue(key, out int n) ? n : 0) + 1;
			}
		}
		foreach (string part in new[] { "gpu", "cpu" })
		{
			float? load = s.Get(part + ".load"), temp = s.Get(part + ".temp");
			if (load.HasValue && temp.HasValue)
			{
				if (load.Value < 15f)
				{
					File1(part + ".rest", temp);
				}
				else if (load.Value > 60f)
				{
					File1(part + ".busy", temp);
				}
			}
		}
		for (int i = 1; i <= 4; i++)
		{
			float? duty = s.Get($"group{i}.duty"), rpm = s.Get($"group{i}.rpm");
			if (duty.HasValue && rpm.HasValue && duty.Value >= 20f)
			{
				// speed per duty in 10 % bands, so a curve that moves doesn't look like a tiring fan
				int band = (int)(duty.Value / 10f) * 10;
				File1($"group{i}.rpm@{band}", rpm);
			}
		}
	}

	/// <summary>Plain warnings, newest knowledge first; empty when all is well or there isn't enough history yet.</summary>
	public List<string> Notes()
	{
		var notes = new List<string>();
		if (Days.Count < 17)
		{
			return notes;
		}
		List<Day> baseline = Days.Take(14).ToList(), recent = Days.Skip(Math.Max(14, Days.Count - 3)).ToList();
		double? Avg(IEnumerable<Day> days, string key)
		{
			var v = days.Select((Day d) => d.Mean(key)).Where((double? x) => x.HasValue).Select((double? x) => x!.Value).ToList();
			return v.Count >= 2 ? v.Average() : null;
		}
		foreach ((string key, string what) in new[] { ("gpu.rest", "graphics card at rest"), ("gpu.busy", "graphics card under load"), ("cpu.rest", "processor at rest"), ("cpu.busy", "processor under load") })
		{
			double? was = Avg(baseline, key), now = Avg(recent, key);
			if (was.HasValue && now.HasValue && now.Value - was.Value >= 6.0)
			{
				notes.Add($"The {what} runs {now.Value - was.Value:0} °C warmer than it did ({was.Value:0} → {now.Value:0} °C). Dust in the filters or fins is the usual cause.");
			}
		}
		var keys = Days.SelectMany((Day d) => d.Count.Keys).Where((string k) => k.Contains(".rpm@")).Distinct();
		foreach (string key in keys)
		{
			double? was = Avg(baseline, key), now = Avg(recent, key);
			if (was.HasValue && now.HasValue && was.Value > 300 && now.Value < was.Value * 0.85)
			{
				string group = key.Substring(5, 1), duty = key.Substring(key.IndexOf('@') + 1);
				notes.Add($"Fan group {group} turns {100 - now.Value / was.Value * 100:0} % slower at {duty} % than it used to ({was.Value:0} → {now.Value:0} rpm). A fan may be tiring or blocked.");
			}
		}
		return notes;
	}

	/// <summary>How many days are recorded, for the Settings card.</summary>
	public string Summary()
	{
		if (Days.Count == 0)
		{
			return "Recording starts now; it compares after about two and a half weeks.";
		}
		int left = Math.Max(0, 17 - Days.Count);
		return left > 0 ? $"{Days.Count} day{(Days.Count == 1 ? "" : "s")} recorded; the first comparison comes in about {left} day{(left == 1 ? "" : "s")}." : $"{Days.Count} days recorded. Nothing to report.";
	}
}
