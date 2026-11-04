using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LcdStudio.Core;
using LcdStudio.Devices;

namespace LcdStudio.UI;

public sealed class HomePage : Page
{
	private readonly Card _warning;

	private readonly Label _warningText;

	private readonly Card _health;

	private readonly Card _cpuHint;

	private readonly Card _update;

	private readonly Caption _updateText;

	private readonly Caption _cpuHintText;

	private readonly Btn _cpuHintAdmin;

	private readonly Btn _cpuHintDriver;

	private readonly Caption _healthText;

	private readonly List<(Label Value, Sparkline Line, string Sensor, string Unit)> _stats = new List<(Label, Sparkline, string, string)>();

	private readonly ScreenPreview _screen;

	private readonly Select _layoutSelect;

	private readonly FlowLayoutPanel _fanRow;

	private readonly Label _fanEmpty;

	private readonly List<FanPreview> _fanPreviews = new List<FanPreview>();

	private readonly List<Btn> _modeButtons = new List<Btn>();

	private readonly Label _coolingLine;

	private readonly Label _coolingWhy;

	private readonly Toggle _fanLights;

	private readonly Toggle _strip;

	private readonly Label _effect;

	public override string Title => "Home";

	public override string Subtitle => "Your PC and everything LumaLi is driving, at a glance.";

