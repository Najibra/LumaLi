using System;
using System.Drawing;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

internal sealed class EffectTile : Card
{
	private readonly Engine E;

	private readonly string _effect;

	private readonly LightingPage _page;

	private readonly RingsPreview _preview;

	private readonly Slider _speed;

	private readonly Slider _bright;

	private readonly ColorField _c1;

	private readonly ColorField _c2;

	private readonly Control _colors;

	private bool IsSelected
	{
		get
		{
			if (E.Settings.Lighting.FanEffect == _effect)
			{
				return E.Settings.Lighting.FansOn;
			}
			return false;
		}
	}

	public EffectTile(Engine e, string effect, LightingPage page)
	{
		E = e;
		_effect = effect;
		_page = page;
		base.Width = Theme.S(248f);
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		MinimumSize = new Size(Theme.S(248f), 0);
		MaximumSize = new Size(Theme.S(248f), 0);
		base.Margin = new Padding(0, 0, Theme.S(10f), Theme.S(10f));
		base.Padding = new Padding(Theme.S(12f), Theme.S(10f), Theme.S(12f), Theme.S(10f));
		EffectSettings effectSettings = e.Settings.Lighting.For(effect);
		_preview = new RingsPreview(e, effect)
		{
			Size = new Size(Theme.S(222f), Theme.S(70f)),
			Cursor = Cursors.Hand
		};
		_preview.Click += delegate
		{
			_page.UseEffect(_effect);
		};
		_speed = new Slider(0, 100, effectSettings.Speed)
		{
			Width = Theme.S(160f)
		};
		_bright = new Slider(0, 100, effectSettings.Brightness)
		{
			Width = Theme.S(160f),
			Format = (int v) => v + "%"
		};
		_speed.ValueChanged += delegate
		{
			Change(delegate(EffectSettings x)
			{
				x.Speed = _speed.Value;
			}, upload: false);
		};
		_bright.ValueChanged += delegate
		{
			Change(delegate(EffectSettings x)
			{
				x.Brightness = _bright.Value;
			}, upload: false);
		};
		_speed.Committed += delegate
		{
			Commit();
		};
		_bright.Committed += delegate
		{
			Commit();
		};
		_c1 = new ColorField(effectSettings.Color1);
		_c2 = new ColorField(effectSettings.Color2);
		_c1.ColorChanged += delegate
		{
			Change(delegate(EffectSettings x)
			{
				x.Color1 = _c1.Hex;
			}, upload: true);
		};
		_c2.ColorChanged += delegate
		{
			Change(delegate(EffectSettings x)
			{
				x.Color2 = _c2.Hex;
			}, upload: true);
		};
		FormGrid formGrid = new FormGrid(60);
		formGrid.Add("Speed", _speed);
		formGrid.Add("Brightness", _bright);
		FormGrid formGrid2 = new FormGrid(60);
		formGrid2.Add("Colour", _c1);
		formGrid2.Add("Colour 2", _c2);
		_colors = formGrid2;
		Caption caption = new Caption(effect, Theme.Text, Theme.BodyBold)
		{
			Margin = new Padding(0, Theme.S(4f), 0, Theme.S(2f)),
			Cursor = Cursors.Hand
		};
		caption.Click += delegate
		{
			_page.UseEffect(_effect);
		};
		base.Controls.Add(new Stack
		{
			Controls = 
			{
				(Control?)_preview,
				(Control?)caption,
				(Control?)formGrid,
				(Control?)formGrid2
			}
		});
		base.Click += delegate
		{
			_page.UseEffect(_effect);
		};
		RefreshSelection();
	}

	private void Change(Action<EffectSettings> change, bool upload)
	{
		lock (E.Settings)
		{
			change(E.Settings.Lighting.For(_effect));
		}
		if (upload)
		{
			Commit();
		}
		else
		{
			E.SaveSoon();
		}
	}

	private void Commit()
	{
		if (IsSelected)
		{
			E.LightingChanged();
		}
		else
		{
			E.SaveSoon();
		}
	}

	public void RefreshSelection()
	{
		bool isSelected = IsSelected;
		base.Border = (isSelected ? new Color?(Theme.Cyan) : null);
		Control colors = _colors;
		bool flag = isSelected;
		if (!flag)
		{
			string effect = _effect;
			bool flag2 = ((effect == "Static" || effect == "Breathing") ? true : false);
			flag = flag2;
		}
		colors.Visible = flag;
		Invalidate();
	}

	public void Animate()
	{
		if (base.Visible)
		{
			_preview.Invalidate();
		}
	}
}
