using System;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The gauges the Lottie Gauge face draws itself when no Lottie is uploaded. Each runs from the fan's first colour at
/// nothing to its second at full, follows the reading on the face's spring, and keeps the number white with a glow of
/// the colour the gauge has reached.
/// </summary>
public sealed partial class Faces
{
	public static readonly string[] GaugeStyles = { "Arc", "Needle", "Liquid", "LED ring" };

	/// <summary>Gauges sweep three quarters of a turn, from bottom left round over the top to bottom right.</summary>
	private const float GaugeStart = 135f, GaugeSweep = 270f;

	private static readonly SKRect GaugeRing = new SKRect(50f, 50f, 350f, 350f);

	/// <summary>
	/// How far the face has turned to its warning colour, 0 to 1. It turns once the reading reaches the fan's Warn at
	/// level and back once it drops a little below, so a reading sitting on the level doesn't flicker; the change eases
	/// in over about half a second. While warning the light breathes: <paramref name="dim"/> is how far to darken it
	/// now (0 at the swell, up to 0.45 at the low), on a smooth cycle of about 1.6 s. It only ever darkens, so the
	/// swell is the warning colour itself and never burns out.
	/// </summary>
	private static float WarnStep(FaceContext x, GlowState s, double dt, out float dim)
	{
		float level = WarnLevel(x.Fan);
		if (level < 0f)
		{
			s.Warning = false;
		}
		else if (x.Fraction >= level)
		{
			s.Warning = true;
		}
		else if (x.Fraction < level - 0.02f)
		{
			s.Warning = false;
		}
		double target = s.Warning ? 1.0 : 0.0;
		s.Warn = double.IsNaN(s.Warn) ? target : s.Warn + (target - s.Warn) * (1.0 - Math.Exp(-dt * 6.0));
		s.WarnBreath += dt * Math.PI * 2.0 / 1.6;
		double low = 0.5 - 0.5 * Math.Cos(s.WarnBreath);
		float warn = (float)Math.Clamp(s.Warn, 0.0, 1.0);
		dim = (float)(0.45 * low * low * warn);
		return warn;
	}

	/// <summary>Where the fan's Warn at level sits along its sensor's range (0 to 1), or -1 when there is none.</summary>
	private static float WarnLevel(FanScreen fan)
	{
		if (fan.WarnAt <= 0f || fan.Sensor == "time" || fan.WarnAt > Sensors.Def(fan.Sensor).Max)
		{
			return -1f;
		}
		return Fraction(fan.Sensor, fan.WarnAt);
	}

	/// <summary>A short mark just outside the gauge where the warning starts, faint until it is reached.</summary>
	private static void WarnMark(SKCanvas c, FanScreen fan, string style, float f)
	{
		float level = WarnLevel(fan);
		if (level < 0f)
		{
			return;
		}
		Color hot = Imaging.Hex(fan.WarnColor, Color.FromArgb(255, 90, 60));
		bool reached = f >= level;
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
		p.Color = SkiaKit.Sk(Imaging.Mix(Color.White, hot, reached ? 0.8 : 0.5), reached ? 0.9f : 0.55f);
		if (style == "Liquid")
		{
			// a notch on each side of the glass at the height the liquid reaches the level
			const float r = 150f;
			float y = 200f + r - (0.04f + level * 0.92f) * 2f * r;
			float half = (float)Math.Sqrt(Math.Max(0.0, 170f * 170f - (y - 200f) * (y - 200f)));
			c.DrawLine(200f - half, y, 200f - half + 12f, y, p);
			c.DrawLine(200f + half - 12f, y, 200f + half, y, p);
			return;
		}
		float inner = style == "Needle" ? 166f : 169f;
		c.DrawLine(OnGauge(level, inner), OnGauge(level, inner + 11f), p);
	}

