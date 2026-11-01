using System;
using System.Collections.Generic;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Interstellar on a fan: the screen layout's lensed black hole made round. A ring of light, swelling into a bright
/// crescent on the left, with lensed arcs outside it and warm haze inside (never a dark void); the disc edge-on as a
/// line through the ring trailing a smoky dust tail to the lower left that flows faster with the reading; a planet
/// that circles the hole and eclipses it every 40 seconds. The reading sits in white inside the ring and decodes
/// through random digits when it changes. Below, a label in the Micro style: an inverted tag with the reading's name
/// and a barcode made from the value, swept by a red laser.
/// Colours: Light (the ring) and Dust (the tail).
/// </summary>
public sealed partial class Faces
{
	private SKSurface? _isGlow;

	private readonly Dictionary<string, (string Value, double At)> _isFaceChanged = new Dictionary<string, (string, double)>();

	private static string IsDecode(string target, double since, double seconds, double t)
	{
		if (since >= seconds)
		{
			return target;
		}
		var chars = target.ToCharArray();
		int tick = (int)Math.Floor(t * 20.0);
		for (int i = 0; i < chars.Length; i++)
		{
			double settle = seconds * (0.35 + 0.65 * (i + 1) / chars.Length);
			if (since < settle && char.IsDigit(chars[i]))
			{
				chars[i] = (char)('0' + (int)(Hash01(tick * 31 + i * 7 + 3) * 10.0));
			}
		}
		return new string(chars);
	}

	private void InterstellarFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Interstellar");
		s.Travel += dt * (0.55 + 1.6 * f);
		s.Beat += dt * (0.5 + 0.6 * f);
		s.Orbit += dt * Math.PI * 2.0 / 40.0;
		float breath = 0.88f + 0.12f * (float)Math.Sin(s.Beat);

		if (!_isFaceChanged.TryGetValue(x.Fan.Serial, out var ch))
		{
			ch = (x.Value, -100.0);
		}
		else if (ch.Value != x.Value)
		{
			ch = (x.Value, x.T);
		}
		_isFaceChanged[x.Fan.Serial] = ch;
		string shown = IsDecode(x.Value, x.T - ch.At, 0.45, x.T);

		const float cx = 206f, cy = 172f, R = 92f, tilt = -8.5f;
		SKColor light = SkiaKit.Sk(x.C1, 1f), dust = SkiaKit.Sk(x.C2, 1f);
		SKColor L(float a) => light.WithAlpha((byte)Math.Clamp(a * breath, 0f, 255f));
		bool warn = f >= 0.9f;
		var red = new SKColor(240, 74, 62);

