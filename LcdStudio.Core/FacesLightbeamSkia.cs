using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The Lightbeam face's light, drawn by Skia as on the screen layout: smooth strands laid on additively so they
/// brighten where they cross, a soft out-of-focus layer behind them for depth, a truly blurred glow and bloom mirrored
/// in the floor, the sharp strands mirrored too, and the flare and embers as added light.
/// </summary>
public sealed partial class Faces
{
	private Bitmap? _lightFrame;

	private SKSurface? _lightQ, _lightLit, _lightWide, _lightBack;

	/// <summary>
	/// The strands' colour across the face; crest scales the light over the crest so piled-up strands don't burn out.
	/// The descent and landing are held lower than on the screen: the strands gather there into a few pixels.
	/// </summary>
	private static SKShader LightShader(float cx, Color warm, Color flank, int alpha, float crest = 1f)
	{
		float P(float px) => Math.Clamp((px + 11f) / 422f, 0.001f, 0.999f);
		SKColor A(Color c, float m) => new SKColor(c.R, c.G, c.B, (byte)Math.Clamp((int)(alpha * m), 0, 255));
		Color white = Color.FromArgb(236, 240, 255);
		var colors = new[] { A(flank, 0.35f), A(flank, 0.8f), A(warm, crest), A(Imaging.Mix(warm, flank, 0.55), 0.85f * (0.5f + 0.5f * crest)), A(Imaging.Mix(flank, white, 0.5), 0.6f), A(white, 0.75f), A(Imaging.Mix(flank, white, 0.6), 0.6f) };
		var stops = new[] { 0f, P(cx - 110f), P(cx), P(cx + 70f), P(LightImpact - 40f), P(LightImpact), 1f };
		return SKShader.CreateLinearGradient(new SKPoint(-11f, 0f), new SKPoint(411f, 0f), colors, stops, SKShaderTileMode.Clamp);
	}

