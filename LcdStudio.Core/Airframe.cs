using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Airframe: the skin of an aircraft with a pack of technical labels stuck on it.
/// - Brushed aluminium plates with seams, rows of rivets and screws, a strip of blue tape and stencilled markings.
/// - The processor load as a huge black panel number (wide and bold, like a fuselage number) under "PROCESSOR
///   COMPT 2.1.3"; it decodes through random digits when it changes.
/// - A hinged access plate (pull rings, hinge) with the graphics readings stencilled on it.
/// - A cooling vent with a fan that turns with the graphics load, under its CAUTION stencil.
/// - Stickers: the time label with barcode and week, the processor temperature, memory, and a status strip; their
///   barcodes come from the readings and a red laser sweeps them.
/// A soft band of light glides across the metal. The static metal is drawn once and kept.
/// </summary>
public sealed partial class ScreenRenderer
{
	private const string AfStencil = "Courier New|B", AfBig = "Bahnschrift|B", AfLabel = "Bahnschrift SemiCondensed", AfLabelBold = "Bahnschrift SemiBold SemiCondensed", AfLabelLight = "Bahnschrift Light SemiCondensed";

	private static readonly SKColor AfInk = new SKColor(26, 27, 28), AfPaper = new SKColor(222, 221, 216), AfRed = new SKColor(232, 72, 63), AfStencilInk = new SKColor(34, 36, 37, 225);

	private SKImage? _afBase;

	private double _afAt = double.NaN, _afFan;

	private readonly Dictionary<string, (string Value, double At)> _afChanged = new Dictionary<string, (string, double)>();

	private static readonly SKRect AfP1 = new SKRect(14f, 14f, 770f, 466f), AfP2a = new SKRect(782f, 14f, 1300f, 214f), AfP2b = new SKRect(782f, 226f, 1300f, 466f), AfP3 = new SKRect(1312f, 14f, 1906f, 466f);

	private static readonly SKRect AfS1 = new SKRect(1332f, 32f, 1886f, 170f), AfS2 = new SKRect(1332f, 186f, 1602f, 330f), AfS3 = new SKRect(1616f, 186f, 1886f, 330f), AfS4 = new SKRect(1332f, 346f, 1886f, 448f);

	private const float AfFanX = 1030f, AfFanY = 384f, AfFanR = 50f;

	private float _afSpin;