		// the planet: where it is, and whether it is in front
		double ps = Math.Sin(s.Orbit), pc = Math.Cos(s.Orbit);
		float px = cx + 175f * (float)pc, py = cy + 12f * (float)ps - 6f * (float)pc, prad = 30f + 8f * (float)ps;
		bool pFront = ps > 0.0;

		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(18, 11, 8)));
			_isGlow ??= SKSurface.Create(new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _isGlow.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);

			// warm haze round the hole
			if (!x.Over)
			{
				using var shader = SKShader.CreateRadialGradient(new SKPoint(cx - 30f, cy + 10f), 330f, new[] { new SKColor(92, 54, 36), new SKColor(40, 23, 16), new SKColor(14, 9, 7) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
				using var bg = new SKPaint { Shader = shader };
				c.DrawRect(0f, 0f, 400f, 400f, bg);
			}
			SkiaKit.Glow(c, cx - 20f, cy, 260f, 170f, new SKColor(190, 120, 80, (byte)(60 * breath)), SKBlendMode.Plus);
			using (var star = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
			{
				for (int i = 0; i < 40; i++)
				{
					float tw = (float)(0.5 + 0.5 * Math.Sin(x.T * (0.4 + Hash01(i) * 0.8) + i));
					star.Color = new SKColor(255, 226, 200, (byte)(16 + 46 * tw * (float)Hash01(i * 11)));
					c.DrawCircle((float)Hash01(i * 37 + 3) * 400f, (float)Hash01(i * 53 + 1) * 400f, 0.6f + 0.6f * (float)Hash01(i * 19), star);
				}
			}

			// the planet behind: fading as it slips into the lensed light
			if (!pFront)
			{
				float vis = Math.Clamp((Math.Abs(px - cx) - R * 0.5f) / (R * 0.7f), 0f, 1f) * 0.75f;
				IsFacePlanet(c, px, py, prad, vis, 0f, cx, cy);
			}

			// inside the ring: lit haze, just a touch darker
			using (var inner = SKShader.CreateRadialGradient(new SKPoint(cx + 8f, cy), R, new[] { new SKColor(40, 22, 15, 60), new SKColor(40, 22, 15, 0) }, null, SKShaderTileMode.Clamp))
			using (var ip = new SKPaint { Shader = inner })
			{
				c.DrawCircle(cx + 8f, cy, R, ip);
			}

			// the ring: full on the left, fading round to the right, with a wide bloom
			var colors = new[] { L(95f), L(150f), L(215f), L(228f), L(215f), L(150f), L(95f) };
			using (var sweep = SKShader.CreateSweepGradient(new SKPoint(cx, cy), colors, new[] { 0f, 0.3f, 0.45f, 0.5f, 0.55f, 0.7f, 1f }))
			using (var ring = new SKPaint { Shader = sweep, IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus, StrokeWidth = 3f })
			{
				c.DrawCircle(cx, cy, R, ring);
				ring.StrokeWidth = 24f;
				q.DrawCircle(cx, cy, R, ring);
			}
			using (var cres = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
			{
				var o1 = new SKRect(cx - R - 1f, cy - R, cx + R - 1f, cy + R);
				for (int k = 0; k < 4; k++)
				{
					float sw = 150f - k * 30f;
					cres.StrokeWidth = 2f + k * 1.6f;
					cres.Color = light.WithAlpha((byte)Math.Clamp(22f * breath, 0f, 255f));
					c.DrawArc(o1, 180f - sw / 2f, sw, false, cres);
				}
				cres.StrokeWidth = 20f;
				cres.Color = new SKColor(255, 170, 105, (byte)(110 * breath));
				q.DrawArc(o1, 120f, 120f, false, cres);
			}
			SkiaKit.Glow(q, cx - R * 0.75f, cy - 12f, 140f, 150f, new SKColor(255, 160, 100, (byte)(80 * breath)), SKBlendMode.Plus);
			// lensed arcs outside it, shimmering
			using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus, StrokeWidth = 1f })
			{
				for (int k = 0; k < 5; k++)
				{
					float rr = R + 7f + k * 5.5f + 1.8f * (float)Math.Sin(x.T * 0.35 + k * 1.3);
					float sh = 0.6f + 0.4f * (float)Math.Sin(x.T * (0.5 + 0.07 * k) + k * 2.1);
					arc.Color = light.WithAlpha((byte)Math.Clamp((140f - k * 22f) * sh * breath, 0f, 255f));
					var oval = new SKRect(cx - rr, cy - rr, cx + rr, cy + rr);
					float start = 108f + k * 4f + 6f * (float)Math.Sin(x.T * 0.2 + k);
					c.DrawArc(oval, start, 150f - k * 9f, false, arc);
				}
			}

			// the disc edge-on, and the tail flowing in from the lower left
			c.Save();
			c.Translate(cx, cy);
			c.RotateDegrees(tilt);
			q.Save();
			q.Translate(cx, cy);
			q.RotateDegrees(tilt);
			using (var line = SKShader.CreateLinearGradient(new SKPoint(-R - 30f, 0f), new SKPoint(R * 1.45f, 0f),
				new[] { light.WithAlpha(0), light.WithAlpha(175), light.WithAlpha(110), light.WithAlpha(0) }, new[] { 0f, 0.14f, 0.55f, 1f }, SKShaderTileMode.Clamp))
			using (var lp = new SKPaint { Shader = line, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, BlendMode = SKBlendMode.Plus })
			{
				c.DrawLine(-R - 30f, 0f, R * 1.45f, 0f, lp);
				lp.StrokeWidth = 12f;
				q.DrawLine(-R - 30f, 0f, R * 1.45f, 0f, lp);
			}
			SkiaKit.Glow(q, -R + 4f, 0f, 70f, 36f, new SKColor(255, 190, 140, 90), SKBlendMode.Plus);
			const float tail = 260f;
			for (int i = 0; i < 12; i++)
			{
				float u = -R - 20f - i * 22f;
				float hw = 6f + (-u - R) * 0.07f;
				float roll = (float)Math.Sin(s.Travel * 0.35 + i * 1.9) * hw * 0.35f;
				byte a = (byte)Math.Clamp(80 - i * 4, 10, 255);
				SkiaKit.Glow(c, u, roll, 40f, hw, dust.WithAlpha(a), SKBlendMode.Plus);
				SkiaKit.Glow(q, u, roll, 46f, hw * 1.4f, dust.WithAlpha((byte)(a * 0.9f)), SKBlendMode.Plus);
			}
			using (var streak = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
			{
				for (int i = 0; i < 90; i++)
				{
					float speed = 30f + 26f * (float)Hash01(i * 7 + 1);
					double pos = (Hash01(i * 13 + 5) * tail + s.Travel * speed) % tail;
					float frac = (float)(pos / tail);
					float u = -R - tail + (float)pos;
					float dist = -u - R;
					float hw = 3f + dist * 0.07f;
					float side = (float)(Hash01(i * 29 + 3) * 2.0 - 1.0);
					float v = side * Math.Abs(side) * hw + dist * 0.02f;
					float near = Math.Clamp(1f - dist / tail, 0f, 1f);
					float len = 12f + 26f * (float)Hash01(i * 3) * (0.4f + near);
					float a = (80f + 80f * near) * MathF.Sin(frac * MathF.PI);
					streak.StrokeWidth = 0.7f + 0.8f * (float)Hash01(i * 17);
					SKColor sc = near > 0.5f ? light : dust;
					streak.Color = sc.WithAlpha((byte)Math.Clamp(a, 0f, 255f));
					c.DrawLine(u - len, v, u, v, streak);
				}
			}
			for (int i = 0; i < 14; i++)
			{
				double pos = (Hash01(i * 41 + 9) * tail + s.Travel * (20f + 12f * (float)Hash01(i))) % tail;
				float frac = (float)(pos / tail);
				float u = -R - tail + (float)pos;
				float dist = -u - R;
				float v = (float)(Hash01(i * 23 + 4) * 2.0 - 1.0) * (3f + dist * 0.05f) + dist * 0.015f;
				float size = 4f + 9f * (float)Hash01(i * 5 + 2) * (0.4f + dist / tail);
				byte a = (byte)Math.Clamp(160f * MathF.Sin(frac * MathF.PI), 0f, 255f);
				SkiaKit.Glow(c, u, v, size * 1.6f, size * 0.55f, dust.WithAlpha(a), SKBlendMode.Plus);
			}
			c.Restore();
			q.Restore();

			q.Restore();
			using (SKImage sharp = _isGlow.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.8f, 1.8f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glow = _isGlow.Snapshot())
			using (SKPixmap pixels = _faceSurface!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glow, pixels);
			}

			// the planet in front, over the soft light, so it truly eclipses it
			if (pFront)
			{
				float eclipse = Math.Clamp(1f - Math.Abs(px - cx) / (R + prad), 0f, 1f);
				IsFacePlanet(c, px, py, prad, 1f, eclipse, cx, cy);
			}

			// the reading in white inside the ring
			GlowNumber(c, shown, "Bahnschrift Light SemiCondensed", FontStyle.Regular, new RectangleF(cx - 62f, cy - 30f, 124f, 60f), Color.FromArgb(255, 150, 90));

			// the label: a hairline card on smoked glass, an inverted tag and a barcode of the value with a red laser
			var card = new SKRect(112f, 280f, 288f, 338f);
			using (var glass = new SKPaint { Color = new SKColor(10, 6, 5, 150) })
			{
				c.DrawRect(card, glass);
			}
			using (var hair = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(232, 226, 216, 120) })
			{
				c.DrawRect(new SKRect(card.Left + 0.5f, card.Top + 0.5f, card.Right - 0.5f, card.Bottom - 0.5f), hair);
			}
			using (var tag = new SKPaint { Color = warn ? red : new SKColor(232, 226, 216) })
			{
				c.DrawRect(new SKRect(card.Left + 6f, card.Top + 6f, card.Right - 6f, card.Top + 24f), tag);
			}
			int hash = 17;
			foreach (char ch2 in x.Value)
			{
				hash = hash * 31 + ch2;
			}
			using (var bar = new SKPaint { Color = new SKColor(232, 226, 216, 215) })
			{
				float bx = card.Left + 10f;
				int i = 0;
				while (bx < card.Right - 10f)
				{
					float bw = 1f + (float)Math.Floor(Hash01(hash + i * 13) * 3.2);
					float gap = 1f + (float)Math.Floor(Hash01(hash + i * 29 + 7) * 2.6);
					c.DrawRect(bx, card.Top + 30f, Math.Min(bw, card.Right - 10f - bx), 20f, bar);
					bx += bw + gap;
					i++;
				}
			}
			double ph = (x.T * 0.45) % 1.0;
			if (ph < 0.45)
			{
				float lx = card.Left + 10f + (card.Width - 20f) * (float)(ph / 0.45);
				using var laser = new SKPaint { IsAntialias = true, StrokeWidth = 1.6f, Color = red.WithAlpha(230) };
				c.DrawLine(lx, card.Top + 27f, lx, card.Top + 53f, laser);
				SkiaKit.Glow(c, lx, card.Top + 40f, 8f, 16f, red.WithAlpha(110), SKBlendMode.Plus);
			}
		});
		Small(x.G, x.Label, Color.FromArgb(255, 18, 12, 10), 295f, 11.5f, bold: true, 1.6f);
		Small(x.G, x.Time, Color.FromArgb(210, 232, 214, 196), 350f, 14f, bold: false);
	}

	/// <summary>The planet: a warm banded body (never flat black), its limb lit on the side facing the hole, and a bead of light at full eclipse.</summary>
	private static void IsFacePlanet(SKCanvas c, float px, float py, float rad, float vis, float eclipse, float hx, float hy)
	{
		if (vis <= 0.01f)
		{
			return;
		}
		float lx = hx - px, ly = hy - py;
		float ll = MathF.Max(1f, MathF.Sqrt(lx * lx + ly * ly));
		lx /= ll;
		ly /= ll;
		using (var body = SKShader.CreateRadialGradient(new SKPoint(px + lx * rad * 0.45f, py + ly * rad * 0.45f), rad * 1.5f,
			new[] { new SKColor(112, 72, 50), new SKColor(52, 32, 24), new SKColor(22, 14, 11) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp))
		using (var bp = new SKPaint { Shader = body, IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(255 * vis)) })
		{
			c.DrawCircle(px, py, rad, bp);
		}
		float face = MathF.Atan2(ly, lx) * 180f / MathF.PI;
		using (var limb = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
		{
			var oval = new SKRect(px - rad, py - rad, px + rad, py + rad);
			for (int k = 0; k < 3; k++)
			{
				float sweep = 160f - k * 40f;
				limb.StrokeWidth = 1f + k * 0.8f;
				limb.Color = new SKColor(255, 200, 160, (byte)Math.Clamp((45f + 45f * eclipse) * vis, 0f, 255f));
				c.DrawArc(oval, face - sweep / 2f, sweep, false, limb);
			}
		}
		SkiaKit.Glow(c, px + lx * rad, py + ly * rad, rad * 0.7f, rad * 0.7f, new SKColor(255, 170, 110, (byte)Math.Clamp((40f + 90f * eclipse) * vis, 0f, 255f)), SKBlendMode.Plus);
		if (eclipse > 0.55f)
		{
			float e = (eclipse - 0.55f) / 0.45f;
			e = e * e * (3f - 2f * e);
			float bx = px + lx * (rad + 1f), by = py + ly * (rad + 1f);
			SkiaKit.Glow(c, bx, by, 16f, 16f, new SKColor(255, 214, 176, (byte)(150 * e)), SKBlendMode.Plus);
			using var glint = new SKPaint { IsAntialias = true, StrokeWidth = 1.1f, BlendMode = SKBlendMode.Plus };
			float len = 22f + 28f * e;
			using var hs = SKShader.CreateLinearGradient(new SKPoint(bx - len, by), new SKPoint(bx + len, by), new[] { new SKColor(255, 210, 170, 0), new SKColor(255, 222, 190, (byte)(200 * e)), new SKColor(255, 210, 170, 0) }, null, SKShaderTileMode.Clamp);
			glint.Shader = hs;
			c.DrawLine(bx - len, by, bx + len, by, glint);
		}
	}
}
