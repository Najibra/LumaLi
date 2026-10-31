using System;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Glass Tunnel's background, drawn by Skia: rounded panes of glass flying out of a lit vanishing point. Far panes are
/// thin, faint and blue-violet; near ones are fuller, brighter and cyan, each with a soft real glow and a faint inner
/// edge like the thickness of glass. The flight speeds up with the processor, eased so it never jumps, and the panes
/// fade before they reach the readings at the sides, so they never cut through the type.
/// </summary>
public sealed partial class ScreenRenderer
{
	private SKSurface? _skTunnelGlow;

	private double _tunnelAt = double.NaN, _tunnelPhase, _tunnelSpeed = 0.1;

	private const int TunnelPanes = 16, TunnelMotes = 90;

	private static float Smooth(float a, float b, float x)
	{
		float u = Math.Clamp((x - a) / (b - a), 0f, 1f);
		return u * u * (3f - 2f * u);
	}

	/// <summary>A pane's rectangle at depth u (0 at the vanishing point, 1 just past the viewer) and its scale.</summary>
	private static (SKRect R, float S) TunnelPane(float u)
	{
		float s = (float)Math.Pow(u, 2.2);
		return (new SKRect(960f - 1056f * s, 240f - 360f * s, 960f + 1056f * s, 240f + 360f * s), s);
	}

	/// <summary>How much of a pane shows: it fades in from the distance and out before it reaches the side readings.</summary>
	private static float TunnelShare(float u) => Smooth(0.02f, 0.16f, u) * (1f - Smooth(0.6f, 0.86f, u));

	private void TunnelSkia(Graphics g, Color page, Color deep, Color accent, double t)
	{
		// the flight: the phase only ever moves forward, at a speed that eases toward the processor's pace
		double load = Math.Clamp((_sensors.Get("cpu.load") ?? 10f) / 100.0, 0.0, 1.0);
		double pace = 0.13 + 0.3 * load;
		if (double.IsNaN(_tunnelAt))
		{
			// the first frame starts part-way through the flight, already at the processor's pace
			_tunnelSpeed = pace;
			_tunnelPhase = t * pace;
		}
		double dt = double.IsNaN(_tunnelAt) || t < _tunnelAt ? 0.0 : Math.Min(0.5, t - _tunnelAt);
		_tunnelAt = t;
		_tunnelSpeed += (pace - _tunnelSpeed) * (1.0 - Math.Exp(-dt * 1.2));
		_tunnelPhase = (_tunnelPhase + dt * _tunnelSpeed) % 1000.0;
		double phase = _tunnelPhase;

		Color violet = Color.FromArgb(122, 92, 255);
		Color far = Imaging.Mix(Imaging.Mix(deep, accent, 0.35), violet, 0.3);
		// the panes run through a ring of hues (the accent, blue, violet, magenta), each pane a step on from the one
		// before, the whole ring drifting slowly round; a pane keeps its hue as it nears, only lifting toward the accent
		Color[] ring = { accent, Color.FromArgb(70, 120, 255), Color.FromArgb(138, 84, 255), Color.FromArgb(232, 70, 214), Color.FromArgb(138, 84, 255), Color.FromArgb(70, 120, 255) };
		Color Hue(double x)
		{
			x = (x % 1.0 + 1.0) % 1.0 * ring.Length;
			int k = (int)x;
			return Imaging.Mix(ring[k % ring.Length], ring[(k + 1) % ring.Length], x - k);
		}
		double drift = t * 0.035;
		Color Pane(int i, float u) => Imaging.Mix(Imaging.Mix(far, Hue(i * 0.17 + drift), 0.35 + 0.65 * u), accent, 0.25 * u * u);
		// a soft wave of light running out through the panes every few seconds
		float Wave(float u) => 1f + 0.45f * (float)Math.Pow(0.5 + 0.5 * Math.Sin(u * 9.0 - t * 1.6), 6.0);

		SkiaDraw(g, new Rectangle(0, 0, 1920, 480), c =>
		{
			// the page, lifting a little toward the vanishing point
			c.Clear(SkiaKit.Sk(page));
			using (var haze = SKShader.CreateRadialGradient(new SKPoint(960f, 240f), 900f,
				new[] { SkiaKit.Sk(Imaging.Mix(page, deep, 0.55)), SkiaKit.Sk(page) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = haze })
			{
				c.Save();
				c.Scale(1f, 0.42f, 960f, 240f);
				c.DrawRect(new SKRect(0f, -400f, 1920f, 880f), p);
				c.Restore();
			}

			// the soft light at a quarter size: the glow round every pane and the light at the vanishing point,
			// blurred, then stretched over the frame and added as light adds
			_skTunnelGlow ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas lc = _skTunnelGlow.Canvas;
			lc.Clear(SKColors.Transparent);
			lc.Save();
			lc.Scale(0.25f);
			float breath = 0.85f + 0.15f * (float)Math.Sin(t * 0.9);
			SkiaKit.Glow(lc, 960f, 240f, 420f, 130f, SkiaKit.Sk(Hue(drift + 0.33), 0.42f * breath), SKBlendMode.Plus);
			SkiaKit.Glow(lc, 960f, 240f, 200f, 60f, SkiaKit.Sk(Imaging.Mix(accent, violet, 0.3), 0.5f * breath), SKBlendMode.Plus);
			SkiaKit.Glow(lc, 960f, 240f, 90f, 28f, SkiaKit.Sk(accent, 0.55f * breath), SKBlendMode.Plus);
			using (var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus })
			{
				for (int i = 0; i < TunnelPanes; i++)
				{
					float u = (float)((i / (double)TunnelPanes + phase) % 1.0);
					float share = TunnelShare(u);
					var (r, s) = TunnelPane(u);
					if (share < 0.01f || r.Width < 6f)
					{
						continue;
					}
					soft.StrokeWidth = 10f + 34f * s;
					soft.Color = SkiaKit.Sk(Pane(i, u), Math.Min(1f, 0.85f * share * (0.35f + 0.65f * u) * Wave(u)));
					lc.DrawRoundRect(r, 6f + 40f * s, 6f + 40f * s, soft);
				}
			}
			lc.Restore();
			using (SKImage sharp = _skTunnelGlow.Snapshot())
			{
				lc.Clear(SKColors.Transparent);
				using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(2.2f, 2.2f) })
				{
					lc.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
				}
			}
			using (SKImage glow = _skTunnelGlow.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glow, pixels);
			}

