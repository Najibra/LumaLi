using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class LayoutPage : Page
{
	private readonly LayoutCanvas _canvas;

	private readonly Select _layouts;

	private readonly Btn _undo;

	private readonly Btn _redo;

	private readonly Card _widgetCard;

	private readonly Card _bgCard;

	private readonly Card _layoutCard;

	private readonly FlowLayoutPanel _widgetProps;

	private readonly Caption _builtIn;

	private bool _building;

	private Widget? _propsFor;

	private TextField[]? _posFields;

	private static readonly string[] Fonts = new string[12]
	{
		"Segoe UI", "Segoe UI Light", "Segoe UI Semibold", "Segoe UI Black", "Bahnschrift", "Consolas", "Georgia", "Impact", "Arial Rounded MT Bold", "Cascadia Code",
		"Trebuchet MS", "Verdana"
	};

	private static readonly string[] Backgrounds = new string[9] { "solid", "gradient", "aurora", "grid", "waves", "tunnel", "carved", "image", "video" };

	internal LayoutCanvas Canvas => _canvas;

	public override string Title => "Screen layout";

	public override string Subtitle => "Design what the 8.8\" screen shows. Click a widget to edit it, drag to move, drag its corner to resize.";

	public LayoutPage(Engine e, MainForm m)
		: base(e, m)
	{
		_layouts = new Select(E.Layouts.Select((ScreenLayout l) => l.Name), E.Settings.Layout)
		{
			Width = Theme.S(210f)
		};
		_layouts.SelectedIndexChanged += delegate
		{
			if (!_building && _layouts.SelectedItem != null)
			{
				lock (E.Settings)
				{
					E.Settings.Layout = _layouts.SelectedItem;
				}
				E.SaveSoon();
				_canvas.ResetHistory();
				_canvas.Selected = null;
				Refresh0();
			}
		};
		_undo = new Btn("Undo", delegate
		{
			_canvas.Undo();
			UpdateButtons();
		}, primary: false, "\ue7a7");
		_redo = new Btn("Redo", delegate
		{
			_canvas.Redo();
			UpdateButtons();
		}, primary: false, "\ue7a6");
		Toggle snap = new Toggle("Snap", on: true);
		snap.CheckedChanged += delegate
		{
			_canvas.Snap = snap.Checked;
		};
		Btn add = new Btn("Add", null, primary: true, "\ue710");
		add.Click += delegate
		{
			AddMenu().Show(add, new Point(0, add.Height));
		};
		Stack stack = new Stack(horizontal: true);
		stack.Controls.AddRange(new Control[14]
		{
			_layouts,
			_undo,
			_redo,
			AlignButton("⇤", "left"),
			AlignButton("↔", "center"),
			AlignButton("⇥", "right"),
			AlignButton("⤒", "top"),
			AlignButton("↕", "middle"),
			AlignButton("⤓", "bottom"),
			snap,
			add,
			new Btn("", delegate
			{
				_canvas.Duplicate();
			}, primary: false, "\ue8c8"),
			new Btn("", delegate
			{
				_canvas.DeleteSelected();
			}, primary: false, "\ue74d"),
			new Btn("Save as", SaveAs, primary: false, "\ue74e")
		});
		Body.Controls.Add(stack);
		Body.SetFlowBreak(stack, value: true);
		_canvas = new LayoutCanvas(E)
		{
			Size = new Size(Theme.S(1074f), Theme.S(286f)),
			Margin = new Padding(0, Theme.S(6f), 0, Theme.S(2f))
		};
		_canvas.Model = () => E.CurrentLayout;
		_canvas.BeginEdit = EnsureEditable;
		_canvas.SelectionChanged += delegate(Widget? w)
		{
			if (!_building)
			{
				if (w != null && w == _propsFor && _posFields != null)
				{
					int[] array = new int[4] { w.X, w.Y, w.W, w.H };
					for (int i = 0; i < 4; i++)
					{
						if (!_posFields[i].Box.Focused && _posFields[i].Text != array[i].ToString())
						{
							_posFields[i].Text = array[i].ToString();
						}
					}
				}
				else
				{
					BuildWidgetProps(w);
				}
			}
		};
		_canvas.Edited += UpdateButtons;
		Body.Controls.Add(_canvas);
		Body.SetFlowBreak(_canvas, value: true);
		_builtIn = new Caption("", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(1074f), Theme.S(40f))
		};
		_builtIn.Text = "Arrows nudge (Shift = 10 px) · Delete removes · Ctrl+D duplicates · Ctrl+C / Ctrl+V copy and paste · Ctrl+Z undoes · hold Alt to drag without snapping. Built-in templates are copied on the first change.";
		Body.Controls.Add(_builtIn);
		Body.SetFlowBreak(_builtIn, value: true);
		_widgetCard = AddCard("Widget", 530);
		_widgetProps = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			BackColor = Theme.Card,
			WrapContents = false
		};
		_widgetCard.Controls.Add(_widgetProps);
		_bgCard = AddCard("Background", 262);
		_layoutCard = AddCard("Layout", 254);
		BuildWidgetProps(null);
		Body.SizeChanged += delegate
		{
			int num = Math.Max(Theme.S(700f), Body.ClientSize.Width - Theme.S(14f));
			_canvas.Size = new Size(num, num / 4 + Theme.S(16f));
			_builtIn.Width = num;
		};
	}

	private Btn AlignButton(string glyph, string how)
	{
		string how2 = how;
		Btn btn = new Btn(glyph, delegate
		{
			_canvas.Align(how2);
		})
		{
			Width = Theme.S(34f)
		};
		new ToolTip().SetToolTip(btn, "Align " + how2);
		return btn;
	}

	private void UpdateButtons()
	{
		_undo.Enabled = _canvas.CanUndo;
		_redo.Enabled = _canvas.CanRedo;
	}

	private ScreenLayout EnsureEditable()
	{
		ScreenLayout currentLayout = E.CurrentLayout;
		if (!currentLayout.BuiltIn)
		{
			return currentLayout;
		}
		ScreenLayout copy = currentLayout.Clone();
		copy.BuiltIn = false;
		copy.Name = UniqueName(currentLayout.Name + " (my copy)");
		lock (E.Settings)
		{
			E.Layouts.Add(copy);
			E.Settings.Layout = copy.Name;
		}
		E.SaveSoon();
		BeginInvoke(delegate
		{
			RefreshLists();
			Main.Toast("Saved as “" + copy.Name + "”");
		});
		return copy;
	}

	private string UniqueName(string name)
	{
		string i = name;
		int num = 2;
		while (E.Layouts.Any((ScreenLayout l) => l.Name == i))
		{
			i = $"{name} {num}";
			num++;
		}
		return i;
	}

	private void SaveAs()
	{
		string text = Prompt.Ask(this, "Save layout as", "Name", UniqueName(E.CurrentLayout.Name + " copy"));
		if (!string.IsNullOrWhiteSpace(text))
		{
			ScreenLayout screenLayout = E.CurrentLayout.Clone();
			screenLayout.BuiltIn = false;
			screenLayout.Name = UniqueName(text.Trim());
			lock (E.Settings)
			{
				E.Layouts.Add(screenLayout);
				E.Settings.Layout = screenLayout.Name;
			}
			E.SaveSoon();
			_canvas.ResetHistory();
			Refresh0();
			Main.Toast("Saved");
		}
	}

	private ContextMenuStrip AddMenu()
	{
		ContextMenuStrip menu = new ContextMenuStrip
		{
			Renderer = new NavyMenuRenderer()
		};
		Item("Text", () => new Widget
		{
			Kind = WidgetKind.Text,
			Text = "Hello",
			W = 360,
			H = 70,
			Size = 44f
		});
		Item("Sensor value", () => new Widget
		{
			Kind = WidgetKind.Value,
			Sensor = "cpu.load",
			W = 300,
			H = 120,
			Size = 96f
		});
		Item("Clock", () => new Widget
		{
			Kind = WidgetKind.Text,
			Text = "{time:HH:mm}",
			W = 400,
			H = 140,
			Size = 110f
		});
		Item("Date", () => new Widget
		{
			Kind = WidgetKind.Text,
			Text = "{date:dddd d MMMM}",
			W = 460,
			H = 50,
			Size = 30f,
			Bold = false
		});
		Item("Gauge", () => new Widget
		{
			Kind = WidgetKind.Gauge,
			Sensor = "gpu.load",
			W = 260,
			H = 260,
			Size = 60f,
			Thickness = 16f,
			Color = "#29d3ff",
			Color2 = "#2f6bff",
			Text = "LOAD",
			Align = "center"
		});
		Item("Ring", () => new Widget
		{
			Kind = WidgetKind.Ring,
			Sensor = "cpu.load",
			W = 220,
			H = 220,
			Size = 52f,
			Thickness = 14f,
			Color = "#7c5cff",
			Color2 = "#3fa9ff",
			Text = "CPU",
			Align = "center"
		});
		Item("Bar", () => new Widget
		{
			Kind = WidgetKind.Bar,
			Sensor = "ram.load",
			W = 500,
			H = 16,
			Color = "#7c5cff",
			Color2 = "#3fa9ff",
			Radius = 8f
		});
		Item("Graph", () => new Widget
		{
			Kind = WidgetKind.Graph,
			Sensor = "cpu.load",
			W = 600,
			H = 120,
			Color = "#3fa9ff",
			Thickness = 6f
		});
		Item("CPU threads", () => new Widget
		{
			Kind = WidgetKind.CoreBars,
			W = 560,
			H = 110,
			Color = "#2f8cff",
			Color2 = "#ff5a3c",
			Track = "#14ffffff"
		});
		Item("Panel", () => new Widget
		{
			Kind = WidgetKind.Panel,
			W = 600,
			H = 440,
			Color = "#401a2a6b",
			Color2 = "#26101840",
			Track = "#3384b4ff",
			Thickness = 1.5f,
			Radius = 22f,
			Text = "TITLE",
			Size = 18f,
			WarnColor = "#5fd0ff"
		});
		Item("Image / GIF", () => new Widget
		{
			Kind = WidgetKind.Image,
			W = 400,
			H = 400
		});
		Item("Divider", () => new Widget
		{
			Kind = WidgetKind.Divider,
			W = 600,
			H = 10,
			Color = "#33ffffff",
			Thickness = 2f
		});
		return menu;
		void Item(string text, Func<Widget> make)
		{
			Func<Widget> make2 = make;
			menu.Items.Add(text, null, delegate
			{
				Widget widget = make2();
				widget.X = 960 - widget.W / 2;
				widget.Y = Math.Max(0, 240 - widget.H / 2);
				_canvas.Add(widget);
			});
		}
	}

	public override void Refresh0()
	{
		RefreshLists();
		BuildBackground();
		BuildLayoutCard();
		BuildWidgetProps(_canvas.Selected);
		UpdateButtons();
		_canvas.Tick(force: true);
	}

	private void RefreshLists()
	{
		_building = true;
		_layouts.Items.Clear();
		_layouts.Items.AddRange(from l in E.VisibleLayouts()
			select l.Name);
		_layouts.SetSilently(E.Settings.Layout);
		_building = false;
	}

	public override void Tick()
	{
		_canvas.Tick();
	}

	private void BuildWidgetProps(Widget? w)
	{
		_building = true;
		_propsFor = w;
		_posFields = null;
		_widgetProps.SuspendLayout();
		foreach (Control control in _widgetProps.Controls)
		{
			control.Dispose();
		}
		_widgetProps.Controls.Clear();
		if (w == null)
		{
			_widgetProps.Controls.Add(new Caption("Select a widget on the screen above, or use Add.", Theme.SubText)
			{
				AutoSize = false,
				Size = new Size(Theme.S(500f), Theme.S(30f))
			});
			_widgetCard.Title = "Widget";
			_widgetProps.ResumeLayout();
			_building = false;
			return;
		}
		_widgetCard.Title = "Widget · " + w.Kind;
		FormGrid formGrid = new FormGrid(110);
		Stack stack = new Stack(horizontal: true);
		stack.Controls.Add(Num("X", w.X, delegate(Widget x, double v)
		{
			x.X = (int)v;
		}));
		stack.Controls.Add(Num("Y", w.Y, delegate(Widget x, double v)
		{
			x.Y = (int)v;
		}));
		stack.Controls.Add(Num("W", w.W, delegate(Widget x, double v)
		{
			x.W = Math.Max(4, (int)v);
		}));
		stack.Controls.Add(Num("H", w.H, delegate(Widget x, double v)
		{
			x.H = Math.Max(4, (int)v);
		}));
		formGrid.Add("Position", stack);
		_posFields = (from Control c in stack.Controls
			select c.Controls.OfType<TextField>().First()).ToArray();
		WidgetKind kind = w.Kind;
		bool flag = (uint)(kind - 1) <= 4u;
		bool flag2 = flag;
		kind = w.Kind;
		if ((kind == WidgetKind.Text || kind == WidgetKind.Panel) ? true : false)
		{
			TextField text = new TextField(w.Text, 280);
			text.Box.TextChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Text = text.Text;
				});
			};
			formGrid.Add((w.Kind == WidgetKind.Panel) ? "Title" : "Text", text);
			Select insert = new Select(new string[1] { "Insert reading…" }.Concat(Sensors.Catalog.Select((SensorDef d) => d.Key)).Concat(new string[8] { "time", "date", "cpu.name", "gpu.name", "uptime", "group1.rpm", "group2.rpm", "group3.rpm" }), "Insert reading…")
			{
				Width = Theme.S(280f)
			};
			insert.Display = delegate(string k)
			{
				string j = k;
				object obj;
				if (!(j == "Insert reading…"))
				{
					obj = Sensors.Catalog.FirstOrDefault((SensorDef d) => d.Key == j)?.Label;
					if (obj == null)
					{
						return j;
					}
				}
				else
				{
					obj = j;
				}
				return (string)obj;
			};
			insert.SelectedIndexChanged += delegate
			{
				if (insert.SelectedIndex > 0)
				{
					string selectedItem = insert.SelectedItem;
					string text4;
					switch (selectedItem)
					{
					case "time":
						text4 = "{time:HH:mm}";
						break;
					case "date":
						text4 = "{date:dddd d MMMM}";
						break;
					case "cpu.name":
					case "gpu.name":
					case "uptime":
						text4 = "{" + selectedItem + "}";
						break;
					default:
						text4 = "{" + selectedItem + ":0}";
						break;
					}
					string text5 = text4;
					text.Text += text5;
					insert.SetSilently("Insert reading…");
				}
			};
			formGrid.Add("", insert);
		}
		if (flag2)
		{
			formGrid.Add("Reading", SensorSelect(w.Sensor, delegate(string k)
			{
				string i = k;
				Set(delegate(Widget x)
				{
					x.Sensor = i;
					SensorDef sensorDef = Sensors.Def(i);
					x.Min = sensorDef.Min;
					x.Max = sensorDef.Max;
				});
			}));
			if (w.Kind != WidgetKind.Graph && w.Kind != WidgetKind.Bar)
			{
				Select fmt = new Select(new string[5] { "0", "0.0", "0.00", "HH:mm", "HH:mm:ss" }, w.Format)
				{
					Width = Theme.S(110f)
				};
				fmt.SelectedIndexChanged += delegate
				{
					Set(delegate(Widget x)
					{
						x.Format = fmt.SelectedItem ?? "0";
					});
				};
				formGrid.Add("Decimals", fmt);
			}
			Stack stack2 = new Stack(horizontal: true);
			stack2.Controls.Add(Num("Min", w.Min, delegate(Widget x, double v)
			{
				x.Min = (float)v;
			}));
			stack2.Controls.Add(Num("Max", w.Max, delegate(Widget x, double v)
			{
				x.Max = (float)v;
			}));
			formGrid.Add("Range", stack2);
		}
		kind = w.Kind;
		if ((uint)(kind - 2) <= 1u)
		{
			TextField label = new TextField(w.Text);
			label.Box.TextChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Text = label.Text;
				});
			};
			formGrid.Add("Label", label);
		}
		if (w.Kind == WidgetKind.Value)
		{
			TextField unit = new TextField(w.Text, 120);
			unit.Box.TextChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Text = unit.Text;
				});
			};
			formGrid.Add("Unit (blank = auto)", unit);
		}
		kind = w.Kind;
		if (((uint)kind <= 3u || kind == WidgetKind.Panel) ? true : false)
		{
			Select font = new Select(Fonts, w.Font)
			{
				Width = Theme.S(190f)
			};
			font.SelectedIndexChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Font = font.SelectedItem ?? "Segoe UI";
				});
			};
			Toggle bold = new Toggle("Bold", w.Bold);
			bold.CheckedChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Bold = bold.Checked;
				});
			};
			formGrid.Add("Font", new Stack(horizontal: true)
			{
				Controls = 
				{
					(Control?)font,
					(Control?)bold
				}
			});
			formGrid.Add("Text size", SliderFor(6, 320, (int)w.Size, delegate(Widget x, float v)
			{
				x.Size = v;
			}));
			kind = w.Kind;
			if ((uint)kind <= 1u)
			{
				Select align = new Select(new string[3] { "left", "center", "right" }, w.Align)
				{
					Width = Theme.S(110f)
				};
				align.SelectedIndexChanged += delegate
				{
					Set(delegate(Widget x)
					{
						x.Align = align.SelectedItem ?? "left";
					});
				};
				formGrid.Add("Align", align);
			}
		}
		(string, string, string) tuple;
		switch (w.Kind)
		{
		case WidgetKind.Panel:
			tuple = ("Top fill", "Bottom fill", "Border");
			break;
		case WidgetKind.CoreBars:
			tuple = ("Cool", "Hot", "Track");
			break;
		case WidgetKind.Text:
		case WidgetKind.Value:
			tuple = ("Colour", "Glow", "");
			break;
		case WidgetKind.Graph:
		case WidgetKind.Image:
		case WidgetKind.Divider:
			tuple = ("Colour", "", "");
			break;
		default:
			tuple = ("Colour", "Gradient", "Track");
			break;
		}
		var (label2, text2, text3) = tuple;
		if (w.Kind != WidgetKind.Image)
		{
			formGrid.Add(label2, ColorFor(w.Color, delegate(Widget x, string v)
			{
				x.Color = v;
			}));
		}
		if (text2.Length > 0)
		{
			formGrid.Add(text2, ColorFor(w.Color2, delegate(Widget x, string v)
			{
				x.Color2 = v;
			}));
		}
		if (text3.Length > 0)
		{
			formGrid.Add(text3, ColorFor(w.Track, delegate(Widget x, string v)
			{
				x.Track = v;
			}));
		}
		if (w.Kind == WidgetKind.Panel)
		{
			formGrid.Add("Title colour", ColorFor(w.WarnColor, delegate(Widget x, string v)
			{
				x.WarnColor = v;
			}));
		}
		switch (w.Kind)
		{
		case WidgetKind.Gauge:
		case WidgetKind.Ring:
		case WidgetKind.Graph:
		case WidgetKind.Panel:
		case WidgetKind.Divider:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			formGrid.Add((w.Kind == WidgetKind.Panel) ? "Border width" : "Thickness", SliderFor(0, 60, (int)w.Thickness, delegate(Widget x, float v)
			{
				x.Thickness = v;
			}));
		}
		kind = w.Kind;
		if ((kind == WidgetKind.Bar || kind == WidgetKind.Panel) ? true : false)
		{
			formGrid.Add("Corner radius", SliderFor(0, 120, (int)w.Radius, delegate(Widget x, float v)
			{
				x.Radius = v;
			}));
		}
		kind = w.Kind;
		if ((uint)kind <= 1u)
		{
			Select effect = new Select(new string[3] { "", "inflated", "vinyl" }, w.Effect)
			{
				Width = Theme.S(150f),
				Display = delegate(string v)
				{
					if (v != null && v.Length == 0)
					{
						return "Flat";
					}
					return (v == "inflated") ? "Balloon" : "Vinyl (quilted)";
				}
			};
			effect.SelectedIndexChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Effect = effect.SelectedItem ?? "";
				});
			};
			Toggle flt = new Toggle("Float", w.Float);
			flt.CheckedChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Float = flt.Checked;
				});
			};
			formGrid.Add("Style", new Stack(horizontal: true)
			{
				Controls = 
				{
					(Control?)effect,
					(Control?)flt
				}
			});
			TextField print = new TextField(w.Print);
			print.Box.TextChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Print = print.Text;
				});
			};
			formGrid.Add("Printed tag (\\n = new line)", print);
		}
		kind = w.Kind;
		if ((uint)kind <= 3u)
		{
			Toggle glow = new Toggle("Glow", w.Glow);
			glow.CheckedChanged += delegate
			{
				Set(delegate(Widget x)
				{
					x.Glow = glow.Checked;
				});
			};
			formGrid.Add("", glow);
		}
		kind = w.Kind;
		if ((uint)(kind - 1) <= 3u)
		{
			Stack stack3 = new Stack(horizontal: true);
			stack3.Controls.Add(Num("at", w.WarnAt, delegate(Widget x, double v)
			{
				x.WarnAt = (float)v;
			}));
			stack3.Controls.Add(ColorFor(w.WarnColor, delegate(Widget x, string v)
			{
				x.WarnColor = v;
			}));
			formGrid.Add("Warn colour", stack3);
		}
		if (w.Kind == WidgetKind.Image)
		{
			Caption value = new Caption((w.Image == null) ? "(none)" : Path.GetFileName(w.Image), Theme.Text, null, 200);
			formGrid.Add("Image / GIF", new Stack(horizontal: true)
			{
				Controls = 
				{
					(Control?)value,
					(Control?)new Btn("Upload", delegate
					{
						string f = PickImage();
						if (f != null)
						{
							Set(delegate(Widget x)
							{
								x.Image = f;
							});
						}
						BuildWidgetProps(_canvas.Selected);
					}, primary: false, "\ue898")
				}
			});
		}
		Stack c2 = new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)new Btn("Bring to front", delegate
				{
					_canvas.BringForward(toFront: true);
				}),
				(Control?)new Btn("Send to back", delegate
				{
					_canvas.BringForward(toFront: false);
				}),
				(Control?)new Btn("Delete", delegate
				{
					_canvas.DeleteSelected();
				}, primary: false, "\ue74d")
			}
		};
		formGrid.AddWide(c2);
		_widgetProps.Controls.Add(formGrid);
		_widgetProps.ResumeLayout();
		_building = false;
	}

	private void Set(Action<Widget> change)
	{
		if (!_building)
		{
			_canvas.EditSelected(change);
		}
	}

	private Control Num(string label, double value, Action<Widget, double> apply)
	{
		Action<Widget, double> apply2 = apply;
		TextField f = new TextField(value.ToString("0.##", CultureInfo.InvariantCulture), 62)
		{
			Margin = new Padding(0, 0, Theme.S(6f), Theme.S(4f))
		};
		f.Box.Leave += delegate
		{
			Commit();
		};
		f.Box.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				Commit();
				e.SuppressKeyPress = true;
			}
		};
		return new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)new Caption(label, Theme.Faint, Theme.Small)
				{
					Margin = new Padding(0, Theme.S(8f), Theme.S(3f), 0)
				},
				(Control?)f
			}
		};
		void Commit()
		{
			if (double.TryParse(f.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
			{
				Set(delegate(Widget w)
				{
					apply2(w, v);
				});
			}
		}
	}

	private Slider SliderFor(int min, int max, int value, Action<Widget, float> apply)
	{
		Action<Widget, float> apply2 = apply;
		Slider s = new Slider(min, max, value)
		{
			Width = Theme.S(280f)
		};
		s.ValueChanged += delegate
		{
			Set(delegate(Widget w)
			{
				apply2(w, s.Value);
			});
		};
		return s;
	}

	private ColorField ColorFor(string hex, Action<Widget, string> apply)
	{
		Action<Widget, string> apply2 = apply;
		ColorField c = new ColorField(hex);
		c.ColorChanged += delegate
		{
			Set(delegate(Widget w)
			{
				apply2(w, c.Hex);
			});
		};
		return c;
	}

	private static Select SensorSelect(string current, Action<string> apply)
	{
		Action<string> apply2 = apply;
		Select s = new Select(Sensors.Catalog.Select((SensorDef d) => d.Key).Append("time"), current)
		{
			Width = Theme.S(280f),
			Display = (string k) => (!(k == "time")) ? (Sensors.Def(k).Label + " (" + Sensors.Def(k).Unit + ")") : "Time"
		};
		s.SelectedIndexChanged += delegate
		{
			if (s.SelectedItem != null)
			{
				apply2(s.SelectedItem);
			}
		};
		return s;
	}

	private string? PickImage()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Images and GIFs|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
		};
		return (openFileDialog.ShowDialog(this) == DialogResult.OK) ? Store.Import(openFileDialog.FileName) : null;
	}

	private void BuildBackground()
	{
		_bgCard.Controls.Clear();
		ScreenLayout currentLayout = E.CurrentLayout;
		FormGrid formGrid = new FormGrid(70);
		Select style = new Select(Backgrounds, currentLayout.Background)
		{
			Width = Theme.S(150f)
		};
		style.SelectedIndexChanged += delegate
		{
			_canvas.Mutate(delegate(ScreenLayout x)
			{
				x.Background = style.SelectedItem ?? "gradient";
			});
		};
		formGrid.Add("Style", style);
		ColorField c1 = new ColorField(currentLayout.Color1);
		c1.ColorChanged += delegate
		{
			_canvas.Mutate(delegate(ScreenLayout x)
			{
				x.Color1 = c1.Hex;
			});
		};
		ColorField c2 = new ColorField(currentLayout.Color2);
		c2.ColorChanged += delegate
		{
			_canvas.Mutate(delegate(ScreenLayout x)
			{
				x.Color2 = c2.Hex;
			});
		};
		ColorField ac = new ColorField(currentLayout.Accent);
		ac.ColorChanged += delegate
		{
			_canvas.Mutate(delegate(ScreenLayout x)
			{
				x.Accent = ac.Hex;
			});
		};
		formGrid.Add("Colour 1", c1);
		formGrid.Add("Colour 2", c2);
		formGrid.Add("Accent", ac);
		formGrid.Add("Image", new Btn("Upload…", delegate
		{
			string f = PickImage();
			if (f != null)
			{
				_canvas.Mutate(delegate(ScreenLayout x)
				{
					x.Image = f;
					x.Background = "image";
				});
			}
			BuildBackground();
		}, primary: false, "\ue898"));
		formGrid.Add("Video", new Btn("Frames folder…", delegate
		{
			FolderBrowserDialog dlg = new FolderBrowserDialog
			{
				Description = "Folder of 1920×480 JPEG frames (made with LumaLi.exe --make-frames)",
				InitialDirectory = Store.Uploads
			};
			try
			{
				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					_canvas.Mutate(delegate(ScreenLayout x)
					{
						x.Image = dlg.SelectedPath;
						x.Background = "video";
					});
				}
				BuildBackground();
			}
			finally
			{
				if (dlg != null)
				{
					((IDisposable)dlg).Dispose();
				}
			}
		}, primary: false, "\ue838"));
		Slider op = new Slider(10, 100, (int)(currentLayout.ImageOpacity * 100f))
		{
			Width = Theme.S(160f)
		};
		op.ValueChanged += delegate
		{
			_canvas.Mutate(delegate(ScreenLayout x)
			{
				x.ImageOpacity = (float)op.Value / 100f;
			});
		};
		formGrid.Add("Opacity", op);
		_bgCard.Controls.Add(formGrid);
	}

	private void BuildLayoutCard()
	{
		_layoutCard.Controls.Clear();
		ScreenLayout i = E.CurrentLayout;
		Stack stack = new Stack();
		TextField name = new TextField(i.Name, 220);
		name.Enabled = !i.BuiltIn;
		name.Box.Leave += delegate
		{
			string j = name.Text.Trim();
			if (i.BuiltIn || j.Length == 0 || j == i.Name || E.Layouts.Any((ScreenLayout x) => x.Name == j))
			{
				name.Text = i.Name;
			}
			else
			{
				lock (E.Settings)
				{
					i.Name = j;
					E.Settings.Layout = j;
				}
				E.SaveSoon();
				RefreshLists();
			}
		};
		stack.Controls.Add(new Caption(i.BuiltIn ? "Built-in template (edits make a copy)" : "Name", Theme.SubText, Theme.Small));
		stack.Controls.Add(name);
		stack.Controls.Add(new Btn("New blank layout", NewBlank, primary: false, "\ue710"));
		stack.Controls.Add(new Btn("Export…", Export, primary: false, "\ue74e"));
		stack.Controls.Add(new Btn("Import…", Import, primary: false, "\ue838"));
		stack.Controls.Add(new Btn("Remove layout", DeleteLayout, primary: false, "\ue74d"));
		_layoutCard.Controls.Add(stack);
	}

	private void NewBlank()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = UniqueName("New layout"),
			Background = "gradient"
		};
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text,
			Text = "{time:HH:mm}",
			X = 60,
			Y = 140,
			W = 600,
			H = 200,
			Size = 160f
		});
		lock (E.Settings)
		{
			E.Layouts.Add(screenLayout);
			E.Settings.Layout = screenLayout.Name;
		}
		E.SaveSoon();
		_canvas.ResetHistory();
		Refresh0();
	}

	private void Export()
	{
		ScreenLayout currentLayout = E.CurrentLayout;
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "LumaLi layout|*.json",
			FileName = currentLayout.Name + ".json"
		};
		if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			File.WriteAllText(saveFileDialog.FileName, Store.ToJson(currentLayout));
			Main.Toast("Exported");
		}
	}

	public void Import()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "LumaLi layout|*.json"
		};
		if (openFileDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		try
		{
			ScreenLayout screenLayout = Store.LayoutFromJson(File.ReadAllText(openFileDialog.FileName)) ?? throw new InvalidDataException("Empty file");
			screenLayout.BuiltIn = false;
			screenLayout.Name = UniqueName(screenLayout.Name);
			lock (E.Settings)
			{
				E.Layouts.Add(screenLayout);
				E.Settings.Layout = screenLayout.Name;
			}
			E.SaveSoon();
			Refresh0();
			Main.Toast("Imported");
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "That file is not a layout: " + ex.Message, "Import");
		}
	}

	private void DeleteLayout()
	{
		if (TemplatesPage.Remove(Main, E, this, E.CurrentLayout.Name))
		{
			_canvas.Selected = null;
			Refresh0();
		}
	}
}
