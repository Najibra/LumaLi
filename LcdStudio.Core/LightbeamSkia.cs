using System;
using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Lightbeam's light, drawn by Skia. The strands are smooth curves laid on additively so they brighten where they
/// cross, a soft out-of-focus layer behind them gives depth, the glow is a true blur, the flare and embers are
/// additive light, and the floor mirrors the sharp strands as well as the glow. The type is laid on afterwards.
/// </summary>
public sealed partial class ScreenRenderer
{
	private SKSurface? _skLight, _skLit, _skBloom, _skBack;

	/// <summary>The strands' colour along the screen: lilac flanks, warm white over the crest, white-hot at the landing.</summary>
	/// <remarks>crest scales the light over the crest: added strands pile up there, so less of each keeps it from burning out white.</remarks>
	private static SKShader BeamShader(float cx, Color warm, Color flank, int alpha, float crest = 1f)
	{
		float P(float x) => Math.Clamp((x + 41f) / 2002f, 0.001f, 0.999f);
		SKColor A(Color c, float m) => new SKColor(c.R, c.G, c.B, (byte)Math.Clamp((int)(alpha * m), 0, 255));
		Color white = Color.FromArgb(236, 240, 255);
		var colors = new[] { A(flank, 0.35f), A(flank, 0.8f), A(warm, crest), A(Imaging.Mix(warm, flank, 0.55f), 0.85f * (0.5f + 0.5f * crest)),A(Imaging.Mix(flank, white, 0.5f), 0.8f), A(white, 1f), A(Imaging.Mix(flank, white, 0.6f), 0.75f) };
		var stops = new[] { 0f, P(cx - 430f), P(cx), P(cx + 400f), P(BeamImpact - 160f), P(BeamImpact), 1f };
		return SKShader.CreateLinearGradient(new SKPoint(-41f, 0f), new SKPoint(1961f, 0f), colors, stops, SKShaderTileMode.Clamp);
	}