	/// <summary>The page and all the light of the face, drawn by Skia into a frame of its own and laid onto the face.</summary>
	private unsafe void LightSkia(Graphics g, Func<int, float, float> Y, PointF[][] strands, float cx, float arch, float lift, float flare, Color warm, Color flank, double t, Color tint)
	{
		// the page: near-black with a cold haze along the horizon and the floor falling to black, composed once per colour
		if (_lightPage == null || _lightPageKey != tint.ToArgb())
		{
			_lightPage?.Dispose();
			_lightPage = LightPage(tint);
			_lightPageKey = tint.ToArgb();
		}
		_lightFrame ??= new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
		var full = new Rectangle(0, 0, Size, Size);
		BitmapData dst = _lightFrame.LockBits(full, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		var curves = new SKPath[strands.Length];
		try
		{
			BitmapData src = _lightPage.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
			try
			{
				Buffer.MemoryCopy((void*)src.Scan0, (void*)dst.Scan0, (long)dst.Stride * Size, (long)src.Stride * Size);
			}
			finally
			{
				_lightPage.UnlockBits(src);
			}
			for (int k = 0; k < strands.Length; k++)
			{
				curves[k] = SkiaKit.Curve(strands[k]);
			}
			var small = new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
			_lightQ ??= SKSurface.Create(small);
			_lightLit ??= SKSurface.Create(small);
			_lightWide ??= SKSurface.Create(new SKImageInfo(25, 25, SKColorType.Bgra8888, SKAlphaType.Premul));
			_lightBack ??= SKSurface.Create(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul));

			// the soft light at a quarter size: warm smoke behind the crest, the strands wide and faint, and the landing
			SKCanvas q = _lightQ.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);
			SkiaKit.Glow(q, cx, LightHy - arch * 0.75f, 180f, 100f, SkiaKit.Sk(Color.FromArgb(100, Imaging.Mix(warm, Color.Black, 0.6))), SKBlendMode.SrcOver);
			using (SKShader faint = LightShader(cx, warm, flank, 30, 0.45f))
			using (var pen = new SKPaint { Shader = faint, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 13f, StrokeCap = SKStrokeCap.Round })
			{
				foreach (SKPath p in curves)
				{
					q.DrawPath(p, pen);
				}
			}
			SkiaKit.Glow(q, LightImpact + 10f, LightHy, 40f, 8f, new SKColor(230, 236, 255, (byte)Math.Min(255f, 90f * flare)), SKBlendMode.SrcOver);
			q.Restore();
			// a true blur of it, a much wider one for the bloom, and both mirrored into the floor fading toward the foot
			using SKImage light = _lightQ.Snapshot();
			_lightWide.Canvas.Clear(SKColors.Transparent);
			using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.6f, 1.6f) })
			{
				_lightWide.Canvas.DrawImage(light, new SKRect(0f, 0f, 25f, 25f), SkiaKit.Linear, blur);
			}
			using SKImage bloom = _lightWide.Snapshot();
			SKCanvas lc = _lightLit.Canvas;
			lc.Clear(SKColors.Transparent);
			using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(0.8f, 0.8f), BlendMode = SKBlendMode.Plus })
			{
				lc.DrawImage(light, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (var up = new SKPaint { BlendMode = SKBlendMode.Plus })
			{
				lc.DrawImage(bloom, new SKRect(0f, 0f, 100f, 100f), SkiaKit.Linear, up);
			}
			const float qy = LightHy / 4f;
			using (SKImage sky = _lightLit.Snapshot())
			using (var layer = new SKPaint { BlendMode = SKBlendMode.Plus })
			using (var fadeShader = SKShader.CreateLinearGradient(new SKPoint(0f, qy), new SKPoint(0f, 100f), new[] { new SKColor(0, 0, 0, 153), new SKColor(0, 0, 0, 15) }, null, SKShaderTileMode.Clamp))
			using (var fade = new SKPaint { Shader = fadeShader, BlendMode = SKBlendMode.DstIn })
			using (var over = new SKPaint())
			{
				lc.Save();
				lc.ClipRect(new SKRect(0f, qy, 100f, 100f));
				lc.SaveLayer(layer);
				lc.Save();
				lc.Translate(0f, qy);
				lc.Scale(1f, -0.5f);
				lc.Translate(0f, -qy);
				lc.DrawImage(sky, 0f, 0f, SkiaKit.Linear, over);
				lc.Restore();
				lc.DrawRect(new SKRect(0f, qy, 100f, 100f), fade);
				lc.Restore();
				lc.Restore();
			}
			using SKImage lit = _lightLit.Snapshot();

			using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul), dst.Scan0, dst.Stride);
			SKCanvas c = surface.Canvas;
			using SKPixmap pixels = surface.PeekPixels();
			SkiaKit.UpscaleAdd(lit, pixels);

			// depth: a third of the strands drawn at half size and laid on stretched, soft and cooler behind the rest
			SKCanvas bc = _lightBack.Canvas;
			bc.Clear(SKColors.Transparent);
			bc.Save();
			bc.Scale(0.5f);
			using (SKShader back = LightShader(cx, Imaging.Mix(warm, flank, 0.35), flank, (int)(60f * (0.85f + 0.15f * (float)Math.Sin(t * 1.1))), 0.7f))
			using (var pen = new SKPaint { Shader = back, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, BlendMode = SKBlendMode.Plus })
			{
				for (int k = 0; k < curves.Length; k += 3)
				{
					bc.DrawPath(curves[k], pen);
				}
			}
			bc.Restore();
			using (SKImage backImg = _lightBack.Snapshot())
			{
				SkiaKit.UpscaleAdd(backImg, pixels);
			}

			// the sharp strands in two weights, each shimmering on its own slow beat and a touch finer or fuller than its
			// neighbour, fainter than on the screen since they crowd closer here; in the floor, the boldest mirrored and fading toward the foot
			void Strands(float fade, SKShader? mask, int first)
			{
				for (int tier = first; tier < 3; tier++)
				{
					float shimmer = 0.85f + 0.15f * (float)Math.Sin(t * 1.1 + tier * 2.1);
					using SKShader b = LightShader(cx, warm, flank, (int)(new[] { 35, 70, 110 }[tier] * shimmer * fade), 0.6f);
					using SKShader s = mask == null ? b : SKShader.CreateCompose(b, mask, SKBlendMode.DstIn);
					using var pen = new SKPaint { Shader = s, IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus };
					for (int k = tier; k < curves.Length; k += 3)
					{
						pen.StrokeWidth = (tier == 2 ? 1.05f : 0.75f) * (0.8f + 0.45f * (float)Hash01(k * 7 + 1));
						c.DrawPath(curves[k], pen);
					}
				}
			}
			Strands(1f, null, 1);
			using (var mirror = SKShader.CreateLinearGradient(new SKPoint(0f, LightHy), new SKPoint(0f, LightHy - 2f * (Size - LightHy)), new[] { new SKColor(0, 0, 0, 255), new SKColor(0, 0, 0, 25) }, null, SKShaderTileMode.Clamp))
			{
				c.Save();
				c.ClipRect(new SKRect(0f, LightHy, Size, Size));
				c.Translate(0f, LightHy);
				c.Scale(1f, -0.5f);
				c.Translate(0f, -LightHy);
				Strands(0.5f, mirror, 2);
				c.Restore();
			}

			// the horizon, brightening to the landing and burning on to the edge, and the landing flare breathing on it
			using (var lineShader = SKShader.CreateLinearGradient(new SKPoint(-1f, 0f), new SKPoint(401f, 0f),
				new[] { SkiaKit.Sk(Color.FromArgb(40, flank)), SkiaKit.Sk(Color.FromArgb(110, flank)), new SKColor(240, 244, 255, 255), SkiaKit.Sk(Color.FromArgb(200, Imaging.Mix(flank, Color.White, 0.6))) },
				new[] { 0f, 0.5f, LightImpact / 402f, 1f }, SKShaderTileMode.Clamp))
			using (var line = new SKPaint { Shader = lineShader, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f })
			{
				c.DrawLine(0f, LightHy, Size, LightHy, line);
			}
			SKColor Flare(int r, int gr, int b, float alpha) => new SKColor((byte)r, (byte)gr, (byte)b, (byte)Math.Clamp(alpha, 0f, 255f));
			SkiaKit.Glow(c, LightImpact, LightHy, 70f, 20f, Flare(150, 170, 255, 120f * flare), SKBlendMode.Plus);
			SkiaKit.Glow(c, LightImpact, LightHy, 150f, 3f, Flare(220, 230, 255, 190f * flare), SKBlendMode.Plus);
			SkiaKit.Glow(c, LightImpact, LightHy, 24f, 6f, Flare(255, 255, 255, 240f * flare), SKBlendMode.Plus);

			// embers lifting off the crest, more under load, each born and dying at nothing so the drift loops without a seam
			using (var ember = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.7f) })
			{
				Color spark = Imaging.Mix(warm, Color.White, 0.3);
				for (int i = 0; i < 16; i++)
				{
					float share = Math.Clamp(3f + 13f * lift - i, 0f, 1f);
					if (share <= 0f)
					{
						continue;
					}
					float e1 = (float)Hash01(i * 13 + 1), e2 = (float)Hash01(i * 19 + 2), e3 = (float)Hash01(i * 23 + 3);
					float life = (float)((t * (0.16 + 0.12 * e1) + e2) % 1.0);
					float x0 = cx + (e3 - 0.5f) * 170f;
					float ex = x0 + (e1 - 0.5f) * 20f * life, ey = Y(i % LightStrands, x0) - 22f * life * (0.5f + e2);
					float size = 1.2f + 1.6f * e1;
					ember.Color = new SKColor(spark.R, spark.G, spark.B, (byte)Math.Clamp((int)(share * Math.Sin(Math.PI * life) * (110f + 120f * lift)), 0, 255));
					c.DrawCircle(ex, ey, size / 2f + 0.25f, ember);
				}
			}
			c.Flush();
		}
		finally
		{
			foreach (SKPath? p in curves)
			{
				p?.Dispose();
			}
			_lightFrame.UnlockBits(dst);
		}
		CompositingMode mode = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_lightFrame, 0, 0);
		g.CompositingMode = mode;
	}
}
