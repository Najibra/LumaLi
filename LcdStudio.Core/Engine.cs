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
			Devices = WinUsb.Enumerate();
			string[] array = CompetingProcesses();
			Warning = ((array.Length != 0) ? (string.Join(", ", array) + " is running and may hold the devices. Close it so LumaLi can take over.") : null);
			UsbDeviceInfo screen = Devices.FirstOrDefault((UsbDeviceInfo d) => d.Kind == DeviceKind.Screen);
			Screen.Present = screen != null;
			Screen.Serial = screen?.Serial ?? "";
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
			foreach (UsbDeviceInfo fan in Devices.Where((UsbDeviceInfo d) => d.Kind == DeviceKind.FanLcd))
			{
				OutputStatus status = new OutputStatus
				{
					Name = fan.Name,
					Serial = fan.Serial,
					Kind = DeviceKind.FanLcd,
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

	public void LightingChanged()
	{
		lock (Settings)
		{
			Settings.Lighting.Applied = true;
		}
		_lightingDirty = true;
		SaveSoon();
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
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				if (!Settings.ScreenOn)
				{
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
					lock (Settings)
					{
						screenLayout = standby ? StandbyLayout() : CurrentLayout.Clone();
					}
					int brightness = standby ? Math.Min(Settings.ScreenBrightness, Settings.Standby.Brightness) : Settings.ScreenBrightness;
					bool flag = ScreenRenderer.IsAnimated(screenLayout);
					string text2 = (flag ? null : string.Join("|", Sensors.Version, EditVersion, DateTime.Now.ToString(ScreenRenderer.ShowsSeconds(screenLayout) ? "HHmmss" : "HHmm"), Settings.Flip, Settings.ScreenQuality, brightness));
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
						using Bitmap bitmap = screenRenderer.Render(screenLayout, Time);
						// the light strip, when it matches the screen, takes its colours from this frame as drawn
						if (Settings.Lighting.StripMatchesScreen && Strip.Present)
						{
							_screenGlow = Lighting.FromScreen(bitmap, StripLeds);
						}
						bitmap.RotateFlip((!Settings.Flip) ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
						array2 = Imaging.Jpeg(bitmap, Settings.ScreenQuality);
					}
					lcdLink.SendFrame(array2, brightness);
					// going to sleep or shutting down: the standby face is on the screen; start its own clock if asked, then signal
					if (_farewell && standby && !_farewellSent)
					{
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
				int num2 = 1000 / Math.Clamp(standby ? Math.Min(Settings.ScreenFps, 8) : Settings.ScreenFps, 1, 30);
				Wait(ct, num2 - (int)(DateTime.UtcNow - utcNow).TotalMilliseconds);
			}
		}
		finally
		{
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
		LcdLink lcdLink = null;
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
						lcdLink = new LcdLink(info);
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
						fanScreen2 = new FanScreen
						{
							Face = fanScreen.Face,
							Sensor = fanScreen.Sensor,
							Label = fanScreen.Label,
							Color1 = fanScreen.Color1,
							Color2 = fanScreen.Color2,
							Rotation = fanScreen.Rotation,
							Background = fanScreen.Background
						};
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
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
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
						text = ((!lighting.StripOn) ? "Off" : (flag ? lighting.FanEffect : lighting.StripEffect));
						effectSettings = (flag ? lighting.For(lighting.FanEffect) : lighting.Strip);
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
						Wait(ct, 33, null);
						continue;
					}
					if (!Settings.Lighting.Applied)
					{
						Strip.State = lighting.StripMatchesScreen ? "waiting for the screen" : "waiting (choose lighting to start)";
						Wait(ct, 1000, autoResetEvent);
						continue;
					}
					double phase = Time / Lighting.LoopSeconds(effectSettings.Speed) % 1.0;
					ledStrip.Send(Lighting.Frame(text, effectSettings, 1, 60, phase));
					Strip.Running = true;
					Strip.Fps = fpsCounter.Tick();
					Strip.State = ((text == "Off") ? "off" : (text ?? ""));
					Wait(ct, Lighting.IsAnimated(text) ? 33 : 1000, Lighting.IsAnimated(text) ? null : autoResetEvent);
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
					if (_lightingDirty && Settings.Lighting.Applied)
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
			text = (lighting.FansOn ? lighting.FanEffect : "Off");
			EffectSettings effectSettings = lighting.For(lighting.FanEffect);
			s = new EffectSettings
			{
				Speed = effectSettings.Speed,
				Brightness = effectSettings.Brightness,
				Color1 = effectSettings.Color1,
				Color2 = effectSettings.Color2
			};
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<FanGroup> list = hub.Groups.ToList();
		string?[] status = new string[list.Count];
		Dictionary<int, (byte[], List<(byte, byte, byte)[]>, int)> dictionary = new Dictionary<int, (byte[], List<(byte, byte, byte)[]>, int)>();
		for (int i = 0; i < list.Count; i++)
		{
			try
			{
				(List<(byte R, byte G, byte B)[]> Frames, int Ticks) tuple = Lighting.FanLoop(text, s, list[i].FanCount, list[i].LedsPerFan);
				List<(byte, byte, byte)[]> item = tuple.Frames;
				int item2 = tuple.Ticks;
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
		LightingResult = text + " → " + string.Join(", ", values) + $" ({stopwatch.Elapsed.TotalSeconds:0.0} s)";
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

	public List<UsbDeviceInfo> StuckDevices()
	{
		List<UsbDeviceInfo> list = new List<UsbDeviceInfo>();
		if (Screen.State.Contains("not responding"))
		{
			list.AddRange(Devices.Where((UsbDeviceInfo d) => d.Kind == DeviceKind.Screen));
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
