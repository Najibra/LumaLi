using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LcdStudio.Devices;
using LcdStudio.Native;

namespace LcdStudio.Core;

public sealed class Engine : IDisposable
{
	private readonly CancellationTokenSource _quit = new CancellationTokenSource();

	private CancellationTokenSource _run = new CancellationTokenSource();

	private readonly List<Thread> _threads = new List<Thread>();

	private readonly object _gate = new object();

	private readonly Dictionary<string, double> _identifyUntil = new Dictionary<string, double>();

	private readonly Dictionary<string, double> _currentDuty = new Dictionary<string, double>();

	private volatile bool _lightingDirty = true;

	private readonly List<AutoResetEvent> _wakes = new List<AutoResetEvent>();

	private Timer? _saveTimer;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly object _rescanGate = new object();

	private bool _resumeAfterWake;

	private volatile bool _exiting;

	private int _editVersion;

	private string _lastSignature = "";

	private DateTime _lastCoolingTick = DateTime.UtcNow;

	private volatile bool _calibrating;

	private readonly Dictionary<string, byte[]> _originalPwm = new Dictionary<string, byte[]>();

	private bool _controlling;

	public Settings Settings { get; }

	public List<ScreenLayout> Layouts { get; } = new List<ScreenLayout>();


	public Sensors Sensors { get; } = new Sensors();


	public List<UsbDeviceInfo> Devices { get; private set; } = new List<UsbDeviceInfo>();


	public OutputStatus Screen { get; } = new OutputStatus
	{
		Name = "8.8\" screen",
		Kind = DeviceKind.Screen
	};


	public OutputStatus Strip { get; } = new OutputStatus
	{
		Name = "Screen light strip",
		Kind = DeviceKind.ScreenLeds
	};


	public OutputStatus Wireless { get; } = new OutputStatus
	{
		Name = "Fan lights & speed",
		Kind = DeviceKind.WirelessTx
	};


	public List<OutputStatus> FanOutputs { get; } = new List<OutputStatus>();


	public WirelessHub? Hub { get; private set; }

	public string? Warning { get; private set; }

	public bool Released { get; private set; }

	public double Time => _clock.Elapsed.TotalSeconds;

	public ScreenLayout CurrentLayout => Layouts.FirstOrDefault((ScreenLayout l) => l.Name == Settings.Layout) ?? Layouts[0];

	/// <summary>The game profile in effect (its game is running), or null.</summary>
	public GameProfile? ActiveGame { get; private set; }

	/// <summary>
	/// What the screen shows: a running game's layout, else (in auto mode) the layout set for what the PC is doing, else
	/// the chosen one. The editor still edits the chosen one.
	/// </summary>
	public ScreenLayout ShownLayout
	{
		get
		{
			GameProfile? game = ActiveGame;
			ScreenLayout? shown = game == null ? null : Layouts.FirstOrDefault((ScreenLayout l) => l.Name == game.Layout);
			if (shown == null && Settings.Smart.AutoMode)
			{
				string name = Settings.Smart.LayoutFor(CurrentActivity);
				shown = name.Length == 0 ? null : Layouts.FirstOrDefault((ScreenLayout l) => l.Name == name);
			}
			return shown ?? CurrentLayout;
		}
	}

	/// <summary>What the PC is doing (Activity.Gaming, Watching or Working), settled over a few seconds.</summary>
	public string CurrentActivity { get; private set; } = Activity.Working;

	private string _activitySeen = Activity.Working;

	private DateTime _activitySeenAt = DateTime.UtcNow;

	/// <summary>The monitor's two main colours, while the strip or fans follow them.</summary>
	public Color[]? MonitorPalette { get; private set; }

	/// <summary>The colours last sent to the fans, and when.</summary>
	private Color[]? _fanPalette;

	private DateTime _fanPaletteAt = DateTime.MinValue;

	private DateTime _paletteAt = DateTime.MinValue;

	private HealthWatch? _health;

	private readonly TopApp _topApp = new TopApp();

	private DateTime _healthAt = DateTime.MinValue;

	private DateTime _topAt = DateTime.MinValue;

	/// <summary>Warnings from the health watch (dust, a tiring fan); empty when all is well.</summary>
	public List<string> HealthNotes { get; private set; } = new List<string>();

	public string HealthSummary { get; private set; } = "";

	private bool _smartOn;

	/// <summary>The fan lighting effect in force: auto mode's for the activity, or the chosen one.</summary>
	public string FanEffectNow(LightingSettings l)
	{
		if (Settings.Smart.AutoMode)
		{
			string e = Settings.Smart.EffectFor(CurrentActivity);
			if (e.Length > 0 && Lighting.Effects.Contains(e))
			{
				return e;
			}
		}
		return l.FanEffect;
	}

	/// <summary>The settings for that effect, with the monitor's colours when the fans follow them.</summary>
	private EffectSettings FanSettingsNow(LightingSettings l, string effect)
	{
		EffectSettings s = l.For(effect);
		Color[]? p = _fanPalette;
		if (Settings.Smart.FansFollowMonitor && p != null && p.Length >= 2)
		{
			s = new EffectSettings { Speed = s.Speed, Brightness = s.Brightness, Color1 = MonitorColors.Hex(p[0]), Color2 = MonitorColors.Hex(p[1]) };
		}
		return s;
	}

	/// <summary>
	/// Once a second from the sensor loop (full app only): the activity (it must hold 5 s before it counts, so alt-tab
	/// doesn't flip the screen), the monitor's colours every 2 s while something follows them, the busiest app every
	/// 3 s, and the health record once a minute.
	/// </summary>
	private void SmartTick()
	{
		SmartSettings smart = Settings.Smart;
		DateTime now = DateTime.UtcNow;
		string seen = Activity.Detect(Sensors);
		if (seen != _activitySeen)
		{
			_activitySeen = seen;
			_activitySeenAt = now;
		}
		if (seen != CurrentActivity && (now - _activitySeenAt).TotalSeconds >= 5.0)
		{
			string before = CurrentActivity;
			CurrentActivity = seen;
			Sensors.SetText("activity", seen);
			if (smart.AutoMode)
			{
				Log.Write($"auto mode: {before} → {seen}");
				if (smart.EffectFor(before) != smart.EffectFor(seen))
				{
					_lightingDirty = true;
				}
			}
		}
		if ((smart.StripFollowsMonitor || smart.FansFollowMonitor) && (now - _paletteAt).TotalSeconds >= 2.0)
		{
			_paletteAt = now;
			Color[]? p = MonitorColors.Sample();
			if (p != null)
			{
				MonitorPalette = p;
				// the fans hold their loop in memory, so new colours go only when they've clearly changed, at most every 30 s
				if (smart.FansFollowMonitor && (_fanPalette == null || MonitorColors.Distance(p, _fanPalette) >= 25.0) && (now - _fanPaletteAt).TotalSeconds >= 30.0)
				{
					_fanPalette = p;
					_fanPaletteAt = now;
					_lightingDirty = true;
				}
			}
		}
		if ((now - _topAt).TotalSeconds >= 3.0)
		{
			_topAt = now;
			var top = _topApp.Sample();
			if (top.HasValue)
			{
				Sensors.SetText("top.app", top.Value.Name);
				Sensors.Set("top.cpu", top.Value.Percent);
			}
		}
		if (smart.HealthWatch)
		{
			_health ??= HealthWatch.Load();
			_health.Tick(Sensors, DateTime.Now);
			if (HealthSummary.Length == 0 || (now - _healthAt).TotalSeconds >= 60.0)
			{
				_healthAt = now;
				List<string> notes = _health.Notes();
				if (notes.Count != HealthNotes.Count)
				{
					foreach (string n in notes)
					{
						Log.Write("health: " + n);
					}
				}
				HealthNotes = notes;
				HealthSummary = _health.Summary();
			}
		}
	}

