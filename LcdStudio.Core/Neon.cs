using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// The neon dashboard pieces: segmented gradient rings, glowing bar and multi-line history charts, panels with
/// a lit edge, and a month calendar. Colours are lists of hex values ("#ff2d95,#7b5cff,#2dd4ff") so a ring or
/// chart can blend through several neon tones.
/// </summary>
public sealed partial class ScreenRenderer
{
	private readonly Dictionary<(int, int, string), (double Shown, double Time)> _neonEase = new Dictionary<(int, int, string), (double, double)>();

	private bool DrawNeon(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "neon" when w.Kind == WidgetKind.Ring:
			NeonRing(g, w, r, t);
			return true;
		case "neon" when w.Kind == WidgetKind.Panel:
			NeonPanel(g, w, r);
			return true;
		case "bars" when w.Kind == WidgetKind.Graph:
			NeonBars(g, w, r, t);
			return true;
		case "lines" when w.Kind == WidgetKind.Graph:
			NeonLines(g, w, r);
			return true;
		case "calendar" when w.Kind == WidgetKind.Text:
			Calendar(g, w, r, t);
			return true;
		case "glow" when w.Kind == WidgetKind.Text:
			GlowText(g, w, r);
			return true;
		default:
			return DrawDeer(g, w, r, t);
		}
	}

	private static Color[] Palette(string list, Color fallback)
	{
		Color[] colors = (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select((string s) => Imaging.Hex(s.Trim(), fallback)).ToArray();
		return colors.Length > 0 ? colors : new[] { fallback };
	}

	/// <summary>The colour at 0..1 along a palette.</summary>
	private static Color Along(Color[] palette, double f)
	{
		if (palette.Length == 1)
		{
			return palette[0];
		}
		double x = Math.Clamp(f, 0.0, 1.0) * (palette.Length - 1);
		int i = Math.Min(palette.Length - 2, (int)x);
		return Imaging.Mix(palette[i], palette[i + 1], x - i);
	}

	/// <summary>A reading that glides to its new value instead of jumping (about half a second).</summary>
	private double Eased(Widget w, double target, double t)
	{
		var key = (w.X, w.Y, w.Sensor);
		if (!_neonEase.TryGetValue(key, out var st))
		{
			st = (0.0, t);
		}
		double dt = Math.Clamp(t - st.Time, 0.0, 1.0);
		double shown = st.Shown + (target - st.Shown) * (1.0 - Math.Exp(-dt * 5.0));
		_neonEase[key] = (shown, t);
		return shown;
	}

	/// <summary>
	/// A donut of short segments filled up to the reading, blending through the palette in Text, each segment
	/// glowing; the value sits in the middle with the label from Print under it, and a bright bead rides the end.
	/// </summary>
	private void NeonRing(Graphics g, Widget w, RectangleF r, double t)
	{
		float side = Math.Min(r.Width, r.Height);
		float th = Math.Max(4f, w.Thickness);
		var box = new RectangleF(r.X + (r.Width - side) / 2f + th, r.Y + (r.Height - side) / 2f + th, side - th * 2f, side - th * 2f);
		Color[] palette = Palette(w.Text, Color.Cyan);
		double f = Eased(w, Fraction(w), t);
		const int segments = 48;
		const float gapDeg = 2.2f, segDeg = 360f / segments;
		using (var track = new Pen(Imaging.Hex(w.Track, Color.FromArgb(28, 120, 140, 200)), th))
		{
			for (int i = 0; i < segments; i++)
			{
				g.DrawArc(track, box, -90f + i * segDeg + gapDeg / 2f, segDeg - gapDeg);
			}
		}
		int lit = (int)Math.Round(f * segments);
		for (int i = 0; i < lit; i++)
		{
			Color c = Along(palette, i / (double)Math.Max(1, segments - 1));
			float start = -90f + i * segDeg + gapDeg / 2f;
			using (var glow = new Pen(Color.FromArgb(45, c), th * 2.2f))
			{
				g.DrawArc(glow, box, start, segDeg - gapDeg);
			}
			using var pen = new Pen(c, th);
			g.DrawArc(pen, box, start, segDeg - gapDeg);
		}
		if (lit > 0)
		{
			double a = (-90.0 + lit * segDeg) * Math.PI / 180.0;
			float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f, rad = box.Width / 2f;
			float bx = cx + (float)(Math.Cos(a) * rad), by = cy + (float)(Math.Sin(a) * rad);
			float pulse = (float)(th * (0.75 + 0.25 * Math.Sin(t * 3.0)));
			using var bead = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
			using var halo = new SolidBrush(Color.FromArgb(70, Along(palette, f)));
			g.FillEllipse(halo, bx - pulse * 1.6f, by - pulse * 1.6f, pulse * 3.2f, pulse * 3.2f);
			g.FillEllipse(bead, bx - pulse * 0.55f, by - pulse * 0.55f, pulse * 1.1f, pulse * 1.1f);
		}
		string value = Formatted(w) + (Sensors.Def(w.Sensor).Unit == "%" ? "%" : "");
		var mid = new RectangleF(box.X, box.Y + box.Height * 0.28f, box.Width, w.Size * 1.3f);
		Text(g, w, mid, value, ValueColor(w), w.Size);
		if (w.Print.Length > 0)
		{
			var below = new RectangleF(box.X, mid.Bottom - w.Size * 0.15f, box.Width, w.Size * 0.6f);
			Text(g, w, below, w.Print, Color.FromArgb(215, 200, 212, 238), w.Size * 0.36f);
		}
	}

	/// <summary>
	/// Neon lettering: the letters stay crisp in Color while soft strokes of Color2 halo their outlines, widest
	/// and faintest outside. Readings above WarnAt (when Sensor is set) switch to the warning colour.
	/// </summary>
	private readonly Dictionary<(int, int, string), (string Look, Bitmap Image, PointF Origin)> _glowCache = new Dictionary<(int, int, string), (string, Bitmap, PointF)>();

	private void GlowText(Graphics g, Widget w, RectangleF r)
	{
		string text = Resolve(w.Text);
		Color color = Imaging.Hex(w.Color, Color.White);
		if (w.WarnAt > 0f && w.Sensor.Length > 0 && _sensors.Get(w.Sensor) >= w.WarnAt)
		{
			color = Imaging.Hex(w.WarnColor, Color.Red);
		}
		// the halo is costly to stroke, so each text is drawn once into an image and reused until it changes
		string look = $"{text}|{color.ToArgb()}|{w.Color2}|{w.Font}|{w.Size}|{w.Bold}|{w.Align}|{w.W}|{w.H}";
		var key = (w.X, w.Y, w.Text);
		if (!_glowCache.TryGetValue(key, out var hit) || hit.Look != look)
		{
			hit.Image?.Dispose();
			float pad = w.Size * 0.4f;
			var bmp = new Bitmap((int)Math.Ceiling(r.Width + pad * 2f), (int)Math.Ceiling(r.Height + pad * 2f), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			using (Graphics bg = Graphics.FromImage(bmp))
			{
				Setup(bg);
				DrawGlowText(bg, w, new RectangleF(pad, pad, r.Width, r.Height), text, color);
			}
			_glowCache[key] = hit = (look, bmp, new PointF(-pad, -pad));
		}
		g.DrawImageUnscaled(hit.Image, (int)Math.Round(r.X + hit.Origin.X), (int)Math.Round(r.Y + hit.Origin.Y));
	}

	private void DrawGlowText(Graphics g, Widget w, RectangleF r, string text, Color color)
	{
		Color halo = w.Color2.Length > 0 ? Imaging.Hex(w.Color2, color) : color;
		using var path = new GraphicsPath();
		using var format = new StringFormat { Alignment = Align(w.Align), LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
		FontFamily family;
		try
		{
			family = new FontFamily(w.Font);
		}
		catch
		{
			family = new FontFamily("Segoe UI");
		}
		using (family)
		{
			FontStyle style = w.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
			path.AddString(text, family, (int)style, w.Size, r, format);
		}
		float[] widths = { w.Size * 0.32f, w.Size * 0.18f, w.Size * 0.08f };
		int[] alphas = { 22, 40, 70 };
		for (int i = 0; i < widths.Length; i++)
		{
			using var pen = new Pen(Color.FromArgb(alphas[i], halo), Math.Max(2f, widths[i])) { LineJoin = LineJoin.Round };
			g.DrawPath(pen, path);
		}
		using var brush = new SolidBrush(color);
		g.FillPath(brush, path);
	}

	/// <summary>A dark panel with a fine border, a letter-spaced title and a short glowing stroke along its top edge in Print's colour.</summary>
	private void NeonPanel(Graphics g, Widget w, RectangleF r)
	{
		using (GraphicsPath path = Imaging.Round(r, w.Radius))
		{
			using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Imaging.Hex(w.Color, Color.FromArgb(11, 16, 30)), Imaging.Hex(w.Color2, Color.FromArgb(7, 10, 20)), 90f))
			{
				g.FillPath(fill, path);
			}
			using var edge = new Pen(Imaging.Hex(w.Track, Color.FromArgb(34, 48, 79)), 1f);
			g.DrawPath(edge, path);
		}
		Color accent = Imaging.Hex(w.Print, Color.Cyan);
		float x0 = r.X + w.Radius + 6f, len = Math.Min(90f, r.Width * 0.3f);
		using (var glow = new Pen(Color.FromArgb(60, accent), 6f))
		{
			g.DrawLine(glow, x0, r.Y, x0 + len, r.Y);
		}
		using (var stroke = new Pen(accent, 2f))
		{
			g.DrawLine(stroke, x0, r.Y, x0 + len, r.Y);
		}
		if (w.Text.Length > 0)
		{
			string spaced = string.Join(" ", Resolve(w.Text).ToUpperInvariant().ToCharArray());
			Text(g, w, new RectangleF(r.X + 16f, r.Y + 10f, r.Width - 32f, w.Size * 1.5f), spaced, Imaging.Hex(w.WarnColor, Color.FromArgb(174, 185, 216)), w.Size);
		}
	}

	private float[] HistoryCopy(string sensor)
	{
		float[] history = _sensors.History(sensor);
		lock (history)
		{
			return (float[])history.Clone();
		}
	}

	private static void ChartGrid(Graphics g, RectangleF r)
	{
		using var pen = new Pen(Color.FromArgb(30, 140, 170, 230), 1f) { DashPattern = new[] { 2f, 4f } };
		for (int i = 1; i <= 3; i++)
		{
			float y = (float)Math.Round(r.Bottom - r.Height * i / 4f);
			g.DrawLine(pen, r.X, y, r.Right, y);
		}
		using var axis = new Pen(Color.FromArgb(60, 140, 170, 230), 1f);
		g.DrawLine(axis, r.X, r.Bottom, r.Right, r.Bottom);
	}

	/// <summary>
	/// History as glowing bars (Thickness wide) fading from the first palette colour at the top to the last at
	/// the base; the newest bar is brightest and a dashed line marks the average.
	/// </summary>
	private void NeonBars(Graphics g, Widget w, RectangleF r, double t)
	{
		ChartGrid(g, r);
		float[] data = HistoryCopy(w.Sensor);
		if (data.Length < 1)
		{
			return;
		}
		Color[] palette = Palette(w.Text, Color.Cyan);
		Color top = palette[0], bottom = palette[palette.Length - 1];
		float barW = Math.Max(3f, w.Thickness), gap = Math.Max(2f, barW * 0.45f);
		int n = Math.Min(data.Length, (int)((r.Width + gap) / (barW + gap)));
		SmoothingMode mode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.None;
		double sum = 0;
		for (int i = 0; i < n; i++)
		{
			float v = data[data.Length - n + i];
			sum += v;
			float f = (float)Math.Clamp((v - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0);
			float h = Math.Max(2f, f * r.Height);
			float x = (float)Math.Round(r.Right - (n - i) * (barW + gap) + gap);
			var bar = new RectangleF(x, r.Bottom - h, barW, h);
			bool newest = i == n - 1;
			int alpha = newest ? 255 : (int)(150 + 80 * i / (double)n);
			using (var fill = new LinearGradientBrush(new RectangleF(bar.X, bar.Y - 1f, bar.Width, bar.Height + 2f), Color.FromArgb(alpha, top), Color.FromArgb(alpha / 4, bottom), 90f))
			{
				g.FillRectangle(fill, bar);
			}
			using var cap = new SolidBrush(Color.FromArgb(newest ? 255 : 200, Imaging.Mix(top, Color.White, newest ? 0.6 : 0.25)));
			g.FillRectangle(cap, bar.X, bar.Y, bar.Width, 2f);
		}
		g.SmoothingMode = mode;
		// the newest bar breathes a glow, and the average is a dashed line across the chart
		float lastF = (float)Math.Clamp((data[data.Length - 1] - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0);
		float lx = r.Right - barW, ly = r.Bottom - Math.Max(2f, lastF * r.Height);
		using (var halo = new SolidBrush(Color.FromArgb((int)(50 + 40 * Math.Sin(t * 3.0)), top)))
		{
			g.FillRectangle(halo, lx - barW * 0.6f, ly - 6f, barW * 2.2f, r.Bottom - ly + 6f);
		}
		float avg = (float)Math.Clamp((sum / n - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0);
		using var avgPen = new Pen(Color.FromArgb(150, Imaging.Mix(top, Color.White, 0.4)), 1f) { DashPattern = new[] { 6f, 4f } };
		float ay = (float)Math.Round(r.Bottom - avg * r.Height);
		g.DrawLine(avgPen, r.X, ay, r.Right, ay);
	}

	/// <summary>
	/// Several readings' histories as glowing lines: Sensor holds a comma-separated list, Text the matching
	/// colours, and a small legend of their names and current values sits in the top-left corner.
	/// </summary>
	private void NeonLines(Graphics g, Widget w, RectangleF r)
	{
		ChartGrid(g, r);
		string[] sensors = w.Sensor.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((string s) => s.Trim()).ToArray();
		Color[] colors = Palette(w.Text, Color.Cyan);
		var legendFont = GetFont(w, Math.Max(10f, w.Size));
		float lx = r.X + 6f;
		for (int k = 0; k < sensors.Length; k++)
		{
			Color c = colors[k % colors.Length];
			float[] data = HistoryCopy(sensors[k]);
			SensorDef def = Sensors.Def(sensors[k]);
			if (data.Length >= 2)
			{
				int n = Math.Min(data.Length, Math.Max(10, (int)(r.Width / 5f)));
				var pts = new PointF[n];
				for (int i = 0; i < n; i++)
				{
					float f = (float)Math.Clamp((data[data.Length - n + i] - def.Min) / Math.Max(0.001, def.Max - def.Min), 0.0, 1.0);
					pts[i] = new PointF(r.Right - r.Width * (n - 1 - i) / (float)(n - 1), r.Bottom - f * r.Height);
				}
				using (var glow = new Pen(Color.FromArgb(50, c), 6f) { LineJoin = LineJoin.Round })
				{
					g.DrawLines(glow, pts);
				}
				using var pen = new Pen(c, 2f) { LineJoin = LineJoin.Round };
				g.DrawLines(pen, pts);
			}
			float? now = _sensors.Get(sensors[k]);
			string legend = $"{def.Label.ToUpperInvariant()}  {(now.HasValue ? now.Value.ToString("0", CultureInfo.InvariantCulture) : "--")}{def.Unit}";
			using var dot = new SolidBrush(c);
			g.FillRectangle(dot, lx, r.Y + 6f, 8f, 8f);
			g.DrawString(legend, legendFont, dot, lx + 12f, r.Y + 10f - legendFont.Size * 0.62f, StringFormat.GenericTypographic);
			lx += 24f + g.MeasureString(legend, legendFont, PointF.Empty, StringFormat.GenericTypographic).Width;
		}
	}

	/// <summary>
	/// This month as a grid starting on Monday: a letter-spaced month heading, weekday initials, day numbers
	/// (weekends in the second colour), and today in a glowing tile that breathes.
	/// </summary>
	private void Calendar(Graphics g, Widget w, RectangleF r, double t)
	{
		DateTime now = DateTime.Now;
		var first = new DateTime(now.Year, now.Month, 1);
		int lead = ((int)first.DayOfWeek + 6) % 7;
		int days = DateTime.DaysInMonth(now.Year, now.Month);
		int rows = (lead + days + 6) / 7;
		Color text = Imaging.Hex(w.Color, Color.FromArgb(200, 210, 235));
		Color weekend = Imaging.Hex(w.Color2, Color.FromArgb(244, 114, 182));
		Color today = Imaging.Hex(w.WarnColor, Color.FromArgb(255, 45, 149));
		float headH = w.Size * 1.9f, dowH = w.Size * 1.5f;
		float cellW = r.Width / 7f, cellH = (r.Height - headH - dowH) / rows;
		string heading = string.Join(" ", now.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant().ToCharArray());
		Text(g, w, new RectangleF(r.X, r.Y, r.Width, headH), heading, Color.FromArgb(230, 240, 255), w.Size * 1.05f);
		string[] dow = { "M", "T", "W", "T", "F", "S", "S" };
		for (int i = 0; i < 7; i++)
		{
			Text(g, w, new RectangleF(r.X + i * cellW, r.Y + headH, cellW, dowH), dow[i], Color.FromArgb(200, 160, 182, 220), w.Size * 0.8f);
		}
		using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
		Font font = GetFont(w, w.Size);
		for (int d = 1; d <= days; d++)
		{
			int slot = lead + d - 1, col = slot % 7, row = slot / 7;
			var cell = new RectangleF(r.X + col * cellW, r.Y + headH + dowH + row * cellH, cellW, cellH);
			if (d == now.Day)
			{
				var tile = RectangleF.Inflate(cell, -cellW * 0.12f, -cellH * 0.1f);
				int glow = (int)(70 + 50 * Math.Sin(t * 2.2));
				using (GraphicsPath halo = Imaging.Round(RectangleF.Inflate(tile, 4f, 4f), 8f))
				using (var hb = new SolidBrush(Color.FromArgb(glow, today)))
				{
					g.FillPath(hb, halo);
				}
				using (GraphicsPath tp = Imaging.Round(tile, 6f))
				using (var tb = new SolidBrush(today))
				{
					g.FillPath(tb, tp);
				}
				using var white = new SolidBrush(Color.White);
				g.DrawString(d.ToString(CultureInfo.InvariantCulture), font, white, cell, sf);
				continue;
			}
			using var b = new SolidBrush(col >= 5 ? weekend : (d < now.Day ? Color.FromArgb(170, text) : text));
			g.DrawString(d.ToString(CultureInfo.InvariantCulture), font, b, cell, sf);
		}
	}
}