	private void BuiltInGauge(FaceContext x, GlowState s, float f, double dt, Color now, bool over)
	{
		string style = Array.IndexOf(GaugeStyles, x.Fan.GaugeStyle) >= 0 ? x.Fan.GaugeStyle : "Arc";
		bool needle = style == "Needle";
		FaceSkia(x.G, c =>
		{
			c.Clear(over ? SKColors.Transparent : SKColors.Black);
			WarnMark(c, x.Fan, style, f);
			switch (style)
			{
			case "Needle":
				NeedleGauge(c, f, x.C1, x.C2, now);
				break;
			case "Liquid":
				s.Travel += dt * (0.8 + f * 1.6);
				LiquidGauge(c, f, s.Travel, x.C1, x.C2, now);
				break;
			case "LED ring":
				LedGauge(c, f, x.C1, x.C2);
				break;
			default:
				ArcGauge(c, f, x.C1, x.C2, now);
				break;
			}
			GlowNumber(c, x.Value, "Segoe UI", FontStyle.Bold, needle ? new RectangleF(135f, 262f, 130f, 48f) : new RectangleF(120f, 160f, 160f, 64f), now);
		}, over);
		Small(x.G, x.Label, Color.FromArgb(190, 255, 255, 255), needle ? 322f : 248f, 13f, bold: true);
	}

	/// <summary>The fan's colours laid along the gauge's sweep, so every part of it keeps the colour of its place.</summary>
	private static SKShader GaugeTint(Color from, Color to)
	{
		// the gap under the gauge splits between the two colours, so the rounded cap behind its start keeps the first
		SKColor a = SkiaKit.Sk(from, 1f), b = SkiaKit.Sk(to, 1f);
		float end = GaugeSweep / 360f, middle = (1f + end) / 2f;
		return SKShader.CreateSweepGradient(new SKPoint(200f, 200f), new[] { a, b, b, a, a }, new[] { 0f, end, middle, middle, 1f },
			SKShaderTileMode.Clamp, 0f, 360f, SKMatrix.CreateRotationDegrees(GaugeStart, 200f, 200f));
	}

	private static SKPoint OnGauge(float fraction, float radius)
	{
		double a = (GaugeStart + GaugeSweep * fraction) * Math.PI / 180.0;
		return new SKPoint(200f + (float)Math.Cos(a) * radius, 200f + (float)Math.Sin(a) * radius);
	}

