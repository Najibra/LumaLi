using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The Retro Rack layout's devices as fan faces, on the same matte orange plate.
/// - Rack Porthole: tonight's moon, turning slowly behind glass in a dark rim; a ring of ticks round it lights up with
///   the reading (a marker rides its end); a cream technical screen below with the name, the value and a dimension
///   line; the weekday above; a red lamp that blinks at 90 % and up.
/// - Rack Meter: a cream VU meter filling the top of the face, its red needle on a spring that overshoots and trembles
///   a little; the name and value on the face; a red PEAK lamp at 90 % and up; the time and a perforated grille below.
/// </summary>
public sealed partial class Faces
{
	private static SKImage? _rkMoon;

	private readonly Dictionary<string, (float X, float V)> _rkNeedle = new();

	/// <summary>The orange plate with its speckled wear and a bevelled rim (the face's round edge).</summary>
	private static void RkPlate(SKCanvas c, SKColor plate)
	{
		SKColor hi = FwMix(plate, SKColors.White, 0.14f), lo = FwMix(plate, SKColors.Black, 0.16f);
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(400f, 400f), new[] { hi, plate, lo }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(0f, 0f, 400f, 400f, p);
		}
		var rnd = new Random(3);
		using (var p = new SKPaint { IsAntialias = true })
		{
			for (int i = 0; i < 260; i++)
			{
				p.Color = rnd.Next(2) == 0 ? new SKColor(255, 230, 200, (byte)rnd.Next(10, 24)) : new SKColor(80, 26, 8, (byte)rnd.Next(10, 24));
				c.DrawCircle((float)rnd.NextDouble() * 400f, (float)rnd.NextDouble() * 400f, 0.6f + (float)rnd.NextDouble() * 1.3f, p);
			}
		}
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(400f, 400f), new[] { new SKColor(255, 200, 160, 150), new SKColor(255, 200, 160, 0), new SKColor(60, 20, 6, 0), new SKColor(60, 20, 6, 170) }, new[] { 0f, 0.35f, 0.65f, 1f }, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh, Style = SKPaintStyle.Stroke, StrokeWidth = 4f })
		{
			c.DrawCircle(200f, 200f, 196f, p);
		}
	}

	private static void RkBolt(SKCanvas c, float x, float y)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120) };
		c.DrawCircle(x + 1f, y + 2f, 6.5f, p);
		using var sh = SKShader.CreateLinearGradient(new SKPoint(x - 6f, y - 6f), new SKPoint(x + 6f, y + 6f), new[] { new SKColor(204, 200, 194), new SKColor(90, 88, 86) }, null, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = sh;
		c.DrawCircle(x, y, 6f, p);
		p.Shader = null;
		p.Color = new SKColor(50, 48, 46);
		c.DrawRect(x - 3.5f, y - 0.9f, 7f, 1.8f, p);
	}

	private static void RkLamp(SKCanvas c, float x, float y, float r, SKColor col, bool on)
	{
		if (on)
		{
			SkiaKit.Glow(c, x, y, r * 3.4f, r * 3.4f, col.WithAlpha(130), SKBlendMode.Plus);
		}
		using var p = new SKPaint { IsAntialias = true, Color = on ? col : new SKColor((byte)(col.Red / 4), (byte)(col.Green / 4), (byte)(col.Blue / 4)) };
		c.DrawCircle(x, y, r, p);
		p.Color = new SKColor(255, 255, 255, 120);
		c.DrawCircle(x - r * 0.3f, y - r * 0.35f, r * 0.3f, p);
	}

	/// <summary>A recess with a dark top-left edge and a lit lip.</summary>
	private static void RkInset(SKCanvas c, SKRect r, float rad)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(60, 22, 8, 200) };
		c.DrawRoundRect(r, rad, rad, p);
		using var sh = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), new[] { new SKColor(30, 10, 4, 200), new SKColor(255, 190, 150, 140) }, null, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = sh;
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 2f;
		c.DrawRoundRect(r, rad, rad, p);
	}

	// ---------- Rack Porthole ----------

	private void RackPortholeFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Rack Porthole");
		DateTime now = WatchNow(x);
		SKColor plate = SkiaKit.Sk(x.C1, 1f), cream = SkiaKit.Sk(x.C2, 1f);
		var ink = new SKColor(26, 26, 28);
		const float cx = 200f, cy = 172f, mr = 100f;

		FaceSkia(x.G, c =>
		{
			RkPlate(c, plate);
			AfText(c, now.ToString("dddd", CultureInfo.CurrentCulture).ToUpperInvariant(), 200f, 26f, 11f, cream.WithAlpha(220), align: 0, track: 3f, font: "Bahnschrift SemiCondensed");

			// the ring of ticks: lit up to the reading, a marker at its end
			using (var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Butt })
			{
				for (int i = 0; i < 90; i++)
				{
					float k = i / 90f;
					float a = (k * 360f - 90f) * MathF.PI / 180f;
					bool lit = k <= f;
					float r0 = i % 9 == 0 ? 112f : 116f;
					p.StrokeWidth = i % 9 == 0 ? 2.4f : 1.6f;
					p.Color = lit ? cream : cream.WithAlpha(70);
					c.DrawLine(cx + MathF.Cos(a) * r0, cy + MathF.Sin(a) * r0, cx + MathF.Cos(a) * 126f, cy + MathF.Sin(a) * 126f, p);
				}
				float am = (f * 360f - 90f) * MathF.PI / 180f;
				float ux = MathF.Cos(am), uy = MathF.Sin(am);
				using var tri = new SKPath();
				tri.MoveTo(cx + ux * 128f, cy + uy * 128f);
				tri.LineTo(cx + ux * 140f - uy * 6f, cy + uy * 140f + ux * 6f);
				tri.LineTo(cx + ux * 140f + uy * 6f, cy + uy * 140f - ux * 6f);
				tri.Close();
				p.Color = cream;
				c.DrawPath(tri, p);
			}

			// the rim and the moon behind glass, shadowed to tonight's phase
			using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) })
			{
				c.DrawCircle(cx + 2f, cy + 4f, mr + 9f, p);
			}
			using (var sh = SKShader.CreateSweepGradient(new SKPoint(cx, cy), new[] { new SKColor(64, 62, 60), new SKColor(20, 20, 22), new SKColor(74, 70, 68), new SKColor(18, 18, 20), new SKColor(64, 62, 60) }, null))
			using (var p = new SKPaint { IsAntialias = true, Shader = sh })
			{
				c.DrawCircle(cx, cy, mr + 8f, p);
			}
			_rkMoon ??= ScreenRenderer.RrMakeMoon();
			c.Save();
			using (var clip = new SKPath())
			{
				clip.AddCircle(cx, cy, mr);
				c.ClipPath(clip, SKClipOperation.Intersect, true);
			}
			using (var p = new SKPaint { Color = new SKColor(8, 8, 12) })
			{
				c.DrawRect(cx - mr, cy - mr, 2f * mr, 2f * mr, p);
			}
			c.Save();
			c.Translate(cx, cy);
			c.RotateDegrees((float)(x.T * 2.0 % 360.0));
			c.DrawImage(_rkMoon, new SKRect(-mr, -mr, mr, mr), SkiaKit.Linear);
			c.Restore();
			using (SKPath shade = ScreenRenderer.MoonShade(cx, cy, mr, now))
			using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(6, 6, 10, 215), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) })
			{
				c.DrawPath(shade, p);
			}
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(cx - mr, cy - mr), new SKPoint(cx, cy), new[] { new SKColor(255, 255, 255, 60), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = sh })
			{
				c.DrawRect(cx - mr, cy - mr, 2f * mr, 2f * mr, p);
			}
			c.Restore();
			using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round, Color = new SKColor(255, 255, 255, 110) })
			{
				using var arc = new SKPath();
				arc.AddArc(new SKRect(cx - mr + 12f, cy - mr + 12f, cx + mr - 12f, cy + mr - 12f), 200f, 50f);
				c.DrawPath(arc, p);
			}

			// the cream screen: name, value, a dimension line
			var scr = new SKRect(112f, 306f, 288f, 368f);
			RkInset(c, SKRect.Inflate(scr, 6f, 6f), 6f);
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, scr.Top), new SKPoint(0f, scr.Bottom), new[] { new SKColor(244, 241, 232), new SKColor(224, 220, 208) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = sh })
			{
				c.DrawRect(scr, p);
			}
			AfText(c, x.Label.ToUpperInvariant(), scr.Left + 8f, scr.Top + 7f, 10f, ink, track: 1f, font: "Consolas");
			AfText(c, x.Value, scr.Right - 8f, scr.Top + 5f, 24f, ink, align: 1, font: "Bahnschrift SemiBold");
			using (var p = new SKPaint { IsAntialias = true, StrokeWidth = 1.2f, Color = ink.WithAlpha(60) })
			{
				float ly = scr.Bottom - 10f, x0 = scr.Left + 8f, x1 = x0 + (scr.Width - 16f) * Math.Clamp(f, 0f, 1f);
				c.DrawLine(x0, ly, scr.Right - 8f, ly, p);
				p.Color = ink;
				c.DrawLine(x0, ly - 4f, x0, ly + 4f, p);
				if (x1 - x0 > 6f)
				{
					c.DrawLine(x0, ly, x1, ly, p);
					c.DrawLine(x1, ly - 4f, x1, ly + 4f, p);
					c.DrawLine(x1, ly, x1 - 5f, ly - 2.5f, p);
					c.DrawLine(x1, ly, x1 - 5f, ly + 2.5f, p);
				}
			}
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(scr.Left, scr.Top), new SKPoint(scr.MidX, scr.Bottom), new[] { new SKColor(255, 255, 255, 50), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = sh })
			{
				c.DrawRect(scr, p);
			}
			RkLamp(c, 152f, 384f - 4f, 4.5f, new SKColor(255, 40, 30), f >= 0.9f && x.T * 1.6 % 1.0 < 0.55);
			RkLamp(c, 248f, 384f - 4f, 4.5f, new SKColor(40, 140, 255), true);
			RkBolt(c, 40f, 172f);
			RkBolt(c, 360f, 172f);
		});
	}

	// ---------- Rack Meter ----------

	private void RackMeterFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Rack Meter");
		DateTime now = WatchNow(x);
		SKColor plate = SkiaKit.Sk(x.C1, 1f), red = SkiaKit.Sk(x.C2, 1f);
		var ink = new SKColor(26, 26, 28);
		string key = x.Fan.Serial ?? x.Label;
		(float nx, float nv) = _rkNeedle.TryGetValue(key, out var st) ? st : (f, 0f);
		// a sprung needle with a small tremble, like a real VU meter
		float target = Math.Clamp(f + 0.008f * MathF.Sin((float)x.T * 23f) + 0.005f * MathF.Sin((float)x.T * 37f + 1f), -0.02f, 1.04f);
		float h = (float)dt / 4f;
		for (int i = 0; i < 4; i++)
		{
			nv += ((target - nx) * 60f - nv * 9f) * h;
			nx += nv * h;
		}
		_rkNeedle[key] = (nx, nv);

		FaceSkia(x.G, c =>
		{
			RkPlate(c, plate);
			var face = new SKRect(58f, 62f, 342f, 262f);
			RkInset(c, SKRect.Inflate(face, 8f, 8f), 16f);
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, face.Top), new SKPoint(0f, face.Bottom), new[] { new SKColor(242, 234, 210), new SKColor(220, 208, 178) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { IsAntialias = true, Shader = sh })
			{
				c.DrawRoundRect(face, 12f, 12f, p);
			}
			const float px = 200f, py = 330f, rr = 200f, span = 34f;
			c.Save();
			c.ClipRoundRect(new SKRoundRect(face, 12f, 12f), SKClipOperation.Intersect, true);
			using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = ink })
			{
				using var arc = new SKPath();
				arc.AddArc(new SKRect(px - rr, py - rr, px + rr, py + rr), -90f - span, 2f * span);
				c.DrawPath(arc, p);
				p.Color = red;
				p.StrokeWidth = 6f;
				using var hot = new SKPath();
				hot.AddArc(new SKRect(px - rr + 5f, py - rr + 5f, px + rr - 5f, py + rr - 5f), -90f + span * 0.6f, span * 0.4f);
				c.DrawPath(hot, p);
				for (int i = 0; i <= 20; i++)
				{
					float a = (-90f - span + i * 2f * span / 20f) * MathF.PI / 180f;
					float len = i % 5 == 0 ? 14f : 7f;
					p.StrokeWidth = i % 5 == 0 ? 1.8f : 1f;
					p.Color = i >= 16 ? red : ink;
					c.DrawLine(px + MathF.Cos(a) * rr, py + MathF.Sin(a) * rr, px + MathF.Cos(a) * (rr + len), py + MathF.Sin(a) * (rr + len), p);
					if (i % 5 == 0)
					{
						AfText(c, (i * 5).ToString(CultureInfo.InvariantCulture), px + MathF.Cos(a) * (rr + 28f), py + MathF.Sin(a) * (rr + 28f) - 6f, 13f, i >= 16 ? red : ink, align: 0, font: "Bahnschrift SemiCondensed");
					}
				}
			}
			AfText(c, x.Label.ToUpperInvariant(), 200f, 184f, 13f, ink, align: 0, track: 4f, font: "Bahnschrift SemiCondensed");
			float na = (-90f - span + 2f * span * nx) * MathF.PI / 180f;
			using (var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = 2.4f, Color = new SKColor(0, 0, 0, 60) })
			{
				c.DrawLine(px + 4f, py + 5f, px + MathF.Cos(na) * 214f + 4f, py + MathF.Sin(na) * 214f + 5f, p);
				p.Color = red;
				c.DrawLine(px, py, px + MathF.Cos(na) * 214f, py + MathF.Sin(na) * 214f, p);
			}
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(face.Left, face.Top), new SKPoint(face.MidX, face.Bottom), new[] { new SKColor(255, 255, 255, 80), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { Shader = sh })
			{
				c.DrawRect(face, p);
			}
			c.Restore();
			// the needle's pivot cover: an orange bar across the meter's foot
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 246f), new SKPoint(0f, 262f), new[] { FwMix(plate, SKColors.White, 0.2f), FwMix(plate, SKColors.Black, 0.2f) }, null, SKShaderTileMode.Clamp))
			using (var p = new SKPaint { IsAntialias = true, Shader = sh })
			{
				c.DrawRoundRect(new SKRect(58f, 246f, 342f, 262f), 4f, 4f, p);
			}

			// below: the value in white, then the PEAK lamp and the time, a strip of perforations
			using (var sh2 = new SKPaint { IsAntialias = true, Color = new SKColor(90, 30, 8, 90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) })
			{
				c.Save();
				c.Translate(2f, 3f);
				AfText(c, x.Value, 200f, 278f, 48f, new SKColor(90, 30, 8, 110), align: 0, font: "Bahnschrift SemiBold");
				c.Restore();
			}
			AfText(c, x.Value, 200f, 278f, 48f, new SKColor(255, 248, 240), align: 0, font: "Bahnschrift SemiBold");
			RkLamp(c, 138f, 350f, 5.5f, new SKColor(255, 40, 30), f >= 0.9f);
			AfText(c, "PEAK", 150f, 345f, 11f, new SKColor(255, 238, 226), track: 2f, font: "Bahnschrift SemiCondensed");
			AfText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), 266f, 343f, 15f, new SKColor(255, 240, 230), align: 1, font: "Bahnschrift SemiBold");
			using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(40, 16, 6, 200) })
			{
				for (int row = 0; row < 2; row++)
				{
					for (float gx = 160f + (row % 2) * 4f; gx < 242f; gx += 8f)
					{
						c.DrawCircle(gx, 370f + row * 7f, 2f, p);
					}
				}
			}
			RkBolt(c, 40f, 200f);
			RkBolt(c, 360f, 200f);
		});
	}
}
