using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LcdStudio.Core;
using Microsoft.Win32;

namespace LcdStudio.UI;

public sealed class MainForm : Form
{
	private readonly Engine _engine;

	private readonly Sidebar _sidebar;

	private readonly Panel _host;

	private readonly Dictionary<string, Page> _pages = new Dictionary<string, Page>();

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer
	{
		Interval = 140
	};

	private readonly NotifyIcon _tray;

	private Page? _current;

	private readonly Label _toast;

	private readonly System.Windows.Forms.Timer _toastTimer = new System.Windows.Forms.Timer
	{
		Interval = 2200
	};

	private bool _exiting;

	public static readonly string[] PageNames = new string[7] { "Home", "Screen layout", "Templates", "Fan screens", "Lighting", "Cooling", "Settings" };

	private readonly System.Windows.Forms.Timer _usbDebounce = new System.Windows.Forms.Timer
	{
		Interval = 2000
	};

	private bool _usbHooked;

	public MainForm(Engine engine)
	{
		_engine = engine;
		Text = "LumaLi";
		BackColor = Theme.Window;
		ForeColor = Theme.Text;
		Font = Theme.Body;
		base.StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(Theme.S(1100f), Theme.S(700f));
		base.Size = new Size(Theme.S(1380f), Theme.S(860f));
		base.Icon = AppIcon.Create();
		DoubleBuffered = true;
		_host = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Theme.Window
		};
		_sidebar = new Sidebar(engine, PageNames)
		{
			Dock = DockStyle.Left,
			Width = Theme.S(208f)
		};
		_sidebar.Navigate += Show;
		base.Controls.Add(_host);
		base.Controls.Add(_sidebar);
		_toast = new Label
		{
			AutoSize = false,
			Visible = false,
			BackColor = Theme.Accent,
			ForeColor = Color.White,
			Font = Theme.BodyBold,
			TextAlign = ContentAlignment.MiddleCenter,
			Size = new Size(Theme.S(420f), Theme.S(34f)),
			AutoEllipsis = true
		};
		base.Controls.Add(_toast);
		_toast.BringToFront();
		_toastTimer.Tick += delegate
		{
			_toast.Visible = false;
			_toastTimer.Stop();
		};
		_tray = new NotifyIcon
		{
			Icon = base.Icon,
			Text = "LumaLi",
			Visible = true
		};
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Renderer = new NavyMenuRenderer()
		};
		contextMenuStrip.Items.Add("Open LumaLi", null, delegate
		{
			RestoreFromTray();
		});
		contextMenuStrip.Items.Add("Release devices", null, delegate
		{
			_engine.Release();
		});
		contextMenuStrip.Items.Add("Take over devices", null, delegate
		{
			_engine.Acquire();
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("Exit", null, delegate
		{
			ExitApp();
		});
		_tray.ContextMenuStrip = contextMenuStrip;
		_tray.DoubleClick += delegate
		{
			RestoreFromTray();
		};
		_timer.Tick += delegate
		{
			TickVisible();
		};
		EventWaitHandle exitSignal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\LumaLi.Exit");
		Thread thread = new Thread((ThreadStart)delegate
		{
			exitSignal.WaitOne();
			try
			{
				BeginInvoke(ExitApp);
			}
			catch
			{
			}
		});
		thread.IsBackground = true;
		thread.Name = "exit-signal";
		thread.Start();
		SystemEvents.PowerModeChanged += OnPowerModeChanged;
		SystemEvents.SessionSwitch += OnSessionSwitch;
		_engine.Changed += delegate
		{
			if (!base.IsHandleCreated || base.IsDisposed)
			{
				return;
			}
			try
			{
				BeginInvoke(delegate
				{
					_sidebar.Invalidate();
					_current?.DevicesChanged();
				});
			}
			catch
			{
			}
		};
		Show("Home");
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 537 && (m.WParam == 7 || m.WParam == 32768 || m.WParam == 32772))
		{
			if (!_usbHooked)
			{
				_usbHooked = true;
				_usbDebounce.Tick += async delegate
				{
					_usbDebounce.Stop();
					string text = await Task.Run((Func<string>)_engine.RescanIfChanged);
					if (text != null)
					{
						Toast(text);
						if (!base.Visible)
						{
							_tray.ShowBalloonTip(2500, "LumaLi", text, ToolTipIcon.None);
						}
					}
				};
			}
			_usbDebounce.Stop();
			_usbDebounce.Start();
		}
		base.WndProc(ref m);
	}

	// locking the PC puts the 8.8" screen on its standby face; unlocking brings the layout back
	private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
	{
		if (e.Reason == SessionSwitchReason.SessionLock)
		{
			_engine.SetLocked(locked: true);
		}
		else if (e.Reason == SessionSwitchReason.SessionUnlock)
		{
			_engine.SetLocked(locked: false);
		}
	}

	private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
	{
		if (e.Mode == PowerModes.Suspend)
		{
			_engine.Suspend();
		}
		else if (e.Mode == PowerModes.Resume)
		{
			Task.Run((Action)_engine.Resume);
		}
	}

	private void TickVisible()
	{
		if (base.WindowState != FormWindowState.Minimized && base.Visible)
		{
			_current?.Tick();
			_sidebar.Tick();
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		UseDarkTitleBar();
		_timer.Start();
		if (_engine.Settings.StartMinimized && Environment.GetCommandLineArgs().Contains("--tray"))
		{
			HideToTray();
		}
	}

	public void Show(string name)
	{
		if (!_pages.TryGetValue(name, out Page value))
		{
			value = name switch
			{
				"Home" => new HomePage(_engine, this), 
				"Screen layout" => new LayoutPage(_engine, this), 
				"Templates" => new TemplatesPage(_engine, this), 
				"Fan screens" => new FansPage(_engine, this), 
				"Lighting" => new LightingPage(_engine, this), 
				"Cooling" => new CoolingPage(_engine, this), 
				_ => new SettingsPage(_engine, this), 
			};
			value.Dock = DockStyle.Fill;
			value.Visible = false;
			_pages[name] = value;
			_host.Controls.Add(value);
		}
		if (_current != null)
		{
			_current.Visible = false;
		}
		_current = value;
		value.Refresh0();
		value.Visible = true;
		value.BringToFront();
		_sidebar.Selected = name;
	}

	public T Get<T>(string name) where T : Page
	{
		if (!_pages.ContainsKey(name))
		{
			string selected = _sidebar.Selected;
			Show(name);
			Show(selected);
		}
		return (T)_pages[name];
	}

	public void Toast(string text)
	{
		_toast.Text = text;
		_toast.Location = new Point((base.ClientSize.Width - _toast.Width + _sidebar.Width) / 2, base.ClientSize.Height - _toast.Height - Theme.S(24f));
		_toast.Visible = true;
		_toast.BringToFront();
		_toastTimer.Stop();
		_toastTimer.Start();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (!_exiting && e.CloseReason == CloseReason.UserClosing && _engine.Settings.CloseToTray)
		{
			e.Cancel = true;
			HideToTray();
		}
		else
		{
			// Windows is shutting down or signing out: leave the standby face on the screen as it goes
			if (e.CloseReason == CloseReason.WindowsShutDown)
			{
				_engine.Farewell();
			}
			base.OnFormClosing(e);
		}
	}

	private void HideToTray()
	{
		Hide();
		_tray.ShowBalloonTip(1500, "LumaLi", "Still running. Your screens, lights and fans keep going.", ToolTipIcon.None);
	}

	private void RestoreFromTray()
	{
		Show();
		base.WindowState = FormWindowState.Normal;
		Activate();
	}

	public void ExitApp()
	{
		_exiting = true;
		_timer.Stop();
		_tray.Visible = false;
		Close();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			SystemEvents.PowerModeChanged -= OnPowerModeChanged;
			SystemEvents.SessionSwitch -= OnSessionSwitch;
			_tray.Dispose();
			_timer.Dispose();
		}
		base.Dispose(disposing);
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

	private void UseDarkTitleBar()
	{
		try
		{
			int value = 1;
			DwmSetWindowAttribute(base.Handle, 20, ref value, 4);
			int value2 = Theme.Window.R | (Theme.Window.G << 8) | (Theme.Window.B << 16);
			DwmSetWindowAttribute(base.Handle, 35, ref value2, 4);
		}
		catch
		{
		}
	}
}
