using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Clearance: a yellow technical poster, after a road-signage type specimen (huge chamfered black figures on thick
/// rules, hairline frames, a red block, hazard stripes, warning triangles, wireframe boxes, a red pipe, small trucks).
/// - Four columns: CPU load, GPU load and the time as huge chamfered figures (drawn here from strokes, not a font),
///   each on a thick rule with a label; CPU and GPU temperature as smaller figures; memory as the red block, solid up to
///   the reading and hatched after it; a wireframe box holding a minute of CPU (black) and GPU (red) history.
/// - A changed figure rolls up in its cell like a sign flap. Hazard stripes scroll with the graphics load, light flows
///   down the red pipe, trucks drive along the road at the foot (faster with network traffic), small lines of text type
///   themselves out, and the warning triangles flash red when a temperature passes 85 °C or a load passes 95 %.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly SKColor ClrYellow = new(243, 199, 28), ClrInk = new(27, 26, 24), ClrRed = new(226, 56, 27);

	private const string ClrLabel = "Bahnschrift SemiCondensed", ClrMono = "Consolas";

	private sealed class ClrItem
	{
		public readonly char[] Ch = new char[6];

		public readonly char[] Was = new char[6];

		public readonly double[] Since = { -100, -100, -100, -100, -100, -100 };

		public readonly float[] X = { float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN };
	}

	private readonly Dictionary<string, ClrItem> _clrItems = new();

	private readonly Dictionary<char, SKPath> _clrGlyphs = new();

	private readonly float[] _clrCpu = new float[120], _clrGpu = new float[120];

	private double _clrAt = double.NaN, _clrStripe, _clrFlow, _clrRoad, _clrSample;

	private int _clrHead, _clrCount;

	private bool DrawClearance(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "clearance" || w.Kind != WidgetKind.Panel)
		{
			return DrawRetroRack(g, w, r, t);
		}
		Clearance(g, t);
		return true;
	}

	private void Clearance(Graphics g, double t)
	{
		double dt = double.IsNaN(_clrAt) ? 0.0 : Math.Clamp(t - _clrAt, 0.0, 0.25);
		_clrAt = t;
		float cpu = Math.Clamp(McEased("clr.cpu", Val("cpu.load") / 100f, dt, 2.2), 0f, 1f);
		float gpu = Math.Clamp(McEased("clr.gpu", Val("gpu.load") / 100f, dt, 2.2), 0f, 1f);
		float ram = Math.Clamp(McEased("clr.ram", Val("ram.load") / 100f, dt, 2.2), 0f, 1f);
		bool cpuT = McOnline("cpu.temp"), gpuT = McOnline("gpu.temp");
		float ct = cpuT ? McEased("clr.ct", Val("cpu.temp"), dt, 2.2) : 0f;
		float gt = gpuT ? McEased("clr.gt", Val("gpu.temp"), dt, 2.2) : 0f;
		float down = McOnline("net.down") ? Val("net.down") : 0f;
		float up = McOnline("net.up") ? Val("net.up") : 0f;
		bool alert = ct >= 85f || gt >= 85f || cpu >= 0.95f || gpu >= 0.95f;
		_clrStripe += dt * (14.0 + 50.0 * gpu);
		_clrFlow += dt * (40.0 + 160.0 * cpu);
		_clrRoad += dt * (60.0 + Math.Min(260.0, down * 25.0));
		_clrSample += dt;
		while (_clrSample >= 0.5)
		{
			_clrSample -= 0.5;
			_clrCpu[_clrHead] = cpu;
			_clrGpu[_clrHead] = gpu;
			_clrHead = (_clrHead + 1) % _clrCpu.Length;
			_clrCount = Math.Min(_clrCount + 1, _clrCpu.Length);
		}
		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;
		string Pct(float v) => Math.Round(v * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
		string Deg(bool on, float v) => on ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "°" : "--";
		string Opt(string key, string fmt, string unit) => McOnline(key) ? Val(key).ToString(fmt, CultureInfo.InvariantCulture) + unit : "--";

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			using var fill = new SKPaint { IsAntialias = true };
			using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = ClrInk.WithAlpha(150) };
			c.Clear(ClrYellow);

			// the bottom right: dark, with yellow stripes, where the pipes run
			fill.Color = ClrInk;
			c.DrawRect(1440f, 392f, 480f, 88f, fill);
			ClrHatch(c, new SKRect(1440f, 392f, 1920f, 480f), ClrYellow.WithAlpha(210), 22f, 7f, (float)_clrStripe);

			// hairline frames
			foreach (float x in new[] { 20f, 484f, 954f, 1434f, 1904f })
			{
				c.DrawLine(x, 26f, x, 392f, line);
			}
			c.DrawLine(20f, 26f, 1904f, 26f, line);
			foreach (float x0 in new[] { 20f, 484f, 954f })
			{
				c.DrawLine(x0, 248f, x0 + 470f, 248f, line);
			}
			c.DrawLine(0f, 392f, 1440f, 392f, line);

			// small headers
			McText(c, "CPU MEASUREMENT", 30f, 10f, 11f, ClrInk, font: ClrMono);
			McText(c, "GPU MEASUREMENT", 494f, 10f, 11f, ClrInk, font: ClrMono);
			McText(c, "TIME MEASUREMENT", 964f, 10f, 11f, ClrInk, font: ClrMono);
			McText(c, "DANGER: HOT SECTION. DO NOT EXCEED 90°C", 1448f, 4f, 11f, ClrRed, font: ClrMono);
			McText(c, "UNLESS YOU HAVE CHECKED THE COOLING.", 1448f, 16f, 11f, ClrRed, font: ClrMono);

			// the big figures on thick rules
			ClrNumber(c, "cpu", Pct(cpu), 30f, 40f, 140f, t);
			ClrNumber(c, "gpu", Pct(gpu), 494f, 40f, 140f, t);
			ClrNumber(c, "time", now.ToString("HH:mm", CultureInfo.InvariantCulture), 964f, 44f, 134f, t);
			fill.Color = ClrInk;
			foreach (float x0 in new[] { 30f, 494f, 964f })
			{
				c.DrawRect(x0, 192f, 450f, 10f, fill);
			}
			McText(c, "PROCESSOR: LOAD", 30f, 214f, 26f, ClrInk, font: ClrLabel);
			McText(c, "GRAPHICS: LOAD", 494f, 214f, 26f, ClrInk, font: ClrLabel);
			McText(c, "TIME: " + now.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant(), 964f, 214f, 26f, ClrInk, font: ClrLabel);

			// temperatures, each with a small warning sign and a typed note
			ClrNumber(c, "ct", Deg(cpuT, ct), 30f, 266f, 58f, t);
			ClrNumber(c, "gt", Deg(gpuT, gt), 494f, 266f, 58f, t);
			ClrSign(c, 300f, 270f, 46f, ct >= 85f, t);
			ClrSign(c, 764f, 270f, 46f, gt >= 85f, t);
			McText(c, "CPU TEMP", 360f, 272f, 20f, ClrInk, font: ClrLabel);
			McText(c, "GPU TEMP", 824f, 272f, 20f, ClrInk, font: ClrLabel);
			ClrType(c, $"CLOCK {Opt("cpu.clock", "0", " MHZ")} / POWER {Opt("cpu.power", "0", " W")}", 30f, 344f, t, 0.0);
			ClrType(c, $"UP {Opt("uptime.h", "0", " H")} / BUSIEST THREAD {Opt("cpu.busiest", "0", "%")}", 30f, 360f, t, 0.6);
			ClrType(c, $"CLOCK {Opt("gpu.clock", "0", " MHZ")} / POWER {Opt("gpu.power", "0", " W")}", 494f, 344f, t, 1.2);
			ClrType(c, $"VRAM {Opt("gpu.vram", "0.0", " GB")} / FAN {Opt("gpu.fan", "0", "%")}", 494f, 360f, t, 1.8);

			// a minute of history in a wireframe box
			ClrChart(c, new SKRect(968f, 262f, 1420f, 380f));

			// memory: the red block, solid to the reading, hatched after it
			var block = new SKRect(1448f, 40f, 1840f, 176f);
			float xe = block.Left + block.Width * ram;
			fill.Color = ClrRed;
			c.DrawRect(block.Left, block.Top, xe - block.Left, block.Height, fill);
			ClrHatch(c, new SKRect(xe, block.Top, block.Right, block.Bottom), ClrRed, 12f, 3f, 0f);
			line.Color = ClrRed;
			line.StrokeWidth = 2f;
			c.DrawRect(block, line);
			ClrHatch(c, new SKRect(1448f, 192f, 1840f, 202f), ClrRed, 8f, 2f, (float)_clrStripe * 0.5f);
			McText(c, "MEMORY: " + Pct(ram) + " IN USE", 1448f, 214f, 26f, ClrInk, font: ClrLabel);

			// warning signs and the big "!", flashing on an alert
			ClrSign(c, 1452f, 268f, 64f, alert, t, red: true);
			ClrSign(c, 1530f, 268f, 64f, alert, t, red: true);
			fill.Color = ClrInk;
			c.DrawRect(1626f, 262f, 16f, 50f, fill);
			c.DrawRect(1626f, 320f, 16f, 14f, fill);
			McText(c, alert ? "WARNING: LIMIT REACHED." : "CLEARANCE OK.", 1452f, 350f, 11f, ClrRed, font: ClrMono);
			McText(c, alert ? "SLOW DOWN." : "ALL SYSTEMS WITHIN LIMITS.", 1452f, 364f, 11f, ClrRed, font: ClrMono);

			// the pipes: red down from the block and along the foot, white up from the bottom; light flows in the red one
			using (var pipe = new SKPath())
			{
				pipe.MoveTo(1870f, 40f);
				pipe.LineTo(1870f, 412f);
				pipe.ArcTo(new SKRect(1830f, 372f, 1870f, 452f), 0f, 90f, false);
				pipe.LineTo(1700f, 452f);
				pipe.ArcTo(new SKRect(1660f, 452f, 1700f, 492f), 270f, -90f, false);
				pipe.LineTo(1660f, 490f);
				using var pp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12f, Color = ClrRed, StrokeJoin = SKStrokeJoin.Round };
				c.DrawPath(pipe, pp);
				pp.StrokeWidth = 4f;
				pp.Color = new SKColor(255, 196, 120, 220);
				pp.PathEffect = SKPathEffect.CreateDash(new[] { 14f, 46f }, -(float)(_clrFlow % 60.0));
				c.DrawPath(pipe, pp);
				pp.PathEffect = null;
				using var white = new SKPath();
				white.MoveTo(1520f, 490f);
				white.LineTo(1520f, 440f);
				white.ArcTo(new SKRect(1520f, 400f, 1560f, 440f), 180f, 90f, false);
				white.LineTo(1620f, 400f);
				pp.StrokeWidth = 10f;
				pp.Color = SKColors.White;
				c.DrawPath(white, pp);
			}
			// a white box with a cross where the pipes cross the frame
			line.Color = SKColors.White;
			line.StrokeWidth = 2f;
			c.DrawRect(1610f, 386f, 30f, 30f, line);
			c.DrawLine(1610f, 386f, 1640f, 416f, line);
			c.DrawLine(1640f, 386f, 1610f, 416f, line);

			// the foot: a scrolling hazard strip, the road and the trucks, traffic figures
			ClrHatch(c, new SKRect(0f, 416f, 1434f, 428f), ClrInk, 14f, 5f, (float)_clrStripe);
			fill.Color = ClrInk;
			c.DrawRect(0f, 468f, 1434f, 4f, fill);
			float road = (float)(_clrRoad % 1500.0);
			for (int i = 0; i < 3; i++)
			{
				float x = (road + i * 520f) % 1560f - 120f;
				ClrTruck(c, x, 468f, i == 1 ? 0.8f : 1f, i == 2);
			}
			fill.Color = ClrRed;
			c.DrawRoundRect(new SKRect(0f, 397f, 26f, 411f), 3f, 3f, fill);
			McText(c, "5.01", 4f, 400f, 9f, ClrYellow, font: ClrMono);
			McText(c, $"NET DOWN {ClrRate(down)}  /  UP {ClrRate(up)}", 34f, 399f, 12f, ClrInk, font: ClrMono);
			McText(c, now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 1424f, 399f, 12f, ClrInk, align: 1, font: ClrMono);
		});
	}

	private static string ClrRate(float mb)
	{
		return mb >= 1f ? mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB/S" : (mb * 1024f).ToString("0", CultureInfo.InvariantCulture) + " KB/S";
	}

	/// <summary>Diagonal stripes inside a rectangle, shifted along by offset so they can scroll.</summary>
	private static void ClrHatch(SKCanvas c, SKRect r, SKColor color, float gap, float width, float offset)
	{
		if (r.Width <= 0f || r.Height <= 0f)
		{
			return;
		}
		c.Save();
		c.ClipRect(r);
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, Color = color };
		float start = r.Left - r.Height - gap + offset % gap;
		for (float x = start; x < r.Right + gap; x += gap)
		{
			c.DrawLine(x, r.Bottom, x + r.Height, r.Top, p);
		}
		c.Restore();
	}

	/// <summary>A warning triangle with an exclamation mark; when on it flashes, filled red.</summary>
	private static void ClrSign(SKCanvas c, float x, float y, float size, bool on, double t, bool red = false)
	{
		float h = size * 0.87f;
		using var tri = new SKPath();
		tri.MoveTo(x + size / 2f, y);
		tri.LineTo(x + size, y + h);
		tri.LineTo(x, y + h);
		tri.Close();
		bool lit = on && t * 1.6 % 1.0 < 0.55;
		SKColor ink = red ? ClrRed : ClrInk;
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.08f, StrokeJoin = SKStrokeJoin.Miter, Color = ink };
		if (lit)
		{
			p.Style = SKPaintStyle.StrokeAndFill;
			p.Color = ClrRed;
		}
		c.DrawPath(tri, p);
		p.Style = SKPaintStyle.Fill;
		p.Color = lit ? ClrYellow : ink;
		float bw = size * 0.08f;
		c.DrawRect(x + size / 2f - bw / 2f, y + h * 0.36f, bw, h * 0.32f, p);
		c.DrawRect(x + size / 2f - bw / 2f, y + h * 0.74f, bw, bw, p);
	}

	/// <summary>A small line of text that types itself out every 8 s, behind a block cursor.</summary>
	private void ClrType(SKCanvas c, string text, float x, float y, double t, double delay)
	{
		double k = (t - delay) % 8.0;
		if (k < 0)
		{
			k += 8.0;
		}
		int n = Math.Min(text.Length, (int)(k * 30.0));
		string shown = text[..n];
		SKRect b = n > 0 ? McText(c, shown, x, y, 12f, ClrInk, font: ClrMono) : new SKRect(x, y, x, y);
		if (n < text.Length || k % 1.0 < 0.5)
		{
			using var p = new SKPaint { Color = ClrRed };
			c.DrawRect(n > 0 ? b.Right + 3f : x, y - 1f, 7f, 12f, p);
		}
	}

	/// <summary>A wireframe picture box (a frame with a faint cross) holding a minute of CPU and GPU load.</summary>
	private void ClrChart(SKCanvas c, SKRect r)
	{
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = ClrInk.WithAlpha(170) };
		c.DrawRect(r, p);
		p.Color = ClrInk.WithAlpha(50);
		c.DrawLine(r.Left, r.Top, r.Right, r.Bottom, p);
		c.DrawLine(r.Right, r.Top, r.Left, r.Bottom, p);
		McText(c, "LOAD / 60 S", r.Left + 6f, r.Top + 5f, 11f, ClrInk, font: ClrMono);
		McText(c, "CPU", r.Right - 70f, r.Top + 5f, 11f, ClrInk, font: ClrMono);
		McText(c, "GPU", r.Right - 34f, r.Top + 5f, 11f, ClrRed, font: ClrMono);
		if (_clrCount < 2)
		{
			return;
		}
		float top = r.Top + 22f, bottom = r.Bottom - 6f;
		void Line(float[] data, SKColor col)
		{
			using var path = new SKPath();
			int n = _clrCount;
			for (int i = 0; i < n; i++)
			{
				float v = data[(_clrHead - n + i + data.Length * 2) % data.Length];
				float x = r.Right - 6f - (n - 1 - i) * (r.Width - 12f) / (data.Length - 1);
				float y = bottom - v * (bottom - top);
				if (i == 0)
				{
					path.MoveTo(x, y);
				}
				else
				{
					path.LineTo(x, y);
				}
			}
			p.Color = col;
			p.StrokeWidth = 2.5f;
			p.StrokeJoin = SKStrokeJoin.Round;
			c.DrawPath(path, p);
		}
		Line(_clrCpu, ClrInk);
		Line(_clrGpu, ClrRed);
	}

	/// <summary>A small generic truck standing on the road at y: a box trailer, a cab, wheels.</summary>
	private static void ClrTruck(SKCanvas c, float x, float y, float s, bool red)
	{
		c.Save();
		c.Translate(x, y);
		c.Scale(s);
		using var p = new SKPaint { IsAntialias = true, Color = red ? ClrRed : ClrInk };
		c.DrawRect(0f, -38f, 78f, 28f, p);
		using (var cab = new SKPath())
		{
			cab.MoveTo(81f, -10f);
			cab.LineTo(81f, -32f);
			cab.LineTo(96f, -32f);
			cab.LineTo(104f, -22f);
			cab.LineTo(104f, -10f);
			cab.Close();
			c.DrawPath(cab, p);
		}
		c.DrawRect(-2f, -10f, 108f, 4f, p);
		p.Color = ClrYellow;
		c.DrawRect(91f, -29f, 7f, 7f, p);
		foreach (float wx in new[] { 12f, 26f, 62f, 94f })
		{
			p.Color = ClrInk;
			c.DrawCircle(wx, -5f, 6f, p);
			p.Color = ClrYellow;
			c.DrawCircle(wx, -5f, 2f, p);
		}
		c.Restore();
	}

	// ---------- chamfered figures ----------

	private const float ClrW = 6f, ClrH = 10f, ClrStroke = 1.75f, ClrC = 1.3f;

	/// <summary>The centre lines of a chamfered figure on a 6 × 10 grid; strokes are 1.75 wide.</summary>
	private SKPath ClrGlyph(char ch)
	{
		if (_clrGlyphs.TryGetValue(ch, out SKPath? cached))
		{
			return cached;
		}
		const float w = ClrW, h = ClrH, k = ClrC, m = 5f;
		var p = new SKPath();
		void Poly(bool close, params float[] v)
		{
			p.MoveTo(v[0], v[1]);
			for (int i = 2; i < v.Length; i += 2)
			{
				p.LineTo(v[i], v[i + 1]);
			}
			if (close)
			{
				p.Close();
			}
		}
		switch (ch)
		{
		case '0': Poly(true, k, 0, w - k, 0, w, k, w, h - k, w - k, h, k, h, 0, h - k, 0, k); break;
		case '1': Poly(false, 1f, 2.2f, 3.6f, 0, 3.6f, h); break;
		case '2': Poly(false, 0, k, k, 0, w - k, 0, w, k, w, 4f, 0, 8.4f, 0, h, w, h); break;
		case '3': Poly(false, 0, 0, w - k, 0, w, k, w, 4f - 0.2f, w - k, m, 1.8f, m); Poly(false, w - k, m, w, m + k, w, h - k, w - k, h, 0, h); break;
		case '4': Poly(false, 4.4f, h, 4.4f, 0, 0, 6.6f, w, 6.6f); break;
		case '5': Poly(false, w, 0, 0, 0, 0, 4.6f, w - k, 4.6f, w, 4.6f + k, w, h - k, w - k, h, 0, h); break;
		case '6': Poly(false, w - k * 0.6f, 0, k, 0, 0, k, 0, h - k, k, h, w - k, h, w, h - k, w, m + k, w - k, m, 0, m); break;
		case '7': Poly(false, 0, 0, w, 0, w, k, 2.2f, h); break;
		case '8':
			Poly(true, k, 0, w - k, 0, w, k, w, m - k, w - k, m, k, m, 0, m - k, 0, k);
			Poly(false, k, m, 0, m + k, 0, h - k, k, h, w - k, h, w, h - k, w, m + k, w - k, m);
			break;
		case '9': Poly(false, k * 0.6f, h, w - k, h, w, h - k, w, k, w - k, 0, k, 0, 0, k, 0, m - k, k, m, w, m); break;
		case '%': Poly(true, 0, 0, 2.2f, 0, 2.2f, 2.8f, 0, 2.8f); Poly(true, w - 2.2f, h - 2.8f, w, h - 2.8f, w, h, w - 2.2f, h); Poly(false, w, 0.2f, 0, h - 0.2f); break;
		case '°': Poly(true, 0, 0, 2.6f, 0, 2.6f, 2.6f, 0, 2.6f); break;
		case ':': Poly(false, 0, 2.6f, 0, 3.4f); Poly(false, 0, h - 3.4f, 0, h - 2.6f); break;
		case '-': Poly(false, 0.6f, m, w - 0.6f, m); break;
		}
		return _clrGlyphs[ch] = p;
	}

	private static float ClrAdvance(char ch) => ch switch { ':' => ClrStroke + 1.6f, '°' => 2.6f + ClrStroke + 1.4f, _ => ClrW + ClrStroke + 1.4f };

	/// <summary>
	/// A line of chamfered figures, left-aligned at x. Cells are numbered from the right and ease when the line gets
	/// longer or shorter; a changed figure rolls up in its cell like a sign flap.
	/// </summary>
	private void ClrNumber(SKCanvas c, string id, string text, float x, float top, float height, double t, SKColor? ink = null)
	{
		if (!_clrItems.TryGetValue(id, out ClrItem? it))
		{
			it = _clrItems[id] = new ClrItem();
		}
		float u = height / (ClrH + ClrStroke);
		int n = Math.Min(6, text.Length);
		var left = new float[n];
		float xx = x;
		for (int i = 0; i < n; i++)
		{
			left[i] = xx;
			xx += ClrAdvance(text[i]) * u;
		}
		float e = 1f - MathF.Exp(-(float)(t - (double.IsNaN(_clrAt) ? t : _clrAt) + 0.033) * 8f);
		using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = ClrStroke, StrokeJoin = SKStrokeJoin.Miter, StrokeCap = SKStrokeCap.Butt, Color = ink ?? ClrInk };
		for (int k = 0; k < 6; k++)
		{
			char ch = k < n ? text[n - 1 - k] : '\0';
			float target = k < n ? left[n - 1 - k] : x - (k - n + 1) * ClrAdvance('0') * u;
			if (it.Ch[k] != ch)
			{
				it.Was[k] = it.Ch[k];
				it.Since[k] = float.IsNaN(it.X[k]) ? -100.0 : t;
				it.Ch[k] = ch;
			}
			it.X[k] = float.IsNaN(it.X[k]) ? target : it.X[k] + (target - it.X[k]) * e;
			float s = (float)Math.Clamp((t - it.Since[k]) / 0.45, 0.0, 1.0);
			s = 1f - MathF.Pow(1f - s, 3f);
			float cx = it.X[k];
			float cw = Math.Max(ClrAdvance(ch == '\0' ? '0' : ch), ClrAdvance(it.Was[k] == '\0' ? '0' : it.Was[k])) * u;
			c.Save();
			c.ClipRect(new SKRect(cx - u, top - u, cx + cw, top + height + u));
			if (s < 1f)
			{
				ClrDraw(c, it.Was[k], cx, top - s * height * 1.15f, u, paint);
				ClrDraw(c, ch, cx, top + (1f - s) * height * 1.15f, u, paint);
			}
			else
			{
				ClrDraw(c, ch, cx, top, u, paint);
			}
			c.Restore();
		}
	}

	private void ClrDraw(SKCanvas c, char ch, float x, float top, float u, SKPaint paint)
	{
		if (ch == '\0' || ch == ' ')
		{
			return;
		}
		c.Save();
		c.Translate(x + ClrStroke / 2f * u, top + ClrStroke / 2f * u);
		c.Scale(u);
		if (ch == ':')
		{
			// two square dots
			using var dot = new SKPaint { IsAntialias = true, Color = paint.Color };
			c.DrawRect(-ClrStroke / 2f, 2.2f, ClrStroke, ClrStroke, dot);
			c.DrawRect(-ClrStroke / 2f, ClrH - 2.2f - ClrStroke, ClrStroke, ClrStroke, dot);
		}
		else
		{
			c.DrawPath(ClrGlyph(ch), paint);
		}
		c.Restore();
	}
}
