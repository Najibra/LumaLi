using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The two watch layouts as fan faces.
/// - Station Clock: the whole fan is a white railway-style clock (black bar markers, tapered black hands, a slim red
///   second hand with a hollow ring that sweeps in 58.5 s and waits at twelve, a minute hand that jumps on). The fan's
///   reading sits in a small gauge dial above six, its name under twelve.
/// - Sport Dial: the sports smartwatch: a cream bezel with a minute scale, 12 / 3 / 6 and words round the ring; the
///   inside split black and cream; lume hands and a red seconds hand; a date window at nine; upright segment bars with
///   red lettering for the reading and its peak over the last minute; on the cream side the reading, the digital time
///   and the week, with a seconds arc along the edge.
/// </summary>
public sealed partial class Faces
{
	private readonly Dictionary<string, (float Peak, double At)> _sdPeak = new Dictionary<string, (float, double)>();

	private static SKPath FwHand(float deg, float back, float len, float wBase, float wTip, float point = 0f)
	{
		var p = new SKPath();
		p.MoveTo(-wBase / 2f, back);
		p.LineTo(wBase / 2f, back);
		p.LineTo(wTip / 2f, -len + point);
		if (point > 0f)
		{
			p.LineTo(0f, -len);
		}
		p.LineTo(-wTip / 2f, -len + point);
		p.Close();
		p.Transform(SKMatrix.CreateRotationDegrees(deg).PostConcat(SKMatrix.CreateTranslation(200f, 200f)));
		return p;
	}

