using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The Telemetry layout's cards as fan faces: Telemetry (a sage card, ink figures and bars, an orange accent) and
/// Telemetry Dark (charcoal, sage-white figures, orange bars). Both are drawn by one face: a caption with a live dot,
/// hazard dashes running faster with the reading, the reading in big bold figures that roll up when they change (shrunk
/// to fit), hexagons lighting in turn, a 40-second bar history (one bar a second, scrolling smoothly, the live bar in
/// the accent, a slowly falling peak line), and the time. At 90 % and up the dashes turn the accent colour and a
/// CAUTION tag blinks beside the caption.
/// </summary>
public sealed partial class Faces
{
	private const string TfBold = "Segoe UI Variable Display|B", TfCap = "Bahnschrift SemiBold";

	private readonly Dictionary<string, (float[] Hist, double At, float Peak)> _tfHist = new();

	private readonly Dictionary<string, (char[] Ch, char[] Was, double[] Since)> _tfRoll = new();

	private void TelemetryFace(FaceContext x, bool dark)
	{
		string face = dark ? "Telemetry Dark" : "Telemetry";
		var (s, f, dt) = GlowStep(x, face);
		f = Math.Clamp(f, 0f, 1f);
		DateTime now = WatchNow(x);
		SKColor card = SkiaKit.Sk(x.C1, 1f), accent = SkiaKit.Sk(x.C2, 1f);
		float lum = (0.299f * card.Red + 0.587f * card.Green + 0.114f * card.Blue) / 255f;
		bool light = lum > 0.5f;
		SKColor ink = light ? new SKColor(32, 35, 34) : new SKColor(228, 238, 228);
		SKColor dim = ink.WithAlpha(light ? (byte)170 : (byte)150);
		string key = x.Fan.Serial ?? x.Label;

		// the reading once a second, kept for 40 seconds, with a peak that falls slowly
		if (!_tfHist.TryGetValue(key + face, out var h))
		{
			h = (new float[41], x.T, f);
			Array.Fill(h.Hist, f);
		}
		while (x.T - h.At >= 1.0)
		{
			Array.Copy(h.Hist, 1, h.Hist, 0, h.Hist.Length - 1);
			h.Hist[^1] = f;
			h.At += 1.0;
			if (x.T - h.At > 5.0)
			{
				h.At = x.T;
			}
		}
		if (x.T < h.At)
		{
			h.At = x.T;
		}
		h.Peak = Math.Max(f, h.Peak - (float)dt * 0.02f);
		_tfHist[key + face] = h;
		float frac = (float)Math.Clamp(x.T - h.At, 0.0, 1.0);
		bool hot = x.Fraction >= 0.9f;
		s.Travel += dt * (8.0 + 50.0 * f);

		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, card));
			using var p = new SKPaint { IsAntialias = true, Color = ink };

			// caption with a live dot, and a CAUTION tag when high
			float blink = 0.5f + 0.5f * MathF.Cos((float)(x.T * Math.PI * (hot ? 4.0 : 2.0)));
			p.Color = accent.WithAlpha((byte)(110 + 145 * blink));
			string cap = x.Label.ToUpperInvariant();
			if (cap.Length > 16)
			{
				cap = cap[..16];
			}
			SKRect cb = AfText(c, cap, 214f, 44f, 20f, ink, align: 0, track: 1.4f, font: TfCap);
			c.DrawCircle(cb.Left - 14f, cb.MidY, 5.5f, p);

			// hazard dashes, running with the reading
			var hz = new SKRect(130f, 80f, 270f, 92f);
			c.Save();
			c.ClipRect(hz);
			using (var path = new SKPath())
			{
				float off = (float)(s.Travel % 12.0);
				for (float k = hz.Left - 24f + off; k < hz.Right + 12f; k += 12f)
				{
					path.MoveTo(k, hz.Bottom);
					path.LineTo(k + 6f, hz.Bottom);
					path.LineTo(k + 12f, hz.Top);
					path.LineTo(k + 6f, hz.Top);
					path.Close();
				}
				p.Color = hot && blink > 0.5f ? accent : ink;
				c.DrawPath(path, p);
			}
			c.Restore();
			if (hot && x.T % 1.0 < 0.6)
			{
				var tag = new SKRect(150f, 98f, 250f, 120f);
				p.Color = accent;
				c.DrawRect(tag, p);
				AfText(c, "CAUTION", tag.MidX, tag.Top + 5f, 13f, light ? ink : new SKColor(32, 35, 34), align: 0, track: 1.4f, font: TfCap);
			}

			// a crosshair box on the left, hexagons lighting in turn on the right
			using (var o = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = ink })
			{
				c.DrawRect(new SKRect(98f, 78f, 114f, 94f), o);
				c.DrawLine(102f, 86f, 110f, 86f, o);
				c.DrawLine(106f, 82f, 106f, 90f, o);
				o.Color = ink.WithAlpha(90);
				o.StrokeWidth = 1.4f;
				(float X, float Y)[] cells = { (300f, 86f), (324f, 100f), (300f, 114f) };
				for (int i = 0; i < cells.Length; i++)
				{
					float lit = MathF.Max(0f, MathF.Cos((float)((x.T * (0.6 + 1.6 * f) - i) / cells.Length * Math.PI * 2.0)));
					lit = MathF.Pow(lit, 4f);
					using var fp = new SKPaint { IsAntialias = true, Color = accent.WithAlpha((byte)(170 * lit)) };
					TfHex(c, cells[i].X, cells[i].Y, 14f, fp);
					TfHex(c, cells[i].X, cells[i].Y, 14f, o);
				}
			}

			// the reading in big figures
			TfNumber(c, key + face, x.Value, 200f, 124f, 96f, 290f, ink, x.T);

			// the 40-second history
			var g = new SKRect(72f, 214f, 328f, 314f);
			using (var gp = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = ink.WithAlpha(40), PathEffect = SKPathEffect.CreateDash(new[] { 3f, 4f }, 0f) })
			{
				for (int k = 1; k < 4; k++)
				{
					float y = g.Bottom - g.Height * k / 4f;
					c.DrawLine(g.Left, y, g.Right, y, gp);
				}
			}
			float step = g.Width / 40f;
			c.Save();
			c.ClipRect(new SKRect(g.Left, g.Top - 2f, g.Right, g.Bottom));
			for (int i = 0; i < 41; i++)
			{
				float x0 = g.Right - (40 - i + frac) * step;
				float x1 = i < 40 ? x0 + step : g.Right;
				float v = i < 40 ? h.Hist[i + 1] : f;
				if (x1 - x0 < 2f)
				{
					continue;
				}
				float age = (40 - i) / 40f;
				// the newest second and the live bar stand out: the accent on a light card, the ink on a dark one
				bool newest = i >= 39;
				p.Color = newest ? (light ? accent : ink) : light ? ink.WithAlpha((byte)(255 - 120 * age)) : accent.WithAlpha((byte)(230 - 140 * age));
				float top = g.Bottom - Math.Max(1.5f, v * g.Height);
				c.DrawRect(new SKRect(x0 + 1f, top, x1 - 1f, g.Bottom), p);
			}
			c.Restore();
			p.Color = ink;
			c.DrawRect(g.Left, g.Bottom, g.Width, 1.6f, p);
			if (h.Peak - f > 0.015f)
			{
				float py = g.Bottom - h.Peak * g.Height;
				using var pk = new SKPaint { IsAntialias = true, StrokeWidth = 1.6f, Color = accent, PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0f) };
				c.DrawLine(g.Left, py, g.Right, py, pk);
				AfText(c, "PEAK", g.Left, py - 15f, 11f, accent, track: 1f, font: TfCap);
			}
			AfText(c, "40 S", g.Left, g.Bottom + 8f, 13f, dim, track: 0.8f, font: TfCap);
			AfText(c, "NOW", g.Right, g.Bottom + 8f, 13f, dim, align: 1, track: 0.8f, font: TfCap);

			// the time
			AfText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), 200f, 336f, 24f, ink, align: 0, font: TfBold);
		});
	}

	private static void TfHex(SKCanvas c, float x, float y, float r, SKPaint p)
	{
		using var path = new SKPath();
		for (int i = 0; i < 6; i++)
		{
			float a = i * 60f * MathF.PI / 180f;
			if (i == 0)
			{
				path.MoveTo(x + MathF.Cos(a) * r, y + MathF.Sin(a) * r);
			}
			else
			{
				path.LineTo(x + MathF.Cos(a) * r, y + MathF.Sin(a) * r);
			}
		}
		path.Close();
		c.DrawPath(path, p);
	}

	/// <summary>
	/// Bold figures centred on x with their tops at top, in fixed cells; a changed character rolls up in its cell.
	/// The size shrinks so the line fits maxW.
	/// </summary>
	private void TfNumber(SKCanvas c, string id, string text, float x, float top, float size, float maxW, SKColor color, double t)
	{
		int n = Math.Min(12, text.Length);
		float Measure(float sz, float[] widths)
		{
			SKRect zb0 = AfPath("0", TfBold, sz).TightBounds;
			float gap0 = sz * 0.06f, tot = 0f;
			for (int i = 0; i < n; i++)
			{
				char ch = text[i];
				widths[i] = char.IsDigit(ch) ? zb0.Width : ch == ' ' ? sz * 0.22f : Math.Max(sz * 0.12f, AfPath(ch.ToString(), TfBold, sz).TightBounds.Width);
				tot += widths[i] + (i < n - 1 ? gap0 : 0f);
			}
			return tot;
		}
		var w = new float[n];
		float total = Measure(size, w);
		if (total > maxW)
		{
			size = MathF.Floor(size * maxW / total);
			total = Measure(size, w);
		}
		SKRect zb = AfPath("0", TfBold, size).TightBounds;
		float hgt = zb.Height, gap = size * 0.06f;
		// the line sits in the same box whatever its size: centred on the 96-px figures' height
		top += (AfPath("0", TfBold, 96f).TightBounds.Height - hgt) / 2f;
		bool fresh = !_tfRoll.TryGetValue(id, out var st);
		if (fresh)
		{
			st = (new char[12], new char[12], new double[12]);
			_tfRoll[id] = st;
		}
		for (int k = n; k < 12; k++)
		{
			st.Ch[k] = '\0';
		}
		using var paint = new SKPaint { IsAntialias = true, Color = color };
		float cx = x - total / 2f;
		for (int i = 0; i < n; i++)
		{
			int k = n - 1 - i;
			char ch = text[i];
			if (st.Ch[k] != ch)
			{
				st.Was[k] = st.Ch[k];
				st.Since[k] = fresh ? -100.0 : t;
				st.Ch[k] = ch;
			}
			float sp = (float)Math.Clamp((t - st.Since[k]) / 0.42, 0.0, 1.0);
			sp = 1f - MathF.Pow(1f - sp, 3f);
			if (sp < 1f)
			{
				c.Save();
				c.ClipRect(new SKRect(cx - size * 0.1f, top - size * 0.03f, cx + w[i] + size * 0.1f, top + hgt + size * 0.06f));
				TfGlyph(c, st.Was[k], cx, w[i], top - zb.Top - sp * hgt * 1.3f, size, paint);
				TfGlyph(c, ch, cx, w[i], top - zb.Top + (1f - sp) * hgt * 1.3f, size, paint);
				c.Restore();
			}
			else
			{
				TfGlyph(c, ch, cx, w[i], top - zb.Top, size, paint);
			}
			cx += w[i] + gap;
		}
	}

	private void TfGlyph(SKCanvas c, char ch, float cellX, float cellW, float dy, float size, SKPaint paint)
	{
		if (ch == '\0' || ch == ' ')
		{
			return;
		}
		SKPath path = AfPath(ch.ToString(), TfBold, size);
		SKRect b = path.TightBounds;
		float dx = char.IsDigit(ch) ? cellX + (cellW - b.Width) / 2f - b.Left : cellX - b.Left;
		c.Save();
		c.Translate(dx, dy);
		c.DrawPath(path, paint);
		c.Restore();
	}
}