	/// <summary>Writes the health record now (on exit).</summary>
	private void SaveHealth()
	{
		_health?.Save();
	}

	private DateTime _gameCheck;

	/// <summary>Every few seconds, whether a profiled game is running: by program name, so it costs a process lookup each.</summary>
	private void GameTick()
	{
		if ((DateTime.Now - _gameCheck).TotalSeconds < 3.0)
		{
			return;
		}
		_gameCheck = DateTime.Now;
		List<GameProfile> profiles;
		lock (Settings)
		{
			profiles = Settings.GameProfiles.Where((GameProfile p) => p.Game.Length > 0 && p.Layout.Length > 0).ToList();
		}
		GameProfile? found = null;
		foreach (GameProfile p in profiles)
		{
			Process[] running = Process.GetProcessesByName(p.Game);
			bool any = running.Length > 0;
			foreach (Process r in running)
			{
				r.Dispose();
			}
			if (any)
			{
				found = p;
				break;
			}
		}
		if (found?.Game != ActiveGame?.Game || found?.Layout != ActiveGame?.Layout)
		{
			Log.Write(found == null ? "game profile: back to the chosen layout" : $"game profile: {found.Game} is running, showing {found.Layout}");
			ActiveGame = found;
		}
	}

	public int EditVersion => Volatile.Read(ref _editVersion);

	public string LightingResult { get; private set; } = "";


	public string CoolingResult { get; private set; } = "";


	public bool Calibrating => _calibrating;

	public event Action? Changed;

	private AutoResetEvent NewWake()
	{
		AutoResetEvent autoResetEvent = new AutoResetEvent(initialState: false);
		lock (_wakes)
		{
			_wakes.Add(autoResetEvent);
			return autoResetEvent;
		}
	}

	public Engine()
	{
		Settings = Store.LoadSettings();
		Layouts.AddRange(Templates.All());
		foreach (ScreenLayout i in Store.LoadLayouts())
		{
			i.BuiltIn = false;
			Layouts.RemoveAll((ScreenLayout x) => x.Name == i.Name);
			Layouts.Add(i);
		}
	}

	public List<ScreenLayout> VisibleLayouts()
	{
		lock (Settings)
		{
			return Layouts.Where((ScreenLayout l) => !l.BuiltIn || !Settings.HiddenTemplates.Contains(l.Name) || l.Name == Settings.Layout).ToList();
		}
	}

	/// <summary>
	/// Takes a layout off the list: a built-in is hidden (Restore removed templates brings it back), one of the
	/// user's own is deleted, and a deleted copy of a built-in keeps the original hidden too. If it was on the screen
	/// the next layout in the list takes its place. Returns false, changing nothing, for the last layout left.
	/// </summary>
	public bool RemoveLayout(string name)
	{
		lock (Settings)
		{
			ScreenLayout? layout = Layouts.FirstOrDefault((ScreenLayout l) => l.Name == name);
			List<ScreenLayout> visible = VisibleLayouts();
			List<ScreenLayout> rest = visible.Where((ScreenLayout l) => l.Name != name).ToList();
			if (layout == null || rest.Count == 0)
			{
				return false;
			}
			if (!layout.BuiltIn)
			{
				Layouts.Remove(layout);
			}
			if ((layout.BuiltIn || Templates.All().Any((ScreenLayout t) => t.Name == name)) && !Settings.HiddenTemplates.Contains(name))
			{
				Settings.HiddenTemplates.Add(name);
			}
			if (Settings.Layout == name)
			{
				int at = Math.Max(0, visible.FindIndex((ScreenLayout l) => l.Name == name));
				Settings.Layout = rest[Math.Min(at, rest.Count - 1)].Name;
			}
		}
		SaveNow();
		return true;
	}

	/// <summary>Brings every removed built-in template back, including originals whose copies were deleted.</summary>
	public void RestoreTemplates()
	{
		lock (Settings)
		{
			Settings.HiddenTemplates.Clear();
			foreach (ScreenLayout t in Templates.All())
			{
				if (!Layouts.Any((ScreenLayout l) => l.Name == t.Name))
				{
					Layouts.Add(t);
				}
			}
		}
		SaveNow();
	}

	public void Start()
	{
		StartSensorsOnly();
		Sensors.StartGameFps();
		_smartOn = true;
		Acquire();
	}

	public void StartSensorsOnly()
	{
		Sensors.Open();
		Thread thread = new Thread(SensorLoop);
		thread.IsBackground = true;
		thread.Name = "sensors";
		thread.Priority = ThreadPriority.BelowNormal;
		thread.Start();
	}

