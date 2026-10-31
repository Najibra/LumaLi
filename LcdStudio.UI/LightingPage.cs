using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class LightingPage : Page
{
	private readonly Toggle _stripOn;

	private readonly Toggle _follow;

	private readonly Toggle _match;

	private readonly Toggle _reverse;

	private readonly Toggle _fansOn;

	private readonly Select _stripEffect;

	private readonly Slider _stripSpeed;

	private readonly Slider _stripBright;

	private readonly ColorField _stripC1;

	private readonly ColorField _stripC2;

	private readonly Caption _stripState;

	private readonly Caption _result;

	private readonly List<EffectTile> _tiles = new List<EffectTile>();

	private readonly Toggle _react;

	private readonly Select _reactSensor;

	private readonly TextField _reactLow;

	private readonly TextField _reactHigh;

	private readonly Caption _reactUnit;

	private readonly ColorField _reactHot1;

	private readonly ColorField _reactHot2;

	private readonly Caption _reactState;

	public override string Title => "Lighting";

	public override string Subtitle => "Fan rings and the light strip around the 8.8\" screen. Click an animation to use it; each keeps its own speed, brightness and colours.";

	public LightingPage(Engine e, MainForm m)
		: base(e, m)
	{
		LightingSettings lighting = E.Settings.Lighting;
		Card card = AddCard("Screen light strip", 530);
		_stripOn = new Toggle("On", lighting.StripOn);
		_follow = new Toggle("Follow the fan lights", lighting.StripFollowsFans);
		_match = new Toggle("Match the screen layout", lighting.StripMatchesScreen);
		_reverse = new Toggle("Reverse direction", lighting.StripMatchReversed);
		_stripEffect = new Select(Lighting.StripEffects, lighting.StripEffect)
		{
			Width = Theme.S(220f)
		};
		_stripSpeed = new Slider(0, 100, lighting.Strip.Speed)
		{
			Width = Theme.S(300f)
		};
		_stripBright = new Slider(0, 100, lighting.Strip.Brightness)
		{
			Width = Theme.S(300f),
			Format = (int v) => v + "%"
		};
		_stripC1 = new ColorField(lighting.Strip.Color1);
		_stripC2 = new ColorField(lighting.Strip.Color2);
		_stripState = new Caption("", Theme.Cyan, Theme.Small);
		_stripOn.CheckedChanged += delegate
		{
			StripChange(delegate
			{
				E.Settings.Lighting.StripOn = _stripOn.Checked;
			});
		};
		_follow.CheckedChanged += delegate
		{
			StripChange(delegate
			{
				E.Settings.Lighting.StripFollowsFans = _follow.Checked;
			});
		};
		_match.CheckedChanged += delegate
		{
			StripChange(delegate
			{
				E.Settings.Lighting.StripMatchesScreen = _match.Checked;
			});
			Main.Toast(_match.Checked ? "The light strip now follows the screen layout's colours and movement" : "The light strip is back on its own effect");
		};
		_reverse.CheckedChanged += delegate
		{
			StripChange(delegate
			{
				E.Settings.Lighting.StripMatchReversed = _reverse.Checked;
			});
		};
		_stripEffect.SelectedIndexChanged += delegate
		{
			StripChange(delegate
			{
				E.Settings.Lighting.StripEffect = _stripEffect.SelectedItem ?? "Rainbow";
			});
		};
		_stripSpeed.ValueChanged += delegate
		{
			StripChange(delegate(EffectSettings s)
			{
				s.Speed = _stripSpeed.Value;
			});
		};
		_stripBright.ValueChanged += delegate
		{
			StripChange(delegate(EffectSettings s)
			{
				s.Brightness = _stripBright.Value;
			});
		};
		_stripC1.ColorChanged += delegate
		{
			StripChange(delegate(EffectSettings s)
			{
				s.Color1 = _stripC1.Hex;
			});
		};
		_stripC2.ColorChanged += delegate
		{
			StripChange(delegate(EffectSettings s)
			{
				s.Color2 = _stripC2.Hex;
			});
		};
		FormGrid formGrid = new FormGrid(100);
		formGrid.AddWide(new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)_stripOn,
				(Control?)_follow
			}
		});
		formGrid.AddWide(new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)_match,
				(Control?)_reverse
			}
		});
		formGrid.Add("Effect", _stripEffect);
		formGrid.Add("Speed", _stripSpeed);
		formGrid.Add("Brightness", _stripBright);
		formGrid.Add("Colour", _stripC1);
		formGrid.Add("Colour 2", _stripC2);
		formGrid.AddWide(_stripState);
		card.Controls.Add(formGrid);
		AddCard("How it works", 530).Controls.Add(new Caption("Fan animations are uploaded to the fans once and then play on the fans themselves — they keep running with no CPU cost, even if LumaLi closes.\n\nThe screen's light strip is animated live by LumaLi (about 30 frames a second, very light). With Match the screen layout on, it takes its colours and movement from whatever the screen is showing, left to right; use Reverse direction if it runs the wrong way round.\n\nChanging speed, brightness or colours re-uploads the selected animation when you let go of the slider.\n\nFollow a reading warms the fan animation in four steps as the reading climbs, turning its colours toward the hot ones and quickening it. Each step is a new upload, at most one every 20 seconds.", Theme.SubText)
		{
			AutoSize = false,
			Size = new Size(Theme.S(490f), Theme.S(170f))
		});
		Card card2 = AddCard("Fan lights", 1074);
		_fansOn = new Toggle("On", lighting.FansOn);
		_fansOn.CheckedChanged += delegate
		{
			lock (E.Settings)
			{
				E.Settings.Lighting.FansOn = _fansOn.Checked;
			}
			E.LightingChanged();
			Main.Toast(_fansOn.Checked ? "Fan lights on" : "Fan lights off");
		};
		_result = new Caption("", Theme.SubText, Theme.Small)
		{
			AutoSize = false,
			Size = new Size(Theme.S(900f), Theme.S(24f))
		};
		// following a reading: calm below the first level, hot at the second, in steps between
		_react = new Toggle("Follow a reading", lighting.React);
		_react.CheckedChanged += delegate
		{
			ReactChange(delegate(LightingSettings l)
			{
				l.React = _react.Checked;
			});
			Main.Toast(_react.Checked ? "The fan lights now warm up with the reading" : "The fan lights are back to their own colours");
		};
		_reactSensor = new Select(Sensors.Catalog.Select((SensorDef d) => d.Key), lighting.ReactSensor)
		{
			Width = Theme.S(220f),
			Display = (string k) => Sensors.Def(k).Label + " (" + Sensors.Def(k).Unit + ")"
		};
		_reactSensor.SelectedIndexChanged += delegate
		{
			if (_reactSensor.SelectedItem != null)
			{
				ReactChange(delegate(LightingSettings l)
				{
					l.ReactSensor = _reactSensor.SelectedItem;
				});
			}
		};
		_reactLow = LevelField(lighting.ReactLow, delegate(LightingSettings l, float v)
		{
			l.ReactLow = v;
		});
		_reactHigh = LevelField(lighting.ReactHigh, delegate(LightingSettings l, float v)
		{
			l.ReactHigh = v;
		});
		_reactUnit = new Caption("", Theme.Faint, Theme.Small)
		{
			Margin = new Padding(Theme.S(2f), Theme.S(8f), Theme.S(10f), 0)
		};
		_reactHot1 = new ColorField(lighting.ReactHot1, 84);
		_reactHot1.ColorChanged += delegate
		{
			ReactChange(delegate(LightingSettings l)
			{
				l.ReactHot1 = _reactHot1.Hex;
			});
		};
		_reactHot2 = new ColorField(lighting.ReactHot2, 84);
		_reactHot2.ColorChanged += delegate
		{
			ReactChange(delegate(LightingSettings l)
			{
				l.ReactHot2 = _reactHot2.Hex;
			});
		};
		_reactState = new Caption("", Theme.SubText, Theme.Small)
		{
			Margin = new Padding(0, Theme.S(8f), 0, 0)
		};
		Caption Small(string s) => new Caption(s, Theme.SubText, Theme.Small)
		{
			Margin = new Padding(Theme.S(4f), Theme.S(8f), Theme.S(6f), 0)
		};
		Stack reactRow = new Stack(horizontal: true)
		{
			Controls =
			{
				(Control?)_react,
				(Control?)_reactSensor,
				(Control?)Small("calm below"),
				(Control?)_reactLow,
				(Control?)Small("hot at"),
				(Control?)_reactHigh,
				(Control?)_reactUnit,
				(Control?)Small("hot colours"),
				(Control?)_reactHot1,
				(Control?)_reactHot2,
				(Control?)_reactState
			}
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			MaximumSize = new Size(Theme.S(1044f), 0),
			BackColor = Theme.Card
		};
		string[] effects = Lighting.Effects;
		foreach (string effect in effects)
		{
			EffectTile effectTile = new EffectTile(E, effect, this);
			_tiles.Add(effectTile);
			flowLayoutPanel.Controls.Add(effectTile);
		}
		card2.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)new Stack(horizontal: true)
				{
					Controls = 
					{
						(Control?)_fansOn,
						(Control?)_result
					}
				},
				(Control?)reactRow,
				(Control?)flowLayoutPanel
			}
		});
	}

	private void ReactChange(Action<LightingSettings> change)
	{
		lock (E.Settings)
		{
			change(E.Settings.Lighting);
		}
		E.LightingChanged();
	}

	/// <summary>A small number box for a level, taken when you press Enter or leave it, so typing never sends half a number.</summary>
	private TextField LevelField(float value, Action<LightingSettings, float> apply)
	{
		TextField f = new TextField(value.ToString("0.##", CultureInfo.InvariantCulture), 52);
		void Commit()
		{
			if (float.TryParse(f.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
			{
				ReactChange(delegate(LightingSettings l)
				{
					apply(l, v);
				});
			}
		}
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
		return f;
	}

	private void StripChange(Action<EffectSettings> change)
	{
		lock (E.Settings)
		{
			change(E.Settings.Lighting.Strip);
			E.Settings.Lighting.Applied = true;
		}
		E.SaveSoon();
	}

	public void UseEffect(string name)
	{
		lock (E.Settings)
		{
			E.Settings.Lighting.FanEffect = name;
			E.Settings.Lighting.FansOn = true;
		}
		_fansOn.SetSilently(on: true);
		E.LightingChanged();
		foreach (EffectTile tile in _tiles)
		{
			tile.RefreshSelection();
		}
		Main.Toast(name + " sent to the fans");
	}

	public override void Refresh0()
	{
		LightingSettings lighting = E.Settings.Lighting;
		_stripOn.SetSilently(lighting.StripOn);
		_follow.SetSilently(lighting.StripFollowsFans);
		_match.SetSilently(lighting.StripMatchesScreen);
		_reverse.SetSilently(lighting.StripMatchReversed);
		_stripEffect.SetSilently(lighting.StripEffect);
		_fansOn.SetSilently(lighting.FansOn);
		_react.SetSilently(lighting.React);
		_reactSensor.SetSilently(lighting.ReactSensor);
		_reactHot1.SetSilently(lighting.ReactHot1);
		_reactHot2.SetSilently(lighting.ReactHot2);
		foreach (EffectTile tile in _tiles)
		{
			tile.RefreshSelection();
		}
	}

	public override void Tick()
	{
		foreach (EffectTile tile in _tiles)
		{
			tile.Animate();
		}
		_stripState.Text = (E.Strip.Present ? ("Light strip: " + E.Strip.State) : "Light strip not found");
		_result.Text = ((E.Hub == null) ? "Wireless fans not connected." : E.LightingResult);
		// matching the screen overrides the effect and following the fans; only brightness still applies
		bool matching = E.Settings.Lighting.StripMatchesScreen;
		bool own = !E.Settings.Lighting.StripFollowsFans && !matching;
		_stripEffect.Enabled = own;
		_stripSpeed.Enabled = own;
		_stripBright.Enabled = own || matching;
		_follow.Enabled = !matching;
		_reverse.Enabled = matching;
		LightingSettings l = E.Settings.Lighting;
		_reactUnit.Text = Sensors.Def(l.ReactSensor).Unit;
		bool on = l.React;
		_reactSensor.Enabled = _reactLow.Enabled = _reactHigh.Enabled = _reactHot1.Enabled = _reactHot2.Enabled = on;
		float? now = E.Sensors.Get(l.ReactSensor);
		string state = !on ? "" : $"now {(now.HasValue ? now.Value.ToString("0", CultureInfo.InvariantCulture) : "--")}{Sensors.Def(l.ReactSensor).Unit} · warmth {E.ReactStep}/{Lighting.ReactSteps}";
		if (_reactState.Text != state)
		{
			_reactState.Text = state;
		}
	}
}
