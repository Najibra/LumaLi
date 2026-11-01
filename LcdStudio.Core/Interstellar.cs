using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Interstellar: a lensed black hole in warm haze, after the mood of the film.
/// - A ring of light (the light bent all the way round the hole), blazing on the left where the disc meets it and
///   fading to the right, with fainter lensed arcs outside it that shimmer. Its middle is lit haze, not a void.
/// - The disc seen edge-on: a thin white-hot line through the ring, trailing to the lower left into a long tail of
///   glowing dust, streaks and clumps that flow in toward the ring, faster the busier the processor.
/// - Warm haze drifting over everything. The time in thin white type at the top left, five readings on the right.
/// The soft light is drawn on a quarter-size layer, blurred and added; nothing is allowed to burn out to white.
/// </summary>
public sealed partial class ScreenRenderer
{
	private sealed record IsRow(string Label, string Key, string Unit, float Hi);

	private static readonly IsRow[] IsRows =
	{
		new("PROCESSOR", "cpu.load", "%", 100f),
		new("GRAPHICS", "gpu.load", "%", 100f),
		new("CPU TEMP", "cpu.temp", "°C", 100f),
		new("GPU TEMP", "gpu.temp", "°C", 95f),
		new("MEMORY", "ram.load", "%", 100f)
	};

	private const float IsCx = 1230f, IsCy = 236f, IsR = 158f, IsTilt = -8.5f;

	private SKSurface? _skInter;

	private double _isAt = double.NaN, _isFlow, _isPulse;

	private bool DrawInterstellar(Graphics g, Widget w, RectangleF r, double t)
	{
		if ((w.Effect != "interstellar" && w.Effect != "interstellar-micro") || w.Kind != WidgetKind.Panel)
		{
			return DrawAirframe(g, w, r, t);
		}
		_isMicro = w.Effect == "interstellar-micro";
		Interstellar(g, t);
		return true;
	}

	private void Interstellar(Graphics g, double t)
	{
		double dt = double.IsNaN(_isAt) ? 0.0 : Math.Clamp(t - _isAt, 0.0, 0.25);
		_isAt = t;
		float cpu = Math.Clamp(McEased("is.cpu", Val("cpu.load") / 100f, dt, 1.6), 0f, 1f);
		float gpu = Math.Clamp(McEased("is.gpu", Val("gpu.load") / 100f, dt, 1.6), 0f, 1f);
		// the disc's flow accumulates, so a change in load changes its pace without a jump
		_isFlow += dt * (0.55 + 1.6 * cpu);
		_isPulse += dt * (0.5 + 0.6 * gpu);
		float breath = 0.88f + 0.12f * (float)Math.Sin(_isPulse);
		_isOrbit += dt * Math.PI * 2.0 / 80.0;

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_skInter ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _skInter.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);

			IsSky(c, q, t, breath);
			IsPlanet(c, q, t, front: false);
			IsRing(c, q, t, breath, gpu);
			IsDisc(c, q, t, cpu);