	/// <summary>
	/// The page and all the light of the frame, drawn by Skia straight into the frame's own pixels: the glow at a quarter
	/// size, blurred, with a blurred sixteenth-size bloom, the sharp strands, the horizon, the flare and the embers, and
	/// the floor's reflection of the glow and the strands fading toward the foot.
	/// </summary>
	private unsafe void BeamSkia(Func<int, float, float> Y, PointF[][] strands, float cx, float arch, float lift, float flare, Color warm, Color flank, double t)
	{
		_beamFrame ??= new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
		Bitmap page = BeamPage();
		var full = new Rectangle(0, 0, 1920, 480);
		BitmapData dst = _beamFrame.LockBits(full, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		try
		{
			BitmapData src = page.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
			try
			{
				Buffer.MemoryCopy((void*)src.Scan0, (void*)dst.Scan0, (long)dst.Stride * 480, (long)src.Stride * 480);
			}
			finally
			{
				page.UnlockBits(src);
			}
			// the soft light at a quarter size: the warm smoke behind the crest, every strand wide and faint, the landing
			// spot. Blurred, with a far wider blurred bloom of it added and the two mirrored into the floor fading toward
			// the foot, all still at a quarter size, then laid across the frame once, as light adds
			_skLight ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			_skLit ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			_skBloom ??= SKSurface.Create(new SKImageInfo(120, 30, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas lc = _skLight.Canvas;
			lc.Clear(SKColors.Transparent);
			lc.Save();
			lc.Scale(0.25f);
			float hw = 1200f * (0.8f + 0.2f * lift), hh = 640f * (0.7f + 0.3f * lift);
			SkiaKit.Glow(lc, cx, BeamHy - arch * 0.8f, hw / 2f, hh / 2f, new SKColor(92, 62, 52, 120), SKBlendMode.SrcOver);
			var curves = new SKPath[strands.Length];
			for (int k = 0; k < strands.Length; k++)
			{
				curves[k] = SkiaKit.Curve(strands[k]);
			}
			using (SKShader soft = BeamShader(cx, warm, flank, 30, 0.8f))
			using (var pen = new SKPaint { Shader = soft, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10f, StrokeCap = SKStrokeCap.Round })
			{
				foreach (SKPath p in curves)
				{
					lc.DrawPath(p, pen);
				}
			}
			SkiaKit.Glow(lc, BeamImpact + 20f, BeamHy, 90f, 15f, new SKColor(230, 236, 255, (byte)Math.Min(255f, 90f * flare)), SKBlendMode.SrcOver);
			lc.Restore();
			using SKImage light = _skLight.Snapshot();
			_skBloom.Canvas.Clear(SKColors.Transparent);
			using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.75f, 1.75f) })
			{
				_skBloom.Canvas.DrawImage(light, new SKRect(0f, 0f, 120f, 30f), SkiaKit.Linear, blur);
			}
			using SKImage bloom = _skBloom.Snapshot();
			SKCanvas qc = _skLit.Canvas;
			qc.Clear(SKColors.Transparent);
			using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.25f, 1.25f), BlendMode = SKBlendMode.Plus })
			{
				qc.DrawImage(light, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (var up = new SKPaint { BlendMode = SKBlendMode.Plus })
			{
				qc.DrawImage(bloom, new SKRect(0f, 0f, 480f, 120f), SkiaKit.Linear, up);
			}
			const float qy = BeamHy / 4f;
			using (SKImage sky = _skLit.Snapshot())
			using (var layer = new SKPaint { BlendMode = SKBlendMode.Plus })
			using (var fadeShader = SKShader.CreateLinearGradient(new SKPoint(0f, qy), new SKPoint(0f, 120f), new[] { new SKColor(0, 0, 0, 153), new SKColor(0, 0, 0, 15) }, null, SKShaderTileMode.Clamp))
			using (var fade = new SKPaint { Shader = fadeShader, BlendMode = SKBlendMode.DstIn })
			{
				qc.Save();
				qc.ClipRect(new SKRect(0f, qy, 480f, 120f));
				qc.SaveLayer(layer);
				qc.Save();
				qc.Translate(0f, qy);
				qc.Scale(1f, -0.5f);
				qc.Translate(0f, -qy);
				using (var over = new SKPaint())
				{
					qc.DrawImage(sky, 0f, 0f, SkiaKit.Linear, over);
				}
				qc.Restore();
				qc.DrawRect(new SKRect(0f, qy, 480f, 120f), fade);
				qc.Restore();
				qc.Restore();
			}
			using SKImage lit = _skLit.Snapshot();

			var info = new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul);
			using SKSurface surface = SKSurface.Create(info, dst.Scan0, dst.Stride);
			SKCanvas c = surface.Canvas;
			using SKPixmap pixels = surface.PeekPixels();
			SkiaKit.UpscaleAdd(lit, pixels);

			// the sharp strands in three weights, each shimmering on its own slow beat, added as light adds; in the
			// floor, mirrored and fading toward the foot
			// depth: the back third of the strands is drawn at half size and laid on stretched, so it sits soft and
			// out of focus behind the sharp ones, cooler toward the flank colour as if seen through haze
			_skBack ??= SKSurface.Create(new SKImageInfo(960, 240, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas bc = _skBack.Canvas;
			bc.Clear(SKColors.Transparent);
			bc.Save();
			bc.Scale(0.5f);
			using (SKShader back = BeamShader(cx, Imaging.Mix(warm, flank, 0.35f), flank, (int)(95f * (0.85f + 0.15f * (float)Math.Sin(t * 1.1))), 0.8f))
			using (var pen = new SKPaint { Shader = back, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, BlendMode = SKBlendMode.Plus })
			{
				for (int k = 0; k < curves.Length; k += 3)
				{
					bc.DrawPath(curves[k], pen);
				}
			}
			bc.Restore();
			using (SKImage backImg = _skBack.Snapshot())
			{
				SkiaKit.UpscaleAdd(backImg, pixels);
			}

			// the sharp strands in two weights, each shimmering on its own slow beat, added as light adds and each a
			// touch finer or fuller than its neighbour; in the floor, mirrored and fading toward the foot
			void Strands(SKCanvas on, float fade, SKShader? mask, int first = 1)
			{
				for (int tier = first; tier < 3; tier++)
				{
					float shimmer = 0.85f + 0.15f * (float)Math.Sin(t * 1.1 + tier * 2.1);
					using SKShader b = BeamShader(cx, warm, flank, (int)(new[] { 70, 135, 205 }[tier] * shimmer * fade), 0.72f);
					using SKShader s = mask == null ? b : SKShader.CreateCompose(b, mask, SKBlendMode.DstIn);
					using var pen = new SKPaint { Shader = s, IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus };
					for (int k = tier; k < curves.Length; k += 3)
					{
						pen.StrokeWidth = (tier == 2 ? 1.3f : 0.9f) * (0.8f + 0.45f * (float)Hash01(k * 7 + 1));
						on.DrawPath(curves[k], pen);
					}
				}
			}
			Strands(c, 1f, null);
			using (var mirror = SKShader.CreateLinearGradient(new SKPoint(0f, BeamHy), new SKPoint(0f, BeamHy - 2f * (480f - BeamHy)), new[] { new SKColor(0, 0, 0, 255), new SKColor(0, 0, 0, 25) }, null, SKShaderTileMode.Clamp))
			{
				c.Save();
				c.ClipRect(new SKRect(0f, BeamHy, 1920f, 480f));
				c.Translate(0f, BeamHy);
				c.Scale(1f, -0.5f);
				c.Translate(0f, -BeamHy);
				// only the boldest third: dim and squashed as it is, the rest would cost as much and hardly show
				Strands(c, 0.5f, mirror, 2);
				c.Restore();
			}

			// the horizon, brightening toward the landing point and burning on to the right edge
			using (var lineShader = SKShader.CreateLinearGradient(new SKPoint(-1f, 0f), new SKPoint(1921f, 0f),
				new[] { SkiaKit.Sk(Color.FromArgb(40, flank)), SkiaKit.Sk(Color.FromArgb(110, flank)), new SKColor(240, 244, 255, 255), SkiaKit.Sk(Color.FromArgb(200, Imaging.Mix(flank, Color.White, 0.6f))) },
				new[] { 0f, 0.5f, BeamImpact / 1922f, 1f }, SKShaderTileMode.Clamp))
			using (var line = new SKPaint { Shader = lineShader, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f })
			{
				c.DrawLine(0f, BeamHy, 1920f, BeamHy, line);
			}

			// the landing flare: a white-hot core in a flat blue bloom with a long streak through it, as light
			float sx = (0.7f + 0.5f * flare) , sy = (0.6f + 0.6f * flare), a = Math.Min(1f, flare), fx = BeamImpact + 20f;
			void Flare(float x, float y, float w, float h, Color col, int alpha) =>
				SkiaKit.Glow(c, fx + (x + w / 2f - 360f) * sx, BeamHy + (y + h / 2f - 80f) * sy, w / 2f * sx, h / 2f * sy, SkiaKit.Sk(Color.FromArgb(alpha, col), a), SKBlendMode.Plus);
			Flare(160f, 20f, 400f, 120f, Color.FromArgb(150, 170, 255), 120);
			Flare(0f, 70f, 720f, 20f, Color.FromArgb(220, 230, 255), 200);
			Flare(250f, 50f, 220f, 60f, Color.White, 230);
			Flare(310f, 68f, 100f, 24f, Color.White, 255);

			// embers lifting off the crest, each a soft spark of light, born and dying at nothing
			using (var ember = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.9f) })
			{
				Color spark = Imaging.Mix(warm, Color.White, 0.3f);
				for (int i = 0; i < 36; i++)
				{
					float share = Math.Clamp(6f + 30f * lift - i, 0f, 1f);
					if (share <= 0f)
					{
						continue;
					}
					float e1 = (float)Hash01(i * 13 + 1), e2 = (float)Hash01(i * 19 + 2), e3 = (float)Hash01(i * 23 + 3);
					float life = (float)((t * (0.16 + 0.12 * e1) + e2) % 1.0);
					float x0 = cx + (e3 - 0.5f) * 720f;
					float x = x0 + (e1 - 0.5f) * 70f * life, y = Y(i % BeamStrands, x0) - 56f * life * (0.5f + e2);
					float size = 1.4f + 2.2f * e1;
					ember.Color = new SKColor(spark.R, spark.G, spark.B, (byte)Math.Clamp((int)(share * Math.Sin(Math.PI * life) * (110f + 120f * lift)), 0, 255));
					c.DrawCircle(x, y, size / 2f + 0.3f, ember);
				}
			}
			c.Flush();
			foreach (SKPath p in curves)
			{
				p.Dispose();
			}
		}
		finally
		{
			_beamFrame.UnlockBits(dst);
		}
	}
}
