using System;
using System.Collections.Generic;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// More fan faces drawn by Skia: Warp, Dot Matrix and Molten Glass. As with Neon Ring, their light is added onto the
/// dark with real glow, the reading drives them through a spring, and every pace they keep is a phase of their own,
/// so a change in the reading speeds them up or slows them down instead of making them jump.
/// </summary>
public sealed partial class Faces
{
	/// <summary>Each fan's Dot Matrix dots, how lit each is, easing on and off as the digits change.</summary>
	private readonly Dictionary<string, (string Value, float[] Level)> _dotLevels = new Dictionary<string, (string, float[])>();

	/// <summary>
	/// Warp: stars streaming out of the centre, each streak fading in from its tail to a bright head, the nearer ones
	/// glowing; the busier the sensor the faster the flight and the longer the streaks. The reading in C2 glows in C1.
	/// </summary>
	private void WarpSkia(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Warp");
		double pace = 0.25 + f * 1.4;
		s.Travel += dt * pace;
		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(2, 3, 8)));
			SkiaKit.Glow(c, 200f, 200f, 170f, 170f, SkiaKit.Sk(x.C1, 0.05f + 0.12f * f), SKBlendMode.Plus);
			using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus };
			float stretch = (float)(0.05 + 0.09 * pace);
			for (int i = 0; i < 140; i++)
			{
				double angle = Hash01(i) * Math.PI * 2.0;
				double u = (s.Travel + Hash01(i * 3 + 1)) % 1.0;
				double u0 = Math.Max(0.0, u - stretch);
				float r1 = (float)(u * u * 280.0), r0 = Math.Min((float)(u0 * u0 * 280.0), r1 - 0.6f);
				float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
				var head = new SKPoint(200f + cos * r1, 200f + sin * r1);
				var tail = new SKPoint(200f + cos * r0, 200f + sin * r0);
				float alpha = (float)Math.Min(1.0, u * 1.8) * (0.55f + 0.45f * (float)Hash01(i * 7 + 2));
				float width = 0.6f + 2.4f * (float)u;
				Color ink = Imaging.Mix(x.C1, Color.White, 0.5 * u);
				using (SKShader streak = SKShader.CreateLinearGradient(tail, head, new[] { SkiaKit.Sk(ink, 0f), SkiaKit.Sk(ink, alpha) }, SKShaderTileMode.Clamp))
				{
					p.Shader = streak;
					p.StrokeWidth = width;
					c.DrawLine(tail, head, p);
				}
				if (u > 0.5)
				{
					// the nearer stars bloom
					using SKShader bloom = SKShader.CreateLinearGradient(tail, head, new[] { SkiaKit.Sk(x.C1, 0f), SkiaKit.Sk(x.C1, alpha * 0.35f) }, SKShaderTileMode.Clamp);
					using SKMaskFilter soft = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, width * 1.3f);
					p.Shader = bloom;
					p.StrokeWidth = width * 3f;
					p.MaskFilter = soft;
					c.DrawLine(tail, head, p);
					p.MaskFilter = null;
				}
			}
			p.Shader = null;
			ColourNumber(c, x.Value, new RectangleF(100f, 150f, 200f, 96f), x.C2, x.C1);
		});
		Small(x.G, x.Label, Imaging.WithAlpha(x.C2, 200), 128f, 12f, bold: true, 1f);
		Small(x.G, x.Time, Imaging.WithAlpha(x.C2, 170), 268f, 12f, bold: false);
	}

	/// <summary>The reading filled in one colour inside a soft glow of another.</summary>
	private void ColourNumber(SKCanvas c, string value, RectangleF box, Color fill, Color glow, FontStyle style = FontStyle.Bold)
	{
		using System.Drawing.Drawing2D.GraphicsPath gp = FitText(value, "Segoe UI", style, box);
		using SKPath path = SkiaKit.FromGdi(gp);
		using var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
		using (SKMaskFilter wide = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f))
		{
			p.Color = SkiaKit.Sk(glow, 0.5f);
			p.MaskFilter = wide;
			c.DrawPath(path, p);
		}
		using (SKMaskFilter close = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f))
		{
			p.Color = SkiaKit.Sk(glow, 0.45f);
			p.MaskFilter = close;
			c.DrawPath(path, p);
		}
		p.MaskFilter = null;
		p.BlendMode = SKBlendMode.SrcOver;
		p.Color = SkiaKit.Sk(fill);
		c.DrawPath(path, p);
	}

	/// <summary>
	/// Dot Matrix: the reading in 5×7 LED dots that glow, with unlit dots faintly showing. When the reading changes the
	/// dots fade on and off rather than snapping, and a slow band of light sweeps across the display now and then.
	/// </summary>
	private void DotMatrixSkia(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Dot Matrix");
		s.Scan += dt / 3.5;
		string value = x.Value;
		int cols = value.Length * 6 - 1;
		float pitch = Math.Min(300f / Math.Max(1, cols), 22f);
		float left = 200f - cols * pitch / 2f, top = 200f - 3.5f * pitch;
		// how lit each dot is, easing toward on or off; a new length of reading starts afresh
		if (!_dotLevels.TryGetValue(x.Fan.Serial, out var dots) || dots.Level.Length != value.Length * 35)
		{
			dots = (value, new float[value.Length * 35]);
			dt = 0.0;
		}
		float ease = dt <= 0.0 ? 1f : (float)Math.Min(1.0, dt * 12.0);
		for (int i = 0; i < value.Length; i++)
		{
			Glyphs5x7.TryGetValue(value[i], out string[]? glyph);
			for (int j = 0; j < 7; j++)
			{
				for (int k = 0; k < 5; k++)
				{
					float want = glyph != null && glyph[j][k] == '1' ? 1f : 0f;
					ref float level = ref dots.Level[i * 35 + j * 5 + k];
					level += (want - level) * ease;
				}
			}
		}
		_dotLevels[x.Fan.Serial] = (value, dots.Level);
		// the sweep crosses the display, then rests off its edge a while before the next
		float sweep = (float)((s.Scan % 1.0) * 2.2 - 0.3) * cols;
		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, SkiaKit.Sk(x.C2)));
			using var unlit = new SKPaint { IsAntialias = true, Color = SkiaKit.Sk(x.C1, 0.11f) };
			using var lit = new SKPaint { IsAntialias = true };
			Color hot = Imaging.Mix(x.C1, Color.White, 0.55);
			for (int i = 0; i < value.Length; i++)
			{
				for (int j = 0; j < 7; j++)
				{
					for (int k = 0; k < 5; k++)
					{
						int col = i * 6 + k;
						float cx = left + col * pitch + pitch / 2f, cy = top + j * pitch + pitch / 2f;
						float level = dots.Level[i * 35 + j * 5 + k];
						float band = (col - sweep) / 2.5f;
						float shine = 0.88f + 0.22f * MathF.Exp(-band * band);
						if (level < 0.98f)
						{
							c.DrawCircle(cx, cy, pitch * 0.4f, unlit);
						}
						if (level > 0.01f)
						{
							SkiaKit.Glow(c, cx, cy, pitch * 1.05f, pitch * 1.05f, SkiaKit.Sk(x.C1, 0.32f * level * shine), SKBlendMode.Plus);
							lit.Color = SkiaKit.Sk(x.C1, Math.Min(1f, level * shine));
							c.DrawCircle(cx, cy, pitch * 0.4f, lit);
							SkiaKit.Glow(c, cx, cy, pitch * 0.28f, pitch * 0.28f, SkiaKit.Sk(hot, 0.55f * level * shine), SKBlendMode.Plus);
						}
					}
				}
			}
		});
		Small(x.G, x.Label, Imaging.WithAlpha(x.C1, 200), top - 26f, 13f, bold: true, 1f);
		Small(x.G, x.Time, Imaging.WithAlpha(x.C1, 160), top + 7f * pitch + 24f, 13f, bold: false);
	}

	/// <summary>
	/// Molten Glass: rings of molten colour round a dark glass core, waves of heat rolling outward through them that
	/// run faster and hotter the busier the sensor, an ember pool glowing under the core, the reading in white.
	/// </summary>
	private void MoltenSkia(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Molten Glass");
		s.Heat += dt * (2.0 + 3.5 * f);
		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(10, 4, 4)));
			using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f };
			for (int r = 190; r >= 70; r -= 6)
			{
				double wave = 0.5 + 0.5 * Math.Sin(r * 0.11 - s.Heat);
				ring.Color = SkiaKit.Sk(Imaging.Mix(Color.FromArgb(40, 0, 0), x.C2, 0.3 + 0.7 * wave * (r / 190.0)));
				c.DrawCircle(200f, 200f, r, ring);
			}
			// the heat glowing off the crests: all the rings' light drawn into one layer, blurred once and added on
			using (var layer = new SKPaint { BlendMode = SKBlendMode.Plus, ImageFilter = SKImageFilter.CreateBlur(5f, 5f) })
			using (var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12f, BlendMode = SKBlendMode.Plus })
			{
				c.SaveLayer(layer);
				for (int r = 190; r >= 70; r -= 6)
				{
					double wave = 0.5 + 0.5 * Math.Sin(r * 0.11 - s.Heat);
					glow.Color = SkiaKit.Sk(Imaging.Mix(x.C2, x.C1, 0.5 * wave), (float)(wave * wave * (0.25 + 0.3 * f)));
					c.DrawCircle(200f, 200f, r, glow);
				}
				c.Restore();
			}
			// the ember pool beneath the core, swelling with the heat
			float swell = 0.75f + 0.25f * (float)Math.Sin(s.Heat * 0.5);
			SkiaKit.Glow(c, 200f, 318f, 70f, 36f, SkiaKit.Sk(x.C1, (0.55f + 0.35f * f) * swell), SKBlendMode.Plus);
			SkiaKit.Glow(c, 200f, 322f, 26f, 12f, SkiaKit.Sk(Imaging.Mix(x.C1, Color.White, 0.5), 0.6f * swell), SKBlendMode.Plus);
			// the dark glass core, its rim catching the glow
			using (var core = new SKPaint { IsAntialias = true, Color = new SKColor(8, 8, 10, 235) })
			{
				c.DrawCircle(200f, 200f, 105f, core);
			}
			using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, BlendMode = SKBlendMode.Plus, Color = SkiaKit.Sk(x.C1, 0.3f) })
			using (SKMaskFilter rimSoft = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f))
			{
				rim.MaskFilter = rimSoft;
				c.DrawCircle(200f, 200f, 105f, rim);
			}
			ColourNumber(c, x.Value, new RectangleF(125f, 158f, 150f, 74f), Color.White, x.C2, FontStyle.Regular);
		});
		Small(x.G, ShortLabelOr(x), Color.FromArgb(220, 255, 255, 255), 140f, 12f, bold: true, 1f);
		Small(x.G, x.Time, Color.FromArgb(200, 255, 255, 255), 252f, 11f, bold: false);
	}
}
