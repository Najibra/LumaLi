using System;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The Neon Grid and Heat Wave backgrounds on Skia: real glow (blurred light added on top) instead of flat lines.
/// Every movement is a phase of the frame time, so nothing jumps.
/// </summary>
public sealed partial class ScreenRenderer
{
	private SKSurface? _skGridGlow, _skWaveGlow;

	/// <summary>
	/// Synthwave: a violet sky with twinkling specks, a striped sun with a breathing halo sinking into the horizon, a
	/// lit horizon line, and a perspective grid rolling toward the viewer whose lines glow and fade into the distance.
	/// </summary>
	private void GridSkia(Graphics g, Color sky, Color deep, Color line, double t)
	{
		const float W = 1920f, H = 480f;
		float horizon = H * 0.52f;
		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			// the sky, darkest at the top, and the floor below the horizon
			using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, horizon), new[] { SkiaKit.Sk(Imaging.Mix(sky, Color.Black, 0.35)), SkiaKit.Sk(deep) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = shader })
			{
				c.DrawRect(0, 0, W, horizon, p);
			}
			using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, horizon), new SKPoint(0, H), new[] { SkiaKit.Sk(Imaging.Mix(deep, Color.Black, 0.55)), SkiaKit.Sk(Imaging.Mix(sky, Color.Black, 0.8)) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = shader })
			{
				c.DrawRect(0, horizon, W, H - horizon, p);
			}
			// specks in the sky, each twinkling on its own slow beat
			using (var speck = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
			{
				for (int i = 0; i < 70; i++)
				{
					float x = (float)(Hash01(i * 31 + 7) * W), y = (float)(Hash01(i * 17 + 3) * (horizon - 20f));
					float tw = (float)(0.5 + 0.5 * Math.Sin(t * (0.6 + Hash01(i) * 1.2) + i * 2.1));
					speck.Color = new SKColor(255, 220, 255, (byte)(30 + 120 * tw * tw));
					c.DrawCircle(x, y, 0.8f + 0.9f * (float)Hash01(i * 5 + 1), speck);
				}
			}
			// the sun: a halo breathing behind it, then the disc from gold to pink with slats cut out toward its foot
			float cx = W / 2f, sunR = 150f, sunY = horizon - 18f;
			float breathe = (float)(0.85 + 0.15 * Math.Sin(t * 0.7));
			SkiaKit.Glow(c, cx, sunY, sunR * 2.4f, sunR * 1.6f, new SKColor(255, 60, 170, (byte)(70 * breathe)), SKBlendMode.Plus);
			SkiaKit.Glow(c, cx, sunY, sunR * 1.35f, sunR * 1.25f, new SKColor(255, 150, 90, (byte)(90 * breathe)), SKBlendMode.Plus);
			c.Save();
			c.ClipRect(new SKRect(0, 0, W, horizon));
			using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, sunY - sunR), new SKPoint(0, sunY + sunR * 0.3f), new[] { new SKColor(255, 232, 110), new SKColor(255, 120, 120), new SKColor(255, 50, 170) }, null, SKShaderTileMode.Clamp))
			using (var disc = new SKPaint { Shader = shader, IsAntialias = true })
			{
				c.DrawCircle(cx, sunY, sunR, disc);
			}
			using (var slat = new SKPaint { Color = SkiaKit.Sk(deep), IsAntialias = true })
			{
				// the slats drift slowly down, each widening as it goes, so the sun looks like it is setting
				double drift = t * 0.08 % 1.0;
				for (int k = 0; k < 7; k++)
				{
					double f = (k + drift) / 7.0;
					float y = sunY - sunR * 0.55f + (float)(f * sunR * 0.9);
					c.DrawRect(cx - sunR - 4f, y, sunR * 2f + 8f, 1.5f + (float)(f * 9.0), slat);
				}
			}
			c.Restore();
			// the grid's glow at a quarter size: wide soft strokes and the haze off the horizon, blurred a touch, then
			// stretched over the frame and added as light (a full-size blur here cost 40 ms a frame)
			double roll = t * 0.6 % 1.0;
			_skGridGlow ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _skGridGlow.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);
			using (var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14f, BlendMode = SKBlendMode.Plus })
			{
				for (int m = 0; m < 14; m++)
				{
					double d = (m + roll) / 14.0;
					float y = horizon + (float)(d * d * (H - horizon));
					soft.Color = SkiaKit.Sk(line, 0.12f + 0.3f * (float)d);
					q.DrawLine(0, y, W, y, soft);
				}
				using var shader = SKShader.CreateLinearGradient(new SKPoint(0, horizon), new SKPoint(0, H), new[] { SkiaKit.Sk(line, 0.1f), SkiaKit.Sk(line, 0.42f) }, null, SKShaderTileMode.Clamp);
				soft.Shader = shader;
				for (int n = -24; n <= 24; n++)
				{
					q.DrawLine(cx + n * 22f, horizon, cx + n * 260f, H, soft);
				}
			}
			SkiaKit.Glow(q, cx, horizon, W * 0.55f, 40f, SkiaKit.Sk(line, 0.4f), SKBlendMode.Plus);
			q.Restore();
			using (SKImage sharp = _skGridGlow.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.6f, 1.6f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glowImg = _skGridGlow.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glowImg, pixels);
			}
			// the crisp lines, fading toward the horizon
			using (var crisp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, BlendMode = SKBlendMode.Plus })
			{
				for (int m = 0; m < 14; m++)
				{
					double d = (m + roll) / 14.0;
					float y = horizon + (float)(d * d * (H - horizon));
					crisp.Color = SkiaKit.Sk(Imaging.Mix(line, Color.White, 0.15), 0.25f + 0.6f * (float)d);
					c.DrawLine(0, y, W, y, crisp);
				}
				using var shader = SKShader.CreateLinearGradient(new SKPoint(0, horizon), new SKPoint(0, H), new[] { SkiaKit.Sk(line, 0.25f), SkiaKit.Sk(line, 0.85f) }, null, SKShaderTileMode.Clamp);
				crisp.Shader = shader;
				for (int n = -24; n <= 24; n++)
				{
					c.DrawLine(cx + n * 22f, horizon, cx + n * 260f, H, crisp);
				}
			}
			// the horizon itself, lit
			using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, BlendMode = SKBlendMode.Plus, Color = SkiaKit.Sk(Imaging.Mix(line, Color.White, 0.3), 0.8f) })
			{
				c.DrawLine(0, horizon, W, horizon, edge);
			}
		});
	}

	/// <summary>
	/// Heat Wave: a warm gradient that slowly breathes, five rolling waves from back to front, the back ones soft and
	/// out of focus, each with a glowing crest that never burns to white.
	/// </summary>
	private void WavesSkia(Graphics g, Color warm, Color deep, Color crest, double t)
	{
		const float W = 1920f, H = 480f;
		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			float sway = (float)(0.5 + 0.5 * Math.Sin(t * 0.15));
			using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, H * 0.6f),
				new[] { SkiaKit.Sk(Imaging.Mix(warm, crest, 0.12 * sway)), SkiaKit.Sk(Imaging.Mix(warm, deep, 0.5)), SkiaKit.Sk(deep) }, new[] { 0f, 0.55f + 0.1f * sway, 1f }, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = shader })
			{
				c.DrawRect(0, 0, W, H, p);
			}
			// a soft sun of light drifting across the top
			SkiaKit.Glow(c, W * (0.3f + 0.4f * sway), H * 0.1f, 700f, 260f, SkiaKit.Sk(crest, 0.22f), SKBlendMode.Plus);
			// the crests' glow goes on a quarter-size layer, blurred there and added over the frame at the end
			_skWaveGlow ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _skWaveGlow.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);
			for (int i = 0; i < 5; i++)
			{
				float baseY = H * (0.42f + i * 0.12f);
				double speed = 0.35 + i * 0.12;
				var pts = new PointF[42];
				for (int k = 0; k < pts.Length; k++)
				{
					float x = -40f + k * 48f;
					float y = baseY + (float)(Math.Sin(x / 150.0 + t * speed + i * 1.3) * (24.0 - i * 2.0) + Math.Sin(x / 67.0 - t * speed * 1.6 + i) * 8.0);
					pts[k] = new PointF(x, y);
				}
				using SKPath crestLine = SkiaKit.Curve(pts);
				using SKPath body = new SKPath(crestLine);
				body.LineTo(pts[^1].X, H + 10f);
				body.LineTo(pts[0].X, H + 10f);
				body.Close();
				float depth = i / 4f; // 0 at the back, 1 at the front
				Color fill = Imaging.Mix(Imaging.Mix(warm, crest, 0.35), deep, 0.25 + 0.6 * depth);
				// the back waves out of focus, for depth: their tops fade in instead of a full-size blur (which cost 7 ms)
				SKColor[] cols = i < 2
					? new[] { SkiaKit.Sk(fill, 0f), SkiaKit.Sk(fill, 0.5f), SkiaKit.Sk(Imaging.Mix(fill, deep, 0.6), 0.85f) }
					: new[] { SkiaKit.Sk(fill, 0.55f), SkiaKit.Sk(Imaging.Mix(fill, deep, 0.6), 0.85f) };
				float[]? stops = i < 2 ? new[] { 0f, 0.3f, 1f } : null;
				using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, baseY - 34f), new SKPoint(0, H), cols, stops, SKShaderTileMode.Clamp))
				using (var p = new SKPaint { Shader = shader, IsAntialias = true })
				{
					c.DrawPath(body, p);
				}
				// the crest: a wide soft glow and a fine bright edge, added as light
				using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, BlendMode = SKBlendMode.Plus, Color = SkiaKit.Sk(Imaging.Mix(crest, Color.White, 0.2), 0.22f + 0.2f * depth) };
				c.DrawPath(crestLine, edge);
				using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14f, BlendMode = SKBlendMode.Plus, Color = SkiaKit.Sk(crest, 0.18f + 0.1f * depth) };
				q.DrawPath(crestLine, glow);
			}
			q.Restore();
			using (SKImage sharp = _skWaveGlow.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.8f, 1.8f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glowImg = _skWaveGlow.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glowImg, pixels);
			}
		});
	}
}