			q.Restore();
			using (SKImage sharp = _skInter.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(2.2f, 2.2f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glow = _skInter.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glow, pixels);
			}
			// in front, the planet is drawn over the soft light too, so it truly eclipses it
			IsPlanet(c, null, t, front: true);
			if (_isMicro)
			{
				IsMicroText(c, dt, t);
			}
			else
			{
				IsText(c, q, dt, t);
			}
		});
	}

	/// <summary>The warm dark, the great haze round the hole, slow drifting veils and a few faint stars.</summary>
	private void IsSky(SKCanvas c, SKCanvas q, double t, float breath)
	{
		using (var shader = SKShader.CreateRadialGradient(new SKPoint(IsCx - 60f, IsCy + 20f), 1250f,
			new[] { new SKColor(92, 54, 36), new SKColor(52, 30, 21), new SKColor(24, 14, 10), new SKColor(12, 8, 7) }, new[] { 0f, 0.28f, 0.6f, 1f }, SKShaderTileMode.Clamp))
		using (var bg = new SKPaint { Shader = shader })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, bg);
		}
		// the haze lit by the disc, breathing slowly with the graphics load
		SkiaKit.Glow(c, IsCx - 40f, IsCy, 560f, 330f, new SKColor(190, 120, 80, (byte)(70 * breath)), SKBlendMode.Plus);
		SkiaKit.Glow(q, IsCx - 30f, IsCy, 320f, 230f, new SKColor(255, 190, 140, (byte)(70 * breath)), SKBlendMode.Plus);
		// drifting veils of dust light
		for (int i = 0; i < 7; i++)
		{
			double ph = t * (0.025 + 0.012 * Hash01(i * 13)) + Hash01(i * 7) * Math.PI * 2.0;
			float vx = (float)(200.0 + Hash01(i * 31 + 2) * 1300.0 + Math.Sin(ph) * 120.0);
			float vy = (float)(90.0 + Hash01(i * 17 + 9) * 300.0 + Math.Cos(ph * 0.8) * 40.0);
			byte a = (byte)(18 + 22 * (0.5 + 0.5 * Math.Sin(ph * 1.7)));
			SkiaKit.Glow(c, vx, vy, 260f, 90f, new SKColor(170, 105, 70, a), SKBlendMode.Plus);
		}
		using var star = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
		for (int i = 0; i < 90; i++)
		{
			float sx = (float)Hash01(i * 37 + 3) * 1920f, sy = (float)Hash01(i * 53 + 1) * 480f;
			float tw = (float)(0.5 + 0.5 * Math.Sin(t * (0.4 + Hash01(i) * 0.8) + i));
			star.Color = new SKColor(255, 226, 200, (byte)(14 + 40 * tw * (float)Hash01(i * 11)));
			c.DrawCircle(sx, sy, 0.6f + 0.7f * (float)Hash01(i * 19), star);
		}
	}

	/// <summary>
	/// The ring of light, brightest on the left where the disc meets it, with lensed arcs outside it and the lit haze
	/// inside it.
	/// </summary>
	private void IsRing(SKCanvas c, SKCanvas q, double t, float breath, float gpu)
	{
		// inside: lit haze, only a touch darker than outside
		using (var inner = SKShader.CreateRadialGradient(new SKPoint(IsCx + 12f, IsCy), IsR, new[] { new SKColor(40, 22, 15, 70), new SKColor(40, 22, 15, 30), new SKColor(40, 22, 15, 0) }, new[] { 0f, 0.8f, 1f }, SKShaderTileMode.Clamp))
		using (var ip = new SKPaint { Shader = inner })
		{
			c.DrawCircle(IsCx + 12f, IsCy, IsR, ip);
		}
		SkiaKit.Glow(c, IsCx - 30f, IsCy, IsR * 0.9f, IsR * 0.8f, new SKColor(200, 140, 100, 34), SKBlendMode.Plus);

		float lit = (0.8f + 0.2f * gpu) * breath;
		// the ring: a sweep of light, full on the left, fading round to the right
		SKColor Ring(float a) => new SKColor(255, 232, 210, (byte)Math.Clamp(a * lit, 0f, 255f));
		var colors = new[] { Ring(95f), Ring(150f), Ring(215f), Ring(228f), Ring(215f), Ring(150f), Ring(95f) };
		var stops = new[] { 0f, 0.3f, 0.45f, 0.5f, 0.55f, 0.7f, 1f };
		using (var sweep = SKShader.CreateSweepGradient(new SKPoint(IsCx, IsCy), colors, stops))
		using (var ring = new SKPaint { Shader = sweep, IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus })
		{
			ring.StrokeWidth = 4.2f;
			c.DrawCircle(IsCx, IsCy, IsR, ring);
			ring.StrokeWidth = 12f;
			ring.Shader = sweep;
			ring.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(255, 255, 255, 135), SKBlendMode.DstIn);
			c.DrawCircle(IsCx, IsCy, IsR + 1f, ring);
			ring.ColorFilter = null;
			ring.StrokeWidth = 34f;
			q.DrawCircle(IsCx, IsCy, IsR, ring);
		}

		// the left side swells: a thick crescent of light, a fainter second image just inside it, and a wide bloom
		using (var cres = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
		{
			var o1 = new SKRect(IsCx - IsR - 2f, IsCy - IsR, IsCx + IsR - 2f, IsCy + IsR);
			for (int k = 0; k < 4; k++)
			{
				// stacked arcs, each shorter and thicker toward the middle of the crescent, so it tapers at both ends
				float sweep = 150f - k * 30f;
				cres.StrokeWidth = 2.5f + k * 2.2f;
				cres.Color = new SKColor(255, 196, 150, (byte)Math.Clamp(20f * lit, 0f, 255f));
				c.DrawArc(o1, 180f - sweep / 2f, sweep, false, cres);
			}
			var o2 = new SKRect(IsCx - IsR + 9f, IsCy - IsR + 9f, IsCx + IsR - 9f, IsCy + IsR - 9f);
			cres.StrokeWidth = 1.2f;
			cres.Color = new SKColor(255, 214, 186, (byte)Math.Clamp(80f * lit, 0f, 255f));
			c.DrawArc(o2, 128f + 4f * (float)Math.Sin(t * 0.3), 104f, false, cres);
			cres.StrokeWidth = 26f;
			cres.Color = new SKColor(255, 170, 105, (byte)Math.Clamp(115f * lit, 0f, 255f));
			q.DrawArc(o1, 120f, 120f, false, cres);
		}
		SkiaKit.Glow(q, IsCx - IsR * 0.75f, IsCy - 20f, 230f, 250f, new SKColor(255, 160, 100, (byte)(80 * lit)), SKBlendMode.Plus);

		// lensed arcs outside the ring: thin, warm, each shimmering and breathing on its own
		using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus };
		for (int k = 0; k < 7; k++)
		{
			float rr = IsR + 9f + k * 7.5f + 2.5f * (float)Math.Sin(t * 0.35 + k * 1.3);
			float shimmer = 0.6f + 0.4f * (float)Math.Sin(t * (0.5 + 0.07 * k) + k * 2.1);
			float a = (150f - k * 16f) * shimmer * lit;
			arc.StrokeWidth = 1.1f + 0.5f * (float)Hash01(k * 5);
			arc.Color = new SKColor(255, (byte)(206 - k * 6), (byte)(170 - k * 8), (byte)Math.Clamp(a, 0f, 255f));
			var oval = new SKRect(IsCx - rr, IsCy - rr, IsCx + rr, IsCy + rr);
			float start = 108f + k * 4f + 6f * (float)Math.Sin(t * 0.2 + k);
			c.DrawArc(oval, start, 150f - k * 7f, false, arc);
			float w0 = arc.StrokeWidth;
			arc.StrokeWidth = 12f;
			q.DrawArc(oval, start, 150f - k * 7f, false, arc);
			arc.StrokeWidth = w0;
			// the lower-right arcs, dimmer
			arc.Color = arc.Color.WithAlpha((byte)Math.Clamp(a * 0.45f, 0f, 255f));
			c.DrawArc(oval, 20f + k * 3f, 70f - k * 3f, false, arc);
		}
	}

	/// <summary>
	/// The disc edge-on: a white-hot line through the ring, and the tail to the lower left. Streaks flow in toward the
	/// ring, faster close to it; clumps of dust drift with them.
	/// </summary>
	private void IsDisc(SKCanvas c, SKCanvas q, double t, float cpu)
	{
		c.Save();
		c.Translate(IsCx, IsCy);
		c.RotateDegrees(IsTilt);
		q.Save();
		q.Translate(IsCx, IsCy);
		q.RotateDegrees(IsTilt);

		// the line through the ring: hottest where it meets the ring on the left, thinning out to the right
		using (var line = SKShader.CreateLinearGradient(new SKPoint(-IsR - 40f, 0f), new SKPoint(IsR * 1.45f, 0f),
			new[] { new SKColor(255, 225, 200, 0), new SKColor(255, 228, 204, 175), new SKColor(255, 216, 186, 110), new SKColor(255, 210, 180, 0) }, new[] { 0f, 0.14f, 0.55f, 1f }, SKShaderTileMode.Clamp))
		using (var lp = new SKPaint { Shader = line, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
		{
			c.DrawLine(-IsR - 40f, 0f, IsR * 1.45f, 0f, lp);
			lp.StrokeWidth = 16f;
			q.DrawLine(-IsR - 40f, 0f, IsR * 1.45f, 0f, lp);
		}
		// the flare where the disc meets the ring
		SkiaKit.Glow(c, -IsR + 6f, 0f, 40f, 18f, new SKColor(255, 210, 176, 70), SKBlendMode.Plus);
		SkiaKit.Glow(q, -IsR + 6f, 0f, 120f, 60f, new SKColor(255, 190, 140, 80), SKBlendMode.Plus);

		// the tail's body: a long soft band, widening and cooling to orange as it leaves
		const float tail = 1500f;
		for (int i = 0; i < 20; i++)
		{
			// smoke: soft lobes along the tail, each rolling slowly up and down
			float u = -IsR - 40f - i * 72f;
			float hw = 10f + (-u - IsR) * 0.045f;
			float roll = (float)Math.Sin(_isFlow * 0.35 + i * 1.9) * hw * 0.35f;
			byte a = (byte)Math.Clamp(58 - i * 2, 10, 255);
			SkiaKit.Glow(c, u, hw * 0.2f + roll, 110f, hw * (0.8f + 0.3f * (float)Hash01(i * 3)), new SKColor(215, 108, 50, (byte)Math.Min(255, a * 1.5f)), SKBlendMode.Plus);
			SkiaKit.Glow(q, u, roll, 120f, hw * 1.4f, new SKColor(225, 112, 50, (byte)Math.Min(255, a * 1.4f)), SKBlendMode.Plus);
		}

		// billows: big soft clouds of dust drifting in along the tail, swelling and fading as they go, so it reads as
		// smoke with depth rather than a band
		for (int i = 0; i < 30; i++)
		{
			float span = tail;
			double pos = (Hash01(i * 61 + 4) * span + _isFlow * (16f + 10f * (float)Hash01(i * 3 + 1))) % span;
			float frac = (float)(pos / span);
			float u = -IsR - tail + (float)pos;
			float dist = -u - IsR;
			float hw = 6f + Math.Max(0f, dist) * 0.05f;
			float v = (float)(Hash01(i * 17 + 8) * 2.0 - 1.0) * hw * 0.9f + dist * 0.015f + (float)Math.Sin(t * 0.25 + i) * hw * 0.25f;
			float size = 30f + 70f * (float)Hash01(i * 9 + 2) * (0.3f + dist / tail);
			byte a = (byte)Math.Clamp(72f * MathF.Sin(frac * MathF.PI) * (0.6f + 0.4f * (float)Hash01(i * 5)), 0f, 255f);
			SkiaKit.Glow(c, u, v, size * 1.5f, size * 0.6f, new SKColor(205, 98, 44, a), SKBlendMode.Plus);
			SkiaKit.Glow(q, u, v, size * 1.8f, size * 0.8f, new SKColor(190, 100, 56, (byte)(a * 0.7f)), SKBlendMode.Plus);
		}

		// streaks: one population along the whole tail, one quick one close to the ring
		using var streak = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus };
		using var soft = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus, StrokeWidth = 7f };
		for (int i = 0; i < 230; i++)
		{
			bool inner = i < 80;
			float span = inner ? 420f : tail;
			float speed = inner ? 150f : 42f + 30f * (float)Hash01(i * 7 + 1);
			double pos = (Hash01(i * 13 + 5) * span + _isFlow * speed) % span;
			float frac = (float)(pos / span);
			// flows toward the ring: from the far end in
			float u = inner ? -IsR - 420f + (float)pos : -IsR - tail + (float)pos;
			float dist = -u - IsR;
			float hw = 3f + Math.Max(0f, dist) * 0.05f;
			float side = (float)(Hash01(i * 29 + 3) * 2.0 - 1.0);
			float v = side * Math.Abs(side) * hw + Math.Max(0f, dist) * 0.012f;
			float near = Math.Clamp(1f - dist / 900f, 0f, 1f);
			float fade = MathF.Sin(frac * MathF.PI);
			float len = (inner ? 40f : 36f) + 80f * (float)Hash01(i * 3) * (0.4f + near);
			float a = (inner ? 120f : 75f + 75f * near) * fade;
			streak.StrokeWidth = 0.8f + 1.0f * (float)Hash01(i * 17);
			streak.Color = new SKColor(255, (byte)(140 + 80 * near), (byte)(70 + 110 * near), (byte)Math.Clamp(a, 0f, 255f));
			c.DrawLine(u - len, v, u, v, streak);
			if (i % 3 == 0)
			{
				soft.Color = streak.Color.WithAlpha((byte)Math.Clamp(a * 0.7f, 0f, 255f));
				q.DrawLine(u - len, v, u, v, soft);
			}
		}

		// clumps of dust: knots of orange light tumbling along the tail
		for (int i = 0; i < 46; i++)
		{
			float span = tail - 120f;
			double pos = (Hash01(i * 41 + 9) * span + _isFlow * (30f + 18f * (float)Hash01(i))) % span;
			float frac = (float)(pos / span);
			float u = -IsR - tail + (float)pos;
			float dist = -u - IsR;
			float hw = 3f + Math.Max(0f, dist) * 0.04f;
			float v = (float)(Hash01(i * 23 + 4) * 2.0 - 1.0) * hw * 0.7f + dist * 0.01f;
			float size = 8f + 22f * (float)Hash01(i * 5 + 2) * (0.4f + dist / tail);
			float wob = (float)Math.Sin(t * 0.9 + i) * 0.15f + 1f;
			byte a = (byte)Math.Clamp(165f * MathF.Sin(frac * MathF.PI), 0f, 255f);
			SkiaKit.Glow(c, u, v, size * 1.6f * wob, size * 0.55f, new SKColor(240, 120, 52, a), SKBlendMode.Plus);
			SkiaKit.Glow(c, u + size * 0.2f, v, size * 0.5f, size * 0.22f, new SKColor(255, 196, 140, (byte)(a * 0.8f)), SKBlendMode.Plus);
		}

		c.Restore();
		q.Restore();
	}

	// ---------- the eclipse ----------

	/// <summary>The planet's place on its orbit round the hole (radians); a lap takes 80 seconds.</summary>
	private double _isOrbit = 0.9;

	/// <summary>Where the planet is now, its radius, and whether it is on the near side of the hole.</summary>
	private (float X, float Y, float Rad, bool Front) IsPlanetAt()
	{
		double s = Math.Sin(_isOrbit), co = Math.Cos(_isOrbit);
		float x = IsCx + 400f * (float)co;
		float y = IsCy + 24f * (float)s - 14f * (float)co;
		float depth = (float)s;
		return (x, y, 62f + 16f * depth, depth > 0f);
	}

	/// <summary>
	/// The planet: a dark warm body (never a flat black disc), faint bands, an atmosphere, and its limb lit by the
	/// hole's light on the side facing it. In front of the ring it eclipses the light, and at the deepest point a
	/// bright bead of light flares on the limb.
	/// </summary>
	private void IsPlanet(SKCanvas c, SKCanvas? q, double t, bool front)
	{
		var (px, py, rad, isFront) = IsPlanetAt();
		if (isFront != front)
		{
			return;
		}
		float away = Math.Abs(px - IsCx);
		// behind the hole it slips into the lensed light and fades; in front it eclipses it
		float vis = front ? 1f : Math.Clamp((away - IsR * 0.5f) / (IsR * 0.7f), 0f, 1f) * 0.75f;
		if (vis <= 0.01f)
		{
			return;
		}
		float eclipse = front ? Math.Clamp(1f - away / (IsR + rad), 0f, 1f) : 0f;
		float lx = IsCx - px, ly = IsCy - py;
		float ll = MathF.Max(1f, MathF.Sqrt(lx * lx + ly * ly));
		lx /= ll;
		ly /= ll;

		// the body, lit faintly from the hole's side
		using (var body = SKShader.CreateRadialGradient(new SKPoint(px + lx * rad * 0.45f, py + ly * rad * 0.45f), rad * 1.5f,
			new[] { new SKColor(112, 72, 50), new SKColor(52, 32, 24), new SKColor(22, 14, 11) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp))
		using (var bp = new SKPaint { Shader = body, IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(255 * vis)) })
		{
			c.DrawCircle(px, py, rad, bp);
		}
		// faint bands of cloud, drifting
		c.Save();
		using (var clip = new SKPath())
		{
			clip.AddCircle(px, py, rad - 1f);
			c.ClipPath(clip, antialias: true);
		}
		using (var band = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus })
		{
			for (int i = 0; i < 6; i++)
			{
				float by = py - rad + (i + 0.5f) * rad * 2f / 6f + 3f * (float)Math.Sin(t * 0.2 + i);
				band.StrokeWidth = 4f + 6f * (float)Hash01(i * 7 + 3);
				band.Color = new SKColor(170, 110, 78, (byte)(24 * vis));
				c.DrawLine(px - rad, by, px + rad, by + 6f, band);
			}
		}
		c.Restore();

		// the lit limb: a crescent on the side facing the hole, brighter as the eclipse deepens
		float face = MathF.Atan2(ly, lx) * 180f / MathF.PI;
		using (var limb = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus })
		{
			var oval = new SKRect(px - rad, py - rad, px + rad, py + rad);
			for (int k = 0; k < 4; k++)
			{
				float sweep = 170f - k * 38f;
				limb.StrokeWidth = 1.2f + k * 0.9f;
				limb.Color = new SKColor(255, 200, 160, (byte)Math.Clamp((40f + 40f * eclipse) * vis, 0f, 255f));
				c.DrawArc(oval, face - sweep / 2f, sweep, false, limb);
			}
			// the thin atmosphere all round
			limb.StrokeWidth = 1f;
			limb.Color = new SKColor(255, 190, 150, (byte)(46 * vis));
			c.DrawCircle(px, py, rad + 1.5f, limb);
		}
		SkiaKit.Glow(c, px + lx * rad, py + ly * rad, rad * 0.7f, rad * 0.7f, new SKColor(255, 170, 110, (byte)Math.Clamp((40f + 90f * eclipse) * vis, 0f, 255f)), SKBlendMode.Plus);
		if (q != null)
		{
			SkiaKit.Glow(q, px + lx * rad, py + ly * rad, rad, rad, new SKColor(255, 170, 110, (byte)(90 * vis)), SKBlendMode.Plus);
		}

		// the bead of light at the deepest eclipse, with a soft four-point glint
		if (eclipse > 0.55f)
		{
			float e = (eclipse - 0.55f) / 0.45f;
			e = e * e * (3f - 2f * e);
			float bx = px + lx * (rad + 1f), byy = py + ly * (rad + 1f);
			SkiaKit.Glow(c, bx, byy, 30f, 30f, new SKColor(255, 214, 176, (byte)(150 * e)), SKBlendMode.Plus);
			using var glint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus, StrokeWidth = 1.3f };
			float len = 40f + 50f * e;
			using (var hs = SKShader.CreateLinearGradient(new SKPoint(bx - len, byy), new SKPoint(bx + len, byy), new[] { new SKColor(255, 210, 170, 0), new SKColor(255, 222, 190, (byte)(200 * e)), new SKColor(255, 210, 170, 0) }, null, SKShaderTileMode.Clamp))
			{
				glint.Shader = hs;
				c.DrawLine(bx - len, byy, bx + len, byy, glint);
			}
			using (var vs = SKShader.CreateLinearGradient(new SKPoint(bx, byy - len * 0.5f), new SKPoint(bx, byy + len * 0.5f), new[] { new SKColor(255, 210, 170, 0), new SKColor(255, 222, 190, (byte)(150 * e)), new SKColor(255, 210, 170, 0) }, null, SKShaderTileMode.Clamp))
			{
				glint.Shader = vs;
				c.DrawLine(bx, byy - len * 0.5f, bx, byy + len * 0.5f, glint);
			}
		}
	}

	// ---------- the type ----------

	private const string IsThin = "Montserrat ExtraLight", IsLight = "Montserrat Light";

	private string _isShownTime = "";

	private double _isTimeAt = -100.0, _isCapAt = double.NaN;

	private readonly System.Collections.Generic.Dictionary<string, (int Value, double At)> _isChanged = new System.Collections.Generic.Dictionary<string, (int, double)>();

	/// <summary>
	/// Draws a line of letters one by one, spaced by track, each letter centred in a cell (digits share one width so
	/// numbers never jitter). each(i, n) gives a letter its offset, opacity and horizontal smear (0 = sharp). Returns
	/// the width.
	/// </summary>
	private float IsWord(SKCanvas c, string text, float x, float y, float size, float track, string font, SKColor color, int align, Func<int, int, (float Dx, float Alpha, float Smear)>? each = null, SKColor? glow = null)
	{
		float digit = McPath("0", font, size, 0f).TightBounds.Width * 1.08f;
		var cells = new float[text.Length];
		float total = 0f;
		for (int i = 0; i < text.Length; i++)
		{
			char ch = text[i];
			cells[i] = ch == ' ' ? size * 0.32f : char.IsDigit(ch) ? digit : Math.Max(size * 0.18f, McPath(ch.ToString(), font, size, 0f).TightBounds.Width);
			total += cells[i] + (i < text.Length - 1 ? track : 0f);
		}
		float cx = align < 0 ? x : align == 0 ? x - total / 2f : x - total;
		using var paint = new SKPaint { IsAntialias = true };
		using var halo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.StrokeAndFill, StrokeJoin = SKStrokeJoin.Round };
		for (int i = 0; i < text.Length; i++)
		{
			char ch = text[i];
			if (ch != ' ')
			{
				SKPath p = McPath(ch.ToString(), font, size, 0f);
				SKRect b = p.TightBounds;
				var (dx, alpha, smear) = each?.Invoke(i, text.Length) ?? (0f, 1f, 0f);
				if (alpha > 0.004f)
				{
					// letters sit on one baseline: the path's own top is the font's, so only the x is centred
					float ox = cx + (cells[i] - b.Width) / 2f - b.Left + dx;
					float oy = y;
					c.Save();
					c.Translate(ox, oy);
					if (glow is SKColor gc)
					{
						halo.Color = gc.WithAlpha((byte)Math.Clamp(gc.Alpha * alpha, 0f, 255f));
						halo.StrokeWidth = size * 0.03f;
						halo.MaskFilter ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1.5f, size * 0.05f));
						c.DrawPath(p, halo);
					}
					// the smear: fading copies trailing to each side, like a letter caught moving fast
					if (smear > 0.01f)
					{
						for (int k = 1; k <= 6; k++)
						{
							float off = smear * k / 6f;
							paint.Color = color.WithAlpha((byte)Math.Clamp(color.Alpha * alpha * 0.22f * (1f - k / 7f), 0f, 255f));
							c.Translate(off, 0f);
							c.DrawPath(p, paint);
							c.Translate(-2f * off, 0f);
							c.DrawPath(p, paint);
							c.Translate(off, 0f);
						}
					}
					paint.Color = color.WithAlpha((byte)Math.Clamp(color.Alpha * alpha, 0f, 255f));
					c.DrawPath(p, paint);
					c.Restore();
				}
			}
			cx += cells[i] + track;
		}
		return total;
	}

	/// <summary>
	/// The type, in thin wide-spaced capitals after the film's titles:
	/// - The time, its letters set far apart. When the minute turns, the new time flies in from wide apart with a
	///   streak of light and settles; a slow band of warm light passes along it every few seconds.
	/// - A caption that writes itself out from the middle, holds, and dissolves outward, turning through the date,
	///   the time dilation the processor load would cause, the mission time and the event horizon.
	/// - The readings on the right; a number that changes smears sideways for a moment and settles.
	/// </summary>
	private void IsText(SKCanvas c, SKCanvas q, double dt, double t)
	{
		DateTime now = DateTime.Now;
		string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
		var warm = new SKColor(240, 214, 194, 225);
		if (time != _isShownTime)
		{
			_isShownTime = time;
			_isTimeAt = t;
		}

		// ---- the time ----
		const float tx = 58f, ty = 60f, ts = 104f, track = 30f;
		float rp = (float)Math.Clamp((t - _isTimeAt) / 1.8, 0.0, 1.0);
		float ease = 1f - MathF.Pow(1f - rp, 3f);
		float sweepAt = (float)(t / 7.0 % 1.0) * 1.6f - 0.3f;
		float timeW = 0f;
		// the flash of light behind it as it lands
		if (rp < 1f)
		{
			SkiaKit.Glow(c, tx + 260f, ty + 50f, 360f * (1.3f - 0.5f * ease), 70f, new SKColor(255, 170, 110, (byte)(110 * (1f - ease))), SKBlendMode.Plus);
		}
		timeW = IsWord(c, time, tx, ty, ts, track, IsThin, SKColors.White, -1, (i, n) =>
		{
			float mid = (n - 1) / 2f;
			float spread = (i - mid) * 150f * (1f - ease);
			float a = Math.Clamp(rp * 1.6f - Math.Abs(i - mid) * 0.12f, 0f, 1f);
			float smear = 70f * (1f - ease);
			return (spread, a, smear);
		}, new SKColor(255, 176, 120, 60));
		// the passing band of light: a warm glow sliding along behind the letters
		if (sweepAt > -0.2f && sweepAt < 1.2f)
		{
			float bx = tx + timeW * sweepAt;
			float band = 1f - Math.Abs(sweepAt - 0.5f) * 1.4f;
			SkiaKit.Glow(c, bx, ty + 50f, 70f, 70f, new SKColor(255, 180, 120, (byte)Math.Clamp(70f * band, 0f, 255f)), SKBlendMode.Plus);
		}
		// the seconds: each new one drops in, fading up
		float sf = (float)(now.Millisecond / 1000.0);
		float drop = 1f - Math.Clamp(sf / 0.35f, 0f, 1f);
		IsWord(c, now.ToString("ss", CultureInfo.InvariantCulture), tx + timeW + 22f, ty + 52f - 10f * drop * drop, 34f, 6f, IsThin, warm.WithAlpha((byte)(225 * (1f - drop * 0.8f) * ease)), -1);

		// ---- the caption ----
		if (double.IsNaN(_isCapAt))
		{
			_isCapAt = t;
		}
		const double capLen = 7.0;
		double ct = t - _isCapAt;
		int which = (int)Math.Floor(ct / capLen);
		double cp = ct - which * capLen;
		float cpu = Math.Clamp(Val("cpu.load") / 100f, 0f, 1f);
		double beta = 0.2 + 0.75 * cpu;
		double gamma = 1.0 / Math.Sqrt(1.0 - beta * beta);
		TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
		string[] captions =
		{
			now.ToString("dddd   d MMMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
			$"TIME DILATION   ×{gamma.ToString("0.00", CultureInfo.InvariantCulture)}",
			$"MISSION TIME   {up.Days}D {up.Hours:00}:{up.Minutes:00}",
			$"EVENT HORIZON   {(Val("gpu.load")).ToString("0", CultureInfo.InvariantCulture)}% GRAVITY"
		};
		string cap = captions[((which % captions.Length) + captions.Length) % captions.Length];
		IsWord(c, cap, tx + 2f, ty + 150f, 15f, 9f, IsLight, warm, -1, (i, n) =>
		{
			float mid = (n - 1) / 2f;
			float d = Math.Abs(i - mid) / Math.Max(1f, mid);
			// writes out from the middle in 1.1 s, dissolves outward over the last 0.9 s
			float inP = Math.Clamp((float)(cp / 1.1) * 1.5f - d * 0.5f, 0f, 1f);
			float outP = Math.Clamp((float)((cp - (capLen - 0.9)) / 0.9) * 1.5f - (1f - d) * 0.5f, 0f, 1f);
			float a = inP * (1f - outP);
			float side = i < mid ? -1f : 1f;
			float dx = side * (28f * (1f - inP) * (1f - inP) + 40f * outP * outP);
			float smear = 26f * ((1f - inP) + outP);
			return (dx, a, smear);
		});
		// a hairline under the caption that draws out and back with it
		float lineP = (float)Math.Clamp(Math.Min(cp / 1.1, (capLen - cp) / 0.9), 0.0, 1.0);
		using (var rule = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(255, 196, 150, (byte)(110 * lineP)) })
		{
			float half = 170f * lineP;
			c.DrawLine(tx + 172f - half, ty + 182f, tx + 172f + half, ty + 182f, rule);
		}

		// ---- the readings ----
		const float x0 = 1560f, x1 = 1880f, top = 44f, rowH = 82f;
		using var hair = new SKPaint { IsAntialias = true, StrokeWidth = 1.6f, StrokeCap = SKStrokeCap.Round };
		for (int i = 0; i < IsRows.Length; i++)
		{
			IsRow m = IsRows[i];
			float y = top + i * rowH;
			bool online = McOnline(m.Key);
			float v = online ? McEased("is." + m.Key, Val(m.Key), dt, 3.0) : 0f;
			int shown = (int)Math.Round(v);
			if (!_isChanged.TryGetValue(m.Key, out var ch))
			{
				ch = (shown, -100.0);
			}
			else if (ch.Value != shown)
			{
				ch = (shown, t);
			}
			_isChanged[m.Key] = ch;
			float sm = (float)Math.Clamp(1.0 - (t - ch.At) / 0.45, 0.0, 1.0);
			IsWord(c, m.Label, x0, y, 10.5f, 5f, IsLight, new SKColor(226, 190, 160, 205), -1);
			string value = online ? shown.ToString(CultureInfo.InvariantCulture) : "--";
			float vw = IsWord(c, value, x1 - 34f, y + 22f, 38f, 3f, IsThin, SKColors.White, 1, (_, _) => (0f, 1f, 22f * sm * sm), new SKColor(255, 176, 120, 50));
			IsWord(c, m.Unit, x1 - 26f, y + 45f, 14f, 2f, IsLight, warm, -1);
			float f = Math.Clamp(v / m.Hi, 0f, 1f);
			float ly = y + 68f;
			hair.Color = new SKColor(230, 190, 160, 46);
			c.DrawLine(x0, ly, x1, ly, hair);
			hair.Color = new SKColor(255, 206, 166, 200);
			c.DrawLine(x0, ly, x0 + (x1 - x0) * f, ly, hair);
			SkiaKit.Glow(c, x0 + (x1 - x0) * f, ly, 12f, 5f, new SKColor(255, 196, 150, (byte)(online ? 170 : 0)), SKBlendMode.Plus);
		}
	}
}
