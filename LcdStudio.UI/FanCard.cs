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

	private readonly Caption _playlistState;

	private void EditPlaylist()
	{
		var uploads = _background.Items.Cast<string>().Where((string v) => v.Length > 0).ToList();
		FanScreen copy = new FanScreen();
		lock (E.Settings)
		{
			copy.CopyLookFrom(_fan);
		}
		if (!PlaylistDialog.Edit(this, copy, uploads))
		{
			return;
		}
		Change(delegate(FanScreen f)
		{
			f.Playlist = copy.Playlist;
			f.Shuffle = copy.Shuffle;
			f.PlaylistSeconds = copy.PlaylistSeconds;
		});
		_main.Toast(copy.HasPlaylist ? $"Playlist of {copy.Playlist.Count}{(copy.Shuffle ? ", shuffled" : "")}, each {copy.PlaylistSeconds} s" : copy.Playlist.Count == 1 ? "One upload chosen: it shows on its own" : "Playlist off");
		RefreshValues();
	}

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
		// the fan's number: moving it renumbers the fans (and what Identify shows) to match where they sit
		int count = Math.Max(1, E.FanOutputs.Count > 0 ? E.FanOutputs.Count : E.Settings.Fans.Count);
		int mine = Math.Max(0, (E.FanOutputs.Count > 0 ? E.FanOutputs.Select((OutputStatus f) => f.Serial).ToList() : E.Settings.Fans.Select((FanScreen f) => f.Serial).ToList()).FindIndex((string s) => s.Equals(serial, StringComparison.OrdinalIgnoreCase)));
		Select position = new Select(Enumerable.Range(1, count).Select((int n) => n.ToString()), (mine + 1).ToString())
		{
			Width = Theme.S(100f),
			Display = (string v) => "Fan " + v
		};
		position.SelectedIndexChanged += delegate
		{
			if (!_loading && int.TryParse(position.SelectedItem, out int to) && to != mine + 1)
			{
				E.MoveFan(_serial, to - 1);
				_main.Toast($"This fan is now Fan {to}; use Identify fans to check");
				_page.BeginInvoke(new Action(_page.Rebuild));
			}
		};
		formGrid.Add("Position", position);
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
		_background.Width = Theme.S(210f);
		formGrid.Add("Background", _background);
		formGrid.Add("", new Stack(horizontal: true)
		{
			Controls =
			{
				(Control?)new Btn("Upload", Upload, primary: false, "\ue898"),
				(Control?)new Btn("Delete", DeleteUpload, primary: false, "\ue74d")
			}
		});
		// a playlist: the background takes turns between several uploads, in order or shuffled
		_playlistState = new Caption("", Theme.SubText, Theme.Small)
		{
			Margin = new Padding(Theme.S(4f), Theme.S(8f), 0, 0)
		};
		formGrid.Add("Playlist", new Stack(horizontal: true)
		{
			Controls =
			{
				(Control?)new Btn("Choose\u2026", EditPlaylist, primary: false, "\ue8b1"),
				(Control?)_playlistState
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
			Filter = "Images, GIFs, Lottie animations and videos|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.json;*.lottie;" + VideoClip.DialogPattern
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			string path = Store.Import(openFileDialog.FileName);
			if (VideoClip.IsVideo(path) && !PrepareVideo(this, _main, path))
			{
				return;
			}
			Change(delegate(FanScreen f)
			{
				f.Background = path;
				_ = f.Face == "Image / GIF";
			});
			RefreshValues();
		}
	}

	/// <summary>
	/// Decodes a just-uploaded video into its frames while the cursor waits, so it plays at once. False (and the file
	/// removed again) if Windows can't decode it.
	/// </summary>
	internal static bool PrepareVideo(IWin32Window owner, MainForm main, string path)
	{
		int frames;
		Cursor.Current = Cursors.WaitCursor;
		main.Toast("Preparing the video…");
		try
		{
			frames = VideoClip.Prepare(path);
		}
		catch
		{
			frames = 0;
		}
		finally
		{
			Cursor.Current = Cursors.Default;
		}
		if (frames == 0)
		{
			VideoClip.Forget(path);
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
			MessageBox.Show(owner, "Windows couldn't play this video. MP4 (H.264) works everywhere; other formats need their codec installed.", "Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}
		main.Toast($"Video ready: {frames} frames" + (frames >= VideoClip.MaxSeconds * 24 ? $" (the first {VideoClip.MaxSeconds:0} seconds)" : ""));
		return true;
	}

	/// <summary>Deletes the upload chosen in Background from the uploads folder, after asking.</summary>
	private void DeleteUpload()
	{
		string path = _background.SelectedItem ?? "";
		if (path.Length == 0)
		{
			_main.Toast("Choose an upload under Background first");
			return;
		}
		string name = Path.GetFileName(path);
		string? layout;
		int fans;
		lock (E.Settings)
		{
			layout = E.Layouts.FirstOrDefault((ScreenLayout l) => Same(l.Image, path) || l.Widgets.Any((Widget w) => Same(w.Image, path)))?.Name;
			if (layout == null && Same(E.Settings.Standby?.Logo, path))
			{
				layout = "the standby screen";
			}
			fans = E.Settings.Fans.Count((FanScreen f) => Same(f.Background, path) || f.Playlist.Any((string p) => Same(p, path)));
		}
		if (layout != null)
		{
			MessageBox.Show(this, $"\"{name}\" is also used by {layout}. Remove it there first.", "Delete upload", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		string question = $"Delete \"{name}\" from your uploads?" + (fans > 1 ? $"\n\n{fans} fans use it; they go back to no background." : "");
		if (MessageBox.Show(this, question, "Delete upload", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
		{
			return;
		}
		lock (E.Settings)
		{
			foreach (FanScreen f in E.Settings.Fans)
			{
				if (Same(f.Background, path))
				{
					f.Background = null;
				}
				f.Playlist = f.Playlist.Where((string p) => !Same(p, path)).ToList();
			}
		}
		E.SaveSoon();
		_preview.Forget(path);
		VideoClip.Forget(path);
		try
		{
			if (Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(Path.GetFullPath(Store.Uploads), StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			_main.Toast("Couldn't delete the file: " + ex.Message);
		}
		_page.RefreshCards();
		_main.Toast("Deleted " + name);
	}

	private static bool Same(string? a, string b)
	{
		return !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
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
					where new string[7] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".json", ".lottie" }.Contains(Path.GetExtension(f).ToLowerInvariant()) || VideoClip.IsVideo(f)
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
		_playlistState.Text = _fan.Playlist.Count == 0 ? "off" : $"{_fan.Playlist.Count} uploads{(_fan.Shuffle ? " · shuffle" : "")} · {_fan.PlaylistSeconds} s each";
		_background.Enabled = !_fan.HasPlaylist;
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
