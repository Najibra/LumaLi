using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Airframe on a fan: a round brushed-aluminium inspection plate with a ring of rivets, stencilled markings and the
/// reading as a big black panel number on counter wheels (each digit rolls through the digits between, rightmost
/// first). Round it, a tachometer scale with a red arc and pointer riding the reading on a spring. Two cockpit lamps
/// (NORM / HIGH) flash three times when they change, and a paper sticker at the foot carries a barcode of the value
/// swept by a red laser and the time. A soft band of light glides across the metal.
/// Colours: Ink (the number and stencils) and Accent (arc, pointer, laser).
/// </summary>
public sealed partial class Faces
{
	private const string AfStencil = "Courier New|B", AfBig = "Bahnschrift|B", AfLabel = "Bahnschrift SemiCondensed", AfLabelBold = "Bahnschrift SemiBold SemiCondensed";

	private SKImage? _afBase;

	private readonly Dictionary<string, SKPath> _afPaths = new Dictionary<string, SKPath>();

	/// <summary>Per fan: the value shown before, the value now, when it changed, and when the HIGH lamp last changed.</summary>
	private readonly Dictionary<string, (string Old, string New, double At, bool High, double HighAt)> _afFace = new Dictionary<string, (string, string, double, bool, double)>();

	private SKPath AfPath(string text, string font, float size, float track = 0f)
	{
		string key = $"{font}|{size}|{track}|{text}";
		if (_afPaths.TryGetValue(key, out SKPath? cached))
		{
			return cached;
		}
		if (_afPaths.Count > 600)
		{
			foreach (SKPath old in _afPaths.Values)
			{
				old.Dispose();
			}
			_afPaths.Clear();
		}
		int style = font.EndsWith("|B", StringComparison.Ordinal) ? (int)FontStyle.Bold : 0;
		string name = style != 0 ? font[..^2] : font;
		FontFamily family = Family(name);
		using var gp = new GraphicsPath(FillMode.Winding);
		if (track == 0f)
		{
			gp.AddString(text, family, style, size, PointF.Empty, StringFormat.GenericTypographic);
		}
		else
		{
			float x = 0f;
			foreach (char ch in text)
			{
				if (ch == ' ')
				{
					x += size * 0.3f + track;
					continue;
				}
				using var one = new GraphicsPath(FillMode.Winding);
				one.AddString(ch.ToString(), family, style, size, new PointF(x, 0f), StringFormat.GenericTypographic);
				RectangleF b = one.GetBounds();
				gp.AddPath(one, false);
				x = (b.Width > 0f ? b.Right : x + size * 0.3f) + track;
			}
		}
		return _afPaths[key] = SkiaKit.FromGdi(gp);
	}

	/// <summary>Text placed by its ink: x is its left edge, centre or right edge (align -1, 0, 1), y its top (or foot when bottom).</summary>
	private SKRect AfText(SKCanvas c, string text, float x, float y, float size, SKColor color, int align = -1, float track = 0f, string font = AfLabel, bool bottom = false)
	{
		SKPath p = AfPath(text, font, size, track);
		SKRect b = p.TightBounds;
		if (b.IsEmpty)
		{
			return new SKRect(x, y, x, y);
		}
		float dx = x - (align < 0 ? b.Left : align == 0 ? b.MidX : b.Right);
		float dy = y - (bottom ? b.Bottom : b.Top);
		c.Save();
		c.Translate(dx, dy);
		using (var paint = new SKPaint { Color = color, IsAntialias = true })
		{
			c.DrawPath(p, paint);
		}
		c.Restore();
		return new SKRect(b.Left + dx, b.Top + dy, b.Right + dx, b.Bottom + dy);
	}

	private void AirframeFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Airframe");
		SKColor ink = SkiaKit.Sk(x.C1, 1f), accent = SkiaKit.Sk(x.C2, 1f);
		SKColor stencil = ink.WithAlpha(225);

		// the counter wheels: remember what was shown, so a change rolls from it
		bool high = f >= 0.9f;
		if (!_afFace.TryGetValue(x.Fan.Serial, out var st))
		{
			st = (x.Value, x.Value, -100.0, high, -100.0);
		}
		if (st.New != x.Value)
		{
			st = (st.New, x.Value, x.T, st.High, st.HighAt);
		}
		if (st.High != high)
		{
			st = (st.Old, st.New, st.At, high, x.T);
		}
		_afFace[x.Fan.Serial] = st;