	private static void FwShadow(SKCanvas c, SKPath p, float dx, float dy, float sigma, byte alpha)
	{
		using var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
		using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, alpha), MaskFilter = mf };
		c.Save();
		c.Translate(dx, dy);
		c.DrawPath(p, paint);
		c.Restore();
	}

	private static void FwBar(SKCanvas c, float cx, float cy, float deg, float r0, float r1, float width, SKPaint paint)
	{
		c.Save();
		c.Translate(cx, cy);
		c.RotateDegrees(deg);
		c.DrawRect(new SKRect(-width / 2f, -r1, width / 2f, -r0), paint);
		c.Restore();
	}

	private static SKColor FwMix(SKColor a, SKColor b, float k)
	{
		k = Math.Clamp(k, 0f, 1f);
		return new SKColor((byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k), (byte)(a.Alpha + (b.Alpha - a.Alpha) * k));
	}

	// ---------- Station Clock ----------

	private void StationFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Station Clock");
		DateTime now = WatchNow(x);
		SKColor dial = SkiaKit.Sk(x.C1, 1f), red = SkiaKit.Sk(x.C2, 1f);
		bool light = dial.Red * 0.3f + dial.Green * 0.59f + dial.Blue * 0.11f > 128f;
		SKColor ink = light ? new SKColor(20, 20, 22) : new SKColor(240, 240, 238);
		SKColor soft = light ? new SKColor(96, 98, 102) : new SKColor(170, 172, 176);
		const float rf = 186f;

		FaceSkia(x.G, c =>
		{
			// brushed grey rim round a gently shaded face
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 400f), new[] { new SKColor(236, 237, 239), new SKColor(150, 152, 156) }, null, SKShaderTileMode.Clamp))
			using (var rim = new SKPaint { IsAntialias = true, Shader = sh })
			{
				c.DrawRect(0f, 0f, 400f, 400f, rim);
			}
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 400f), new[] { new SKColor(150, 152, 156), new SKColor(246, 246, 247) }, null, SKShaderTileMode.Clamp))
			using (var bevel = new SKPaint { IsAntialias = true, Shader = sh })
			{
				c.DrawCircle(200f, 200f, rf + 5f, bevel);
			}
			SKColor centre = FwMix(dial, SKColors.White, light ? 0.5f : 0.08f), edge = FwMix(dial, SKColors.Black, light ? 0.05f : 0.2f);
			using (var sh = SKShader.CreateRadialGradient(new SKPoint(200f, 140f), rf * 1.4f, new[] { centre, edge }, null, SKShaderTileMode.Clamp))
			using (var face = new SKPaint { IsAntialias = true, Shader = sh })
			{
				c.DrawCircle(200f, 200f, rf, face);
			}
			using (var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f))
			using (var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 9f, Color = new SKColor(0, 0, 0, 45), MaskFilter = mf })
			{
				c.Save();
				using var clip = new SKPath();
				clip.AddCircle(200f, 200f, rf);
				c.ClipPath(clip, SKClipOperation.Intersect, true);
				c.DrawCircle(200f, 203f, rf + 2f, inner);
				c.Restore();
			}
			using (var bar = new SKPaint { IsAntialias = true, Color = ink })
			{
				for (int i = 0; i < 60; i++)
				{
					if (i % 5 == 0)
					{
						FwBar(c, 200f, 200f, i * 6f, rf * 0.70f, rf * 0.94f, rf * 0.075f, bar);
					}
					else
					{
						FwBar(c, 200f, 200f, i * 6f, rf * 0.865f, rf * 0.94f, rf * 0.024f, bar);
					}
				}
			}

			// the reading: its name under twelve, a small gauge dial above six
			AfText(c, x.Label.ToUpperInvariant(), 200f, 106f, 12f, soft, align: 0, track: 2f, font: "Bahnschrift SemiBold");
			const float gx = 200f, gy = 266f, gr = 46f;
			using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = soft.WithAlpha(120) })
			{
				c.DrawCircle(gx, gy, gr, ring);
			}
			using (var tick = new SKPaint { IsAntialias = true, Color = ink })
			{
				for (int k = 0; k <= 20; k++)
				{
					float deg = -135f + 270f * k / 20f;
					if (k % 5 == 0)
					{
						FwBar(c, gx, gy, deg, gr * 0.68f, gr * 0.92f, 3f, tick);
					}
					else
					{
						FwBar(c, gx, gy, deg, gr * 0.80f, gr * 0.92f, 1.4f, tick);
					}
				}
			}
			using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = red })
			{
				float rr = gr * 0.58f;
				c.DrawArc(new SKRect(gx - rr, gy - rr, gx + rr, gy + rr), 18f, 27f, false, arc);
			}
			AfText(c, x.Value, gx, gy + gr * 0.36f, 15f, ink, align: 0, font: "Bahnschrift SemiBold");
			using (SKPath needle = FwHand(0f, gr * 0.16f, gr * 0.78f, 4.5f, 1.6f))
			{
				needle.Transform(SKMatrix.CreateTranslation(-200f, -200f).PostConcat(SKMatrix.CreateRotationDegrees(-135f + 270f * f)).PostConcat(SKMatrix.CreateTranslation(gx, gy)));
				FwShadow(c, needle, 1.5f, 3f, 2f, 60);
				using var np = new SKPaint { IsAntialias = true, Color = ink };
				c.DrawPath(needle, np);
				np.Color = red;
				c.DrawCircle(gx, gy, 3.5f, np);
			}

			// the clock's hands: the second hand sweeps in 58.5 s and waits; the minute hand jumps with a small overshoot
			float sec = now.Second + now.Millisecond / 1000f;
			float u = Math.Clamp(sec / 0.4f, 0f, 1f) - 1f;
			float jump = 1f + u * u * (3.2f * u + 2.2f);
			float minute = (now.Minute - 1 + jump) * 6f;
			float hour = (now.Hour % 12 + (now.Minute - 1 + jump) / 60f) * 30f;
			float second = Math.Min(sec / 58.5f, 1f) * 360f;
			using SKPath hh = FwHand(hour, rf * 0.24f, rf * 0.62f, rf * 0.105f, rf * 0.085f);
			using SKPath mh = FwHand(minute, rf * 0.24f, rf * 0.92f, rf * 0.085f, rf * 0.06f);
			using (var both = new SKPath())
			{
				both.AddPath(hh);
				both.AddPath(mh);
				FwShadow(c, both, 3f, 7f, 4f, light ? (byte)70 : (byte)120);
			}
			using (var hp = new SKPaint { IsAntialias = true, Color = ink })
			{
				c.DrawPath(hh, hp);
				c.DrawPath(mh, hp);
			}
			float w = rf * 0.022f, ringAt = rf * 0.66f, ringR = rf * 0.068f;
			using var shape = new SKPath();
			shape.AddRect(new SKRect(-w / 2f, -(ringAt - ringR), w / 2f, rf * 0.12f));
			shape.AddRect(new SKRect(-w * 0.95f, rf * 0.1f, w * 0.95f, rf * 0.3f));
			shape.AddRect(new SKRect(-w / 2f, -rf * 0.9f, w / 2f, -(ringAt + ringR)));
			shape.AddCircle(0f, 0f, rf * 0.035f);
			shape.AddCircle(0f, -ringAt, ringR);
			shape.AddCircle(0f, -ringAt, ringR - rf * 0.026f, SKPathDirection.CounterClockwise);
			shape.Transform(SKMatrix.CreateRotationDegrees(second).PostConcat(SKMatrix.CreateTranslation(200f, 200f)));
			FwShadow(c, shape, 4f, 8f, 3f, 70);
			using (var rp = new SKPaint { IsAntialias = true, Color = red })
			{
				c.DrawPath(shape, rp);
			}
		});
	}

	// ---------- Sport Dial ----------

	private void SportDialFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Sport Dial");
		DateTime now = WatchNow(x);
		SKColor cream = SkiaKit.Sk(x.C1, 1f), accent = SkiaKit.Sk(x.C2, 1f);
		SKColor black = new SKColor(17, 17, 18), lume = FwMix(cream, SKColors.White, 0.35f);
		SKColor creamOff = FwMix(cream, black, 0.12f), blackOff = new SKColor(52, 52, 50);
		const float R = 198f, inR = 160f;

		// the peak of the last minute, falling back slowly
		string key = x.Fan.Serial ?? "";
		if (!_sdPeak.TryGetValue(key, out var pk) || x.T < pk.At)
		{
			pk = (f, x.T);
		}
		float peak = Math.Max(f, pk.Peak - (float)(x.T - pk.At) / 60f);
		_sdPeak[key] = (peak, x.T);

		FaceSkia(x.G, c =>
		{
			c.Clear(new SKColor(10, 10, 11));
			// the bezel ring
			using (var ring = new SKPaint { IsAntialias = true, Color = cream })
			{
				c.DrawCircle(200f, 200f, R, ring);
			}
			// the inside: black on the left, cream on the right
			c.Save();
			using (var clip = new SKPath())
			{
				clip.AddCircle(200f, 200f, inR);
				c.ClipPath(clip, SKClipOperation.Intersect, true);
			}
			using (var bp = new SKPaint { Color = black })
			{
				c.DrawRect(0f, 0f, 200f, 400f, bp);
			}
			using (var cp = new SKPaint { Color = FwMix(cream, SKColors.White, 0.12f) })
			{
				c.DrawRect(200f, 0f, 200f, 400f, cp);
			}
			c.Restore();
			using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = black })
			{
				c.DrawCircle(200f, 200f, inR, edge);
				edge.StrokeWidth = 3f;
				c.DrawCircle(200f, 200f, R - 1.5f, edge);
			}
			using (var ink = new SKPaint { IsAntialias = true, Color = black })
			{
				for (int i = 0; i < 60; i++)
				{
					if (i % 15 == 0)
					{
						continue;
					}
					if (i % 5 == 0)
					{
						FwBar(c, 200f, 200f, i * 6f, R - 22f, R - 6f, 4f, ink);
					}
					else
					{
						FwBar(c, 200f, 200f, i * 6f, R - 13f, R - 6f, 1.6f, ink);
					}
				}
			}
			AfText(c, "12", 200f, 200f - R + 5f, 38f, black, align: 0, font: "Bahnschrift|B");
			SKPath three = AfPath("3", "Bahnschrift|B", 38f);
			AfText(c, "3", 200f + R - 8f, 200f - three.TightBounds.Height / 2f, 38f, black, align: 1, font: "Bahnschrift|B");
			AfText(c, "6", 200f, 200f + R - 5f, 38f, black, align: 0, font: "Bahnschrift|B", bottom: true);
			void Word(string text, float deg, SKColor color)
			{
				float a = deg * MathF.PI / 180f, rr = (R + inR) / 2f - 1f;
				bool top = deg < 90f || deg > 270f;
				c.Save();
				c.Translate(200f + MathF.Sin(a) * rr, 200f - MathF.Cos(a) * rr);
				c.RotateDegrees(top ? deg : deg + 180f);
				SKPath p = AfPath(text, "Bahnschrift SemiBold", 13f, 1.2f);
				SKRect b = p.TightBounds;
				c.Translate(-b.MidX, -b.MidY);
				using var paint = new SKPaint { IsAntialias = true, Color = color };
				c.DrawPath(p, paint);
				c.Restore();
			}
			Word("TIME", 330f, black);
			Word("LOAD", 300f, black);
			Word("TEMP", 240f, black);
			Word("FANS", 210f, black);
			Word("MAX", 30f, accent);
			Word("MIN", 150f, black);

			// upright bars with red lettering: the reading on top, its peak below
			string tag = x.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[0].ToUpperInvariant() : "";
			if (tag.Length > 4)
			{
				tag = tag[..4];
			}
			void Upright(float top, float k, string letters)
			{
				const int n = 8;
				using var paint = new SKPaint { IsAntialias = true };
				for (int i = 0; i < n; i++)
				{
					float lit = Math.Clamp((k - i / (float)n) * n, 0f, 1f);
					float yy = top + (n - 1 - i) * 10f;
					float w = 16f + 6f * (1f - i / (float)(n - 1));
					paint.Color = FwMix(blackOff, i == n - 1 ? accent : lume, lit);
					c.DrawRect(new SKRect(166f - w, yy, 166f, yy + 6.5f), paint);
				}
				for (int i = 0; i < letters.Length; i++)
				{
					AfText(c, letters[i].ToString(), 182f, top + 2f + i * 19f, 14f, accent, align: 0, font: "Bahnschrift SemiBold");
				}
			}
			Upright(66f, f, tag);
			Upright(256f, peak, "MAX");

			// date window at nine
			var pill = new SKRect(52f, 180f, 148f, 220f);
			using (var pp = new SKPaint { IsAntialias = true, Color = cream })
			{
				c.DrawRoundRect(pill, 20f, 20f, pp);
			}
			AfText(c, now.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant(), 66f, 185f, 13f, black, font: "Bahnschrift SemiBold");
			AfText(c, now.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), 66f, 215f, 13f, black, font: "Bahnschrift SemiBold", bottom: true);
			AfText(c, now.Day.ToString(CultureInfo.InvariantCulture), 138f, 186f, 26f, accent, align: 1, font: "Bahnschrift|B");
			AfText(c, "LUMALI", 104f, 160f, 11f, lume, align: 0, track: 3f, font: "Bahnschrift SemiBold");

			// the cream side: the reading, the digital time, the week
			AfText(c, x.Label.ToUpperInvariant(), 216f, 92f, 10f, black, track: 1.5f, font: "Bahnschrift SemiBold");
			AfText(c, x.Value, 216f, 108f, 34f, black, font: "Bahnschrift|B");
			using (var rule = new SKPaint { IsAntialias = true, StrokeWidth = 1.5f, Color = black })
			{
				c.DrawLine(216f, 156f, 316f, 156f, rule);
				c.DrawLine(216f, 252f, 316f, 252f, rule);
			}
			SKRect tr = AfText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), 214f, 172f, 38f, black, font: "Bahnschrift|B");
			AfText(c, now.ToString("ss", CultureInfo.InvariantCulture), tr.Right + 4f, tr.Bottom, 15f, accent, bottom: true, font: "Bahnschrift SemiBold");
			AfText(c, "WEEK " + ISOWeek.GetWeekOfYear(now).ToString(CultureInfo.InvariantCulture), 216f, 266f, 13f, black, track: 1.5f, font: "Bahnschrift SemiBold");

			// the seconds arc along the cream edge
			float sec = now.Second + now.Millisecond / 1000f;
			using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f })
			{
				const int n = 16;
				float rr = inR - 9f;
				var oval = new SKRect(200f - rr, 200f - rr, 200f + rr, 200f + rr);
				for (int i = 0; i < n; i++)
				{
					float lit = Math.Clamp((sec / 60f - i / (float)n) * n, 0f, 1f);
					arc.Color = FwMix(creamOff, black, lit);
					c.DrawArc(oval, -40f + i * 80f / n + 0.6f, 80f / n - 1.6f, false, arc);
				}
			}

			// lume hands with a dark edge and slot, a slim red seconds hand
			float hour = (now.Hour % 12 + now.Minute / 60f + sec / 3600f) * 30f;
			float minute = (now.Minute + sec / 60f) * 6f;
			using SKPath hh = FwHand(hour, 16f, 92f, 14f, 12f, 14f);
			using SKPath mh = FwHand(minute, 16f, 136f, 12f, 10f, 14f);
			using (var both = new SKPath())
			{
				both.AddPath(hh);
				both.AddPath(mh);
				FwShadow(c, both, 3f, 6f, 4f, 110);
			}
			using (var fill = new SKPaint { IsAntialias = true, Color = lume })
			using (var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = black, StrokeJoin = SKStrokeJoin.Round })
			{
				foreach (SKPath p in new[] { hh, mh })
				{
					c.DrawPath(p, fill);
					c.DrawPath(p, line);
				}
				line.StrokeCap = SKStrokeCap.Round;
				foreach (var (deg, a, b) in new[] { (hour, 24f, 74f), (minute, 24f, 118f) })
				{
					float rad = deg * MathF.PI / 180f;
					c.DrawLine(200f + MathF.Sin(rad) * a, 200f - MathF.Cos(rad) * a, 200f + MathF.Sin(rad) * b, 200f - MathF.Cos(rad) * b, line);
				}
			}
			using SKPath sh = FwHand(sec * 6f, 32f, 150f, 3.6f, 2f);
			FwShadow(c, sh, 3f, 6f, 2.5f, 90);
			using (var rp = new SKPaint { IsAntialias = true, Color = accent })
			{
				c.DrawPath(sh, rp);
				using var cap = new SKPaint { IsAntialias = true, Color = black };
				c.DrawCircle(200f, 200f, 10f, cap);
				c.DrawCircle(200f, 200f, 5.5f, rp);
			}
			// a soft crystal sheen
			using (var sheen = SKShader.CreateLinearGradient(new SKPoint(60f, 0f), new SKPoint(240f, 230f), new[] { new SKColor(255, 255, 255, 30), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
			using (var sp = new SKPaint { IsAntialias = true, Shader = sheen, BlendMode = SKBlendMode.Plus })
			{
				c.DrawCircle(200f, 200f, R - 3f, sp);
			}
		});
	}
}