	public static string[] CompetingProcesses()
	{
		try
		{
			return (from p in Process.GetProcesses()
				where p.ProcessName.Contains("L-Connect", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("LConnect", StringComparison.OrdinalIgnoreCase) || ((p.ProcessName.Equals("LumaLi", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("LCDStudio", StringComparison.OrdinalIgnoreCase)) && p.Id != Environment.ProcessId)
				select p.ProcessName).Distinct().ToArray();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	public void Acquire()
	{
		lock (_gate)
		{
			StopWorkers();
			Released = false;
			_run = CancellationTokenSource.CreateLinkedTokenSource(_quit.Token);
			Devices = OrderFans(WinUsb.Enumerate());
			string[] array = CompetingProcesses();
			Warning = ((array.Length != 0) ? (string.Join(", ", array) + " is running and may hold the devices. Close it so LumaLi can take over.") : null);
			// the 8.8" screen, or else a Lancool 207 Digital's front screen, which then shows the same layouts
			UsbDeviceInfo screen = Devices.FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.Screen) ?? Devices.FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.CaseLcd);
			Screen.Present = screen != null;
			Screen.Serial = screen?.Serial ?? "";
			Screen.Kind = screen?.Kind ?? DeviceKind.Screen;
			Screen.Name = Screen.Kind == DeviceKind.CaseLcd ? "Lancool 207 screen (experimental)" : "8.8\" screen";
			Screen.State = ((screen == null) ? "not found" : "starting");
			if (screen != null)
			{
				Spawn("screen", delegate
				{
					ScreenLoop(screen, _run.Token);
				});
			}
			UsbDeviceInfo strip = Devices.FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.ScreenLeds);
			Strip.Present = strip != null;
			Strip.State = ((strip == null) ? "not found" : "starting");
			if (strip != null)
			{
				Spawn("strip", delegate
				{
					StripLoop(strip, _run.Token);
				});
			}
			FanOutputs.Clear();
			foreach (UsbDeviceInfo fan in Devices.Where((UsbDeviceInfo d) => WinUsb.IsFanScreen(d.Kind)))
			{
				OutputStatus status = new OutputStatus
				{
					Name = fan.Name,
					Serial = fan.Serial,
					Kind = fan.Kind,
					Present = true,
					State = "starting"
				};
				Settings.FanFor(fan.Serial);
				FanOutputs.Add(status);
				Spawn(fan.Name, delegate
				{
					FanLoop(fan, status, _run.Token);
				});
			}
			UsbDeviceInfo tx = Devices.FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessTx);
			UsbDeviceInfo rx = Devices.FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.WirelessRx);
			Wireless.Present = tx != null && rx != null;
			Wireless.State = (Wireless.Present ? "starting" : "not found");
			if (tx != null && rx != null)
			{
				Spawn("wireless", delegate
				{
					WirelessLoop(tx, rx, _run.Token);
				});
			}
		}
		this.Changed?.Invoke();
	}

	public void Release(bool exiting = false)
	{
		if (exiting)
		{
			SaveHealth();
		}
		lock (_gate)
		{
			StopWorkers(exiting);
			Released = true;
			foreach (OutputStatus item in AllOutputs())
			{
				item.Running = false;
				item.Fps = 0.0;
				item.State = (item.Present ? "released" : "not found");
			}
		}
		this.Changed?.Invoke();
	}

	public string? RescanIfChanged()
	{
		if (!Monitor.TryEnter(_rescanGate))
		{
			return null;
		}
		try
		{
			if (Released)
			{
				return null;
			}
			List<UsbDeviceInfo> source = WinUsb.Enumerate();
			HashSet<string> before = Devices.Select((UsbDeviceInfo d) => d.Kind.ToString() + ":" + d.Serial).ToHashSet();
			HashSet<string> after = source.Select((UsbDeviceInfo d) => d.Kind.ToString() + ":" + d.Serial).ToHashSet();
			if (before.SetEquals(after))
			{
				return null;
			}
			List<string> list = (from d in source
				where !before.Contains(d.Kind.ToString() + ":" + d.Serial)
				select d.Name).ToList();
			List<string> list2 = (from d in Devices
				where !after.Contains(d.Kind.ToString() + ":" + d.Serial)
				select d.Name).ToList();
			Log.Write($"USB change: added [{string.Join(", ", list)}] removed [{string.Join(", ", list2)}]");
			Acquire();
			List<string> list3 = new List<string>();
			if (list.Count > 0)
			{
				list3.Add("Found " + string.Join(", ", list));
			}
			if (list2.Count > 0)
			{
				list3.Add("Disconnected " + string.Join(", ", list2));
			}
			return string.Join(" · ", list3);
		}
		finally
		{
			Monitor.Exit(_rescanGate);
		}
	}

	public void Suspend()
	{
		_resumeAfterWake = !Released;
		Farewell();
		Log.Write("system suspending: releasing devices");
		Release();
	}

	public void Resume()
	{
		_farewell = false;
		_farewellSent = false;
		if (!_resumeAfterWake)
		{
			return;
		}
		List<DeviceKind> before = Devices.Select((UsbDeviceInfo d) => d.Kind).ToList();
		for (int i = 0; i < 15; i++)
		{
			Thread.Sleep(2000);
			List<DeviceKind> now = (from d in WinUsb.Enumerate()
				select d.Kind).ToList();
			if (before.All((DeviceKind k) => now.Count((DeviceKind x) => x == k) >= before.Count((DeviceKind x) => x == k)))
			{
				break;
			}
		}
		Log.Write("system resumed: reconnecting devices");
		Acquire();
	}

	public IEnumerable<OutputStatus> AllOutputs()
	{
		return new OutputStatus[3] { Screen, Strip, Wireless }.Concat(FanOutputs);
	}

	private void Spawn(string name, Action body)
	{
		Action body2 = body;
		string name2 = name;
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				body2();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception value)
			{
				Log.Write($"{name2} worker stopped: {value}");
			}
		})
		{
			IsBackground = true,
			Name = name2
		};
		_threads.Add(thread);
		thread.Start();
	}

	private void StopWorkers(bool exiting = false)
	{
		_exiting = exiting;
		_run.Cancel();
		Poke();
		foreach (Thread thread in _threads)
		{
			thread.Join(4000);
		}
		_threads.Clear();
	}

	public void Poke()
	{
		lock (_wakes)
		{
			foreach (AutoResetEvent wake in _wakes)
			{
				wake.Set();
			}
		}
	}

	/// <summary>Fan screens in the owner's order, named Fan 1, Fan 2… in that order.</summary>
	private List<UsbDeviceInfo> OrderFans(List<UsbDeviceInfo> devices)
	{
		List<string> order;
		lock (Settings)
		{
			order = Settings.FanOrder.ToList();
		}
		int Rank(UsbDeviceInfo d)
		{
			int i = order.FindIndex((string s) => s.Equals(d.Serial, StringComparison.OrdinalIgnoreCase));
			return i < 0 ? int.MaxValue : i;
		}
		List<UsbDeviceInfo> fans = devices.Where((UsbDeviceInfo d) => WinUsb.IsFanScreen(d.Kind)).OrderBy(Rank).ToList();
		var result = devices.Where((UsbDeviceInfo d) => !WinUsb.IsFanScreen(d.Kind)).ToList();
		result.AddRange(fans.Select((UsbDeviceInfo d, int i) => d with { Name = WinUsb.FanName(d.Kind, i + 1) }));
		return result;
	}

	/// <summary>Puts a fan screen at a position (0 is Fan 1) and renumbers the fans, keeping each fan's look.</summary>
	public void MoveFan(string serial, int position)
	{
		lock (Settings)
		{
			List<string> serials = FanOutputs.Count > 0 ? FanOutputs.Select((OutputStatus f) => f.Serial).ToList() : Settings.Fans.Select((FanScreen f) => f.Serial).ToList();
			int at = serials.FindIndex((string s) => s.Equals(serial, StringComparison.OrdinalIgnoreCase));
			if (at < 0)
			{
				return;
			}
			serials.RemoveAt(at);
			serials.Insert(Math.Clamp(position, 0, serials.Count), serial);
			Settings.FanOrder = serials;
			// keeps Settings.Fans in the same order, for when no fan is connected
			Settings.Fans = Settings.Fans.OrderBy((FanScreen f) =>
			{
				int i = serials.FindIndex((string s) => s.Equals(f.Serial, StringComparison.OrdinalIgnoreCase));
				return i < 0 ? int.MaxValue : i;
			}).ToList();
			List<OutputStatus> sorted = FanOutputs.OrderBy((OutputStatus f) => serials.FindIndex((string s) => s.Equals(f.Serial, StringComparison.OrdinalIgnoreCase))).ToList();
			FanOutputs.Clear();
			for (int i = 0; i < sorted.Count; i++)
			{
				sorted[i].Name = $"Fan {i + 1}";
				FanOutputs.Add(sorted[i]);
			}
			Devices = OrderFans(Devices);
		}
		SaveSoon();
		Notify();
	}

	private string? _rgbIdentify;

	private DateTime _rgbIdentifyUntil;

	/// <summary>Lights each fan of a wireless group in its own colour for a few seconds, to work out their order.</summary>
	public void IdentifyGroup(string groupId, TimeSpan duration)
	{
		_rgbIdentify = groupId;
		_rgbIdentifyUntil = DateTime.Now + duration;
		_lightingDirty = true;
	}

	private void IdentifyTick()
	{
		if (_rgbIdentify != null && DateTime.Now > _rgbIdentifyUntil)
		{
			_rgbIdentify = null;
			_lightingDirty = true;
		}
	}

	public void LightingChanged()
	{
		lock (Settings)
		{
			Settings.Lighting.Applied = true;
		}
		// the reading is looked at afresh, so a new sensor or level takes effect at once
		_reactEase = double.NaN;
		_lightingDirty = true;
		SaveSoon();
	}

	/// <summary>How many steps toward hot the fan lights stand at while they follow a reading (0 = calm).</summary>
	public int ReactStep => _reactStep;

	private int _reactStep;

	private double _reactEase = double.NaN;

	private DateTime _reactAt = DateTime.MinValue;

	/// <summary>The fewest seconds between two uploads the reading asks for, so the fans are never sent loop after loop.</summary>
	private const double ReactGap = 20.0;

	/// <summary>
	/// Once a second while the fan lights follow a reading: the reading eased over several seconds, so a brief spike
	/// does nothing, and the lights moved one step at a time toward it, only once it is well past the step's middle (so
	/// it does not flicker between two) and at least ReactGap seconds after the last move. A step marks the lighting for
	/// upload.
	/// </summary>
	private void ReactTick()
	{
		LightingSettings l = Settings.Lighting;
		if (!l.React || !l.FansOn || !l.Applied)
		{
			if (_reactStep != 0)
			{
				_reactStep = 0;
				_lightingDirty = true;
			}
			_reactEase = double.NaN;
			return;
		}
		float? v = Sensors.Get(l.ReactSensor);
		if (!v.HasValue)
		{
			return;
		}
		double share = Lighting.ReactShare(l, v.Value);
		if (double.IsNaN(_reactEase))
		{
			// starting out, or after a change of settings: straight to where the reading stands
			_reactEase = share;
			int start = (int)Math.Round(share * Lighting.ReactSteps);
			if (start != _reactStep)
			{
				_reactStep = start;
				_reactAt = DateTime.Now;
				_lightingDirty = true;
			}
			return;
		}
		_reactEase += (share - _reactEase) * 0.15;
		double at = _reactEase * Lighting.ReactSteps;
		int move = at > _reactStep + 0.6 ? 1 : at < _reactStep - 0.6 ? -1 : 0;
		if (move != 0 && (DateTime.Now - _reactAt).TotalSeconds >= ReactGap)
		{
			_reactStep = Math.Clamp(_reactStep + move, 0, Lighting.ReactSteps);
			_reactAt = DateTime.Now;
			_lightingDirty = true;
		}
	}

	public void SaveSoon()
	{
		Interlocked.Increment(ref _editVersion);
		if (_saveTimer == null)
		{
			_saveTimer = new Timer(delegate
			{
				SaveNow();
			});
		}
		_saveTimer.Change(400, -1);
		Poke();
	}

	public void SaveNow()
	{
		try
		{
			lock (Settings)
			{
				Store.Save(Settings);
			}
			Store.SaveLayouts(Layouts);
		}
		catch (Exception ex)
		{
			Log.Write("save failed: " + ex.Message);
		}
	}

	public void Identify(TimeSpan duration)
	{
		lock (_identifyUntil)
		{
			foreach (OutputStatus fanOutput in FanOutputs)
			{
				_identifyUntil[fanOutput.Serial] = Time + duration.TotalSeconds;
			}
		}
		Poke();
	}

	private void Notify()
	{
		string text;
		lock (_gate)
		{
			text = string.Join("|", from o in AllOutputs()
				select $"{o.Name}:{o.Present}:{o.Running}:{(o.State.StartsWith("error") ? o.State : o.State.Split('·')[0].Trim())}") + "|" + string.Join(",", Hub?.Groups.Select((FanGroup g) => g.Id) ?? Enumerable.Empty<string>()) + "|" + Warning + "|" + Released;
		}
		if (!(text == _lastSignature))
		{
			_lastSignature = text;
			this.Changed?.Invoke();
		}
	}

	/// <summary>Milliseconds a run of screen frames spent at each step, for the log.</summary>
	private sealed class ScreenTiming
	{
		public double Render, Encode, Waited, Send, Slept;
		public int Frames;
	}

	private static void Wait(CancellationToken ct, int ms, WaitHandle? wake = null)
	{
		if (ms <= 0)
		{
			ct.ThrowIfCancellationRequested();
			return;
		}
		if (wake != null)
		{
			WaitHandle.WaitAny(new WaitHandle[2] { ct.WaitHandle, wake }, ms);
		}
		else
		{
			ct.WaitHandle.WaitOne(ms);
		}
		ct.ThrowIfCancellationRequested();
	}

	private void SensorLoop()
	{
		while (!_quit.IsCancellationRequested)
		{
			try
			{
				Sensors.Update();
				WirelessHub hub = Hub;
				if (hub != null)
				{
					List<FanGroup> groups = hub.Groups;
					for (int i = 0; i < 4; i++)
					{
						if (i < groups.Count)
						{
							FanGroup fanGroup = groups[i];
							Sensors.Set($"group{i + 1}.rpm", fanGroup.AverageRpm);
							Sensors.Set($"group{i + 1}.duty", fanGroup.DutyPercent);
							Sensors.Set($"group{i + 1}.fans", (int)fanGroup.FanCount);
							Sensors.SetText($"group{i + 1}.name", GroupName(fanGroup, i));
						}
					}
					if (groups.Count > 0)
					{
						Sensors.Set("fans.rpm", (float)groups.Average((FanGroup g) => g.AverageRpm));
						Sensors.Set("fans.duty", (float)groups.Average((FanGroup g) => g.DutyPercent));
					}
				}
			}
			catch (Exception ex)
			{
				Log.Write("sensor update: " + ex.Message);
			}
			if (_smartOn)
			{
				try
				{
					SmartTick();
				}
				catch (Exception ex)
				{
					Log.Write("smart: " + ex.Message);
				}
			}
			Sensors.Bump();
			_quit.Token.WaitHandle.WaitOne(1000);
		}
	}

	public string GroupName(FanGroup g, int index)
	{
		lock (Settings)
		{
			string value;
			return (Settings.Cooling.GroupNames.TryGetValue(g.Id, out value) && !string.IsNullOrWhiteSpace(value)) ? value : $"Group {index + 1}";
		}
	}

	private void ScreenLoop(UsbDeviceInfo info, CancellationToken ct)
	{
		using ScreenRenderer screenRenderer = new ScreenRenderer(Sensors);
		AutoResetEvent wake = NewWake();
		LcdLink lcdLink = null;
		FpsCounter fpsCounter = new FpsCounter();
		int num = 500;
		string text = null;
		byte[] array = null;
		DateTime dateTime = DateTime.MinValue;
		// the screen takes about 30 ms to receive a frame over USB, so each frame goes on its way in the background while
		// the next is drawn and encoded; done one after the other the two would hold the screen near 20 fps
		Task? sending = null;
		ScreenTiming timing = new ScreenTiming();
		using PreciseWait? pause = PreciseWait.Create();
		void Sent()
		{
			Task? s = sending;
			sending = null;
			s?.GetAwaiter().GetResult();
		}
		void Drain()
		{
			try
			{
				Sent();
			}
			catch
			{
			}
		}
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				if (!Settings.ScreenOn)
				{
					Drain();
					Screen.Running = false;
					Screen.State = "off";
					Screen.Fps = 0.0;
					if (lcdLink != null)
					{
						lcdLink.Dispose();
						lcdLink = null;
					}
					Notify();
					Wait(ct, 1000, wake);
					continue;
				}
				DateTime utcNow = DateTime.UtcNow;
				bool standby = false;
				try
				{
					if (lcdLink == null)
					{
						lcdLink = new LcdLink(info);
					}
					ScreenLayout screenLayout;
					standby = StandbyNow();
					GameTick();
					lock (Settings)
					{
						screenLayout = standby ? StandbyLayout() : ShownLayout.Clone();
					}
					int brightness = standby ? Math.Min(Settings.ScreenBrightness, Settings.Standby.Brightness) : Settings.ScreenBrightness;
					bool flag = ScreenRenderer.IsAnimated(screenLayout);
					string text2 = (flag ? null : string.Join("|", Sensors.Version, EditVersion, screenLayout.Name, DateTime.Now.ToString(ScreenRenderer.ShowsSeconds(screenLayout) ? "HHmmss" : "HHmm"), Settings.Flip, Settings.ScreenQuality, brightness));
					if (text2 != null && text2 == text && array != null && (DateTime.UtcNow - dateTime).TotalSeconds < 2.0)
					{
						Wait(ct, 100, wake);
						continue;
					}
					byte[] array2;
					if (text2 != null && text2 == text && array != null)
					{
						array2 = array;
					}
					else
					{
						long r0 = Stopwatch.GetTimestamp();
						using Bitmap bitmap = screenRenderer.Render(screenLayout, Time);
						long r1 = Stopwatch.GetTimestamp();
						// the light strip, when it matches the screen, takes its colours from this frame as drawn
						if (Settings.Lighting.StripMatchesScreen && Strip.Present)
						{
							if (screenLayout.Portrait)
							{
								// the strip runs along the screen as it sits in landscape, so it reads a vertical frame turned back
								using Bitmap wide = (Bitmap)bitmap.Clone();
								wide.RotateFlip(Settings.Flip ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
								_screenGlow = Lighting.FromScreen(wide, StripLeds);
							}
							else
							{
								_screenGlow = Lighting.FromScreen(bitmap, StripLeds);
							}
						}
						// a Lancool 207's 720 × 1472 panel: the layout is fitted whole (see CaseFrame)
						if (info.Kind == DeviceKind.CaseLcd)
						{
							using Bitmap fitted = CaseFrame(bitmap, screenLayout.Portrait, Settings.Flip);
							array2 = Imaging.Jpeg(fitted, Settings.ScreenQuality);
						}
						else
						{
						// the panel itself is 480 × 1920: a wide frame turns a quarter, a vertical one is already upright
						if (screenLayout.Portrait)
						{
							if (Settings.Flip)
							{
								bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
							}
						}
						else
						{
							bitmap.RotateFlip((!Settings.Flip) ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
						}
						array2 = Imaging.Jpeg(bitmap, Settings.ScreenQuality);
						}
						timing.Render += Stopwatch.GetElapsedTime(r0, r1).TotalMilliseconds;
						timing.Encode += Stopwatch.GetElapsedTime(r1).TotalMilliseconds;
					}
					long w0 = Stopwatch.GetTimestamp();
					Sent();
					timing.Waited += Stopwatch.GetElapsedTime(w0).TotalMilliseconds;
					LcdLink link = lcdLink;
					byte[] frame = array2;
					int level = brightness;
					sending = Task.Run(() =>
					{
						long s0 = Stopwatch.GetTimestamp();
						link.SendFrame(frame, level);
						timing.Send += Stopwatch.GetElapsedTime(s0).TotalMilliseconds;
					});
					// going to sleep or shutting down: the standby face is on the screen; start its own clock if asked, then signal
					if (_farewell && standby && !_farewellSent)
					{
						Sent();
						if (Settings.Standby.FirmwareClock)
						{
							try
							{
								lcdLink.Clock(on: true);
							}
							catch (Exception ex2)
							{
								Log.Write("screen clock: " + ex2.Message);
							}
						}
						_farewellSent = true;
						_farewellDone.Set();
					}
					text = text2;
					array = array2;
					dateTime = DateTime.UtcNow;
					Screen.Firmware = lcdLink.Firmware;
					Screen.Running = true;
					Screen.Fps = fpsCounter.Tick();
					Screen.State = (flag ? $"connected · {Screen.Fps:0} fps" : "connected · redraws when values change");
					num = 500;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Drain();
					Screen.Running = false;
					Screen.Fps = 0.0;
					Screen.State = "error: " + Short(ex);
					Log.Write("screen: " + ex.Message);
					lcdLink?.Dispose();
					lcdLink = null;
					Notify();
					Wait(ct, num);
					num = Math.Min(num * 2, 10000);
					continue;
				}
				// standby needs only a few frames a second
				double num2 = 1000.0 / Math.Clamp(standby ? Math.Min(Settings.ScreenFps, 8) : Settings.ScreenFps, 1, 30);
				long z0 = Stopwatch.GetTimestamp();
				double rest = num2 - (DateTime.UtcNow - utcNow).TotalMilliseconds;
				if (pause != null)
				{
					pause.Wait(ct, rest);
				}
				else
				{
					Wait(ct, (int)rest);
				}
				timing.Slept += Stopwatch.GetElapsedTime(z0).TotalMilliseconds;
				// every five minutes or so, where a frame's time goes, for when the screen falls short of its frame rate
				if (++timing.Frames == 9000)
				{
					Log.Write($"screen timing per frame: render {timing.Render / 9000:0.0} ms, encode {timing.Encode / 9000:0.0}, waiting on USB {timing.Waited / 9000:0.0}, send {timing.Send / 9000:0.0}, sleep {timing.Slept / 9000:0.0}; {Screen.Fps:0.0} fps");
					timing = new ScreenTiming();
				}
			}
		}
		finally
		{
			Drain();
			// after a farewell the screen keeps the standby face (or its own clock): let go without stopping it
			if (_farewellSent)
			{
				lcdLink?.Leave();
			}
			else
			{
				lcdLink?.Dispose();
			}
			Screen.Running = false;
		}
	}

	private volatile bool _locked, _farewell, _farewellSent;

	private readonly ManualResetEventSlim _farewellDone = new ManualResetEventSlim(false);

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct LastInputInfo
	{
		public uint Size;

		public uint Time;
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool GetLastInputInfo(ref LastInputInfo info);

	/// <summary>Seconds since the last mouse or keyboard input anywhere in the session.</summary>
	public static double IdleSeconds()
	{
		var info = new LastInputInfo { Size = 8 };
		return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000.0 : 0.0;
	}

	/// <summary>Called by the window when the session is locked or unlocked.</summary>
	public void SetLocked(bool locked)
	{
		_locked = locked;
		Poke();
	}

	/// <summary>Whether the 8.8" screen should be showing its standby face right now.</summary>
	public bool StandbyNow()
	{
		StandbySettings s = Settings.Standby;
		if (!s.Enabled)
		{
			return false;
		}
		return _farewell || (s.WhenLocked && _locked) || (s.IdleMinutes > 0 && IdleSeconds() >= s.IdleMinutes * 60.0);
	}

	/// <summary>The standby layout, a copy with the chosen logo in its panel.</summary>
	private ScreenLayout StandbyLayout()
	{
		string name = Settings.Standby.Layout;
		ScreenLayout layout = (Layouts.FirstOrDefault((ScreenLayout l) => l.Name == name) ?? Templates.All().First((ScreenLayout l) => l.Name == "Standby")).Clone();
		foreach (Widget w in layout.Widgets.Where((Widget w) => w.Effect == "standby"))
		{
			w.Image = string.IsNullOrEmpty(Settings.Standby.Logo) ? null : Settings.Standby.Logo;
		}
		return layout;
	}

	/// <summary>
	/// Puts the standby face on the screen before the PC sleeps, hibernates or shuts down, and waits up to two seconds
	/// for it to be sent, so it is what stays on the screen while it still has power.
	/// </summary>
	public void Farewell()
	{
		if (!Settings.Standby.Enabled || !Settings.Standby.BeforeSleep || !Screen.Running)
		{
			return;
		}
		_farewellSent = false;
		_farewellDone.Reset();
		_farewell = true;
		Poke();
		_farewellDone.Wait(2000);
	}

	private static string Short(Exception e)
	{
		string message = e.Message;
		if (e is Win32Exception { NativeErrorCode: 121 } || message.Contains("did not answer"))
		{
			return "not responding — use Settings › Restart stuck devices, or replug it";
		}
		if (message.Length <= 90)
		{
			return message;
		}
		return message.Substring(0, 90) + "…";
	}

	private void FanLoop(UsbDeviceInfo info, OutputStatus status, CancellationToken ct)
	{
		using Faces faces = new Faces();
		AutoResetEvent wake = NewWake();
		IFanLink? lcdLink = null;
		FpsCounter fpsCounter = new FpsCounter();
		int num = 500;
		string text = null;
		string text2 = null;
		DateTime dateTime = DateTime.MinValue;
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				FanScreen fanScreen;
				lock (Settings)
				{
					fanScreen = Settings.FanFor(info.Serial);
				}
				if (!Settings.FanScreensOn || !fanScreen.Enabled)
				{
					status.Running = false;
					status.State = "off";
					status.Fps = 0.0;
					if (lcdLink != null)
					{
						lcdLink.Dispose();
						lcdLink = null;
					}
					Notify();
					Wait(ct, 1000, wake);
					continue;
				}
				DateTime utcNow = DateTime.UtcNow;
				try
				{
					if (text != fanScreen.Background)
					{
						faces.Forget(text);
						text = fanScreen.Background;
					}
					if (lcdLink == null)
					{
						lcdLink = info.Kind == DeviceKind.TlLcd ? new TlLcdLink(info) : new LcdLink(info);
					}
					bool flag;
					lock (_identifyUntil)
					{
						flag = _identifyUntil.TryGetValue(info.Serial, out var value) && value > Time;
					}
					string text3 = null;
					Bitmap bitmap;
					if (flag)
					{
						bitmap = IdentifyFrame(status.Name);
						goto IL_02b0;
					}
					FanScreen fanScreen2;
					lock (Settings)
					{
						// every setting the face draws with (playlist, gauge style, warning level too), copied so the
						// card can change them while this frame draws
						fanScreen2 = fanScreen.Snapshot();
					}
					if (Faces.IsAnimated(fanScreen2))
					{
						goto IL_028e;
					}
					text3 = string.Join("|", Sensors.Version, EditVersion, DateTime.Now.ToString((fanScreen2.Face == "Clock") ? "HHmmss" : "HHmm"), Settings.FanBrightness);
					if (!(text3 == text2) || !((DateTime.UtcNow - dateTime).TotalSeconds < 2.0))
					{
						goto IL_028e;
					}
					Wait(ct, 100, wake);
					goto end_IL_00c5;
					IL_028e:
					bitmap = faces.Render(fanScreen2, Sensors.Get(fanScreen2.Sensor), Time);
					goto IL_02b0;
					IL_02b0:
					text2 = text3;
					dateTime = DateTime.UtcNow;
					byte[] jpeg = Imaging.Jpeg(bitmap, 85);
					lcdLink.SendFrame(jpeg, Settings.FanBrightness);
					Bitmap preview;
					lock (status)
					{
						preview = status.Preview;
						status.Preview = bitmap;
					}
					preview?.Dispose();
					status.Firmware = lcdLink.Firmware;
					status.Running = true;
					status.Fps = fpsCounter.Tick();
					status.State = "connected";
					num = 500;
					goto IL_03e0;
					end_IL_00c5:;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					status.Running = false;
					status.Fps = 0.0;
					status.State = "error: " + Short(ex);
					Log.Write(info.Name + ": " + ex.Message);
					lcdLink?.Dispose();
					lcdLink = null;
					Notify();
					Wait(ct, num);
					num = Math.Min(num * 2, 10000);
				}
				continue;
				IL_03e0:
				int num2 = 1000 / Math.Clamp(Settings.FanFps, 1, 30);
				Wait(ct, num2 - (int)(DateTime.UtcNow - utcNow).TotalMilliseconds);
			}
		}
		finally
		{
			lcdLink?.Dispose();
			status.Running = false;
		}
	}

	private static Bitmap IdentifyFrame(string name)
	{
		Bitmap bitmap = new Bitmap(400, 400);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.Clear(Color.FromArgb(255, 200, 0));
		using Font font = new Font("Segoe UI Black", 170f, FontStyle.Bold, GraphicsUnit.Pixel);
		using StringFormat format = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};
		graphics.DrawString(new string(name.Where(char.IsDigit).ToArray()), font, Brushes.Black, new RectangleF(0f, 0f, 400f, 400f), format);
		return bitmap;
	}

	private const int StripLeds = 60;

	/// <summary>The light strip's colours from the latest screen frame, set by the screen loop while the strip matches it.</summary>
	private volatile (byte R, byte G, byte B)[]? _screenGlow;

	private void StripLoop(UsbDeviceInfo info, CancellationToken ct)
	{
		LedStrip ledStrip = null;
		// what the strip shows while matching the screen, eased toward each new frame so it glides rather than steps
		var shown = new float[StripLeds * 3];
		FpsCounter fpsCounter = new FpsCounter();
		AutoResetEvent autoResetEvent = NewWake();
		// a steady 30 frames a second: each frame waits out what is left of its 33 ms, to the millisecond, where a plain
		// wait would round up to the system's 15.6 ms tick and move the light in uneven steps
		using PreciseWait? pause = PreciseWait.Create();
		long started = 0;
		void NextFrame()
		{
			double rest = 1000.0 / 30.0 - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			if (pause != null)
			{
				pause.Wait(ct, rest);
			}
			else
			{
				Wait(ct, (int)rest);
			}
		}
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				started = Stopwatch.GetTimestamp();
				LightingSettings lighting = Settings.Lighting;
				try
				{
					if (ledStrip == null)
					{
						ledStrip = new LedStrip(info);
					}
					string text;
					EffectSettings effectSettings;
					lock (Settings)
					{
						bool flag = lighting.StripFollowsFans && lighting.FansOn;
						string fanEffect = FanEffectNow(lighting);
						text = ((!lighting.StripOn) ? "Off" : (flag ? fanEffect : lighting.StripEffect));
						// following the fans, the strip warms with them, step for step
						effectSettings = (flag ? Lighting.Warmed(FanSettingsNow(lighting, fanEffect), lighting, lighting.React ? _reactStep : 0) : lighting.Strip);
					}
					Color[]? monitor = MonitorPalette;
					if (lighting.StripOn && Settings.Smart.StripFollowsMonitor && monitor != null && monitor.Length >= 2)
					{
						// the monitor's two colours flowing slowly round the strip, easing over a second or two as they change
						double level = Math.Clamp(lighting.Strip.Brightness, 0, 100) / 100.0;
						var frame = new (byte R, byte G, byte B)[StripLeds];
						double drift = Time * 0.05;
						for (int i = 0; i < StripLeds; i++)
						{
							double k = 0.5 + 0.5 * Math.Sin((i / (double)StripLeds + drift) * Math.PI * 2.0);
							Color a = monitor[0], b = monitor[1];
							float r = (float)(a.R + (b.R - a.R) * k), gg = (float)(a.G + (b.G - a.G) * k), bb = (float)(a.B + (b.B - a.B) * k);
							shown[i * 3] += (r - shown[i * 3]) * 0.08f;
							shown[i * 3 + 1] += (gg - shown[i * 3 + 1]) * 0.08f;
							shown[i * 3 + 2] += (bb - shown[i * 3 + 2]) * 0.08f;
							frame[i] = ((byte)(shown[i * 3] * level), (byte)(shown[i * 3 + 1] * level), (byte)(shown[i * 3 + 2] * level));
						}
						ledStrip.Send(frame);
						Strip.Running = true;
						Strip.Fps = fpsCounter.Tick();
						Strip.State = "following the monitor's colours";
						NextFrame();
						continue;
					}
					(byte R, byte G, byte B)[]? glow = _screenGlow;
					if (lighting.StripOn && lighting.StripMatchesScreen && glow != null)
					{
						double level = Math.Clamp(lighting.Strip.Brightness, 0, 100) / 100.0;
						var frame = new (byte R, byte G, byte B)[StripLeds];
						for (int i = 0; i < StripLeds; i++)
						{
							var c = glow[lighting.StripMatchReversed ? StripLeds - 1 - i : i];
							shown[i * 3] += (c.R - shown[i * 3]) * 0.3f;
							shown[i * 3 + 1] += (c.G - shown[i * 3 + 1]) * 0.3f;
							shown[i * 3 + 2] += (c.B - shown[i * 3 + 2]) * 0.3f;
							frame[i] = ((byte)(shown[i * 3] * level), (byte)(shown[i * 3 + 1] * level), (byte)(shown[i * 3 + 2] * level));
						}
						ledStrip.Send(frame);
						Strip.Running = true;
						Strip.Fps = fpsCounter.Tick();
						Strip.State = "matching the screen layout";
						NextFrame();
						continue;
					}
					if (!Settings.Lighting.Applied)
					{
						Strip.State = lighting.StripMatchesScreen ? "waiting for the screen" : "waiting (choose lighting to start)";
						Wait(ct, 1000, autoResetEvent);
						continue;
					}
					double phase = Time / Lighting.LoopSeconds(effectSettings.Speed) % 1.0;
					ledStrip.Send(StripScenes.Has(text) ? StripScenes.Frame(text, effectSettings, StripLeds, Time) : Lighting.Frame(text, effectSettings, 1, 60, phase));
					Strip.Running = true;
					Strip.Fps = fpsCounter.Tick();
					Strip.State = ((text == "Off") ? "off" : (text ?? ""));
					if (Lighting.IsAnimated(text))
					{
						NextFrame();
					}
					else
					{
						Wait(ct, 1000, autoResetEvent);
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Strip.Running = false;
					Strip.State = "error: " + Short(ex);
					Log.Write("strip: " + ex.Message);
					ledStrip?.Dispose();
					ledStrip = null;
					Notify();
					Wait(ct, 3000);
				}
			}
		}
		finally
		{
			ledStrip?.Dispose();
			Strip.Running = false;
		}
	}

	private void WirelessLoop(UsbDeviceInfo tx, UsbDeviceInfo rx, CancellationToken ct)
	{
		WirelessHub wirelessHub = null;
		DateTime dateTime = default(DateTime);
		string lastGroupIds = "";
		AutoResetEvent wake = NewWake();
		int num = 1000;
		int num2 = 0;
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					if (wirelessHub == null)
					{
						wirelessHub = (Hub = new WirelessHub(tx, rx));
						_lightingDirty = true;
						Notify();
					}
					if ((DateTime.Now - dateTime).TotalSeconds >= 2.0)
					{
						try
						{
							wirelessHub.Refresh();
							num2 = 0;
						}
						catch (IOException) when (++num2 < 5)
						{
						}
						dateTime = DateTime.Now;
						string text = string.Join(",", from g in wirelessHub.Groups
							select g.Id into x
							orderby x
							select x);
						if (lastGroupIds.Length > 0 && text != lastGroupIds && wirelessHub.Groups.Any((FanGroup g) => !lastGroupIds.Contains(g.Id)))
						{
							Log.Write("new wireless group(s): " + text);
							_lightingDirty = true;
						}
						lastGroupIds = text;
					}
					ReactTick();
					IdentifyTick();
					if (_lightingDirty && (Settings.Lighting.Applied || _rgbIdentify != null))
					{
						_lightingDirty = false;
						ApplyLighting(wirelessHub);
					}
					ApplyCooling(wirelessHub);
					Wireless.Running = true;
					Wireless.State = $"{wirelessHub.Groups.Count} groups · " + string.Join(" · ", wirelessHub.Groups.Select((FanGroup g) => $"{g.AverageRpm} rpm"));
					num = 1000;
					Notify();
				}
				catch (Exception ex2) when (!(ex2 is OperationCanceledException))
				{
					Wireless.Running = false;
					Wireless.State = "error: " + Short(ex2);
					Log.Write("wireless: " + ex2.Message);
					Hub = null;
					wirelessHub?.Dispose();
					wirelessHub = null;
					Notify();
					Wait(ct, num);
					num = Math.Min(num * 2, 15000);
					continue;
				}
				Wait(ct, 1000, wake);
			}
		}
		finally
		{
			if (wirelessHub != null)
			{
				if (_exiting && Settings.Cooling.Mode != "Hold")
				{
					foreach (FanGroup group in wirelessHub.Groups)
					{
						try
						{
							wirelessHub.SetSpeed(group, Settings.Cooling.ExitPercent);
						}
						catch
						{
						}
					}
				}
				Hub = null;
				wirelessHub.Dispose();
			}
			Wireless.Running = false;
		}
	}

	private void ApplyLighting(WirelessHub hub)
	{
		string text;
		EffectSettings s;
		lock (Settings)
		{
			LightingSettings lighting = Settings.Lighting;
			string effect = FanEffectNow(lighting);
			text = (lighting.FansOn ? effect : "Off");
			EffectSettings effectSettings = FanSettingsNow(lighting, effect);
			s = Lighting.Warmed(effectSettings, lighting, lighting.React ? _reactStep : 0);
		}
		string warmth = Settings.Lighting.React && text != "Off" ? $" · warmth {_reactStep}/{Lighting.ReactSteps}" : "";
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<FanGroup> list = hub.Groups.ToList();
		string?[] status = new string[list.Count];
		Dictionary<int, (byte[], List<(byte, byte, byte)[]>, int)> dictionary = new Dictionary<int, (byte[], List<(byte, byte, byte)[]>, int)>();
		for (int i = 0; i < list.Count; i++)
		{
			try
			{
				(List<(byte R, byte G, byte B)[]> Frames, int Ticks) tuple = Lighting.FanLoop(text, s, list[i].FanCount, list[i].LedsPerFan);
				string? orderText;
				lock (Settings)
				{
					Settings.Lighting.GroupOrders.TryGetValue(list[i].Id, out orderText);
				}
				List<(byte, byte, byte)[]> item = Lighting.Reorder(tuple.Frames, list[i].LedsPerFan, Lighting.ParseOrder(orderText, list[i].FanCount));
				int item2 = tuple.Ticks;
				if (_rgbIdentify == list[i].Id)
				{
					item = Lighting.IdentifyFrame(list[i].FanCount, list[i].LedsPerFan);
					item2 = 8000;
				}
				dictionary[i] = (Array.Empty<byte>(), item, item2);
			}
			catch (Exception ex)
			{
				status[i] = ex.Message;
			}
		}
		for (int j = 1; j <= 3; j++)
		{
			if (dictionary.Count <= 0)
			{
				break;
			}
			foreach (int item3 in dictionary.Keys.ToList())
			{
				try
				{
					(byte[], List<(byte, byte, byte)[]>, int) tuple2 = dictionary[item3];
					dictionary[item3] = (hub.UploadLighting(list[item3], tuple2.Item2, tuple2.Item3), tuple2.Item2, tuple2.Item3);
				}
				catch (Exception ex2)
				{
					status[item3] = ex2.Message;
					dictionary.Remove(item3);
				}
			}
			ApplyCooling(hub);
			for (int k = 0; k < 8 && dictionary.Count > 0; k++)
			{
				Thread.Sleep(200);
				try
				{
					hub.Refresh();
				}
				catch (IOException)
				{
					continue;
				}
				foreach (int item4 in dictionary.Keys.ToList())
				{
					if (hub.Applied(list[item4], dictionary[item4].Item1))
					{
						status[item4] = "applied";
						dictionary.Remove(item4);
					}
				}
			}
		}
		foreach (int key in dictionary.Keys)
		{
			status[key] = "not confirmed";
		}
		List<string> values = list.Select((FanGroup g, int gi) => GroupName(g, gi) + ": " + status[gi]).ToList();
		LightingResult = text + warmth + " → " + string.Join(", ", values) + $" ({stopwatch.Elapsed.TotalSeconds:0.0} s)";
		Log.Write("lighting " + LightingResult);
	}

	public double? TargetPercent(out string why)
	{
		CoolingSettings cooling = Settings.Cooling;
		int num = CoolingSettings.PresetPercent(cooling.Mode, cooling.CustomPercent);
		if (cooling.Mode == "Hold")
		{
			why = "Hold: fan speed is left as it is";
			return null;
		}
		if (num >= 0)
		{
			why = $"{cooling.Mode}: {num}%";
			return num;
		}
		float? num2 = Sensors.Get(cooling.AutoSensor);
		double num3 = Sensors.Age(cooling.AutoSensor);
		if (num2.HasValue)
		{
			float valueOrDefault = num2.GetValueOrDefault();
			if (!(num3 > 5.0))
			{
				double num4 = Math.Clamp((double)(valueOrDefault - (float)cooling.StartAt) / Math.Max(1.0, cooling.FullAt - cooling.StartAt), 0.0, 1.0);
				double value = (double)cooling.MinPercent + (double)(100 - cooling.MinPercent) * num4;
				why = $"Automatic: {LcdStudio.Core.Sensors.Def(cooling.AutoSensor).Label} {valueOrDefault:0}° → {value:0}%";
				return value;
			}
		}
		why = LcdStudio.Core.Sensors.Def(cooling.AutoSensor).Label + " unavailable — fans at 100 % for safety";
		return 100.0;
	}

	private void ApplyCooling(WirelessHub hub)
	{
		if (_calibrating)
		{
			return;
		}
		foreach (FanGroup group in hub.Groups)
		{
			if (!_controlling && !_originalPwm.ContainsKey(group.Id))
			{
				_originalPwm[group.Id] = (byte[])group.Pwm.Clone();
			}
		}
		DateTime utcNow = DateTime.UtcNow;
		double num = Math.Clamp((utcNow - _lastCoolingTick).TotalSeconds, 0.2, 5.0);
		_lastCoolingTick = utcNow;
		string why;
		double? num2 = TargetPercent(out why);
		CoolingResult = why;
		if (!num2.HasValue)
		{
			_currentDuty.Clear();
			if (!_controlling)
			{
				return;
			}
			_controlling = false;
			{
				foreach (FanGroup group2 in hub.Groups)
				{
					if (_originalPwm.TryGetValue(group2.Id, out byte[] value))
					{
						hub.SetRawPwm(group2, value);
					}
				}
				return;
			}
		}
		_controlling = true;
		CoolingSettings cooling = Settings.Cooling;
		foreach (FanGroup group3 in hub.Groups)
		{
			int value2;
			int num3 = (cooling.GroupTrim.TryGetValue(group3.Id, out value2) ? value2 : 0);
			double num4 = Math.Clamp(num2.Value + (double)num3, 0.0, 100.0);
			if (cooling.Mode == "Auto" && num2.Value < 100.0 && _currentDuty.TryGetValue(group3.Id, out var value3))
			{
				double num5 = (double)(cooling.Response * 2) * num;
				double num6 = (double)cooling.Response * num;
				num4 = ((num4 > value3) ? Math.Min(num4, value3 + num5) : Math.Max(num4, value3 - num6));
			}
			_currentDuty[group3.Id] = num4;
			hub.SetSpeed(group3, (int)Math.Round(num4));
		}
	}

	public async Task<string> CalibrateAsync(IProgress<string> progress)
	{
		WirelessHub hub = Hub ?? throw new InvalidOperationException("The wireless fans are not connected.");
		_calibrating = true;
		try
		{
			int[] steps = new int[4] { 25, 50, 75, 100 };
			Dictionary<string, int[]> results = hub.Groups.ToDictionary((FanGroup g) => g.Id, (FanGroup _) => new int[steps.Length]);
			for (int s = 0; s < steps.Length; s++)
			{
				for (int k = 0; k < 7; k++)
				{
					foreach (FanGroup group in hub.Groups)
					{
						hub.SetSpeed(group, steps[s]);
					}
					progress.Report($"Running fans at {steps[s]}% … {7 - k}s");
					await Task.Delay(1000);
				}
				hub.Refresh();
				foreach (FanGroup group2 in hub.Groups)
				{
					results[group2.Id][s] = group2.AverageRpm;
				}
			}
			lock (Settings)
			{
				foreach (KeyValuePair<string, int[]> item in results)
				{
					Settings.Cooling.Calibration[item.Key] = item.Value;
				}
			}
			SaveSoon();
			return string.Join("\n", hub.Groups.Select((FanGroup g, int i) => GroupName(g, i) + ": " + string.Join("  ", steps.Select((int p, int j) => $"{p}% → {results[g.Id][j]} rpm"))));
		}
		finally
		{
			_calibrating = false;
		}
	}

	/// <summary>
	/// A layout frame made ready for a Lancool 207 Digital's 720 × 1472 panel, which sits landscape (1472 × 720) in the
	/// case. The whole layout is shown, scaled to fit; the space it leaves holds a darkened, stretched copy of the same
	/// frame, so the edges carry the layout's colours instead of black bars. Wide layouts turn a quarter like on the 8.8"
	/// screen; vertical ones go upright. Flip turns either half a turn.
	/// </summary>
	public static Bitmap CaseFrame(Bitmap frame, bool portrait, bool flip)
	{
		Size panel = LcdLink.PanelSize(DeviceKind.CaseLcd);
		int w = portrait ? panel.Width : panel.Height, h = portrait ? panel.Height : panel.Width;
		Bitmap result = new Bitmap(w, h);
		using (Graphics g = Graphics.FromImage(result))
		{
			g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
			float cover = Math.Max((float)w / frame.Width, (float)h / frame.Height);
			float fit = Math.Min((float)w / frame.Width, (float)h / frame.Height);
			// the copy is drawn tiny and stretched back up, so it reads as soft colour, not as ghost text
			const int Shrink = 24;
			using (Bitmap small = new Bitmap(Math.Max(1, w / Shrink), Math.Max(1, h / Shrink)))
			{
				using (Graphics gs = Graphics.FromImage(small))
				{
					gs.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
					float cw = frame.Width * cover / Shrink, ch = frame.Height * cover / Shrink;
					gs.DrawImage(frame, (small.Width - cw) / 2f, (small.Height - ch) / 2f, cw, ch);
				}
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
				g.DrawImage(small, -w / (float)small.Width / 2f, -h / (float)small.Height / 2f, w + w / (float)small.Width, h + h / (float)small.Height);
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
			}
			using (SolidBrush shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
			{
				g.FillRectangle(shade, 0, 0, w, h);
			}
			float fw = frame.Width * fit, fh = frame.Height * fit;
			g.DrawImage(frame, (w - fw) / 2f, (h - fh) / 2f, fw, fh);
		}
		if (portrait)
		{
			if (flip)
			{
				result.RotateFlip(RotateFlipType.Rotate180FlipNone);
			}
		}
		else
		{
			result.RotateFlip(flip ? RotateFlipType.Rotate270FlipNone : RotateFlipType.Rotate90FlipNone);
		}
		return result;
	}

	public List<UsbDeviceInfo> StuckDevices()
	{
		List<UsbDeviceInfo> list = new List<UsbDeviceInfo>();
		if (Screen.State.Contains("not responding"))
		{
			list.AddRange(Devices.Where((UsbDeviceInfo d) => (d.Kind == DeviceKind.Screen || d.Kind == DeviceKind.CaseLcd) && d.Serial == Screen.Serial));
		}
		if (Wireless.State.Contains("not responding"))
		{
			list.AddRange(Devices.Where(delegate(UsbDeviceInfo d)
			{
				DeviceKind kind = d.Kind;
				return (uint)(kind - 3) <= 1u;
			}));
		}
		foreach (OutputStatus f2 in FanOutputs.Where((OutputStatus f) => f.State.Contains("not responding")))
		{
			list.AddRange(Devices.Where((UsbDeviceInfo d) => d.Serial == f2.Serial));
		}
		return list;
	}

	public bool RestartDevices(IEnumerable<UsbDeviceInfo> devices, bool takeOver = true)
	{
		List<string> list = devices.Select((UsbDeviceInfo d) => WinUsb.InstanceId(d.Path)).Distinct().ToList();
		if (list.Count == 0)
		{
			return true;
		}
		Release();
		try
		{
			string text = string.Join(" & ", list.Select((string id) => "pnputil /restart-device \"" + id + "\""));
			Process process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + text)
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			});
			process?.WaitForExit(30000);
			Log.Write($"restarted {string.Join(", ", list)} (exit {process?.ExitCode})");
			Thread.Sleep(3000);
			return true;
		}
		catch (Win32Exception)
		{
			return false;
		}
		finally
		{
			if (takeOver)
			{
				Acquire();
			}
		}
	}

	public void Dispose()
	{
		SaveNow();
		Release(exiting: true);
		_quit.Cancel();
		Sensors.Dispose();
	}
}
