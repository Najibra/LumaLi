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
						}, primary: false, "\ue72c"),
						(Control?)new Btn("Restart stuck devices", RestartStuck, primary: false, "\ue7ba")
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
		_startup = new Toggle("Start with Windows", IsStartupEnabled());
		_startup.CheckedChanged += delegate
		{
			SetStartup(_startup.Checked);
			Change(delegate(Settings x)
			{
				x.StartWithWindows = _startup.Checked;
			});
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

	private async void RestartStuck()
	{
		List<UsbDeviceInfo> stuck = E.StuckDevices();
		if (stuck.Count == 0)
		{
			stuck = E.Devices.Where((UsbDeviceInfo d) => d.Kind != DeviceKind.FanLcd).ToList();
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
		string text = (E.Sensors.CpuTempAvailable ? $"CPU temperature: {E.Sensors.Get("cpu.temp"):0} °C" : (IsAdmin() ? "CPU temperature unavailable. On Ryzen 9000 it needs the PawnIO driver: winget install namazso.PawnIO" : "CPU temperature needs administrator rights (and on Ryzen 9000 the PawnIO driver). GPU readings work without."));
		if (_cpuTemp.Text != text)
		{
			_cpuTemp.Text = text;
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

	private static bool IsAdmin()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	private void RestartAsAdmin()
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