		FaceSkia(x.G, c =>
		{
			_afBase ??= AfFaceBase();
			c.DrawImage(_afBase, 0f, 0f);

			// stencils
			AfText(c, x.Label.ToUpperInvariant(), 200f, 62f, 17f, stencil, align: 0, track: 2f, font: AfStencil);
			AfText(c, "COMPT  2.1.3", 200f, 86f, 11f, stencil, align: 0, track: 1.5f, font: AfStencil);

			AfFaceTacho(c, f, ink, accent);
			AfFaceNumber(c, st.Old, st.New, x.T - st.At, ink);
			AfFaceLamps(c, x.T, high, x.T - st.HighAt);
			AfFaceSticker(c, x, ink, accent);

			// a soft band of light gliding across the metal
			float sweep = (float)(x.T / 9.0 % 1.0) * 900f - 250f;
			using var band = SKShader.CreateLinearGradient(new SKPoint(sweep - 120f, 0f), new SKPoint(sweep + 120f, 400f),
				new[] { new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 34), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp);
			using var bp = new SKPaint { Shader = band, BlendMode = SKBlendMode.Plus };
			c.DrawRect(0f, 0f, 400f, 400f, bp);
		});
	}

	/// <summary>The metal, drawn once: brushed aluminium, a seam and a ring of rivets, two screws.</summary>
	private static SKImage AfFaceBase()
	{
		using var surface = SKSurface.Create(new SKImageInfo(400, 400, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		using (var shader = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(400f, 400f), new[] { new SKColor(196, 199, 200), new SKColor(176, 179, 180), new SKColor(188, 191, 192) }, null, SKShaderTileMode.Clamp))
		using (var fill = new SKPaint { Shader = shader })
		{
			c.DrawRect(0f, 0f, 400f, 400f, fill);
		}
		using (var streak = new SKPaint { StrokeWidth = 1f })
		{
			for (int i = 0; i < 1100; i++)
			{
				double a = Hash01(i * 3 + 5), b = Hash01(i * 7 + 1), k = Hash01(i * 11 + 2);
				float y = (float)b * 400f, x0 = (float)a * 400f - 60f, len = 20f + (float)k * 120f;
				streak.Color = k > 0.5 ? new SKColor(255, 255, 255, (byte)(10 + 14 * k)) : new SKColor(60, 62, 64, (byte)(8 + 16 * (1 - k)));
				c.DrawLine(x0, y, x0 + len, y, streak);
			}
		}
		// the panel seam with a lit edge, and the rivets just outside it
		using (var seam = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = new SKColor(70, 73, 75, 220) })
		{
			c.DrawCircle(200f, 200f, 177f, seam);
			seam.StrokeWidth = 1f;
			seam.Color = new SKColor(236, 238, 238, 150);
			c.DrawCircle(200f, 200f, 175.5f, seam);
		}
		using (var head = new SKPaint { IsAntialias = true })
		using (var shade = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f })
		{
			for (int i = 0; i < 40; i++)
			{
				float a = i * MathF.PI * 2f / 40f;
				float x = 200f + MathF.Cos(a) * 186f, y = 200f + MathF.Sin(a) * 186f;
				head.Color = new SKColor(60, 62, 64, 90);
				c.DrawCircle(x + 0.8f, y + 1f, 2.9f, head);
				head.Color = new SKColor(196, 199, 200);
				c.DrawCircle(x, y, 2.5f, head);
				shade.Color = new SKColor(255, 255, 255, 170);
				c.DrawArc(new SKRect(x - 2f, y - 2f, x + 2f, y + 2f), 180f, 90f, false, shade);
			}
		}
		foreach (float sx in new[] { 112f, 288f })
		{
			using var p = new SKPaint { IsAntialias = true, Color = new SKColor(70, 72, 74, 120) };
			const float r = 5f, sy = 40f;
			c.DrawCircle(sx + 1f, sy + 1.4f, r + 1f, p);
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(sx - r, sy - r), new SKPoint(sx + r, sy + r), new[] { new SKColor(226, 228, 229), new SKColor(140, 143, 145) }, null, SKShaderTileMode.Clamp))
			{
				p.Shader = sh;
				c.DrawCircle(sx, sy, r, p);
				p.Shader = null;
			}
			p.Color = new SKColor(60, 62, 64, 220);
			p.StrokeWidth = 1.4f;
			c.DrawLine(sx - r * 0.65f, sy, sx + r * 0.65f, sy, p);
			c.DrawLine(sx, sy - r * 0.65f, sx, sy + r * 0.65f, p);
		}
		// the sticker's paper and shadow
		var paper = new SKRect(108f, 282f, 292f, 344f);
		using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 60), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) })
		{
			c.DrawRoundRect(new SKRect(paper.Left + 2f, paper.Top + 3f, paper.Right + 2f, paper.Bottom + 3f), 3f, 3f, shadow);
		}
		using (var pp = new SKPaint { Color = new SKColor(222, 221, 216), IsAntialias = true })
		{
			c.DrawRoundRect(paper, 3f, 3f, pp);
		}
		return surface.Snapshot();
	}

	/// <summary>The scale round the plate: 0 to 100 over 270°, a red arc and wedge pointer at the eased reading.</summary>
	private void AfFaceTacho(SKCanvas c, float f, SKColor ink, SKColor accent)
	{
		const float cx = 200f, cy = 200f, r0 = 150f, start = 135f, sweep = 270f;
		using var tick = new SKPaint { IsAntialias = true, Color = ink.WithAlpha(215) };
		for (int i = 0; i <= 40; i++)
		{
			float a = (start + sweep * i / 40f) * MathF.PI / 180f;
			bool major = i % 4 == 0;
			tick.StrokeWidth = major ? 2.4f : 1.2f;
			float r2 = r0 + (major ? 12f : 6f);
			c.DrawLine(cx + MathF.Cos(a) * r0, cy + MathF.Sin(a) * r0, cx + MathF.Cos(a) * r2, cy + MathF.Sin(a) * r2, tick);
			if (major && i % 8 == 0)
			{
				float rl = r0 - 12f;
				AfText(c, (i / 4 * 10).ToString(), cx + MathF.Cos(a) * rl, cy + MathF.Sin(a) * rl - 5f, 11f, ink.WithAlpha(215), align: 0, font: AfLabelBold);
			}
		}
		using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = accent })
		{
			if (f > 0.002f)
			{
				c.DrawArc(new SKRect(cx - r0 + 3f, cy - r0 + 3f, cx + r0 - 3f, cy + r0 - 3f), start, sweep * f, false, arc);
			}
		}
		float pa = (start + sweep * f) * MathF.PI / 180f;
		float cs = MathF.Cos(pa), sn = MathF.Sin(pa);
		using var ptr = new SKPath();
		ptr.MoveTo(cx + cs * (r0 - 6f), cy + sn * (r0 - 6f));
		ptr.LineTo(cx + cs * (r0 + 16f) - sn * 6f, cy + sn * (r0 + 16f) + cs * 6f);
		ptr.LineTo(cx + cs * (r0 + 16f) + sn * 6f, cy + sn * (r0 + 16f) - cs * 6f);
		ptr.Close();
		using var pp = new SKPaint { IsAntialias = true, Color = accent };
		c.DrawPath(ptr, pp);
	}

	/// <summary>
	/// The reading in big black panel figures on counter wheels. After a change each digit rolls up through the digits
	/// between its old and new value, the rightmost first, easing to rest; other characters slide in.
	/// </summary>
	private void AfFaceNumber(SKCanvas c, string oldV, string newV, double since, SKColor ink)
	{
		SKRect zero = AfPath("0", AfBig, 200f).TightBounds;
		const float sx = 1.12f, top = 132f, boxH = 104f, maxW = 250f;
		float scale = boxH / zero.Height;
		// each character's width, digits all one width so the figures sit still while they roll
		float W(char ch) => (char.IsDigit(ch) ? zero.Width : Math.Max(AfPath(ch.ToString(), AfBig, 200f).TightBounds.Width, zero.Width * 0.3f)) * scale * sx;
		float gap = 10f;
		float total = 0f;
		foreach (char ch in newV)
		{
			total += W(ch) + gap;
		}
		total -= gap;
		if (total > maxW)
		{
			float k = maxW / total;
			scale *= k;
			gap *= k;
			total = maxW;
		}
		float h = zero.Height * scale, y0 = top + (boxH - h) / 2f, pitch = h + 24f;
		float x = 200f - total / 2f;
		// pad the old value on the left so the places line up from the right
		string old = oldV.Length >= newV.Length ? oldV[^newV.Length..] : oldV.PadLeft(newV.Length, ' ');
		c.Save();
		c.ClipRect(new SKRect(40f, y0 - 4f, 360f, y0 + h + 4f));
		using var paint = new SKPaint { IsAntialias = true, Color = ink.WithAlpha(240) };
		for (int i = 0; i < newV.Length; i++)
		{
			char to = newV[i], from = old[i];
			int fromRight = newV.Length - 1 - i;
			double u = Math.Clamp((since - fromRight * 0.08) / 0.55, 0.0, 1.0);
			float e = (float)(1.0 - Math.Pow(1.0 - u, 3.0));
			float cw = W(to);
			void Glyph(char ch, float oy)
			{
				if (ch == ' ' || Math.Abs(oy) >= pitch)
				{
					return;
				}
				SKPath p = AfPath(ch.ToString(), AfBig, 200f);
				SKRect b = p.TightBounds;
				c.Save();
				// digits sit on the zero's line; others (%, °) keep their own height from its top
				c.Translate(x + (cw - b.Width * scale * sx) / 2f, y0 + oy);
				c.Scale(scale * sx, scale);
				c.Translate(-b.Left, -zero.Top);
				c.DrawPath(p, paint);
				c.Restore();
			}
			if (from == to || u >= 1.0)
			{
				Glyph(to, 0f);
			}
			else if (char.IsDigit(from) && char.IsDigit(to))
			{
				int steps = (to - from + 10) % 10;
				float pos = (from - '0') + steps * e;
				int d0 = (int)MathF.Floor(pos);
				float frac = pos - d0;
				Glyph((char)('0' + d0 % 10), -frac * pitch);
				Glyph((char)('0' + (d0 + 1) % 10), (1f - frac) * pitch);
			}
			else
			{
				Glyph(from, -e * pitch);
				Glyph(to, (1f - e) * pitch);
			}
			x += cw + gap;
		}
		c.Restore();
	}

	/// <summary>NORM (green) and HIGH (amber) lamps; the one coming on flashes three times.</summary>
	private void AfFaceLamps(SKCanvas c, double t, bool high, double since)
	{
		const float w = 64f, h = 22f, y = 248f;
		var amber = new SKColor(255, 181, 71);
		var green = new SKColor(96, 220, 140);
		foreach (var (label, on0, lit, lx) in new[] { ("NORM", !high, green, 200f - w - 5f), ("HIGH", high, amber, 205f) })
		{
			bool on = on0 && (since >= 0.9 || (int)(since / 0.15) % 2 == 0);
			var r = new SKRect(lx, y, lx + w, y + h);
			using (var face = new SKPaint { IsAntialias = true, Color = on ? lit : new SKColor(58, 61, 63) })
			{
				c.DrawRoundRect(r, 3f, 3f, face);
			}
			if (on)
			{
				SkiaKit.Glow(c, r.MidX, r.MidY, w * 0.7f, h * 1.3f, lit.WithAlpha(90), SKBlendMode.Plus);
			}
			using (var bezel = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(40, 42, 44) })
			{
				c.DrawRoundRect(r, 3f, 3f, bezel);
			}
			AfText(c, label, r.MidX, r.MidY - 5f, 10f, on ? new SKColor(30, 24, 14) : new SKColor(150, 154, 156), align: 0, track: 1.4f, font: AfLabelBold);
		}
	}

	/// <summary>The sticker: a barcode of the value swept by a laser, the time, and its REF / LOT line.</summary>
	private void AfFaceSticker(SKCanvas c, FaceContext x, SKColor ink, SKColor accent)
	{
		var s = new SKRect(108f, 282f, 292f, 344f);
		using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = ink.WithAlpha(190) })
		{
			c.DrawRect(new SKRect(s.Left + 5f, s.Top + 5f, s.Right - 5f, s.Bottom - 5f), edge);
			c.DrawLine(s.Left + 5f, s.Bottom - 20f, s.Right - 5f, s.Bottom - 20f, edge);
		}
		int hash = 17;
		foreach (char ch in x.Value)
		{
			hash = hash * 31 + ch;
		}
		float bx0 = s.Left + 12f, bx1 = s.Left + 108f, by = s.Top + 11f, bh = 22f;
		using (var bar = new SKPaint { Color = ink })
		{
			float bx = bx0;
			int i = 0;
			while (bx < bx1)
			{
				float bw = 1f + (float)Math.Floor(Hash01(hash + i * 13) * 3.2);
				float gap = 1f + (float)Math.Floor(Hash01(hash + i * 29 + 7) * 2.6);
				c.DrawRect(bx, by, Math.Min(bw, bx1 - bx), bh, bar);
				bx += bw + gap;
				i++;
			}
		}
		double ph = (x.T * 0.45) % 1.0;
		if (ph < 0.45)
		{
			float lx = bx0 + (bx1 - bx0) * (float)(ph / 0.45);
			using var laser = new SKPaint { IsAntialias = true, StrokeWidth = 1.6f, Color = accent.WithAlpha(230) };
			c.DrawLine(lx, by - 3f, lx, by + bh + 3f, laser);
			SkiaKit.Glow(c, lx, by + bh / 2f, 7f, 14f, accent.WithAlpha(110), SKBlendMode.Plus);
		}
		AfText(c, x.Time, s.Right - 12f, by + 1f, 22f, ink, align: 1, font: AfLabelBold);
		var dim = ink.WithAlpha(170);
		AfText(c, "REF", s.Left + 12f, s.Bottom - 15f, 9f, dim, track: 1f);
		AfText(c, "11-5110", s.Left + 34f, s.Bottom - 15f, 9f, ink, track: 1f);
		AfText(c, "LOT", s.Left + 110f, s.Bottom - 15f, 9f, dim, track: 1f);
		AfText(c, "96024", s.Left + 132f, s.Bottom - 15f, 9f, ink, track: 1f);
	}
}
