using System;
using System.Drawing;
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

	private readonly Select _background;

	private readonly TextField _label;

	private readonly ColorField _c1;

	private readonly ColorField _c2;

	private readonly Caption _c1Label;

	private readonly Caption _c2Label;

	private readonly Toggle _enabled;

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

	private void Upload()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Images and GIFs|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
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
		_background.Items.Clear();
		_background.Items.Add("");
		try
		{
			if (Directory.Exists(Store.Uploads))
			{
				_background.Items.AddRange(from f in Directory.GetFiles(Store.Uploads)
					where new string[5] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(Path.GetExtension(f).ToLowerInvariant())
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