	private static void Track(SKCanvas c, SKRect ring, float width)
	{
		using var path = new SKPath();
		path.AddArc(ring, GaugeStart, GaugeSweep);
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = new SKColor(255, 255, 255, 24) };
		c.DrawPath(path, p);
	}

	/// <summary>Arc: a neon tube filling round the face.</summary>
	private static void ArcGauge(SKCanvas c, float f, Color from, Color to, Color now)
	{
		Track(c, GaugeRing, 26f);
		if (f < 0.003f)
		{
			return;
		}
		using var arc = new SKPath();
		arc.AddArc(GaugeRing, GaugeStart, GaugeSweep * f);
		using SKShader tint = GaugeTint(from, to);
		Neon(c, arc, 26f, now, tint);
	}

	/// <summary>Needle: a dial with eleven ticks, a thin arc filling behind a glowing needle, and the number below the hub.</summary>
	private static void NeedleGauge(SKCanvas c, float f, Color from, Color to, Color now)
	{
		SKRect dial = SKRect.Inflate(GaugeRing, 8f, 8f);
		Track(c, dial, 6f);
		using SKShader tint = GaugeTint(from, to);
		if (f > 0.003f)
		{
			using var arc = new SKPath();
			arc.AddArc(dial, GaugeStart, GaugeSweep * f);
			Neon(c, arc, 6f, now, tint);
		}
		using var tick = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
		for (int i = 0; i <= 10; i++)
		{
			float u = i / 10f;
			tick.Color = SkiaKit.Sk(u <= f + 0.001f ? Imaging.Mix(from, to, u) : Color.White, u <= f + 0.001f ? 0.9f : 0.22f);
			c.DrawLine(OnGauge(u, i % 5 == 0 ? 112f : 122f), OnGauge(u, 134f), tick);
		}
		using var needle = new SKPath();
		needle.MoveTo(OnGauge(f, -16f));
		needle.LineTo(OnGauge(f, 128f));
		Neon(c, needle, 7f, now, null);
		using var hub = new SKPaint { IsAntialias = true, Color = new SKColor(14, 14, 20) };
		c.DrawCircle(200f, 200f, 13f, hub);
		hub.Style = SKPaintStyle.Stroke;
		hub.StrokeWidth = 3f;
		hub.Color = SkiaKit.Sk(now, 1f);
		c.DrawCircle(200f, 200f, 13f, hub);
	}

	/// <summary>Liquid: a glass of light filling to the reading, its surface rolling in two waves.</summary>
	private static void LiquidGauge(SKCanvas c, float f, double phase, Color from, Color to, Color now)
	{
		const float r = 150f;
		using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, BlendMode = SKBlendMode.Plus })
		{
			rim.Color = SkiaKit.Sk(now, 0.35f);
			rim.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f);
			c.DrawCircle(200f, 200f, r + 10f, rim);
			rim.MaskFilter = null;
			rim.Color = SkiaKit.Sk(now, 0.7f);
			c.DrawCircle(200f, 200f, r + 10f, rim);
		}
		c.Save();
		using (var glass = new SKPath())
		{
			glass.AddCircle(200f, 200f, r);
			c.ClipPath(glass, SKClipOperation.Intersect, antialias: true);
		}
		// a sliver shows even when idle, and the waves still show over the top when full
		float level = 200f + r - (0.04f + f * 0.92f) * 2f * r;
		SKPath Wave(float amp, float freq, double ph)
		{
			var w = new SKPath();
			w.MoveTo(40f, 400f);
			for (float px = 40f; px <= 360f; px += 6f)
			{
				w.LineTo(px, level + amp * (float)Math.Sin(px * freq + ph));
			}
			w.LineTo(360f, 400f);
			w.Close();
			return w;
		}
		using (var p = new SKPaint { IsAntialias = true })
		{
			using SKPath back = Wave(8f, 0.021f, -phase * 1.3 + 1.7);
			p.Color = SkiaKit.Sk(Imaging.Mix(now, Color.Black, 0.35), 0.55f);
			c.DrawPath(back, p);
			using SKPath front = Wave(9f, 0.026f, phase * 2.1);
			using SKShader body = SKShader.CreateLinearGradient(new SKPoint(0f, level - 10f), new SKPoint(0f, 200f + r),
				new[] { SkiaKit.Sk(Imaging.Mix(now, Color.White, 0.12), 0.95f), SkiaKit.Sk(Imaging.Mix(from, Color.Black, 0.45), 0.95f) }, SKShaderTileMode.Clamp);
			p.Shader = body;
			c.DrawPath(front, p);
			p.Shader = null;
			p.Style = SKPaintStyle.Stroke;
			p.StrokeWidth = 2.5f;
			p.BlendMode = SKBlendMode.Plus;
			p.Color = SkiaKit.Sk(Imaging.Mix(now, Color.White, 0.5), 0.5f);
			using var surface = new SKPath();
			for (float px = 40f; px <= 360f; px += 6f)
			{
				SKPoint pt = new SKPoint(px, level + 9f * (float)Math.Sin(px * 0.026f + phase * 2.1));
				if (px == 40f)
				{
					surface.MoveTo(pt);
				}
				else
				{
					surface.LineTo(pt);
				}
			}
			c.DrawPath(surface, p);
		}
		c.Restore();
	}

	/// <summary>LED ring: twenty-eight segments lighting in turn, each in the colour of its place, the last one fading in.</summary>
	private static void LedGauge(SKCanvas c, float f, Color from, Color to)
	{
		const int n = 28;
		const float gap = 3.2f;
		float step = GaugeSweep / n;
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 24f, StrokeCap = SKStrokeCap.Butt };
		using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 30f, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f) };
		for (int i = 0; i < n; i++)
		{
			float lit = Math.Clamp(f * n - i, 0f, 1f);
			Color col = Imaging.Mix(from, to, i / (float)(n - 1));
			using var seg = new SKPath();
			seg.AddArc(GaugeRing, GaugeStart + i * step + gap / 2f, step - gap);
			if (lit > 0f)
			{
				glow.Color = SkiaKit.Sk(col, 0.55f * lit);
				c.DrawPath(seg, glow);
			}
			p.Color = SkiaKit.Sk(lit > 0f ? Imaging.Mix(col, Color.White, 0.12 * lit) : col, 0.12f + 0.88f * lit);
			c.DrawPath(seg, p);
		}
	}
}
