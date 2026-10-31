using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

internal sealed class FanCard : Card
{
	private readonly Engine E;

	private readonly MainForm _main;

	private readonly FansPage _page;

	private readonly FanScreen _fan;

	private readonly string _serial;

	private readonly FanPreview _preview;

	private readonly Select _face;

	private readonly Select _sensor;

	private readonly Select _rotation;

	private readonly Select _style;

	private readonly Caption _styleLabel;

	private readonly Select _background;

	private readonly TextField _label;

	private readonly ColorField _c1;

	private readonly ColorField _c2;

	private readonly Caption _c1Label;

	private readonly Caption _c2Label;

	private readonly Toggle _enabled;

	private readonly Caption _warnLabel;

	private readonly Stack _warnRow;

	private readonly TextField _warnAt;

	private readonly Caption _warnUnit;

	private readonly ColorField _warnColor;

	private bool _loading;

	public FanCard(Engine e, MainForm main, string serial, string name, FansPage page)
		: base(name)
	{
		E = e;
		_main = main;
		_page = page;
		_serial = serial;
		_fan = e.Settings.FanFor(serial);
		base.Width = Theme.S(350f);
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		MinimumSize = new Size(Theme.S(350f), 0);
		MaximumSize = new Size(Theme.S(350f), 0);
		_preview = new FanPreview(e)
		{
			Serial = serial,
			Fan = _fan,
			Size = new Size(Theme.S(318f), Theme.S(200f))
		};
		FormGrid formGrid = new FormGrid(92);
		_face = new Select(Faces.Names, _fan.Face)
		{
			Width = Theme.S(210f)
		};
		_face.SelectedIndexChanged += delegate
		{
			if (!_loading && _face.SelectedItem != null)
			{
				var (a, b) = Faces.DefaultColors(_face.SelectedItem);
				Change(delegate(FanScreen f)
				{
					f.Face = _face.SelectedItem;
					f.Color1 = a;
					f.Color2 = b;
				});
				RefreshValues();
			}
		};
		_sensor = new Select(Sensors.Catalog.Select((SensorDef d) => d.Key).Append("time"), _fan.Sensor)
		{
			Width = Theme.S(210f)
		};
		_sensor.Display = (string k) => (!(k == "time")) ? (Sensors.Def(k).Label + " (" + Sensors.Def(k).Unit + ")") : "Time";
		_sensor.SelectedIndexChanged += delegate
		{
			if (!_loading && _sensor.SelectedItem != null)
			{
				Change(delegate(FanScreen f)
				{
					f.Sensor = _sensor.SelectedItem;
				});
				_warnUnit.Text = WarnUnit(_fan.Sensor);
			}
		};
		_label = new TextField(_fan.Label, 210);
		_label.Box.TextChanged += delegate
		{
			if (!_loading)
			{
				Change(delegate(FanScreen f)
				{
					f.Label = _label.Text;
				});
			}
		};
		_c1 = new ColorField(_fan.Color1);
		_c1.ColorChanged += delegate
		{
			if (!_loading)
			{
				Change(delegate(FanScreen f)
				{
					f.Color1 = _c1.Hex;
				});
			}
		};
		_c2 = new ColorField(_fan.Color2);
		_c2.ColorChanged += delegate
		{
			if (!_loading)
			{
				Change(delegate(FanScreen f)
				{
					f.Color2 = _c2.Hex;
				});
			}
		};
		_style = new Select(Faces.GaugeStyles, _fan.GaugeStyle)
		{
			Width = Theme.S(130f)
		};
		_style.SelectedIndexChanged += delegate
		{
			if (!_loading && _style.SelectedItem != null)
			{
				Change(delegate(FanScreen f)
				{
					f.GaugeStyle = _style.SelectedItem;
				});
			}
		};
		_styleLabel = new Caption("Style", null, null, 90);
		// the gauge turns this colour once the reading reaches the level; empty or 0 is off
		_warnAt = new TextField(WarnText(_fan.WarnAt), 56);
		_warnAt.Box.TextChanged += delegate
		{
			if (!_loading)
			{
				string t = _warnAt.Text.Trim();
				if (t.Length == 0)
				{
					Change(delegate(FanScreen f)
					{
						f.WarnAt = 0f;
					});
				}
				else if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 0f)
				{
					Change(delegate(FanScreen f)
					{
						f.WarnAt = v;
					});
				}
			}
		};
		_warnUnit = new Caption("", Theme.Faint, Theme.Small)
		{
			Margin = new Padding(Theme.S(3f), Theme.S(8f), Theme.S(8f), 0)
		};
		_warnColor = new ColorField(_fan.WarnColor, 84);
		_warnColor.ColorChanged += delegate
		{
			if (!_loading)
			{
				Change(delegate(FanScreen f)
				{
					f.WarnColor = _warnColor.Hex;
				});
			}
		};
		_warnLabel = new Caption("Warn at", null, null, 90);
		_warnRow = new Stack(horizontal: true)
		{
			Controls =
			{
				(Control?)_warnAt,
				(Control?)_warnUnit,
				(Control?)_warnColor
			}
		};
		_rotation = new Select(new string[4] { "0", "90", "180", "270" }, _fan.Rotation.ToString())
		{
			Width = Theme.S(100f),
			Display = (string v) => v + "°"
		};
		_rotation.SelectedIndexChanged += delegate
		{
			if (!_loading)
			{
				Change(delegate(FanScreen f)
				{
					f.Rotation = int.Parse(_rotation.SelectedItem ?? "0");
				});
			}
		};
		_background = new Select
		{
			Width = Theme.S(130f)
		};
		_background.Display = (string v) => (!(v == "")) ? Path.GetFileName(v) : "(none)";
		_background.SelectedIndexChanged += delegate
		{
			if (!_loading)
			{
				string v2 = _background.SelectedItem;
				Change(delegate(FanScreen f)
				{
					f.Background = (string.IsNullOrEmpty(v2) ? null : v2);
				});
			}
		};
		_enabled = new Toggle("Show on this fan", _fan.Enabled);
		_enabled.CheckedChanged += delegate
		{
			if (!_loading)
			{
				Change(delegate(FanScreen f)
				{
					f.Enabled = _enabled.Checked;
				});
			}
		};
		_c1Label = new Caption("", null, null, 90);
		_c2Label = new Caption("", null, null, 90);
		formGrid.Add("Face", _face);
		formGrid.Add("Sensor", _sensor);
		// the gauge's own look, for the Lottie Gauge face until a Lottie is uploaded
		formGrid.Controls.Add(_styleLabel);
		formGrid.Controls.Add(_style);
		formGrid.Controls.Add(_warnLabel);
		formGrid.Controls.Add(_warnRow);
		formGrid.Add("Label", _label);
		formGrid.Controls.Add(_c1Label);
		formGrid.Controls.Add(_c1);
		formGrid.Controls.Add(_c2Label);
		formGrid.Controls.Add(_c2);
		formGrid.Add("Rotation", _rotation);
		formGrid.Add("Background", new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)_background,
				(Control?)new Btn("Upload", Upload, primary: false, "\ue898")
			}
		});
		formGrid.AddWide(_enabled);
		formGrid.AddWide(new Btn("Use this look on all fans", delegate
		{
			_page.CopyLookToAll(_fan);
		}));
		base.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_preview,
				(Control?)formGrid
			}
		});
		RefreshValues();
	}

	private void Change(Action<FanScreen> change)
	{
		lock (E.Settings)
		{
			change(_fan);
		}
		E.SaveSoon();
	}

	private static string WarnText(float v)
	{
		return v > 0f ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";
	}

	private static string WarnUnit(string sensor)
	{
		return sensor == "time" ? "" : Sensors.Def(sensor).Unit;
	}

	private void Upload()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Images, GIFs and Lottie animations|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.json;*.lottie"
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			string path = Store.Import(openFileDialog.FileName);
			Change(delegate(FanScreen f)
			{
				f.Background = path;
				_ = f.Face == "Image / GIF";
			});
			RefreshValues();
		}
	}

	public void RefreshValues()
	{
		_loading = true;
		_face.SetSilently(_fan.Face);
		_sensor.SetSilently(_fan.Sensor);
		if (_label.Text != _fan.Label)
		{
			_label.Text = _fan.Label;
		}
		_c1.SetSilently(_fan.Color1);
		_c2.SetSilently(_fan.Color2);
		var (text, text2) = Faces.ColorLabels(_fan.Face);
		_c1Label.Text = text;
		_c2Label.Text = text2;
		_rotation.SetSilently(_fan.Rotation.ToString());
		_style.SetSilently(_fan.GaugeStyle);
		_style.Visible = _styleLabel.Visible = _fan.Face == "Lottie Gauge";
		_warnRow.Visible = _warnLabel.Visible = _fan.Face == "Lottie Gauge";
		if (!float.TryParse(_warnAt.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float shown) || shown != _fan.WarnAt)
		{
			_warnAt.Text = WarnText(_fan.WarnAt);
		}
		_warnUnit.Text = WarnUnit(_fan.Sensor);
		_warnColor.SetSilently(_fan.WarnColor);
		_background.Items.Clear();
		_background.Items.Add("");
		try
		{
			if (Directory.Exists(Store.Uploads))
			{
				_background.Items.AddRange(from f in Directory.GetFiles(Store.Uploads)
					where new string[7] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".json", ".lottie" }.Contains(Path.GetExtension(f).ToLowerInvariant())
					select f);
			}
		}
		catch
		{
		}
		if (_fan.Background != null && !_background.Items.Contains(_fan.Background))
		{
			_background.Items.Add(_fan.Background);
		}
		_background.SetSilently(_fan.Background ?? "");
		_enabled.SetSilently(_fan.Enabled);
		_loading = false;
	}

	public void Tick()
	{
		_preview.Tick();
		OutputStatus outputStatus = E.FanOutputs.FirstOrDefault((OutputStatus f) => f.Serial == _serial);
		string text = ((outputStatus == null) ? "not connected" : (outputStatus.Running ? $"connected · {outputStatus.Fps:0} fps" : outputStatus.State));
		if (text != base.RightText)
		{
			base.RightText = text;
			Invalidate(new Rectangle(0, 0, base.Width, Theme.S(36f)));
		}
	}
}