	private bool DrawAirframe(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "airframe" || w.Kind != WidgetKind.Panel)
		{
			return DrawWatches(g, w, r, t);
		}
		Airframe(g, t);
		return true;
	}

	/// <summary>A value that decodes through random digits for a moment after it changes.</summary>
	private string AfShown(string key, string value, double t, double seconds)
	{
		if (!_afChanged.TryGetValue(key, out var ch))
		{
			ch = (value, -100.0);
		}
		else if (ch.Value != value)
		{
			ch = (value, t);
		}
		_afChanged[key] = ch;
		return MiDecode(value, t - ch.At, seconds, t);
	}

	private void Airframe(Graphics g, double t)
	{
		double dt = double.IsNaN(_afAt) ? 0.0 : Math.Clamp(t - _afAt, 0.0, 0.25);
		_afAt = t;
		// the fan turns with the case fans' real speed (or the graphics load when there is no fan reading), kept
		// under the speed where 30 frames a second would make it seem to stand still
		bool hasRpm = McOnline("fans.rpm") && Val("fans.rpm") > 0f;
		float rpm = hasRpm ? Math.Max(0f, McEased("af.rpm", Val("fans.rpm"), dt, 2.0)) : 0f;
		float spin = hasRpm ? Math.Clamp(rpm / 2200f, 0f, 1f) : Math.Clamp(McEased("af.gpu", Val("gpu.load") / 100f, dt, 1.2), 0f, 1f);
		_afFan += dt * (0.12 + 1.0 * spin);
		_afSpin = spin;
		AfSamplePower(t);

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_afBase ??= AfMakeBase();
			c.DrawImage(_afBase, 0f, 0f);

			AfBigNumber(c, t, dt);
			AfLamps(c, t);
			AfPlate(c, t, dt);
			AfFan(c, t);
			AfTacho(c, t, rpm, hasRpm);
			AfStickers(c, t, dt);

			// a soft band of light gliding across the metal
			float sweep = (float)(t / 16.0 % 1.0) * 2600f - 340f;
			using var band = SKShader.CreateLinearGradient(new SKPoint(sweep - 220f, 0f), new SKPoint(sweep + 220f, 480f),
				new[] { new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 30), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp);
			using var bp = new SKPaint { Shader = band, BlendMode = SKBlendMode.Plus };
			c.DrawRect(0f, 0f, 1920f, 480f, bp);
		});
	}

	// ---------- the metal, drawn once ----------

	private SKImage AfMakeBase()
	{
		using var surface = SKSurface.Create(new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		using (var shader = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 480f), new[] { new SKColor(150, 153, 155), new SKColor(128, 131, 133) }, null, SKShaderTileMode.Clamp))
		using (var bg = new SKPaint { Shader = shader })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, bg);
		}
		foreach (SKRect p in new[] { AfP1, AfP2b, AfP3 })
		{
			AfPlateSkin(c, p, 0f);
		}
		AfPlateSkin(c, AfP2a, 8f);

		// rivets along every seam
		foreach (SKRect p in new[] { AfP1, AfP2a, AfP2b, AfP3 })
		{
			AfRivetLine(c, p.Left + 10f, p.Top + 9f, p.Right - 10f, p.Top + 9f, 17f);
			AfRivetLine(c, p.Left + 10f, p.Bottom - 9f, p.Right - 10f, p.Bottom - 9f, 17f);
			AfRivetLine(c, p.Left + 9f, p.Top + 26f, p.Left + 9f, p.Bottom - 26f, 17f);
			AfRivetLine(c, p.Right - 9f, p.Top + 26f, p.Right - 9f, p.Bottom - 26f, 17f);
		}
		// screws in the big plate, like the panel fasteners in the reference
		foreach (float sx in new[] { 64f, 222f, 392f, 562f, 720f })
		{
			AfScrew(c, sx, 440f);
		}
		foreach (float sy in new[] { 60f, 190f, 320f })
		{
			AfScrew(c, 40f, sy);
			AfScrew(c, 744f, sy);
		}
		// the strip of blue-grey tape over the seam
		using (var tape = new SKPaint { Color = new SKColor(88, 108, 128, 215) })
		{
			c.DrawRect(726f, 360f, 20f, 112f, tape);
		}
		// stencils
		McText(c, "PROCESSOR", AfP1.MidX, 26f, 17f, AfStencilInk, align: 0, track: 2f, font: AfStencil);
		McText(c, "COMPT", AfP1.MidX, 48f, 17f, AfStencilInk, align: 0, track: 2f, font: AfStencil);
		McText(c, "2.1.3", AfP1.MidX, 70f, 17f, AfStencilInk, align: 0, track: 2f, font: AfStencil);
		McText(c, "LOAD PCT", 60f, 404f, 13f, AfStencilInk, track: 2f, font: AfStencil);
		McText(c, "JACKING POINT", 820f, 440f, 11f, AfStencilInk, track: 2f, font: AfStencil);
		McText(c, "CAUTION", AfP2b.MidX, 240f, 16f, AfStencilInk, align: 0, track: 2f, font: AfStencil);
		McText(c, "HIGH SPEED FAN KEEP", AfP2b.MidX, 262f, 15f, AfStencilInk, align: 0, track: 1.5f, font: AfStencil);
		McText(c, "FINGERS & TOOLS CLEAR", AfP2b.MidX, 282f, 15f, AfStencilInk, align: 0, track: 1.5f, font: AfStencil);
		McText(c, "2.1.2", AfP2b.Right - 40f, 430f, 13f, AfStencilInk, align: 1, track: 1.5f, font: AfStencil);
		McText(c, "2.1.4", AfP2a.MidX, 26f, 15f, AfStencilInk, align: 0, track: 2f, font: AfStencil);
		McText(c, "AVIONICS AIR OUTLET", 804f, 306f, 10f, AfStencilInk, track: 0.6f, font: AfStencil);
		McText(c, "REMOVE BEFORE FLIGHT", 804f, 320f, 10f, AfStencilInk, track: 0.6f, font: AfStencil);
		// pull rings and the hinge on the access plate
		using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, Color = new SKColor(70, 73, 75) })
		{
			foreach (float rx in new[] { 860f, 1222f })
			{
				c.DrawOval(rx, 34f, 11f, 8f, ring);
				AfScrew(c, rx, 26f, 4f);
			}
		}
		var hinge = new SKRect(AfP2a.Left + 40f, AfP2a.Bottom - 26f, AfP2a.Right - 40f, AfP2a.Bottom - 12f);
		using (var hs = SKShader.CreateLinearGradient(new SKPoint(0f, hinge.Top), new SKPoint(0f, hinge.Bottom), new[] { new SKColor(214, 216, 217), new SKColor(150, 153, 155), new SKColor(96, 99, 101) }, null, SKShaderTileMode.Clamp))
		using (var hp = new SKPaint { Shader = hs, IsAntialias = true })
		{
			c.DrawRoundRect(hinge, 4f, 4f, hp);
		}
		using (var knuckle = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(70, 72, 74, 200) })
		{
			for (float kx = hinge.Left + 30f; kx < hinge.Right; kx += 30f)
			{
				c.DrawLine(kx, hinge.Top + 1f, kx, hinge.Bottom - 1f, knuckle);
			}
		}
		// the fan's housing: a pressed ring and a shadowed opening
		using (var lip = SKShader.CreateLinearGradient(new SKPoint(AfFanX - AfFanR, AfFanY - AfFanR), new SKPoint(AfFanX + AfFanR, AfFanY + AfFanR), new[] { new SKColor(226, 228, 229), new SKColor(120, 123, 125) }, null, SKShaderTileMode.Clamp))
		using (var lp = new SKPaint { Shader = lip, IsAntialias = true })
		{
			c.DrawCircle(AfFanX, AfFanY, AfFanR + 12f, lp);
		}
		using (var hole = SKShader.CreateRadialGradient(new SKPoint(AfFanX, AfFanY), AfFanR, new[] { new SKColor(76, 79, 81), new SKColor(52, 54, 56), new SKColor(34, 36, 37) }, new[] { 0f, 0.7f, 1f }, SKShaderTileMode.Clamp))
		using (var hp = new SKPaint { Shader = hole, IsAntialias = true })
		{
			c.DrawCircle(AfFanX, AfFanY, AfFanR, hp);
		}
		// the stickers' paper and shadows
		foreach (SKRect s in new[] { AfS1, AfS2, AfS3, AfS4 })
		{
			using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 60), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) })
			{
				c.DrawRoundRect(new SKRect(s.Left + 2f, s.Top + 3f, s.Right + 2f, s.Bottom + 3f), 3f, 3f, shadow);
			}
			using (var paper = new SKPaint { Color = AfPaper, IsAntialias = true })
			{
				c.DrawRoundRect(s, 3f, 3f, paper);
			}
			using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = AfInk.WithAlpha(190) };
			c.DrawRect(new SKRect(s.Left + 5f, s.Top + 5f, s.Right - 5f, s.Bottom - 5f), edge);
		}
		return surface.Snapshot();
	}

	/// <summary>A plate: its own brushed tone, a dark seam with a lit edge, and fine brushing.</summary>
	private static void AfPlateSkin(SKCanvas c, SKRect p, float raise)
	{
		if (raise > 0f)
		{
			using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, raise * 0.6f) };
			c.DrawRoundRect(new SKRect(p.Left + raise * 0.4f, p.Top + raise * 0.6f, p.Right + raise * 0.4f, p.Bottom + raise * 0.6f), 6f, 6f, shadow);
		}
		float lift = raise > 0f ? 8f : 0f;
		using (var shader = SKShader.CreateLinearGradient(new SKPoint(p.Left, p.Top), new SKPoint(p.Right, p.Bottom),
			new[] { new SKColor((byte)(190 + lift), (byte)(193 + lift), (byte)(194 + lift)), new SKColor((byte)(176 + lift), (byte)(179 + lift), (byte)(180 + lift)), new SKColor((byte)(184 + lift), (byte)(187 + lift), (byte)(188 + lift)) }, null, SKShaderTileMode.Clamp))
		using (var fill = new SKPaint { Shader = shader, IsAntialias = true })
		{
			c.DrawRoundRect(p, 6f, 6f, fill);
		}
		// brushing: fine horizontal streaks, a little lighter and darker
		c.Save();
		c.ClipRoundRect(new SKRoundRect(p, 6f), antialias: true);
		using (var streak = new SKPaint { StrokeWidth = 1f })
		{
			int seed = (int)(p.Left * 7 + p.Top * 13);
			for (int i = 0; i < (int)(p.Width * p.Height / 160f); i++)
			{
				double a = Hash01(seed + i * 3), b = Hash01(seed + i * 7 + 1), k = Hash01(seed + i * 11 + 2);
				float y = p.Top + (float)b * p.Height, x = p.Left + (float)a * p.Width, len = 20f + (float)k * 140f;
				streak.Color = k > 0.5 ? new SKColor(255, 255, 255, (byte)(10 + 14 * k)) : new SKColor(60, 62, 64, (byte)(8 + 16 * (1 - k)));
				c.DrawLine(x, y, x + len, y, streak);
			}
		}
		c.Restore();
		using var seam = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = new SKColor(70, 73, 75, 220) };
		c.DrawRoundRect(p, 6f, 6f, seam);
		seam.StrokeWidth = 1f;
		seam.Color = new SKColor(236, 238, 238, 150);
		c.DrawRoundRect(new SKRect(p.Left + 1.5f, p.Top + 1.5f, p.Right - 1.5f, p.Bottom - 1.5f), 5f, 5f, seam);
	}

	private static void AfRivetLine(SKCanvas c, float x0, float y0, float x1, float y1, float step)
	{
		float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
		int n = Math.Max(1, (int)(len / step));
		using var head = new SKPaint { IsAntialias = true };
		using var shade = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
		for (int i = 0; i <= n; i++)
		{
			float x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n;
			head.Color = new SKColor(60, 62, 64, 90);
			c.DrawCircle(x + 0.8f, y + 1f, 2.9f, head);
			head.Color = new SKColor(196, 199, 200);
			c.DrawCircle(x, y, 2.5f, head);
			shade.Color = new SKColor(255, 255, 255, 170);
			c.DrawArc(new SKRect(x - 2f, y - 2f, x + 2f, y + 2f), 180f, 90f, false, shade);
		}
	}

	private static void AfScrew(SKCanvas c, float x, float y, float r = 6f)
	{
		using var p = new SKPaint { IsAntialias = true };
		p.Color = new SKColor(70, 72, 74, 120);
		c.DrawCircle(x + 1f, y + 1.4f, r + 1f, p);
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(x - r, y - r), new SKPoint(x + r, y + r), new[] { new SKColor(226, 228, 229), new SKColor(140, 143, 145) }, null, SKShaderTileMode.Clamp))
		{
			p.Shader = sh;
			c.DrawCircle(x, y, r, p);
			p.Shader = null;
		}
		p.Color = new SKColor(60, 62, 64, 220);
		p.StrokeWidth = 1.5f;
		c.DrawLine(x - r * 0.65f, y, x + r * 0.65f, y, p);
		c.DrawLine(x, y - r * 0.65f, x, y + r * 0.65f, p);
	}

	// ---------- the live parts ----------

	/// <summary>
	/// The processor load as the huge panel number, on mechanical counter wheels: each digit rolls up to the next, the
	/// tens turning over only as the ones pass nine, and the wheels ease to rest on whole numbers.
	/// </summary>
	private void AfBigNumber(SKCanvas c, double t, double dt)
	{
		bool online = McOnline("cpu.load");
		float target = online ? (float)Math.Round(Val("cpu.load")) : 0f;
		float v = Math.Clamp(McEased("af.cpu", target, dt, 2.6), 0f, 100f);
		int cells = v >= 99.5f ? 3 : 2;
		SKRect zero = McPath("0", AfBig, 300f, 0f).TightBounds;
		const float sx = 1.18f, height = 268f, top = 104f;
		float scale = height / zero.Height;
		float cellW = zero.Width * scale * sx, gap = 34f;
		float total = cells * cellW + (cells - 1) * gap;
		if (total > 680f)
		{
			float k = 680f / total;
			scale *= k;
			cellW *= k;
			gap *= k;
			total = 680f;
		}
		float h = zero.Height * scale;
		float y0 = top + (height - h) / 2f, pitch = h + 46f;
		float x = AfP1.MidX - total / 2f;
		c.Save();
		c.ClipRect(new SKRect(AfP1.Left + 20f, y0 - 6f, AfP1.Right - 20f, y0 + h + 6f));
		using var ink = new SKPaint { IsAntialias = true, Color = AfInk.WithAlpha(240) };
		for (int i = 0; i < cells; i++)
		{
			// i = 0 is the leftmost wheel; work out its place value from the right
			int place = cells - 1 - i;
			float pos;
			if (!online)
			{
				pos = 0f;
			}
			else if (place == 0)
			{
				pos = v % 10f;
			}
			else
			{
				float unit = MathF.Pow(10f, place);
				float below = v % unit;
				// this wheel turns only during the last whole step of the wheels to its right
				pos = MathF.Floor(v / unit) % 10f + Math.Clamp(below - (unit - 1f), 0f, 1f);
			}
			int d0 = (int)MathF.Floor(pos) % 10;
			float frac = pos - MathF.Floor(pos);
			for (int k = 0; k < 2; k++)
			{
				int d = (d0 + k) % 10;
				float oy = (k - frac) * -pitch;
				if (Math.Abs(oy) >= pitch)
				{
					continue;
				}
				SKPath p = McPath(online ? d.ToString(CultureInfo.InvariantCulture) : "-", AfBig, 300f, 0f);
				SKRect b = p.TightBounds;
				c.Save();
				c.Translate(x + (cellW - b.Width * scale * sx) / 2f, y0 - oy + (online ? 0f : h / 2f - b.Height * scale / 2f));
				c.Scale(scale * sx, scale);
				c.Translate(-b.Left, -zero.Top);
				c.DrawPath(p, ink);
				c.Restore();
			}
			x += cellW + gap;
		}
		c.Restore();
	}

	private readonly Dictionary<string, (bool On, double At)> _afLamps = new Dictionary<string, (bool, double)>();

	private double _afFirst = double.NaN;

	/// <summary>
	/// A row of cockpit warning lights. Each lights amber (or green for activity) with a glow, flashing three times as
	/// it comes on. For the first two seconds they all light together, like a lamp test.
	/// </summary>
	private void AfLamps(SKCanvas c, double t)
	{
		if (double.IsNaN(_afFirst))
		{
			_afFirst = t;
		}
		bool test = t - _afFirst < 2.0;
		var lamps = new (string Label, bool On, bool Green)[]
		{
			("CPU HOT", McOnline("cpu.temp") && Val("cpu.temp") >= 85f, false),
			("GPU HOT", McOnline("gpu.temp") && Val("gpu.temp") >= 80f, false),
			("CPU MAX", Val("cpu.load") >= 90f, false),
			("FAN HIGH", McOnline("fans.duty") && Val("fans.duty") >= 80f, false),
			("NET ACT", Val("net.down") + Val("net.up") >= 0.2f, true),
			("SYS OK", !(Val("cpu.temp") >= 85f || Val("gpu.temp") >= 80f), true)
		};
		const float w = 84f, h = 26f, gap = 6f, y = 396f;
		float x = 150f;
		var amber = new SKColor(255, 181, 71);
		var green = new SKColor(96, 220, 140);
		foreach (var (label, on0, isGreen) in lamps)
		{
			if (!_afLamps.TryGetValue(label, out var st) || st.On != on0)
			{
				st = (on0, st.At == 0 && !_afLamps.ContainsKey(label) ? -100.0 : t);
				_afLamps[label] = st;
			}
			bool on = test || on0;
			float since = (float)(t - st.At);
			if (on0 && since < 0.9f && !test)
			{
				// three quick flashes as it comes on
				on = (int)(since / 0.15f) % 2 == 0;
			}
			var r = new SKRect(x, y, x + w, y + h);
			SKColor lit = isGreen ? green : amber;
			using (var face = new SKPaint { IsAntialias = true, Color = on ? lit : new SKColor(58, 61, 63) })
			{
				c.DrawRoundRect(r, 3f, 3f, face);
			}
			if (on)
			{
				SkiaKit.Glow(c, r.MidX, r.MidY, w * 0.7f, h * 1.2f, lit.WithAlpha(90), SKBlendMode.Plus);
			}
			using (var bezel = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(40, 42, 44) })
			{
				c.DrawRoundRect(r, 3f, 3f, bezel);
			}
			McText(c, label, r.MidX, r.MidY - 5f, 10f, on ? new SKColor(30, 24, 14) : new SKColor(150, 154, 156), align: 0, track: 1.2f, font: AfLabelBold);
			x += w + gap;
		}
	}

	/// <summary>
	/// The tachometer round the vent: a scale from 0 to 3000 RPM, a red arc and pointer riding to the fans' speed on a
	/// spring, and the exact speed and duty in a readout box beside it.
	/// </summary>
	private void AfTacho(SKCanvas c, double t, float rpm, bool hasRpm)
	{
		const float max = 3000f, r0 = AfFanR + 13f, start = 135f, sweep = 270f;
		using var tick = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Butt, Color = AfStencilInk };
		for (int i = 0; i <= 24; i++)
		{
			float a = (start + sweep * i / 24f) * MathF.PI / 180f;
			bool major = i % 4 == 0;
			tick.StrokeWidth = major ? 2.2f : 1.2f;
			float r1 = r0, r2 = r0 + (major ? 9f : 5f);
			c.DrawLine(AfFanX + MathF.Cos(a) * r1, AfFanY + MathF.Sin(a) * r1, AfFanX + MathF.Cos(a) * r2, AfFanY + MathF.Sin(a) * r2, tick);
			if (major)
			{
				float rl = r0 + 19f;
				McText(c, (i / 4 * 5).ToString(CultureInfo.InvariantCulture), AfFanX + MathF.Cos(a) * rl, AfFanY + MathF.Sin(a) * rl - 5f, 10f, AfStencilInk, align: 0, font: AfLabelBold);
			}
		}
		McText(c, "×100", AfFanX, AfFanY + r0 + 4f, 9f, AfStencilInk, align: 0, font: AfLabel);
		float f = Math.Clamp(rpm / max, 0f, 1f);
		using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = AfRed })
		{
			var oval = new SKRect(AfFanX - r0 + 2f, AfFanY - r0 + 2f, AfFanX + r0 - 2f, AfFanY + r0 - 2f);
			if (f > 0.002f)
			{
				c.DrawArc(oval, start, sweep * f, false, arc);
			}
		}
		// the pointer: a red wedge on the scale
		float pa = (start + sweep * f) * MathF.PI / 180f;
		float cs = MathF.Cos(pa), sn = MathF.Sin(pa);
		using (var ptr = new SKPath())
		{
			ptr.MoveTo(AfFanX + cs * (r0 - 4f), AfFanY + sn * (r0 - 4f));
			ptr.LineTo(AfFanX + cs * (r0 + 12f) - sn * 5f, AfFanY + sn * (r0 + 12f) + cs * 5f);
			ptr.LineTo(AfFanX + cs * (r0 + 12f) + sn * 5f, AfFanY + sn * (r0 + 12f) - cs * 5f);
			ptr.Close();
			using var pp = new SKPaint { IsAntialias = true, Color = AfRed };
			c.DrawPath(ptr, pp);
		}

		// the readout box
		var box = new SKRect(1132f, 318f, 1284f, 420f);
		using (var face = new SKPaint { Color = new SKColor(30, 32, 33) })
		{
			c.DrawRoundRect(box, 4f, 4f, face);
		}
		using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(226, 228, 229, 160) })
		{
			c.DrawRoundRect(box, 4f, 4f, edge);
		}
		var green = new SKColor(150, 240, 170);
		McText(c, "FAN SPEED", box.Left + 10f, box.Top + 9f, 10f, green.WithAlpha(170), track: 1.6f, font: AfLabel);
		// a dot that blinks with each new reading
		using (var dot = new SKPaint { IsAntialias = true, Color = AfRed.WithAlpha((byte)((int)(t * 2.0) % 2 == 0 ? 255 : 70)) })
		{
			c.DrawCircle(box.Right - 12f, box.Top + 14f, 3f, dot);
		}
		string exact = hasRpm ? Math.Round(Val("fans.rpm")).ToString("#,0", CultureInfo.InvariantCulture) : "--";
		SKRect er = McText(c, exact, box.Right - 44f, box.Top + 30f, 34f, green, align: 1, font: AfLabelBold);
		McText(c, "RPM", box.Right - 10f, er.Bottom, 12f, green.WithAlpha(200), align: 1, bottom: true, font: AfLabel);
		string duty = McOnline("fans.duty") ? $"DUTY {Val("fans.duty"):0} %" : "DUTY --";
		McText(c, duty, box.Left + 10f, box.Bottom - 22f, 12f, green.WithAlpha(210), track: 1.2f, font: AfLabel);
		McText(c, McOnline("gpu.fan") ? $"GPU {Val("gpu.fan"):0}%" : "", box.Right - 10f, box.Bottom - 22f, 12f, green.WithAlpha(160), align: 1, font: AfLabel);
	}

	/// <summary>The system power, eased, kept four times a second for the last 30 seconds.</summary>
	private readonly float[] _afPower = new float[121];

	private double _afPowerAt = double.NaN;

	private void AfSamplePower(double t)
	{
		float now = Val("cpu.power") + Val("gpu.power");
		if (double.IsNaN(_afPowerAt))
		{
			Array.Fill(_afPower, now);
			_afPowerAt = t;
		}
		while (t - _afPowerAt >= 0.25)
		{
			Array.Copy(_afPower, 1, _afPower, 0, _afPower.Length - 1);
			_afPower[^1] = now;
			_afPowerAt += 0.25;
			if (t - _afPowerAt > 2.0)
			{
				_afPowerAt = t;
			}
		}
	}

	/// <summary>The strip chart: hairline grid, the power trace scrolling smoothly left, and the red pen.</summary>
	private void AfChart(SKCanvas c, SKRect r, double t)
	{
		using var grid = new SKPaint { StrokeWidth = 1f, Color = AfInk.WithAlpha(40) };
		for (int i = 1; i < 4; i++)
		{
			float y = r.Top + r.Height * i / 4f;
			c.DrawLine(r.Left, y, r.Right, y, grid);
		}
		float frac = (float)Math.Clamp((t - _afPowerAt) / 0.25, 0.0, 1.0);
		// the grid's vertical lines scroll with the trace
		float step = r.Width / 6f;
		for (int i = 0; i <= 7; i++)
		{
			float x = r.Left + (i - (float)(t / 5.0 % 1.0)) * step;
			if (x > r.Left && x < r.Right)
			{
				c.DrawLine(x, r.Top, x, r.Bottom, grid);
			}
		}
		float lo = float.MaxValue, hi = float.MinValue;
		foreach (float p in _afPower)
		{
			lo = Math.Min(lo, p);
			hi = Math.Max(hi, p);
		}
		float span = Math.Max(40f, hi - lo);
		float mid = (hi + lo) / 2f;
		lo = mid - span * 0.6f;
		hi = lo + span * 1.2f;
		int n = _afPower.Length;
		float dx = r.Width / (n - 2);
		using var path = new SKPath();
		for (int i = 0; i < n; i++)
		{
			float x = r.Left + (i - frac) * dx;
			float y = r.Bottom - (_afPower[i] - lo) / (hi - lo) * r.Height;
			if (i == 0)
			{
				path.MoveTo(x, y);
			}
			else
			{
				path.LineTo(x, y);
			}
		}
		c.Save();
		c.ClipRect(r);
		using (var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, StrokeJoin = SKStrokeJoin.Round, Color = AfInk })
		{
			c.DrawPath(path, line);
		}
		c.Restore();
		float py = r.Bottom - (_afPower[^1] - lo) / (hi - lo) * r.Height;
		float px = r.Left + (n - 1 - frac) * dx;
		using (var pen = new SKPaint { IsAntialias = true, Color = AfRed })
		{
			c.DrawCircle(Math.Min(px, r.Right), Math.Clamp(py, r.Top, r.Bottom), 3.2f, pen);
		}
		using (var frame = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = AfInk.WithAlpha(150) })
		{
			c.DrawRect(r, frame);
		}
	}

	/// <summary>The graphics readings stencilled on the access plate.</summary>
	private void AfPlate(SKCanvas c, double t, double dt)
	{
		bool lOn = McOnline("gpu.load"), tOn = McOnline("gpu.temp");
		float load = lOn ? McEased("af.gpul", Val("gpu.load"), dt, 3.0) : 0f;
		float temp = tOn ? McEased("af.gput", Val("gpu.temp"), dt, 3.0) : 0f;
		string ls = AfShown("gpul", lOn ? Math.Round(load).ToString("0", CultureInfo.InvariantCulture) : "--", t, 0.35);
		string ts = AfShown("gput", tOn ? Math.Round(temp).ToString("0", CultureInfo.InvariantCulture) : "--", t, 0.35);
		McText(c, "GRAPHICS", AfP2a.Left + 40f, 56f, 15f, AfStencilInk, track: 2f, font: AfStencil);
		McText(c, "LOAD", AfP2a.Left + 40f, 96f, 15f, AfStencilInk, track: 2f, font: AfStencil);
		SKRect a = McText(c, ls, AfP2a.Left + 150f, 80f, 64f, AfInk, font: AfBig);
		McText(c, "%", a.Right + 6f, a.Bottom, 22f, AfInk, bottom: true, font: AfBig);
		McText(c, "CORE", AfP2a.Left + 290f, 96f, 15f, AfStencilInk, track: 2f, font: AfStencil);
		SKRect b = McText(c, ts, AfP2a.Left + 370f, 80f, 64f, AfInk, font: AfBig);
		McText(c, "°C", b.Right + 6f, b.Bottom, 22f, AfInk, bottom: true, font: AfBig);
		McText(c, "DATA LINK  ·  ENABLE WOW BYPASS", AfP2a.MidX, 158f, 11f, AfStencilInk, align: 0, track: 1f, font: AfStencil);
	}

	/// <summary>The fan in its vent, turning with the graphics load, with blur when it runs fast.</summary>
	private void AfFan(SKCanvas c, double t)
	{
		float speed = (float)(0.18 + 0.95 * Math.Clamp(Val("gpu.load") / 100f, 0f, 1f));
		float angle = (float)(_afFan * 360.0 % 360.0);
		const int blades = 11;
		c.Save();
		using (var clip = new SKPath())
		{
			clip.AddCircle(AfFanX, AfFanY, AfFanR - 2f);
			c.ClipPath(clip, antialias: true);
		}
		using var blade = new SKPaint { IsAntialias = true };
		using var bladePath = new SKPath();
		bladePath.MoveTo(14f, -4f);
		bladePath.CubicTo(30f, -14f, 48f, -12f, AfFanR, -2f);
		bladePath.LineTo(AfFanR, 10f);
		bladePath.CubicTo(46f, 4f, 30f, 6f, 14f, 6f);
		bladePath.Close();
		// at speed, a few fading copies behind each blade give it motion blur
		int ghosts = speed > 0.6f ? 3 : 1;
		for (int gI = ghosts - 1; gI >= 0; gI--)
		{
			float lag = gI * 6f * speed;
			byte alpha = (byte)(gI == 0 ? 255 : 70 / gI);
			for (int i = 0; i < blades; i++)
			{
				c.Save();
				c.Translate(AfFanX, AfFanY);
				c.RotateDegrees(angle - lag + i * 360f / blades);
				using (var shader = SKShader.CreateLinearGradient(new SKPoint(14f, -10f), new SKPoint(AfFanR, 10f), new[] { new SKColor(190, 193, 195, alpha), new SKColor(122, 125, 127, alpha) }, null, SKShaderTileMode.Clamp))
				{
					blade.Shader = shader;
					c.DrawPath(bladePath, blade);
					blade.Shader = null;
				}
				c.Restore();
			}
		}
		c.Restore();
		// the hub
		using (var hub = SKShader.CreateLinearGradient(new SKPoint(AfFanX - 22f, AfFanY - 22f), new SKPoint(AfFanX + 22f, AfFanY + 22f), new[] { new SKColor(232, 234, 235), new SKColor(150, 153, 155) }, null, SKShaderTileMode.Clamp))
		using (var hp = new SKPaint { Shader = hub, IsAntialias = true })
		{
			c.DrawCircle(AfFanX, AfFanY, 22f, hp);
		}
		using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(70, 72, 74, 180) })
		{
			c.DrawCircle(AfFanX, AfFanY, 22f, rim);
			c.DrawCircle(AfFanX, AfFanY, AfFanR, rim);
		}
	}

	/// <summary>The four stickers with their live text, barcodes and the laser.</summary>
	private void AfStickers(SKCanvas c, double t, double dt)
	{
		DateTime now = DateTime.Now;
		using var rule = new SKPaint { StrokeWidth = 1.2f, Color = AfInk.WithAlpha(190) };
		var inkDim = AfInk.WithAlpha(170);

		// ---- S1: power and clocks, with a strip chart of the system power ----
		SKRect s = AfS1;
		float cpuW = Val("cpu.power"), gpuW = Val("gpu.power");
		MiBarcode(c, $"PWR{cpuW + gpuW:0}", s.Left + 14f, s.Top + 14f, 300f, 20f, AfInk);
		MiLaser(c, s.Left + 14f, s.Top + 14f, 300f, 20f, t, 0.1);
		McText(c, "PWR-00  POWER  &  CLOCKS", s.Left + 330f, s.Top + 16f, 11f, inkDim, track: 1.4f, font: AfLabel);
		c.DrawLine(s.Left + 5f, s.Top + 42f, s.Right - 5f, s.Top + 42f, rule);
		McText(c, "SYSTEM POWER", s.Left + 16f, s.Top + 52f, 11f, inkDim, track: 1.4f, font: AfLabel);
		float totalW = McEased("af.pwr", cpuW + gpuW, dt, 3.0);
		SKRect pr = McText(c, AfShown("pwr", totalW.ToString("0", CultureInfo.InvariantCulture), t, 0.3), s.Left + 16f, s.Top + 70f, 46f, AfInk, font: AfLabelBold);
		McText(c, "W", pr.Right + 5f, pr.Bottom, 18f, AfInk, bottom: true, font: AfLabel);
		McText(c, $"CPU {cpuW:0} W   ·   GPU {gpuW:0} W", s.Left + 16f, s.Top + 124f - 4f, 11f, AfInk, track: 1f, font: AfLabel);
		// the strip chart: the last 30 seconds, scrolling smoothly, with the red pen at the newest point
		var chart = new SKRect(s.Left + 214f, s.Top + 52f, s.Left + 404f, s.Bottom - 12f);
		AfChart(c, chart, t);
		c.DrawLine(s.Left + 414f, s.Top + 42f, s.Left + 414f, s.Bottom - 5f, rule);
		// clocks and graphics memory
		void Row(string label, string value, float y)
		{
			McText(c, label, s.Left + 426f, y + 3f, 10f, inkDim, track: 1.2f, font: AfLabel);
			McText(c, value, s.Right - 14f, y, 16f, AfInk, align: 1, font: AfLabelBold);
		}
		Row("CPU", McOnline("cpu.clock") ? $"{Val("cpu.clock") / 1000f:0.00} GHZ" : "--", s.Top + 52f);
		Row("GPU", McOnline("gpu.clock") ? $"{Val("gpu.clock"):0} MHZ" : "--", s.Top + 78f);
		Row("VRAM", McOnline("gpu.vram") ? $"{Val("gpu.vram"):0.0} GB" : "--", s.Top + 104f);

		// ---- S2: processor temperature, the INTERNAL-style label ----
		s = AfS2;
		bool cOn = McOnline("cpu.temp");
		float ct = cOn ? McEased("af.cput", Val("cpu.temp"), dt, 3.0) : 0f;
		bool hot = cOn && ct >= 90f;
		MiCells(c, s.Left + 14f, s.Top + 14f, 30, 3, 7f, t, Math.Clamp(Val("cpu.load") / 100f, 0f, 1f), AfInk);
		SKRect tag = MiTag(c, hot ? "CAUTION" : "INTERNAL", s.Left + 14f, s.Top + 44f, 16f, hot ? AfRed : AfInk, textColor: AfPaper);
		McText(c, "CPU TEMP", tag.Right + 10f, s.Top + 48f, 11f, inkDim, track: 1.4f, font: AfLabel);
		string cts = AfShown("cput", cOn ? Math.Round(ct).ToString("0", CultureInfo.InvariantCulture) : "--", t, 0.35);
		SKRect vr = McText(c, cts, s.Left + 16f, s.Top + 74f, 44f, AfInk, font: AfLabelLight);
		McText(c, "°C", vr.Right + 4f, vr.Bottom, 16f, AfInk, bottom: true, font: AfLabel);
		c.DrawLine(s.Left + 5f, s.Bottom - 28f, s.Right - 5f, s.Bottom - 28f, rule);
		McText(c, "REF", s.Left + 14f, s.Bottom - 20f, 10f, inkDim, track: 1f, font: AfLabel);
		McText(c, "11-5110", s.Left + 40f, s.Bottom - 20f, 10f, AfInk, track: 1f, font: AfLabel);
		McText(c, "LOT", s.Left + 140f, s.Bottom - 20f, 10f, inkDim, track: 1f, font: AfLabel);
		McText(c, "96024", s.Left + 166f, s.Bottom - 20f, 10f, AfInk, track: 1f, font: AfLabel);

		// ---- S3: memory ----
		s = AfS3;
		bool mOn = McOnline("ram.load");
		float mv = mOn ? McEased("af.ram", Val("ram.load"), dt, 3.0) : 0f;
		McText(c, "MEM-05", s.Left + 14f, s.Top + 14f, 11f, AfInk, track: 1.4f, font: AfLabelBold);
		McText(c, "LO-FI  REALITY  01", s.Right - 14f, s.Top + 14f, 10f, inkDim, align: 1, track: 1f, font: AfLabel);
		string ms = AfShown("ram", mOn ? Math.Round(mv).ToString("0", CultureInfo.InvariantCulture) : "--", t, 0.35);
		SKRect mr = McText(c, ms, s.Left + 16f, s.Top + 34f, 44f, AfInk, font: AfLabelLight);
		McText(c, "%  MEMORY", mr.Right + 6f, mr.Bottom, 14f, AfInk, bottom: true, track: 1f, font: AfLabel);
		// the bar: a track with a solid block filled to the reading
		float f = Math.Clamp(mv / 100f, 0f, 1f);
		c.DrawLine(s.Left + 14f, s.Top + 96f, s.Right - 14f, s.Top + 96f, rule);
		using (var block = new SKPaint { Color = AfInk })
		{
			c.DrawRect(s.Left + 14f, s.Top + 93f, (s.Width - 28f) * f, 6f, block);
		}
		MiBarcode(c, "ram" + ms, s.Left + 14f, s.Top + 106f, s.Width - 80f, 24f, AfInk);
		MiLaser(c, s.Left + 14f, s.Top + 106f, s.Width - 80f, 24f, t, 0.55);
		MiCross(c, s.Right - 34f, s.Top + 118f, 12f, t, AfInk);

		// ---- S4: the status strip ----
		s = AfS4;
		string? caution = null;
		if (hot)
		{
			caution = $"CPU {ct:0}°C";
		}
		else if (McOnline("gpu.temp") && Val("gpu.temp") >= 85f)
		{
			caution = $"GPU {Val("gpu.temp"):0}°C";
		}
		SKRect st = MiTag(c, "INFO", s.Left + 14f, s.Top + 14f, 16f, AfInk, textColor: AfPaper);
		McText(c, caution == null ? "STATUS  NOMINAL" : "CAUTION  " + caution, st.Right + 14f, s.Top + 16f, 18f, caution == null ? AfInk : AfRed, track: 1.4f, font: AfLabelBold);
		TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
		McText(c, $"MISSION TIME  {up.Days}D {up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}     NET  {Val("net.down"):0.0} / {Val("net.up"):0.0} MB/S", s.Left + 16f, s.Top + 50f, 12f, AfInk, track: 1.2f, font: AfLabel);
		McText(c, "DYSTROPHIN WTS URL   DIGIFORM RAAVH74   (EXTENSOR DIGITOURUM)", s.Left + 16f, s.Top + 70f, 9f, inkDim, track: 1f, font: AfLabel);
		MiHatch(c, new SKRect(s.Right - 64f, s.Top + 14f, s.Right - 14f, s.Bottom - 14f), AfInk);
		MiBarcode(c, "status" + now.Minute, s.Right - 230f, s.Top + 14f, 150f, 26f, AfInk);
		MiLaser(c, s.Right - 230f, s.Top + 14f, 150f, 26f, t, 0.8);
	}
}
