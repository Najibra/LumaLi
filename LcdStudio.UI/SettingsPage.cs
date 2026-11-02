using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using LcdStudio.Core;
using LcdStudio.Devices;
using LcdStudio.Native;
using Microsoft.Win32;

namespace LcdStudio.UI;

public sealed class SettingsPage : Page
{
	private readonly FlowLayoutPanel _devices;

	private readonly Toggle _screenOn;

	private readonly Toggle _fansOn;

	private readonly Toggle _flip;

	private readonly Toggle _startup;

	private readonly Toggle _tray;

	private readonly Toggle _minimized;

	private readonly Slider _sBright;

	private readonly Slider _sFps;

	private readonly Slider _sQuality;

	private readonly Slider _fBright;

	private readonly Slider _fFps;

	private readonly Caption _cpuTemp;

	private int _tick;

	private Stack _gameRows = null!;

	private Caption _gameState = null!;

	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public override string Title => "Settings";

	public override string Subtitle => "Turn devices on and off, and set brightness and frame rates.";

	public SettingsPage(Engine e, MainForm m)
		: base(e, m)
	{
		Settings settings = E.Settings;
		Card card = AddCard("Devices", 530);
		_devices = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			BackColor = Theme.Card,
			WrapContents = false
		};
		_screenOn = new Toggle("8.8\" screen", settings.ScreenOn);
		_screenOn.CheckedChanged += delegate
		{
			Change(delegate(Settings x)
			{
				x.ScreenOn = _screenOn.Checked;
			});
		};
		_fansOn = new Toggle("Fan screens", settings.FanScreensOn);
		_fansOn.CheckedChanged += delegate
		{
			Change(delegate(Settings x)
			{
				x.FanScreensOn = _fansOn.Checked;
			});
		};
		card.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_devices,
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)_screenOn,
						(Control?)_fansOn
					}
				},
				(Control?)new Caption("Switching a device off releases it, so L-Connect or another program can use it.", Theme.SubText, Theme.Small)
				{
					AutoSize = false,
					Size = new Size(Theme.S(500f), Theme.S(22f))
				},
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)new Btn("Release all devices", delegate
						{
							E.Release();
							Main.Toast("Devices released");
						}),
						(Control?)new Btn("Take over devices", delegate
						{
							E.Acquire();
							Main.Toast("Devices connected");
						}, primary: true),
						(Control?)new Btn("Rescan", delegate
						{
							E.Acquire();
						}, primary: false, "\ue72c")
					}
				},
				(Control?)new Stack(horizontal: true)
				{
					Controls =
					{
						(Control?)new Btn("Restart stuck devices", RestartStuck, primary: false, "\ue7ba"),
						(Control?)new Btn("Copy device report", CopyReport, primary: false, "\ue8c8")
					}
				}
			}
		});
		Card card2 = AddCard("8.8\" screen", 530);
		FormGrid formGrid = new FormGrid();
		_sBright = Slide(formGrid, "Brightness", 0, 100, settings.ScreenBrightness, delegate(Settings x, int v)
		{
			x.ScreenBrightness = v;
		}, "");
		_flip = new Toggle("", settings.Flip);
		_flip.CheckedChanged += delegate
		{
			Change(delegate(Settings x)
			{
				x.Flip = _flip.Checked;
			});
		};
		formGrid.Add("Flip 180°", _flip);
		_sFps = Slide(formGrid, "Frames / sec", 1, 30, settings.ScreenFps, delegate(Settings x, int v)
		{
			x.ScreenFps = v;
		}, "");
		_sQuality = Slide(formGrid, "Picture quality", 50, 100, settings.ScreenQuality, delegate(Settings x, int v)
		{
			x.ScreenQuality = v;
		}, "");
		formGrid.AddWide(new Caption("Landscape, 1920 × 480. Higher frame rates and quality use more processing; 15–20 fps looks smooth for dashboards.", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(500f), Theme.S(40f))
		});
		card2.Controls.Add(formGrid);
		Card card3 = AddCard("Fan screens", 530);
		FormGrid formGrid2 = new FormGrid();
		_fBright = Slide(formGrid2, "Brightness", 0, 100, settings.FanBrightness, delegate(Settings x, int v)
		{
			x.FanBrightness = v;
		}, "");
		_fFps = Slide(formGrid2, "Frames / sec", 1, 20, settings.FanFps, delegate(Settings x, int v)
		{
			x.FanFps = v;
		}, "");
		formGrid2.AddWide(new Caption("Higher frame rates use more processing. The wireless fan screens manage about 8–15 fps each.", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(500f), Theme.S(40f))
		});
		card3.Controls.Add(formGrid2);
		Card card4 = AddCard("App", 530);
		bool elevatedNow = ElevatedStartup.Enabled();
		_startup = new Toggle("Start with Windows", IsStartupEnabled() || elevatedNow);
		Toggle elevated = new Toggle("…as administrator, so CPU temperature and game FPS work from the start", elevatedNow);
		bool settling = false;
		_startup.CheckedChanged += delegate
		{
			if (settling)
			{
				return;
			}
			if (!_startup.Checked && ElevatedStartup.Enabled())
			{
				string? err = IsAdmin() ? ElevatedStartup.Set(false, Environment.ProcessPath!) : "restart LumaLi as administrator first";
				if (err != null)
				{
					Main.Toast("Couldn't turn off starting as administrator: " + err);
					settling = true;
					_startup.Checked = true;
					settling = false;
					return;
				}
				settling = true;
				elevated.Checked = false;
				settling = false;
			}
			SetStartup(_startup.Checked && !elevated.Checked);
			Change(delegate(Settings x)
			{
				x.StartWithWindows = _startup.Checked;
			});
		};
		elevated.CheckedChanged += delegate
		{
			if (settling)
			{
				return;
			}
			string? err = IsAdmin() ? ElevatedStartup.Set(elevated.Checked, Environment.ProcessPath!) : "use Restart as administrator first, then turn this on";
			settling = true;
			if (err != null)
			{
				elevated.Checked = !elevated.Checked;
				Main.Toast("Couldn't change it: " + err);
			}
			else
			{
				// the task replaces the plain start-up entry (two would start two copies)
				SetStartup(!elevated.Checked && _startup.Checked);
				if (elevated.Checked)
				{
					_startup.Checked = true;
					Change(delegate(Settings x) { x.StartWithWindows = true; });
				}
				Main.Toast(elevated.Checked ? "LumaLi will start as administrator when you sign in" : "LumaLi will start normally");
			}
			settling = false;
		};
		_tray = new Toggle("Closing the window keeps LumaLi running in the tray", settings.CloseToTray);
		_tray.CheckedChanged += delegate
		{
			Change(delegate(Settings x)
			{
				x.CloseToTray = _tray.Checked;
			});
		};
		_minimized = new Toggle("Start in the tray when Windows starts", settings.StartMinimized);
		_minimized.CheckedChanged += delegate
		{
			Change(delegate(Settings x)
			{
				x.StartMinimized = _minimized.Checked;
			});
		};
		_cpuTemp = new Caption("", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(500f), Theme.S(40f))
		};
		Btn value = new Btn("Restart as administrator", RestartAsAdmin)
		{
			Visible = !IsAdmin()
		};
		card4.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_startup,
				(Control?)elevated,
				(Control?)_tray,
				(Control?)_minimized,
				(Control?)_cpuTemp,
				(Control?)value,
				(Control?)new Btn("Exit LumaLi", Main.ExitApp)
			}
		});
		// the 8.8" screen's standby face: when it comes on, how bright it is, and the logo on it
		Card standbyCard = AddCard("Standby screen", 1074);
		StandbySettings sb = settings.Standby;
		Toggle sbOn = new Toggle("Show the standby face (logo, clock and date)", sb.Enabled);
		sbOn.CheckedChanged += delegate { Change(delegate(Settings x) { x.Standby.Enabled = sbOn.Checked; }); };
		Toggle sbLock = new Toggle("When the PC is locked", sb.WhenLocked);
		sbLock.CheckedChanged += delegate { Change(delegate(Settings x) { x.Standby.WhenLocked = sbLock.Checked; }); };
		Toggle sbSleep = new Toggle("Leave it on the screen when the PC sleeps, hibernates or shuts down", sb.BeforeSleep);
		sbSleep.CheckedChanged += delegate { Change(delegate(Settings x) { x.Standby.BeforeSleep = sbSleep.Checked; }); };
		Toggle sbClock = new Toggle("Hand over to the screen's own clock as the PC goes down (keeps the live time while it has power)", sb.FirmwareClock);
		sbClock.CheckedChanged += delegate { Change(delegate(Settings x) { x.Standby.FirmwareClock = sbClock.Checked; }); };
		FormGrid sbGrid = new FormGrid();
		Slide(sbGrid, "After idle", 0, 60, sb.IdleMinutes, delegate(Settings x, int v) { x.Standby.IdleMinutes = v; }, " min");
		Slide(sbGrid, "Brightness", 5, 100, sb.Brightness, delegate(Settings x, int v) { x.Standby.Brightness = v; }, "%");
		Caption logoName = new Caption(string.IsNullOrEmpty(sb.Logo) ? "Logo: the LumaLi mark" : "Logo: " + Path.GetFileName(sb.Logo), Theme.SubText, Theme.Small);
		Btn chooseLogo = new Btn("Choose logo…", delegate
		{
			using OpenFileDialog dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Choose a logo for the standby screen" };
			if (dialog.ShowDialog(this) == DialogResult.OK)
			{
				string copy = Store.Import(dialog.FileName);
				Change(delegate(Settings x) { x.Standby.Logo = copy; });
				logoName.Text = "Logo: " + Path.GetFileName(copy);
			}
		}, primary: false, "");
		Btn clearLogo = new Btn("Use the LumaLi mark", delegate
		{
			Change(delegate(Settings x) { x.Standby.Logo = null; });
			logoName.Text = "Logo: the LumaLi mark";
		});
		standbyCard.Controls.Add(new Stack
		{
			Controls =
			{
				(Control?)sbOn,
				(Control?)sbLock,
				(Control?)sbGrid,
				(Control?)new Caption("After idle: minutes without mouse or keyboard input before the standby face comes on (0 = only when locked or going to sleep).", Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(24f)) },
				(Control?)sbSleep,
				(Control?)sbClock,
				(Control?)new Stack(horizontal: true) { Controls = { (Control?)chooseLogo, (Control?)clearLogo, (Control?)logoName } },
				(Control?)new Caption("When the PC is off, the screen only stays lit if your motherboard keeps USB powered: turn off ErP / deep sleep in the BIOS. With the screen's own clock, the time keeps running; without it, the standby face stays with the time the PC went down.", Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(44f)) }
			}
		});
		// layouts the screen switches to while a chosen game runs
		Card gameCard = AddCard("Game profiles", 1074);
		_gameRows = new Stack();
		_gameState = new Caption("", Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(20f)) };
		gameCard.Controls.Add(new Stack
		{
			Controls =
			{
				(Control?)new Caption("While a game below is running, the 8.8\" screen shows its layout, then goes back to your chosen layout when the game closes. The Layout page still edits your chosen layout.", Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(24f)) },
				(Control?)_gameRows,
				(Control?)new Stack(horizontal: true)
				{
					Controls =
					{
						(Control?)new Btn("Add a running game…", AddRunningGame, primary: true, ""),
						(Control?)new Btn("Add from a program file…", AddGameFile, primary: false, "")
					}
				},
				(Control?)_gameState
			}
		});
		BuildGameRows();
		BuildSmartCard(settings);
		AddCard("About", 1074).Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)new Caption("LumaLi " + Application.ProductVersion.Split('+')[0], Theme.Text, Theme.BodyBold),
				(Control?)new Caption("Layouts, templates and uploads are kept in " + Store.Folder, Theme.SubText, Theme.Small),
				(Control?)new Caption("Protocol work credits: sgtaziz/lian-li-linux (MIT) and sisong/tinyuz (MIT). Not affiliated with Lian Li.", Theme.Faint, Theme.Small),
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)new Btn("Open folder", delegate
						{
							Open(Store.Folder);
						}, primary: false, "\ue838"),
						(Control?)new Btn("Open log", delegate
						{
							Open(Log.Path);
						})
					}
				}
			}
		});
	}

	private Caption _smartState = null!;

	private Caption _healthState = null!;

	/// <summary>
	/// Smart: auto mode (a layout and a fan lighting effect for gaming, watching and working), the strip and fans
	/// taking the monitor's colours, and the health watch.
	/// </summary>
	private void BuildSmartCard(Settings settings)
	{
		SmartSettings smart = settings.Smart;
		Card card = AddCard("Smart", 1074);
		Caption Note(string text, int height = 24) => new Caption(text, Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(height)) };
		Toggle auto = new Toggle("Auto mode: switch the screen and fan lighting by what the PC is doing", smart.AutoMode);
		auto.CheckedChanged += delegate
		{
			Change(delegate(Settings x) { x.Smart.AutoMode = auto.Checked; });
			E.LightingChanged();
		};
		string keep = "(keep my choice)";
		string[] layouts = new[] { keep }.Concat(E.VisibleLayouts().Select((ScreenLayout l) => l.Name)).ToArray();
		string[] effects = new[] { keep }.Concat(Lighting.Effects).ToArray();
		FormGrid grid = new FormGrid(150);
		foreach (string activity in LcdStudio.Core.Activity.All)
		{
			string a = activity;
			string layoutNow = smart.LayoutFor(a), effectNow = smart.EffectFor(a);
			Select layout = new Select(layouts, layoutNow.Length > 0 && layouts.Contains(layoutNow) ? layoutNow : keep) { Width = Theme.S(300f) };
			Select effect = new Select(effects, effectNow.Length > 0 && effects.Contains(effectNow) ? effectNow : keep) { Width = Theme.S(230f) };
			layout.SelectedIndexChanged += delegate
			{
				string v = layout.SelectedItem?.ToString() ?? keep;
				v = v == keep ? "" : v;
				Change(delegate(Settings x)
				{
					switch (a)
					{
					case LcdStudio.Core.Activity.Gaming: x.Smart.GamingLayout = v; break;
					case LcdStudio.Core.Activity.Watching: x.Smart.WatchingLayout = v; break;
					default: x.Smart.WorkingLayout = v; break;
					}
				});
			};
			effect.SelectedIndexChanged += delegate
			{
				string v = effect.SelectedItem?.ToString() ?? keep;
				v = v == keep ? "" : v;
				Change(delegate(Settings x)
				{
					switch (a)
					{
					case LcdStudio.Core.Activity.Gaming: x.Smart.GamingEffect = v; break;
					case LcdStudio.Core.Activity.Watching: x.Smart.WatchingEffect = v; break;
					default: x.Smart.WorkingEffect = v; break;
					}
				});
				E.LightingChanged();
			};
			string label = a switch { LcdStudio.Core.Activity.Gaming => "Gaming", LcdStudio.Core.Activity.Watching => "Watching a video", _ => "Everything else" };
			grid.Add(label, new Stack(horizontal: true) { Controls = { (Control?)layout, (Control?)effect } });
		}
		Toggle strip = new Toggle("Light strip flows through the main colours on your monitor", smart.StripFollowsMonitor);
		strip.CheckedChanged += delegate { Change(delegate(Settings x) { x.Smart.StripFollowsMonitor = strip.Checked; }); };
		Toggle fans = new Toggle("Fan lights take the main colours on your monitor", smart.FansFollowMonitor);
		fans.CheckedChanged += delegate
		{
			Change(delegate(Settings x) { x.Smart.FansFollowMonitor = fans.Checked; });
			E.LightingChanged();
		};
		Toggle health = new Toggle("Health watch: warn when the PC runs warmer than it used to (dust) or a fan slows down", smart.HealthWatch);
		health.CheckedChanged += delegate { Change(delegate(Settings x) { x.Smart.HealthWatch = health.Checked; }); };
		_smartState = new Caption("", Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(22f)) };
		_healthState = new Caption("", Theme.SubText, Theme.Small) { AutoSize = false, Size = new Size(Theme.S(1000f), Theme.S(66f)) };
		card.Controls.Add(new Stack
		{
			Controls =
			{
				(Control?)auto,
				(Control?)Note("Gaming is a game drawing frames in front (needs administrator, like the frame rate); watching is a player or browser full screen. A change waits 5 seconds, and a game profile still comes first. For each: a layout, then a fan lighting effect.", 40),
				(Control?)grid,
				(Control?)_smartState,
				(Control?)strip,
				(Control?)fans,
				(Control?)Note("The monitor is sampled every 2 seconds, on this PC only. The fans keep their effect and take the new colours at most every 30 seconds, because each change is sent to them again."),
				(Control?)health,
				(Control?)_healthState,
				(Control?)Note("Screen layouts can show the busiest app with {top.app} and its share of the processor with {top.cpu:0}%.")
			}
		});
	}

	private void BuildGameRows()
	{
		_gameRows.SuspendLayout();
		while (_gameRows.Controls.Count > 0)
		{
			_gameRows.Controls[0].Dispose();
		}
		List<GameProfile> profiles;
		lock (E.Settings)
		{
			profiles = E.Settings.GameProfiles.ToList();
		}
		string[] layouts = E.Layouts.Select((ScreenLayout l) => l.Name).ToArray();
		foreach (GameProfile p in profiles)
		{
			GameProfile profile = p;
			Caption name = new Caption(string.IsNullOrEmpty(p.Title) || p.Title == p.Game ? p.Game : p.Title + "  (" + p.Game + ".exe)", Theme.Text, Theme.Body)
			{
				AutoSize = false,
				AutoEllipsis = true,
				Size = new Size(Theme.S(420f), Theme.S(30f)),
				TextAlign = ContentAlignment.MiddleLeft
			};
			Select layout = new Select(layouts, layouts.Contains(p.Layout) ? p.Layout : layouts.FirstOrDefault() ?? "") { Width = Theme.S(300f) };
			layout.SelectedIndexChanged += delegate
			{
				string chosen = layout.SelectedItem?.ToString() ?? "";
				Change(delegate { profile.Layout = chosen; });
			};
			Btn remove = new Btn("Remove", delegate
			{
				Change(delegate(Settings x) { x.GameProfiles.Remove(profile); });
				BeginInvoke(BuildGameRows);
			}, primary: false, "");
			_gameRows.Controls.Add(new Stack(horizontal: true) { Controls = { (Control?)name, (Control?)layout, (Control?)remove } });
		}
		if (profiles.Count == 0)
		{
			_gameRows.Controls.Add(new Caption("No games yet.", Theme.Faint, Theme.Small));
		}
		_gameRows.ResumeLayout();
	}

	private void AddGame(string game, string title)
	{
		game = Path.GetFileNameWithoutExtension(game.Trim());
		if (game.Length == 0)
		{
			return;
		}
		bool added = false;
		Change(delegate(Settings x)
		{
			if (!x.GameProfiles.Any((GameProfile g) => g.Game.Equals(game, StringComparison.OrdinalIgnoreCase)))
			{
				x.GameProfiles.Add(new GameProfile { Game = game, Title = title, Layout = x.Layout });
				added = true;
			}
		});
		BuildGameRows();
		Main.Toast(added ? "Added " + (title.Length > 0 ? title : game) + ": pick its layout" : game + " is already in the list");
	}

	/// <summary>Lists the programs that have a window open, newest first, so a running game is easy to pick.</summary>
	private void AddRunningGame()
	{
		var found = new List<(string Game, string Title)>();
		int self = Environment.ProcessId;
		foreach (Process p in Process.GetProcesses())
		{
			try
			{
				if (p.Id != self && p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.SessionId == Process.GetCurrentProcess().SessionId)
				{
					found.Add((p.ProcessName, p.MainWindowTitle));
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
		string[] skip = { "explorer", "TextInputHost", "ApplicationFrameHost", "SystemSettings", "LumaLi" };
		found = found.Where((t) => !skip.Contains(t.Game, StringComparer.OrdinalIgnoreCase)).GroupBy((t) => t.Game, StringComparer.OrdinalIgnoreCase).Select((g) => g.First()).OrderBy((t) => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
		if (found.Count == 0)
		{
			Main.Toast("No program windows found. Start the game, or add its program file");
			return;
		}
		using Form dialog = new Form
		{
			Text = "Add a running game",
			FormBorderStyle = FormBorderStyle.FixedDialog,
			StartPosition = FormStartPosition.CenterParent,
			MinimizeBox = false,
			MaximizeBox = false,
			ShowInTaskbar = false,
			ClientSize = new Size(Theme.S(520f), Theme.S(380f)),
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			Font = Theme.Body
		};
		ListBox list = new ListBox
		{
			Dock = DockStyle.Fill,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			BorderStyle = BorderStyle.None,
			IntegralHeight = false
		};
		foreach (var (game, title) in found)
		{
			list.Items.Add(title + "   —   " + game + ".exe");
		}
		list.SelectedIndex = 0;
		Button ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = Theme.S(34f), FlatStyle = FlatStyle.Flat };
		list.DoubleClick += delegate { dialog.DialogResult = DialogResult.OK; };
		dialog.Controls.Add(list);
		dialog.Controls.Add(ok);
		dialog.AcceptButton = ok;
		if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0)
		{
			AddGame(found[list.SelectedIndex].Game, found[list.SelectedIndex].Title);
		}
	}

	private void AddGameFile()
	{
		using OpenFileDialog dialog = new OpenFileDialog { Filter = "Programs|*.exe", Title = "Choose the game's program file" };
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			AddGame(dialog.FileName, "");
		}
	}

	private Slider Slide(FormGrid grid, string label, int min, int max, int value, Action<Settings, int> apply, string unit)
	{
		string unit2 = unit;
		Action<Settings, int> apply2 = apply;
		Slider s = new Slider(min, max, value)
		{
			Width = Theme.S(310f),
			Format = (int v) => v + unit2
		};
		s.ValueChanged += delegate
		{
			Change(delegate(Settings x)
			{
				apply2(x, s.Value);
			});
		};
		grid.Add(label, s);
		return s;
	}

	/// <summary>Copies a plain-text summary of this PC's Lian Li devices, for a GitHub issue or a Reddit reply.</summary>
	private void CopyReport()
	{
		try
		{
			Clipboard.SetText(DeviceReport(E));
			Main.Toast("Device report copied: paste it into your GitHub issue");
		}
		catch (Exception ex)
		{
			Main.Toast("Couldn't copy the report: " + ex.Message);
		}
	}

	/// <summary>
	/// What LumaLi found and how it is running, plus every Lian Li USB device Windows knows (also ones LumaLi doesn't
	/// support yet, which is what a request for new hardware needs). No serial numbers or personal paths.
	/// </summary>
	internal static string DeviceReport(Engine e)
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("LumaLi device report");
		sb.AppendLine("LumaLi " + Application.ProductVersion.Split('+')[0] + (IsAdmin() ? " (administrator)" : " (not administrator)"));
		sb.AppendLine("Windows " + Environment.OSVersion.Version);
		sb.AppendLine("Processor: " + (e.Sensors.Text("cpu.name") ?? "?") + " · Graphics: " + (e.Sensors.Text("gpu.name") ?? "?"));
		sb.AppendLine("CPU temperature: " + (e.Sensors.CpuTempAvailable ? "available" : "unavailable") + " · Game frame rate: " + (e.Sensors.GameFpsSource ?? "off"));
		string[] rivals = Engine.CompetingProcesses();
		if (rivals.Length > 0)
		{
			sb.AppendLine("Also running: " + string.Join(", ", rivals));
		}
		sb.AppendLine();
		sb.AppendLine("Outputs:");
		foreach (OutputStatus o in e.AllOutputs())
		{
			sb.AppendLine($"  {o.Name}: {(o.Present ? o.State : "not found")}{(o.Firmware != null ? " · firmware " + o.Firmware : "")}{(o.Running ? $" · {o.Fps:0.0} fps" : "")}");
		}
		WirelessHub? hub = e.Hub;
		if (hub != null)
		{
			for (int i = 0; i < hub.Groups.Count; i++)
			{
				sb.AppendLine($"  {e.GroupName(hub.Groups[i], i)}: {hub.Groups[i].Model} ×{hub.Groups[i].FanCount}");
			}
		}
		sb.AppendLine();
		sb.AppendLine("Lian Li USB devices Windows knows (vendor:product, name):");
		foreach (string line in KnownUsb())
		{
			sb.AppendLine("  " + line);
		}
		return sb.ToString();
	}

	private static List<string> KnownUsb()
	{
		var lines = new List<string>();
		// Lian Li's own vendor IDs, from the shared 04FC vendor only the wired TL LCD, and 1A86 (the CH340 chip in Lian Li's
		// desktop-mode screens, newer dongles and Turzx screens); plus any device Windows names Turzx
		string[] vendors = { "vid_1cbe", "vid_0416", "vid_04fc&pid_7393", "vid_1a86" };
		try
		{
			using RegistryKey? usb = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\USB");
			foreach (string id in usb?.GetSubKeyNames() ?? Array.Empty<string>())
			{
				string low = id.ToLowerInvariant();
				using RegistryKey? dev = usb!.OpenSubKey(id);
				if (!vendors.Any(low.StartsWith) && !NamedTurzx(dev))
				{
					continue;
				}
				int count = dev?.SubKeyCount ?? 0;
				string desc = "";
				foreach (string inst in dev?.GetSubKeyNames() ?? Array.Empty<string>())
				{
					using RegistryKey? k = dev!.OpenSubKey(inst);
					string? d = k?.GetValue("FriendlyName") as string ?? k?.GetValue("DeviceDesc") as string;
					if (d != null)
					{
						desc = d.Contains(';') ? d[(d.LastIndexOf(';') + 1)..] : d;
						break;
					}
				}
				string ids = low.Replace("vid_", "").Replace("&pid_", ":").Replace("&mi_", " interface ");
				lines.Add($"{ids.ToUpperInvariant()}  {desc}{(count > 1 ? $" ({count} seen)" : "")}");
			}
		}
		catch (Exception ex)
		{
			lines.Add("(couldn't read: " + ex.Message + ")");
		}
		if (lines.Count == 0)
		{
			lines.Add("(none)");
		}
		return lines;
	}

	private static bool NamedTurzx(RegistryKey? dev)
	{
		foreach (string inst in dev?.GetSubKeyNames() ?? Array.Empty<string>())
		{
			using RegistryKey? k = dev!.OpenSubKey(inst);
			string text = (k?.GetValue("FriendlyName") as string) + " " + (k?.GetValue("DeviceDesc") as string) + " " + (k?.GetValue("Mfg") as string);
			if (text.Contains("turzx", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private async void RestartStuck()
	{
		List<UsbDeviceInfo> stuck = E.StuckDevices();
		if (stuck.Count == 0)
		{
			stuck = E.Devices.Where((UsbDeviceInfo d) => !WinUsb.IsFanScreen(d.Kind)).ToList();
		}
		if (MessageBox.Show(this, "Restart these devices? Windows will ask for administrator permission once. It is the same as unplugging and replugging them.\n\n" + string.Join("\n", stuck.Select((UsbDeviceInfo d) => "• " + d.Name)), "Restart stuck devices", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
		{
			Main.Toast("Restarting devices…");
			bool flag = await Task.Run(() => E.RestartDevices(stuck));
			Main.Toast(flag ? "Devices restarted" : "Restart cancelled");
		}
	}

	private void Change(Action<Settings> change)
	{
		lock (E.Settings)
		{
			change(E.Settings);
		}
		E.SaveSoon();
	}

	private static void Open(string path)
	{
		try
		{
			Directory.CreateDirectory(Store.Folder);
			Process.Start(new ProcessStartInfo(path)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	public override void Refresh0()
	{
		_screenOn.SetSilently(E.Settings.ScreenOn);
		_fansOn.SetSilently(E.Settings.FanScreensOn);
		UpdateDevices();
	}

	public override void DevicesChanged()
	{
		UpdateDevices();
	}

	private void UpdateDevices()
	{
		List<(string, Color)> list = (from o in E.AllOutputs()
			select (Text: o.Name + "  —  " + (o.Present ? o.State : "not found") + ((o.Firmware != null) ? (" · firmware " + o.Firmware) : ""), Color: (!o.Present) ? Theme.Faint : (o.Running ? Theme.Text : (o.State.StartsWith("error") ? Theme.Bad : Theme.Warn)))).ToList();
		WirelessHub hub = E.Hub;
		if (hub != null)
		{
			for (int i = 0; i < hub.Groups.Count; i++)
			{
				list.Add(($"    {E.GroupName(hub.Groups[i], i)}: {hub.Groups[i].Model} ×{hub.Groups[i].FanCount}", Theme.SubText));
			}
		}
		if (E.Warning != null)
		{
			list.Add((E.Warning, Theme.Warn));
		}
		if (_devices.Controls.Count != list.Count)
		{
			_devices.SuspendLayout();
			while (_devices.Controls.Count > list.Count)
			{
				ControlCollection controls = _devices.Controls;
				controls[controls.Count - 1].Dispose();
			}
			while (_devices.Controls.Count < list.Count)
			{
				_devices.Controls.Add(new Caption("", Theme.Text, Theme.Small)
				{
					AutoSize = false,
					Size = new Size(Theme.S(505f), Theme.S(20f)),
					Margin = new Padding(0, 0, 0, Theme.S(2f)),
					AutoEllipsis = true
				});
			}
			_devices.ResumeLayout();
		}
		for (int j = 0; j < list.Count; j++)
		{
			Caption caption = (Caption)_devices.Controls[j];
			if (caption.Text != list[j].Item1)
			{
				caption.Text = list[j].Item1;
			}
			if (caption.ForeColor != list[j].Item2)
			{
				caption.ForeColor = list[j].Item2;
			}
		}
	}

	public override void Tick()
	{
		if (++_tick % 7 == 0)
		{
			UpdateDevices();
		}
		string text = (E.Sensors.CpuTempAvailable ? $"CPU temperature: {E.Sensors.Get("cpu.temp"):0} °C" : (IsAdmin() ? "CPU temperature unavailable. On Ryzen 9000 it needs the PawnIO driver: winget install namazso.PawnIO" : "CPU temperature and game frame rate need administrator rights (CPU temperature also the PawnIO driver on Ryzen 9000). GPU readings work without."));
		if (E.Sensors.GameFpsSource == "Windows")
		{
			text += " · Game frame rate: on";
		}
		if (_cpuTemp.Text != text)
		{
			_cpuTemp.Text = text;
		}
		GameProfile? game = E.ActiveGame;
		string state = game == null ? "" : $"Now showing {game.Layout} for {(game.Title.Length > 0 ? game.Title : game.Game)}.";
		if (_gameState.Text != state)
		{
			_gameState.Text = state;
		}
		string smart = $"Now: {E.CurrentActivity.ToLowerInvariant()}" + (E.Settings.Smart.AutoMode ? $", showing {E.ShownLayout.Name} with {E.FanEffectNow(E.Settings.Lighting)} on the fans." : " (auto mode is off).");
		var palette = E.MonitorPalette;
		if (palette != null && (E.Settings.Smart.StripFollowsMonitor || E.Settings.Smart.FansFollowMonitor))
		{
			smart += $"  Monitor colours: {MonitorColors.Hex(palette[0])}, {MonitorColors.Hex(palette[1])}.";
		}
		if (_smartState.Text != smart)
		{
			_smartState.Text = smart;
		}
		string healthText = !E.Settings.Smart.HealthWatch ? "" : E.HealthNotes.Count > 0 ? "⚠ " + string.Join("  ⚠ ", E.HealthNotes) : E.HealthSummary;
		if (_healthState.Text != healthText)
		{
			_healthState.Text = healthText;
			_healthState.ForeColor = E.HealthNotes.Count > 0 ? Theme.Warn : Theme.SubText;
		}
	}

	private static bool IsStartupEnabled()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		return registryKey?.GetValue("LumaLi") is string;
	}

	/// <summary>Moves a start-with-Windows entry from before the rename over to this program, so it keeps starting.</summary>
	public static void MigrateStartup()
	{
		using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
		if (registryKey?.GetValue("LCD Studio") is string)
		{
			registryKey.DeleteValue("LCD Studio", throwOnMissingValue: false);
			registryKey.SetValue("LumaLi", "\"" + Environment.ProcessPath + "\" --tray");
		}
	}

	private static void SetStartup(bool on)
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		if (on)
		{
			registryKey.SetValue("LumaLi", "\"" + Environment.ProcessPath + "\" --tray");
		}
		else
		{
			registryKey.DeleteValue("LumaLi", throwOnMissingValue: false);
		}
	}

	internal static bool IsAdmin()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	internal void RestartAsAdmin()
	{
		try
		{
			E.SaveNow();
			E.Release();
			Process.Start(new ProcessStartInfo(Environment.ProcessPath)
			{
				UseShellExecute = true,
				Verb = "runas",
				Arguments = "--elevated"
			});
			Main.ExitApp();
		}
		catch (Win32Exception)
		{
			E.Acquire();
		}
	}
}
