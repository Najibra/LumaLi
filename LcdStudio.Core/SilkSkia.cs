using System;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The Silk background, drawn by Skia for either shape of screen: ribbons of fine glowing strands flowing along the
/// screen's long side, each ribbon swelling and narrowing as it goes, with soft out-of-focus orbs drifting behind for
/// depth and a slow shimmer running along the strands. Every movement is a phase that only accumulates, at a pace that
/// eases toward the processor's load, so nothing ever jumps.
/// </summary>
public sealed partial class ScreenRenderer
{
	private SKSurface? _skSilkGlow;

	private bool _silkTall;

	private double _silkAt = double.NaN, _silkPhase, _silkSpeed = 0.3;

	private const int SilkRibbons = 4, SilkStrands = 7, SilkOrbs = 16;

	private void Silk(Graphics g, Color page, Color deep, Color accent, double t)
	{
		double load = Math.Clamp((_sensors.Get("cpu.load") ?? 10f) / 100.0, 0.0, 1.0);
		double pace = 0.22 + 0.55 * load;
		if (double.IsNaN(_silkAt))
		{
			_silkSpeed = pace;
			_silkPhase = t * pace;
		}
		double dt = double.IsNaN(_silkAt) || t < _silkAt ? 0.0 : Math.Min(0.5, t - _silkAt);
		_silkAt = t;
		_silkSpeed += (pace - _silkSpeed) * (1.0 - Math.Exp(-dt * 0.8));
		_silkPhase = (_silkPhase + dt * _silkSpeed) % 10000.0;
		float ph = (float)_silkPhase;

		bool tall = _h > _w;
		const float L = 1920f, S = 480f;
		// everything is laid out along u (the long side) and v (across); a tall screen swaps the two
		SKMatrix toFrame = tall ? new SKMatrix(0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f) : SKMatrix.Identity;

		Color violet = Color.FromArgb(124, 70, 255), magenta = Color.FromArgb(236, 64, 196);
		Color[] hues = { accent, Imaging.Mix(deep, accent, 0.5), violet, magenta };
		Color Hue(int i, float drift)
		{
			float x = ((i * 0.9f + drift) % hues.Length + hues.Length) % hues.Length;
			int k = (int)x;
			return Imaging.Mix(hues[k % hues.Length], hues[(k + 1) % hues.Length], x - k);
		}
		float drift = (float)(t * 0.03);

		// a ribbon's centre across the screen and its width at a point along it
		float Centre(int r, float u) => S * (0.5f + 0.06f * (r - 1.5f))
			+ 120f * (float)Math.Sin(u * 0.0026 + ph * (0.9 + 0.17 * r) + r * 1.7)
			+ 46f * (float)Math.Sin(u * 0.0061 - ph * (1.3 + 0.11 * r) + r * 0.6);
		float Width(int r, float u) => 34f + 70f * (0.5f + 0.5f * (float)Math.Sin(u * 0.0034 - ph * (0.7 + 0.13 * r) + r * 2.3));

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			c.Save();
			c.Concat(toFrame);
			// the page: deep at the edges, lifting toward the middle of the short side
			using (var bg = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, S),
				new[] { SkiaKit.Sk(page), SkiaKit.Sk(Imaging.Mix(page, deep, 0.6f)), SkiaKit.Sk(page) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = bg })
			{
				c.DrawRect(new SKRect(0f, 0f, L, S), p);
			}
			c.Restore();

			// the soft light, at a quarter size: orbs out of focus and the glow round each ribbon
			if (_skSilkGlow == null || _silkTall != tall)
			{
				_skSilkGlow?.Dispose();
				_skSilkGlow = SKSurface.Create(new SKImageInfo(_w / 4, _h / 4, SKColorType.Bgra8888, SKAlphaType.Premul));
				_silkTall = tall;
			}
			SKCanvas lc = _skSilkGlow.Canvas;
			lc.Clear(SKColors.Transparent);
			lc.Save();
			lc.Scale(0.25f);
			lc.Concat(toFrame);
			for (int i = 0; i < SilkOrbs; i++)
			{
				// each orb drifts along slowly at its own pace and wraps round, fading in and out at the ends
				double h1 = Hash01(i * 3 + 1), h2 = Hash01(i * 3 + 2), h3 = Hash01(i * 3 + 3);
				float u = (float)(((h1 + ph * (0.012 + 0.02 * h2)) % 1.0 + 1.0) % 1.0) * (L + 400f) - 200f;
				float v = (float)(S * (0.1 + 0.8 * h3) + 30.0 * Math.Sin(ph * 0.4 + i));
				float rad = 50f + 110f * (float)h2;
				float pulse = 0.75f + 0.25f * (float)Math.Sin(ph * (0.5 + h1) + i);
				SkiaKit.Glow(lc, u, v, rad, rad, SkiaKit.Sk(Hue(i, drift), 0.22f * pulse), SKBlendMode.Plus);
			}
			using (var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
			{
				for (int r = 0; r < SilkRibbons; r++)
				{
					var pts = new PointF[41];
					for (int k = 0; k < pts.Length; k++)
					{
						float u = -60f + k * (L + 120f) / (pts.Length - 1);
						pts[k] = new PointF(u, Centre(r, u));
					}
					using SKPath path = SkiaKit.Curve(pts);
					soft.StrokeWidth = 90f;
					soft.Color = SkiaKit.Sk(Hue(r, drift), 0.32f);
					lc.DrawPath(path, soft);
				}
			}
			lc.Restore();
			using (SKImage sharp = _skSilkGlow.Snapshot())
			{
				lc.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(6f, 6f) };
				lc.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glow = _skSilkGlow.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glow, pixels);
			}

			// the strands: fine lines across each ribbon's width, a shimmer running along them
			c.Save();
			c.Concat(toFrame);
			using (var strand = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, BlendMode = SKBlendMode.Plus })
			{
				for (int r = 0; r < SilkRibbons; r++)
				{
					Color col = Hue(r, drift);
					Color lit = Imaging.Mix(col, Color.White, 0.18);
					float shift = (float)((ph * (0.18 + 0.05 * r) + r * 0.31) % 1.0);
					for (int s = 0; s < SilkStrands; s++)
					{
						float f = s / (float)(SilkStrands - 1) - 0.5f;
						var pts = new PointF[57];
						for (int k = 0; k < pts.Length; k++)
						{
							float u = -40f + k * (L + 80f) / (pts.Length - 1);
							pts[k] = new PointF(u, Centre(r, u) + f * Width(r, u));
						}
						using SKPath path = SkiaKit.Curve(pts);
						// edges of a ribbon a little brighter than its middle, like light caught on folded silk
						float edge = 0.55f + 0.45f * Math.Abs(f) * 2f;
						float a = 0.30f * edge;
						using var shimmer = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(L * 0.7f, 0f),
							new[] { SkiaKit.Sk(col, a * 0.35f), SkiaKit.Sk(lit, a), SkiaKit.Sk(col, a * 0.35f), SkiaKit.Sk(col, a * 0.6f), SkiaKit.Sk(col, a * 0.35f) },
							new[] { 0f, 0.18f, 0.4f, 0.7f, 1f }, SKShaderTileMode.Repeat, SKMatrix.CreateTranslation(shift * L * 0.7f + s * 9f, 0f));
						strand.Shader = shimmer;
						c.DrawPath(path, strand);
					}
				}
			}
			c.Restore();
		});
	}
}
