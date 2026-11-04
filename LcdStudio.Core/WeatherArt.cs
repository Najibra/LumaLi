using System;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Animated weather icons (sun with turning rays, moon and twinkling stars, drifting clouds, rain, snow, a storm with
/// lightning, fog bands) and a music note, drawn with Skia for the Now Playing layout and the Weather / Now Playing fan
/// faces. Every motion follows the clock smoothly; nothing jumps.
/// </summary>
internal static class WeatherArt
{
	private static float Hash(int i, int salt)
	{
		uint h = (uint)(i * 374761393 + salt * 668265263);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
	}

	/// <summary>
	/// Draws the icon for a weather kind (Clear, Partly, Cloudy, Fog, Rain, Snow, Storm) about size wide around (cx, cy).
	/// Returns how bright a lightning flash is right now (0–1), so the caller can light its sky with it.
	/// </summary>
	public static float Draw(SKCanvas c, string kind, bool day, float cx, float cy, float size, double t, SKColor sun)
	{
		float drift = size * 0.035f * MathF.Sin((float)(t * 0.32));
		switch (kind)
		{
		case "Clear":
			if (day)
			{
				Sun(c, cx, cy, size * 0.27f, t, sun);
			}
			else
			{
				Stars(c, cx, cy, size, t);
				Moon(c, cx, cy, size * 0.27f);
			}
			return 0f;
		case "Partly":
			if (day)
			{
				Sun(c, cx - size * 0.14f, cy - size * 0.12f, size * 0.22f, t, sun);
			}
			else
			{
				Stars(c, cx, cy, size, t);
				Moon(c, cx - size * 0.14f, cy - size * 0.13f, size * 0.2f);
			}
			Cloud(c, cx + size * 0.08f + drift, cy + size * 0.1f, size * 0.68f, new SKColor(250, 252, 255), new SKColor(196, 210, 232), 1f);
			return 0f;
		case "Cloudy":
			Cloud(c, cx - size * 0.16f - drift * 0.6f, cy - size * 0.1f, size * 0.5f, new SKColor(206, 216, 232), new SKColor(150, 164, 190), 0.85f);
			Cloud(c, cx + size * 0.06f + drift, cy + size * 0.06f, size * 0.74f, new SKColor(246, 249, 255), new SKColor(186, 200, 224), 1f);
			return 0f;
		case "Fog":
			Cloud(c, cx + drift, cy - size * 0.14f, size * 0.6f, new SKColor(226, 232, 242), new SKColor(170, 182, 204), 0.9f);
			Fog(c, cx, cy + size * 0.16f, size, t);
			return 0f;
		case "Rain":
			Rain(c, cx, cy + size * 0.06f, size, t, snow: false);
			Cloud(c, cx + drift, cy - size * 0.1f, size * 0.78f, new SKColor(222, 230, 244), new SKColor(140, 156, 186), 1f);
			return 0f;
		case "Snow":
			Rain(c, cx, cy + size * 0.06f, size, t, snow: true);
			Cloud(c, cx + drift, cy - size * 0.1f, size * 0.78f, new SKColor(242, 246, 255), new SKColor(190, 202, 226), 1f);
			return 0f;
		case "Storm":
		{
			Rain(c, cx, cy + size * 0.06f, size, t, snow: false);
			float flash = Lightning(c, cx, cy, size, t);
			byte lift = (byte)(60 * flash);
			Cloud(c, cx + drift, cy - size * 0.1f, size * 0.8f, new SKColor((byte)(120 + lift), (byte)(128 + lift), (byte)(152 + lift)), new SKColor(70, 76, 98), 1f);
			return flash;
		}
		default:
			Cloud(c, cx + drift, cy, size * 0.74f, new SKColor(246, 249, 255), new SKColor(186, 200, 224), 1f);
			return 0f;
		}
	}

	public static void Sun(SKCanvas c, float cx, float cy, float r, double t, SKColor sun)
	{
		// a warm halo, a disc lit from its centre (never burnt to white) and twelve rays turning slowly and breathing
		SkiaKit.Glow(c, cx, cy, r * 2.6f, r * 2.6f, sun.WithAlpha(70), SKBlendMode.Plus);
		SkiaKit.Glow(c, cx, cy, r * 1.6f, r * 1.6f, sun.WithAlpha(90), SKBlendMode.Plus);
		using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke, StrokeWidth = r * 0.13f, Color = sun.WithAlpha(220) };
		float turn = (float)(t * 0.14);
		for (int i = 0; i < 12; i++)
		{
			float a = turn + i * MathF.PI / 6f;
			float len = r * (0.32f + 0.12f * MathF.Sin((float)(t * 1.3) + i * 1.7f)) * (i % 2 == 0 ? 1.15f : 0.8f);
			float r0 = r * 1.28f;
			c.DrawLine(cx + MathF.Cos(a) * r0, cy + MathF.Sin(a) * r0, cx + MathF.Cos(a) * (r0 + len), cy + MathF.Sin(a) * (r0 + len), p);
		}
		p.Style = SKPaintStyle.Fill;
		SKColor core = new SKColor((byte)Math.Min(255, sun.Red + 30), (byte)Math.Min(240, sun.Green + 60), (byte)Math.Min(200, sun.Blue + 90));
		using var shader = SKShader.CreateRadialGradient(new SKPoint(cx - r * 0.25f, cy - r * 0.25f), r * 1.2f, new[] { core, sun, sun.WithAlpha(255) }, new[] { 0f, 0.7f, 1f }, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = shader;
		c.DrawCircle(cx, cy, r, p);
	}

	public static void Moon(SKCanvas c, float cx, float cy, float r)
	{
		SkiaKit.Glow(c, cx, cy, r * 2.4f, r * 2.4f, new SKColor(170, 190, 255, 60), SKBlendMode.Plus);
		using var disc = new SKPath();
		disc.AddCircle(cx, cy, r);
		using var bite = new SKPath();
		bite.AddCircle(cx + r * 0.5f, cy - r * 0.28f, r * 0.86f);
		using SKPath? crescent = disc.Op(bite, SKPathOp.Difference);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(246, 240, 222) };
		c.DrawPath(crescent ?? disc, p);
	}

	public static void Stars(SKCanvas c, float cx, float cy, float size, double t, float tall = 0.9f, int count = 9)
	{
		using var p = new SKPaint { IsAntialias = true };
		for (int i = 0; i < count; i++)
		{
			float x = cx + (Hash(i, 3) - 0.5f) * size * 1.3f, y = cy + (Hash(i, 7) - 0.5f) * size * tall;
			float tw = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin((float)(t * (0.8 + Hash(i, 11))) + i * 2.1f));
			p.Color = new SKColor(255, 250, 235, (byte)(220 * tw));
			c.DrawCircle(x, y, size * (0.008f + 0.008f * Hash(i, 5)), p);
		}
	}

	/// <summary>A soft cloud about w wide, lit from above (top colour) and shaded below.</summary>
	public static void Cloud(SKCanvas c, float cx, float cy, float w, SKColor top, SKColor bottom, float alpha)
	{
		using var path = new SKPath { FillType = SKPathFillType.Winding };
		path.AddRoundRect(new SKRect(cx - w * 0.46f, cy - w * 0.02f, cx + w * 0.46f, cy + w * 0.2f), w * 0.11f, w * 0.11f);
		path.AddCircle(cx - w * 0.22f, cy + w * 0.02f, w * 0.17f);
		path.AddCircle(cx + w * 0.02f, cy - w * 0.06f, w * 0.25f);
		path.AddCircle(cx + w * 0.26f, cy + w * 0.04f, w * 0.15f);
		using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 20, (byte)(60 * alpha)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, w * 0.04f) };
		c.Save();
		c.Translate(0f, w * 0.04f);
		c.DrawPath(path, shadow);
		c.Restore();
		using var shader = SKShader.CreateLinearGradient(new SKPoint(cx, cy - w * 0.32f), new SKPoint(cx, cy + w * 0.2f), new[] { top.WithAlpha((byte)(255 * alpha)), bottom.WithAlpha((byte)(255 * alpha)) }, null, SKShaderTileMode.Clamp);
		using var p = new SKPaint { IsAntialias = true, Color = SKColors.White, Shader = shader };
		c.DrawPath(path, p);
	}

	private static void Rain(SKCanvas c, float cx, float top, float size, double t, bool snow)
	{
		float h = size * 0.42f;
		using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, Style = snow ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = size * 0.018f };
		int n = snow ? 12 : 14;
		for (int i = 0; i < n; i++)
		{
			float x = cx + (Hash(i, 13) - 0.5f) * size * 0.66f;
			float speed = snow ? 0.18f + 0.08f * Hash(i, 17) : 1.1f + 0.4f * Hash(i, 17);
			float u = (float)((t * speed + Hash(i, 19)) % 1.0);
			float y = top + u * h;
			float fade = MathF.Min(1f, MathF.Min(u * 6f, (1f - u) * 4f));
			if (snow)
			{
				x += MathF.Sin((float)(t * 1.4) + i * 1.3f) * size * 0.03f;
				p.Color = new SKColor(250, 252, 255, (byte)(230 * fade));
				c.DrawCircle(x, y, size * (0.014f + 0.01f * Hash(i, 23)), p);
			}
			else
			{
				p.Color = new SKColor(150, 205, 255, (byte)(220 * fade));
				c.DrawLine(x, y, x - size * 0.03f, y + size * 0.09f, p);
			}
		}
	}

	private static void Fog(SKCanvas c, float cx, float cy, float size, double t)
	{
		using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.05f };
		for (int i = 0; i < 3; i++)
		{
			float slide = size * 0.08f * MathF.Sin((float)(t * (0.25 + 0.07 * i)) + i * 2f);
			float half = size * (0.36f - 0.06f * i);
			p.Color = new SKColor(226, 234, 246, (byte)(180 - 40 * i));
			float y = cy + i * size * 0.1f;
			c.DrawLine(cx - half + slide, y, cx + half + slide, y, p);
		}
	}

	/// <summary>A bolt now and then: two quick flickers every few seconds, in a period picked by a hash so it isn't regular.</summary>
	private static float Lightning(SKCanvas c, float cx, float cy, float size, double t)
	{
		int period = (int)Math.Floor(t / 3.2);
		if (Hash(period, 29) < 0.35f)
		{
			return 0f;
		}
		float u = (float)(t - period * 3.2);
		float flash = MathF.Max(MathF.Max(0f, 1f - MathF.Abs(u - 0.08f) / 0.08f), 0.8f * MathF.Max(0f, 1f - MathF.Abs(u - 0.3f) / 0.07f));
		if (flash <= 0.01f)
		{
			return 0f;
		}
		float x = cx + (Hash(period, 31) - 0.5f) * size * 0.3f, y = cy + size * 0.02f;
		using var bolt = new SKPath();
		bolt.MoveTo(x + size * 0.04f, y);
		bolt.LineTo(x - size * 0.06f, y + size * 0.18f);
		bolt.LineTo(x + size * 0.01f, y + size * 0.18f);
		bolt.LineTo(x - size * 0.05f, y + size * 0.36f);
		bolt.LineTo(x + size * 0.1f, y + size * 0.13f);
		bolt.LineTo(x + size * 0.03f, y + size * 0.13f);
		bolt.LineTo(x + size * 0.1f, y);
		bolt.Close();
		SkiaKit.Glow(c, x, y + size * 0.18f, size * 0.4f, size * 0.4f, new SKColor(255, 220, 140, (byte)(110 * flash)), SKBlendMode.Plus);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(255, 236, 170, (byte)(255 * flash)) };
		c.DrawPath(bolt, p);
		return flash;
	}

	/// <summary>Two beamed eighth notes about h tall, centred on (cx, cy): the placeholder when a song has no cover.</summary>
	public static void Note(SKCanvas c, float cx, float cy, float h, SKColor color)
	{
		using var p = new SKPaint { IsAntialias = true, Color = color };
		float w = h * 0.62f, stem = h * 0.07f;
		float lx = cx - w / 2f, rx = cx + w / 2f, top = cy - h / 2f, foot = cy + h / 2f;
		void Head(float x, float y)
		{
			c.Save();
			c.Translate(x, y);
			c.RotateDegrees(-22f);
			c.DrawOval(0f, 0f, h * 0.15f, h * 0.11f, p);
			c.Restore();
		}
		Head(lx, foot - h * 0.12f);
		Head(rx, foot - h * 0.2f);
		c.DrawRect(lx + h * 0.08f, top + h * 0.12f, stem, h * 0.72f, p);
		c.DrawRect(rx + h * 0.08f, top + h * 0.04f, stem, h * 0.72f, p);
		using var beam = new SKPath();
		beam.MoveTo(lx + h * 0.08f, top + h * 0.12f);
		beam.LineTo(rx + h * 0.08f + stem, top + h * 0.04f);
		beam.LineTo(rx + h * 0.08f + stem, top + h * 0.2f);
		beam.LineTo(lx + h * 0.08f, top + h * 0.28f);
		beam.Close();
		c.DrawPath(beam, p);
	}
}