			// rails running back from the corners to the vanishing point, faint and gone before the corners' readings
			using (var rail = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, BlendMode = SKBlendMode.Plus })
			{
				foreach (var (dx, dy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
				{
					var a = new SKPoint(960f, 240f);
					var b = new SKPoint(960f + dx * 1056f * 0.55f, 240f + dy * 360f * 0.55f);
					using var shade = SKShader.CreateLinearGradient(a, b,
						new[] { SkiaKit.Sk(accent, 0f), SkiaKit.Sk(far, 0.4f), SkiaKit.Sk(far, 0f) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
					rail.Shader = shade;
					c.DrawLine(a, b, rail);
				}
			}

			// the panes: a bright rim and a faint inner edge, like the thickness of the glass
			using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus })
			{
				for (int i = 0; i < TunnelPanes; i++)
				{
					float u = (float)((i / (double)TunnelPanes + phase) % 1.0);
					float share = TunnelShare(u);
					var (r, s) = TunnelPane(u);
					if (share < 0.01f || r.Width < 6f)
					{
						continue;
					}
					Color col = Pane(i, u);
					float radius = 6f + 40f * s, light = Math.Min(1f, share * (0.3f + 0.7f * u) * Wave(u));
					rim.StrokeWidth = 1.1f + 3.2f * s;
					rim.Color = SkiaKit.Sk(Imaging.Mix(col, Color.White, 0.15), 0.9f * light);
					c.DrawRoundRect(r, radius, radius, rim);
					float inset = 2.5f + 9f * s;
					SKRect inner = SKRect.Inflate(r, -inset, -inset);
					rim.StrokeWidth = 0.8f + 1.2f * s;
					rim.Color = SkiaKit.Sk(Imaging.Mix(col, Color.White, 0.45), 0.3f * light);
					c.DrawRoundRect(inner, Math.Max(2f, radius - inset), Math.Max(2f, radius - inset), rim);
				}
			}

			// motes drifting out along the tunnel with the panes, born at nothing and gone before the readings
			using (var mote = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.1f) })
			{
				for (int k = 0; k < TunnelMotes; k++)
				{
					double h1 = Hash01(k * 31 + 5), h2 = Hash01(k * 17 + 9), h3 = Hash01(k * 43 + 2);
					float u = (float)((h1 + phase * (0.8 + 0.5 * h2)) % 1.0);
					float s = (float)Math.Pow(u, 2.2);
					double ang = h3 * Math.PI * 2.0;
					float x = 960f + (float)Math.Cos(ang) * 1056f * s * (0.35f + 0.6f * (float)h2);
					float y = 240f + (float)Math.Sin(ang) * 360f * s * (0.35f + 0.6f * (float)h2);
					float share = TunnelShare(u);
					if (share < 0.01f)
					{
						continue;
					}
					// most specks are pale accent; one in three takes the hue of the ring where it flies
					Color tint = k % 3 == 0 ? Hue(h3 + drift) : accent;
					mote.Color = SkiaKit.Sk(Imaging.Mix(tint, Color.White, 0.3), 0.9f * share * (0.3f + 0.7f * u));
					c.DrawCircle(x, y, 0.9f + 2.6f * s, mote);
				}
			}
		});
	}
}
