using System;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Light-strip effects drawn as a scene rather than worked out LED by LED: Skia lays soft light along a line twelve
/// times finer than the strip, and each LED takes the average of its stretch. Light between two LEDs lights both in
/// proportion, so it glides along the strip instead of stepping from one LED to the next. The strip is sent every
/// frame, so these run on the clock rather than a short loop as the fans' lights must.
/// </summary>
public static class StripScenes
{
	public static readonly string[] Names = { "Aurora", "Comet trail" };

	private const int Fine = 12;

	private static SKSurface? _line;

	public static bool Has(string? effect) => effect != null && Array.IndexOf(Names, effect) >= 0;

	/// <summary>How fast the scene moves for the effect's speed setting: a slow drift at 0, about seven times that at 100.</summary>
	private static double Pace(int speed) => 0.25 + Math.Clamp(speed, 0, 100) / 100.0 * 1.5;

	public static unsafe (byte R, byte G, byte B)[] Frame(string effect, EffectSettings s, int count, double time)
	{
		Color c1 = Imaging.Hex(s.Color1, Color.DeepSkyBlue), c2 = Imaging.Hex(s.Color2, Color.HotPink);
		int w = count * Fine;
		if (_line == null || _line.Canvas.DeviceClipBounds.Width != w)
		{
			_line?.Dispose();
			_line = SKSurface.Create(new SKImageInfo(w, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
		}
		SKCanvas c = _line.Canvas;
		c.Clear(SKColors.Black);
		double t = time * Pace(s.Speed);
		switch (effect)
		{
		case "Aurora":
			Aurora(c, w, t, c1, c2);
			break;
		case "Comet trail":
			Comets(c, w, t, c1, c2);
			break;
		}
		c.Flush();
		var frame = new (byte R, byte G, byte B)[count];
		double level = Math.Clamp(s.Brightness, 0, 100) / 100.0;
		using SKPixmap px = _line.PeekPixels();
		byte* p = (byte*)px.GetPixels();
		for (int i = 0; i < count; i++)
		{
			int r = 0, g = 0, b = 0;
			for (int k = 0; k < Fine; k++)
			{
				byte* q = p + (i * Fine + k) * 4;
				b += q[0];
				g += q[1];
				r += q[2];
			}
			frame[i] = ((byte)(r * level / Fine), (byte)(g * level / Fine), (byte)(b * level / Fine));
		}
		return frame;
	}

	/// <summary>A soft pool of light along the line, brightest at x and fading to nothing a half-width either side.</summary>
	private static void Pool(SKCanvas c, float x, float half, SKColor color)
	{
		const int n = 9;
		var colors = new SKColor[n];
		var stops = new float[n];
		for (int i = 0; i < n; i++)
		{
			float u = i / (n - 1f), d = (u - 0.5f) * 4f;
			stops[i] = u;
			colors[i] = color.WithAlpha((byte)(color.Alpha * MathF.Exp(-d * d)));
		}
		using var shader = SKShader.CreateLinearGradient(new SKPoint(x - half, 0f), new SKPoint(x + half, 0f), colors, stops, SKShaderTileMode.Clamp);
		using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Plus };
		c.DrawRect(x - half, 0f, half * 2f, 1f, paint);
	}

	private static SKColor Sk(Color c, double a) => new SKColor(c.R, c.G, c.B, (byte)Math.Clamp((int)(255 * a), 0, 255));

	/// <summary>
	/// Curtains of light drifting along the strip at their own unhurried paces, each slowly turning between the two
	/// colours and swelling and fading, over a faint glow of the second colour so the strip never goes quite dark.
	/// </summary>
	private static void Aurora(SKCanvas c, int w, double t, Color c1, Color c2)
	{
		Pool(c, w * 0.5f, w * 0.9f, Sk(c2, 0.10));
		for (int i = 0; i < 4; i++)
		{
			double drift = 0.5 + 0.42 * Math.Sin(t * (0.21 + 0.07 * i) + i * 1.9) + 0.08 * Math.Sin(t * 0.53 + i * 4.1);
			double swell = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(t * (0.37 + 0.05 * i) + i * 2.6));
			Color tint = Imaging.Mix(c1, c2, 0.5 + 0.5 * Math.Sin(t * 0.17 + i * 1.3));
			float half = w * (0.16f + 0.05f * (float)Math.Sin(t * 0.29 + i));
			Pool(c, (float)(drift * w), half, Sk(tint, 0.62 * swell));
		}
	}

	/// <summary>
	/// Comets crossing the strip one after another, each a white-hot head in a glow of the first colour trailing a long
	/// tail that cools into the second colour and fades to nothing; they travel at slightly different speeds.
	/// </summary>
	private static void Comets(SKCanvas c, int w, double t, Color c1, Color c2)
	{
		for (int i = 0; i < 3; i++)
		{
			double speed = 0.16 + 0.05 * i, tail = 0.28 + 0.08 * ((i + 1) % 3);
			// each runs from just off one end until its tail has cleared the other
			double run = 1.0 + tail + 0.1;
			double along = (t * speed + i / 3.0) % 1.0;
			float head = (float)((along * run - 0.05) * w), len = (float)(tail * w);
			Color warm = Imaging.Mix(c1, c2, 0.6);
			using (var shader = SKShader.CreateLinearGradient(new SKPoint(head, 0f), new SKPoint(head - len, 0f),
				new[] { Sk(c1, 0.95), Sk(warm, 0.45), Sk(c2, 0.15), Sk(c2, 0) }, new[] { 0f, 0.2f, 0.6f, 1f }, SKShaderTileMode.Clamp))
			using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Plus })
			{
				c.DrawRect(head - len, 0f, len, 1f, paint);
			}
			Pool(c, head, Fine * 2.2f, Sk(c1, 0.7));
			Pool(c, head, Fine * 0.9f, Sk(Imaging.Mix(c1, Color.White, 0.6), 0.9));
		}
	}
}
