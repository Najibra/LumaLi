using System;
using System.Drawing;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

internal sealed class RingsPreview : Control
{
	private readonly Engine _engine;

	private readonly string _effect;

	public RingsPreview(Engine engine, string effect)
	{
		_engine = engine;
		_effect = effect;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(Color.FromArgb(6, 6, 7));
		Theme.Smooth(graphics);
		EffectSettings effectSettings;
		lock (_engine.Settings)
		{
			effectSettings = _engine.Settings.Lighting.For(_effect);
		}
		double phase = _engine.Time / Lighting.LoopSeconds(effectSettings.Speed) % 1.0;
		Color c = Imaging.Hex(effectSettings.Color1, Color.DeepSkyBlue);
		Color c2 = Imaging.Hex(effectSettings.Color2, Color.HotPink);
		float num = Math.Min(base.Height - Theme.S(10f), (float)(base.Width - Theme.S(20f)) / 3f);
		for (int i = 0; i < 3; i++)
		{
			float num2 = (float)base.Width / 2f + (float)(i - 1) * (num + (float)Theme.S(6f));
			float num3 = (float)base.Height / 2f;
			for (int j = 0; j < 26; j++)
			{
				Color color = Lighting.Pixel(_effect, i, 3, (double)j / 26.0, phase, c, c2);
				double num4 = (double)effectSettings.Brightness / 100.0;
				color = Color.FromArgb((int)((double)(int)color.R * num4), (int)((double)(int)color.G * num4), (int)((double)(int)color.B * num4));
				using Pen pen = new Pen(color, Theme.S(5f));
				float startAngle = -90f + (float)j * 360f / 26f;
				graphics.DrawArc(pen, num2 - num / 2f + (float)Theme.S(3f), num3 - num / 2f + (float)Theme.S(3f), num - (float)Theme.S(6f), num - (float)Theme.S(6f), startAngle, 14.646154f);
			}
		}
	}
}
