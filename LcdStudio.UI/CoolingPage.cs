using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;
using LcdStudio.Devices;

namespace LcdStudio.UI;

public sealed class CoolingPage : Page
{
	private sealed class GroupLine : Control
	{
		public float Fraction;

		public GroupLine()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.Clear(Theme.Card);
			Theme.Smooth(graphics);
			Theme.DrawText(graphics, Text, Theme.Body, Theme.Text, new RectangleF(0f, 0f, base.Width, Theme.S(22f)));
			RectangleF rectangleF = new RectangleF(0f, Theme.S(26f), base.Width - Theme.S(4f), Theme.S(6f));
			Theme.FillRound(graphics, rectangleF, Theme.S(3f), Theme.Field);
			if (Fraction > 0f)
			{
				RectangleF r = rectangleF;
				r.Width = Math.Max(Theme.S(6f), rectangleF.Width * Fraction);
				Theme.FillRound(graphics, r, Theme.S(3f), Theme.Cyan);
			}
		}
	}

	private readonly List<PresetCard> _presets = new List<PresetCard>();

	private readonly Slider _custom;

	private readonly Card _now;

	private readonly FlowLayoutPanel _groups;

	private readonly Caption _why;

	private readonly Select _sensor;

	private readonly Slider _min;

	private readonly Slider _start;

	private readonly Slider _full;

	private readonly Slider _response;

	private readonly Slider _exit;

	private readonly FlowLayoutPanel _trims;

	private readonly Caption _calResult;

	private readonly Btn _calibrate;

	private List<string> _builtGroups = new List<string>();

	public override string Title => "Cooling";

	public override string Subtitle => "How fast the case fans spin. Choose a preset, a fixed speed, or let the speed follow a sensor.";

	public CoolingPage(Engine e, MainForm m)
		: base(e, m)
	{
		CoolingSettings cooling = E.Settings.Cooling;
		(string, string, string)[] obj = new(string, string, string)[7]
		{
			("Silent", "Silent", "Quiet, light use"),
			("Balanced", "Balanced", "Everyday default"),
			("Performance", "Performance", "Gaming and heavy work"),
			("Full", "Full speed", "Maximum airflow"),
			("Custom", "Custom", "Pick any fixed speed"),
			("Auto", "Automatic", "Speed follows a sensor"),
			("Hold", "Hold", "Don't control fan speed")
		};
		Stack stack = new Stack(horizontal: true)
		{
			MaximumSize = new Size(Theme.S(1110f), 0)
		};
		(string, string, string)[] array = obj;
		for (int i = 0; i < array.Length; i++)
		{
			(string Mode, string Title, string Sub) p = array[i];
			PresetCard presetCard = new PresetCard(p.Mode, p.Title, p.Sub, () => Percent(p.Mode))
			{
				Size = new Size(Theme.S(146f), Theme.S(78f))
			};
			presetCard.Click += delegate
			{
				SetMode(p.Mode);
			};
			_presets.Add(presetCard);
			stack.Controls.Add(presetCard);
		}
		Body.Controls.Add(stack);
		Body.SetFlowBreak(stack, value: true);
		_now = AddCard("Right now", 530);
		_groups = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			BackColor = Theme.Card,
			WrapContents = false
		};
		_why = new Caption("", Theme.Cyan, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(500f), Theme.S(24f))
		};
		_custom = new Slider(0, 100, cooling.CustomPercent)
		{
			Width = Theme.S(300f),
			Format = (int v) => v + "%"
		};
		_custom.ValueChanged += delegate
		{
			Change(delegate(CoolingSettings x)
			{
				x.CustomPercent = _custom.Value;
			});
		};
		_custom.Committed += delegate
		{
			if (E.Settings.Cooling.Mode != "Custom")
			{
				SetMode("Custom");
			}
		};
		_now.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_why,
				(Control?)_groups,
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)new Caption("Custom speed", null, null, 110),
						(Control?)_custom
					}
				}
			}
		});
		Card card = AddCard("Tuning", 530);
		FormGrid formGrid = new FormGrid(150);
		_sensor = new Select(new string[5] { "gpu.temp", "cpu.temp", "gpu.hotspot", "gpu.load", "cpu.load" }, cooling.AutoSensor)
		{
			Width = Theme.S(220f),
			Display = (string k) => Sensors.Def(k).Label
		};
		_sensor.SelectedIndexChanged += delegate
		{
			Change(delegate(CoolingSettings x)
			{
				x.AutoSensor = _sensor.SelectedItem ?? "gpu.temp";
			});
		};
		_min = Slide(formGrid, "Lowest speed %", 0, 100, cooling.MinPercent, delegate(CoolingSettings x, int v)
		{
			x.MinPercent = v;
		}, "%");
		formGrid.Add("Follow", _sensor);
		_start = Slide(formGrid, "Start rising at °", 20, 90, cooling.StartAt, delegate(CoolingSettings x, int v)
		{
			x.StartAt = v;
		}, "°");
		_full = Slide(formGrid, "Full speed at °", 30, 100, cooling.FullAt, delegate(CoolingSettings x, int v)
		{
			x.FullAt = v;
		}, "°");
		_response = Slide(formGrid, "Response %/s", 1, 50, cooling.Response, delegate(CoolingSettings x, int v)
		{
			x.Response = v;
		}, "");
		_exit = Slide(formGrid, "Speed on exit %", 0, 100, cooling.ExitPercent, delegate(CoolingSettings x, int v)
		{
			x.ExitPercent = v;
		}, "%");
		formGrid.AddWide(new Caption("Response is how quickly speed changes (speed rises twice as fast as it falls). Speed on exit is what the fans are left at when LumaLi closes. If the followed sensor stops reporting for 5 seconds, the fans go to 100 %.", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(500f), Theme.S(56f))
		});
		card.Controls.Add(formGrid);
		Card card2 = AddCard("Per-group trim", 530);
		_trims = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			BackColor = Theme.Card,
			WrapContents = false
		};
		card2.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_trims,
				(Control?)new Caption("Give a group a name (for example “Front intake”) and run it faster or slower than the others.", Theme.SubText, Theme.Small)
				{
					AutoSize = false,
					Size = new Size(Theme.S(500f), Theme.S(40f))
				}
			}
		});
		Card card3 = AddCard("Fan calibration", 530);
		_calibrate = new Btn("Calibrate fans", Calibrate, primary: true);
		_calResult = new Caption("", Theme.Text, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(500f), Theme.S(90f))
		};
		card3.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)new Caption("Runs every fan through four speeds so you can see what each group really does. Takes about 30 seconds; the fans will be loud for part of it.", Theme.SubText, Theme.Small)
				{
					AutoSize = false,
					Size = new Size(Theme.S(500f), Theme.S(40f))
				},
				(Control?)_calibrate,
				(Control?)_calResult
			}
		});
	}

	private Slider Slide(FormGrid grid, string label, int min, int max, int value, Action<CoolingSettings, int> apply, string unit)
	{
		string unit2 = unit;
		Action<CoolingSettings, int> apply2 = apply;
		Slider s = new Slider(min, max, value)
		{
			Width = Theme.S(300f),
			Format = (int v) => v + unit2
		};
		s.ValueChanged += delegate
		{
			Change(delegate(CoolingSettings x)
			{
				apply2(x, s.Value);
			});
		};
		grid.Add(label, s);
		return s;
	}

	private int Percent(string mode)
	{
		CoolingSettings cooling = E.Settings.Cooling;
		return CoolingSettings.PresetPercent(mode, cooling.CustomPercent);
	}

	private void Change(Action<CoolingSettings> change)
	{
		lock (E.Settings)
		{
			change(E.Settings.Cooling);
		}
		E.SaveSoon();
	}

	private void SetMode(string mode)
	{
		string mode2 = mode;
		Change(delegate(CoolingSettings c)
		{
			c.Mode = mode2;
		});
		foreach (PresetCard preset in _presets)
		{
			preset.Invalidate();
		}
		Main.Toast((mode2 == "Hold") ? "Fan speed is no longer controlled" : ("Cooling: " + mode2));
	}

	private async void Calibrate()
	{
		if (MessageBox.Show(this, "The fans will step through 25, 50, 75 and 100 % for about 30 seconds. Continue?", "Calibrate fans", MessageBoxButtons.OKCancel) != DialogResult.OK)
		{
			return;
		}
		_calibrate.Enabled = false;
		try
		{
			Progress<string> progress = new Progress<string>(delegate(string s)
			{
				_calResult.Text = s;
			});
			Caption calResult = _calResult;
			calResult.Text = await E.CalibrateAsync(progress);
		}
		catch (Exception ex)
		{
			_calResult.Text = ex.Message;
		}
		finally
		{
			_calibrate.Enabled = true;
		}
	}

	public override void Refresh0()
	{
		foreach (PresetCard preset in _presets)
		{
			preset.Selected = preset.Mode == E.Settings.Cooling.Mode;
			preset.Invalidate();
		}
		WirelessHub hub = E.Hub;
		List<string> list = hub?.Groups.Select((FanGroup g) => g.Id).ToList() ?? new List<string>();
		if (!list.SequenceEqual(_builtGroups))
		{
			_builtGroups = list;
			_trims.Controls.Clear();
			if (hub == null || hub.Groups.Count == 0)
			{
				_trims.Controls.Add(new Caption("Wireless fans not connected.", Theme.SubText));
			}
			else
			{
				for (int k = 0; k < hub.Groups.Count; k++)
				{
					FanGroup g2 = hub.Groups[k];
					TextField name = new TextField(E.Settings.Cooling.GroupNames.GetValueOrDefault(g2.Id, ""), 170);
					name.Box.PlaceholderText = $"Group {k + 1}";
					name.Box.TextChanged += delegate
					{
						Change(delegate(CoolingSettings c)
						{
							c.GroupNames[g2.Id] = name.Text.Trim();
						});
					};
					Slider slider = new Slider(-20, 20, E.Settings.Cooling.GroupTrim.GetValueOrDefault(g2.Id, 0))
					{
						Width = Theme.S(300f),
						Format = (int v) => ((v > 0) ? "+" : "") + v + "%"
					};
					slider.ValueChanged += delegate
					{
						Change(delegate(CoolingSettings c)
						{
							c.GroupTrim[g2.Id] = slider.Value;
						});
					};
					_trims.Controls.Add(new Stack(horizontal: true)
					{
						Controls = 
						{
							(Control?)name,
							(Control?)new Caption("slower / faster", Theme.Faint, Theme.Small),
							(Control?)slider
						}
					});
				}
			}
		}
		if (E.Settings.Cooling.Calibration.Count <= 0 || _calResult.Text.Length != 0 || hub == null)
		{
			return;
		}
		int[] steps = new int[4] { 25, 50, 75, 100 };
		_calResult.Text = "Last calibration:\n" + string.Join("\n", hub.Groups.Select((FanGroup g, int i) => (!E.Settings.Cooling.Calibration.TryGetValue(g.Id, out int[] r)) ? "" : (E.GroupName(g, i) + ": " + string.Join("  ", steps.Select((int p, int j) => $"{p}% → {r[j]} rpm")))));
	}

	public override void DevicesChanged()
	{
		Refresh0();
	}

	public override void Tick()
	{
		WirelessHub hub = E.Hub;
		_why.Text = (E.Calibrating ? "Calibrating…" : E.CoolingResult);
		List<string> list = ((hub == null) ? new List<string> { "Wireless fans not connected." } : hub.Groups.Select((FanGroup g, int i) => $"{E.GroupName(g, i)}  ·  {g.Model} ×{g.FanCount}  ·  {g.RpmText} RPM  ·  {g.DutyPercent}%").ToList());
		while (_groups.Controls.Count < list.Count)
		{
			_groups.Controls.Add(new GroupLine
			{
				Size = new Size(Theme.S(500f), Theme.S(40f))
			});
		}
		while (_groups.Controls.Count > list.Count)
		{
			ControlCollection controls = _groups.Controls;
			controls[controls.Count - 1].Dispose();
		}
		for (int j = 0; j < list.Count; j++)
		{
			GroupLine obj = (GroupLine)_groups.Controls[j];
			obj.Text = list[j];
			obj.Fraction = ((hub == null || j >= hub.Groups.Count) ? 0f : ((float)hub.Groups[j].DutyPercent / 100f));
			obj.Invalidate();
		}
		if (hub != null && _builtGroups.Count != hub.Groups.Count)
		{
			Refresh0();
		}
	}
}