	public HomePage(Engine e, MainForm m)
		: base(e, m)
	{
		_warning = AddCard(null, 1080);
		_warning.Border = Theme.Warn;
		_warningText = new Caption("", Theme.Warn, Theme.BodyBold)
		{
			AutoSize = false,
			Size = new Size(Theme.S(760f), Theme.S(30f))
		};
		Btn value = new Btn("Close L-Connect", CloseCompeting, primary: false, "\ue7ba");
		Btn value2 = new Btn("Take over devices", delegate
		{
			E.Acquire();
		}, primary: true);
		_warning.Controls.Add(new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)_warningText,
				(Control?)value,
				(Control?)value2
			}
		});
		(string, string, string, float, Color)[] array = new(string, string, string, float, Color)[4]
		{
			("Processor", "cpu.load", "%", 100f, Theme.Accent),
			("Graphics", "gpu.load", "%", 100f, Theme.Text),
			("GPU temperature", "gpu.temp", "°C", 100f, Theme.SubText),
			("Memory", "ram.load", "%", 100f, Theme.SubText)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, string, float, Color) tuple = array[i];
			Card card = AddCard(tuple.Item1, 256, 112);
			Label label = new Label
			{
				Text = "--",
				Font = Theme.Stat,
				ForeColor = Theme.Text,
				AutoSize = false,
				Bounds = new Rectangle(Theme.S(16f), Theme.S(40f), Theme.S(130f), Theme.S(46f)),
				BackColor = Color.Transparent
			};
			Sparkline sparkline = new Sparkline(E)
			{
				Sensor = tuple.Item2,
				Max = tuple.Item4,
				Color = tuple.Item5,
				Bounds = new Rectangle(Theme.S(150f), Theme.S(52f), Theme.S(98f), Theme.S(40f))
			};
			card.Controls.Add(label);
			card.Controls.Add(sparkline);
			_stats.Add((label, sparkline, tuple.Item2, tuple.Item3));
		}
		Body.SetFlowBreak(_warning, value: true);
		// the health watch's warnings (dust, a slowing fan), only while there are some
		_health = AddCard("Health watch", 1080);
		_health.Border = Theme.Warn;
		_healthText = new Caption("", Theme.Warn, Theme.Body) { AutoSize = false, Size = new Size(Theme.S(1040f), Theme.S(46f)) };
		_health.Controls.Add(_healthText);
		_health.Visible = false;
		Body.SetFlowBreak(_health, value: true);
		// without administrator rights and the PawnIO driver (on any processor) Windows gives no CPU temperature, and the
		// layouts show "--" for it; this says so and offers the fix, instead of leaving it a mystery
		_cpuHint = AddCard("CPU temperature", 1080);
		_cpuHintText = new Caption("", Theme.Text, Theme.Body) { AutoSize = false, Size = new Size(Theme.S(1040f), Theme.S(44f)) };
		_cpuHintAdmin = new Btn("Restart as administrator", delegate
		{
			Main.Get<SettingsPage>("Settings").RestartAsAdmin();
		}, primary: true);
		_cpuHintDriver = new Btn("Get PawnIO", delegate
		{
			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true });
			}
			catch
			{
			}
		});
		Btn hide = new Btn("Hide", delegate
		{
			lock (E.Settings)
			{
				E.Settings.HideCpuTempHint = true;
			}
			E.SaveSoon();
			_cpuHint.Visible = false;
		});
		_cpuHint.Controls.Add(new Stack
		{
			Controls =
			{
				(Control?)_cpuHintText,
				(Control?)new Stack(horizontal: true) { Controls = { (Control?)_cpuHintAdmin, (Control?)_cpuHintDriver, (Control?)hide } }
			}
		});
		_cpuHint.Visible = false;
		Body.SetFlowBreak(_cpuHint, value: true);
		// a newer LumaLi on GitHub: a note with a button to its page (nothing is downloaded by the app)
		_update = AddCard("Update", 1080);
		_updateText = new Caption("", Theme.Text, Theme.Body) { AutoSize = false, Size = new Size(Theme.S(1040f), Theme.S(26f)) };
		_update.Controls.Add(new Stack
		{
			Controls =
			{
				(Control?)_updateText,
				(Control?)new Stack(horizontal: true)
				{
					Controls =
					{
						(Control?)new Btn("Open the download page", delegate
						{
							try
							{
								System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updates.Page) { UseShellExecute = true });
							}
							catch
							{
							}
						}, primary: true),
						(Control?)new Btn("Hide", delegate
						{
							lock (E.Settings)
							{
								E.Settings.HiddenUpdate = Updates.Available ?? "";
							}
							E.SaveSoon();
							_update.Visible = false;
						})
					}
				}
			}
		});
		_update.Visible = false;
		Body.SetFlowBreak(_update, value: true);
		Card card2 = AddCard("8.8\" screen", 700);
		_screen = new ScreenPreview(E)
		{
			Size = new Size(Theme.S(668f), Theme.S(170f)),
			Source = () => E.ShownLayout,
			Margin = new Padding(0, 0, 0, Theme.S(10f))
		};
		_screen.Click += delegate
		{
			Main.Show("Screen layout");
		};
		_layoutSelect = new Select(E.VisibleLayouts().Select((ScreenLayout l) => l.Name), E.Settings.Layout)
		{
			Width = Theme.S(230f)
		};
		_layoutSelect.SelectedIndexChanged += delegate
		{
			UseLayout(_layoutSelect.SelectedItem);
		};
		Stack value3 = new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)new Caption("Layout"),
				(Control?)_layoutSelect,
				(Control?)new Btn("Remove", delegate
				{
					if (TemplatesPage.Remove(Main, E, this, E.Settings.Layout))
					{
						Refresh0();
						_screen.Tick(force: true);
					}
				}, primary: false, ""),
				(Control?)new Btn("Edit", delegate
				{
					Main.Show("Screen layout");
				}, primary: false, "\ue7f4"),
				(Control?)new Btn("Templates", delegate
				{
					Main.Show("Templates");
				}, primary: false, "\uf0e2")
			}
		};
		card2.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_screen,
				(Control?)value3
			}
		});
		Card card3 = AddCard("Fan screens", 366);
		_fanRow = new FlowLayoutPanel
		{
			Size = new Size(Theme.S(320f), Theme.S(150f)),
			BackColor = Theme.Card,
			Margin = Padding.Empty
		};
		_fanEmpty = new Caption("No fan screens found. Each LCD fan's USB cable must be plugged in; the wireless dongle can't carry pictures.", Theme.SubText)
		{
			Margin = new Padding(0, Theme.S(6f), 0, Theme.S(6f))
		};
		Stack value4 = new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)new Btn("Change faces", delegate
				{
					Main.Show("Fan screens");
				}),
				(Control?)new Btn("Identify fans", delegate
				{
					E.Identify(TimeSpan.FromSeconds(4.0));
					Main.Toast("Each fan shows its number");
				})
			}
		};
		card3.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_fanEmpty,
				(Control?)_fanRow,
				(Control?)value4
			}
		});
		Card card4 = AddCard("Cooling", 700);
		Stack stack = new Stack(horizontal: true);
		(string, string)[] array2 = new(string, string)[6]
		{
			("Silent", "Silent"),
			("Balanced", "Balanced"),
			("Performance", "Performance"),
			("Full", "Full"),
			("Auto", "Auto"),
			("Hold", "Hold")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple2 = array2[i];
			string mode = tuple2.Item1;
			Btn btn = new Btn(tuple2.Item2, delegate
			{
				SetMode(mode);
			})
			{
				Width = Theme.S(102f),
				Tag = mode
			};
			_modeButtons.Add(btn);
			stack.Controls.Add(btn);
		}
		_coolingLine = new Caption("", Theme.Text, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(560f), Theme.S(22f))
		};
		_coolingWhy = new Caption("", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(560f), Theme.S(22f))
		};
		card4.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)stack,
				(Control?)_coolingLine,
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)_coolingWhy,
						(Control?)new Btn("Tune cooling", delegate
						{
							Main.Show("Cooling");
						})
					}
				}
			}
		});
		Card card5 = AddCard("Lighting", 366);
		_fanLights = new Toggle("Fan lights", E.Settings.Lighting.FansOn);
		_strip = new Toggle("Screen light strip", E.Settings.Lighting.StripOn);
		_fanLights.CheckedChanged += delegate
		{
			lock (E.Settings)
			{
				E.Settings.Lighting.FansOn = _fanLights.Checked;
			}
			E.LightingChanged();
		};
		_strip.CheckedChanged += delegate
		{
			lock (E.Settings)
			{
				E.Settings.Lighting.StripOn = _strip.Checked;
			}
			E.LightingChanged();
		};
		_effect = new Caption("", Theme.Cyan, Theme.BodyBold);
		card5.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)_fanLights,
						(Control?)_strip
					}
				},
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)_effect,
						(Control?)new Btn("Change", delegate
						{
							Main.Show("Lighting");
						})
					}
				}
			}
		});
	}

	private void CloseCompeting()
	{
		string[] array = Engine.CompetingProcesses();
		if (array.Length == 0)
		{
			E.Acquire();
		}
		else
		{
			if (MessageBox.Show(this, "Close " + string.Join(", ", array) + " so LumaLi can drive the screens, lights and fans?\n\nTip: also turn off its \"start with Windows\" option.", "LumaLi", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			{
				return;
			}
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				if (array.Contains(process.ProcessName))
				{
					try
					{
						process.Kill(entireProcessTree: true);
						process.WaitForExit(3000);
					}
					catch
					{
					}
				}
			}
			Thread.Sleep(600);
			E.Acquire();
		}
	}

	private void UseLayout(string? name)
	{
		if (name != null && !(name == E.Settings.Layout))
		{
			lock (E.Settings)
			{
				E.Settings.Layout = name;
			}
			E.SaveSoon();
			_screen.Tick(force: true);
		}
	}

	private void SetMode(string mode)
	{
		lock (E.Settings)
		{
			E.Settings.Cooling.Mode = mode;
		}
		E.SaveSoon();
		Refresh0();
	}

	public override void Refresh0()
	{
		_warning.Visible = E.Warning != null;
		_warningText.Text = E.Warning ?? "";
		_layoutSelect.Items.Clear();
		_layoutSelect.Items.AddRange(from l in E.VisibleLayouts()
			select l.Name);
		_layoutSelect.SetSilently(E.Settings.Layout);
		foreach (Btn modeButton in _modeButtons)
		{
			modeButton.Primary = (string)modeButton.Tag == E.Settings.Cooling.Mode;
		}
		foreach (Btn modeButton2 in _modeButtons)
		{
			modeButton2.Invalidate();
		}
		_fanLights.SetSilently(E.Settings.Lighting.FansOn);
		_strip.SetSilently(E.Settings.Lighting.StripOn);
		_effect.Text = (E.Settings.Lighting.FansOn ? E.Settings.Lighting.FanEffect : "Fan lights off");
		List<string> second = E.FanOutputs.Select((OutputStatus f) => f.Serial).ToList();
		if (!_fanPreviews.Select((FanPreview p) => p.Serial).SequenceEqual(second))
		{
			_fanRow.Controls.Clear();
			foreach (FanPreview fanPreview2 in _fanPreviews)
			{
				fanPreview2.Dispose();
			}
			_fanPreviews.Clear();
			foreach (OutputStatus fanOutput in E.FanOutputs)
			{
				FanPreview fanPreview = new FanPreview(E)
				{
					Serial = fanOutput.Serial,
					Fan = E.Settings.FanFor(fanOutput.Serial),
					Size = new Size(Theme.S(96f), Theme.S(96f)),
					Margin = new Padding(0, 0, Theme.S(8f), Theme.S(8f)),
					Cursor = Cursors.Hand
				};
				fanPreview.Click += delegate
				{
					Main.Show("Fan screens");
				};
				_fanPreviews.Add(fanPreview);
				_fanRow.Controls.Add(fanPreview);
			}
		}
		_fanEmpty.Visible = _fanPreviews.Count == 0;
		_fanRow.Height = ((_fanPreviews.Count != 0) ? Theme.S((_fanPreviews.Count > 3) ? 210 : 106) : 0);
	}

	public override void DevicesChanged()
	{
		Refresh0();
	}

	public override void Tick()
	{
		// give the sensors 15 s to find the CPU temperature before saying it is missing
		bool missing = !E.Sensors.CpuTempAvailable && !E.Settings.HideCpuTempHint && (DateTime.Now - E.Sensors.Started).TotalSeconds > 15;
		if (_cpuHint.Visible != missing)
		{
			bool admin = SettingsPage.IsAdmin();
			bool pawn = LcdStudio.Core.Sensors.PawnIoInstalled;
			_cpuHintText.Text = !pawn
				? "The CPU temperature needs the free PawnIO driver on every processor, Intel and AMD alike" + (admin ? "" : ", and LumaLi running as administrator") + ". Until then layouts show \"--\" for it. Install PawnIO (or: winget install namazso.PawnIO), restart the PC, then start LumaLi" + (admin ? " again." : " as administrator.")
				: admin
				? "PawnIO is installed and LumaLi runs as administrator, but Windows still gives no CPU temperature. Restart the PC once after installing PawnIO. If it stays \"--\", copy the device report (Settings › Devices) into a GitHub issue."
				: "The CPU temperature needs LumaLi to run as administrator; until then layouts show \"--\" for it (the game frame rate needs it too). Restart it as administrator; to start that way every time, then turn on Settings › App › \"…as administrator\".";
			_cpuHintAdmin.Visible = !admin;
			_cpuHintDriver.Visible = !pawn;
			_cpuHint.Visible = missing;
		}
		string? newer = E.Settings.CheckUpdates ? Updates.Available : null;
		bool showUpdate = newer != null && newer != E.Settings.HiddenUpdate;
		if (showUpdate)
		{
			string text = $"LumaLi {newer} is available (you have {Updates.Running.ToString(3)}). Download it from GitHub, exit LumaLi from the tray, and replace LumaLi.exe; your layouts and settings stay.";
			if (_updateText.Text != text)
			{
				_updateText.Text = text;
			}
		}
		if (_update.Visible != showUpdate)
		{
			_update.Visible = showUpdate;
		}
		List<string> notes = E.Settings.Smart.HealthWatch ? E.HealthNotes : new List<string>();
		string healthText = string.Join("\n", notes);
		if (_healthText.Text != healthText)
		{
			_healthText.Text = healthText;
			_healthText.Height = Theme.S(24f * Math.Max(1, notes.Count) + 6f);
			_health.Visible = notes.Count > 0;
		}
		foreach (var stat in _stats)
		{
			Label item = stat.Value;
			Sparkline item2 = stat.Line;
			string item3 = stat.Sensor;
			string item4 = stat.Unit;
			float? num = E.Sensors.Get(item3);
			object obj;
			if (num.HasValue)
			{
				float valueOrDefault = num.GetValueOrDefault();
				obj = $"{valueOrDefault:0}{item4}";
			}
			else
			{
				obj = "--";
			}
			string text = (string)obj;
			if (item.Text != text)
			{
				item.Text = text;
			}
			item2.Invalidate();
		}
		_screen.Tick();
		foreach (FanPreview fanPreview in _fanPreviews)
		{
			fanPreview.Tick();
		}
		WirelessHub hub = E.Hub;
		string text2 = ((hub == null) ? "Wireless fans not connected." : string.Join("  |  ", hub.Groups.Select((FanGroup g, int i) => $"{E.GroupName(g, i)}: {g.DutyPercent}% · {g.RpmText} RPM")));
		if (_coolingLine.Text != text2)
		{
			_coolingLine.Text = text2;
		}
		if (_coolingWhy.Text != E.CoolingResult)
		{
			_coolingWhy.Text = E.CoolingResult;
		}
	}
}
