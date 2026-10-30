using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// Instrument tiles: pale grey panels on black, each a physical-looking gauge in black and burnt orange, the big
/// numbers with their decimals faded. Text picks the tile: "cpu" (load against its two-minute low and high),
/// "gpu" (load, and an orange dial that turns with the clock), "mem" (memory in use on an arc), "time" (the clock,
/// date, and a line per second), "power" (graphics power in bars) and "status" (network, on a panel that turns
/// coral and names the problem when something needs attention).
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color TileFace = Color.FromArgb(220, 220, 215), TileInk = Color.FromArgb(17, 17, 17), TileFade = Color.FromArgb(150, 150, 145);

	private static readonly Color TileOrange = Color.FromArgb(176, 85, 42), TileSage = Color.FromArgb(188, 194, 170), TileCoral = Color.FromArgb(224, 113, 79);

	private const string TileFont = "Segoe UI Semibold", TileLabel = "Segoe UI";

	private bool DrawInstrument(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "instrument" || w.Kind != WidgetKind.Panel)
		{
			return DrawVoid(g, w, r, t);
		}
		if (w.Text == "status")
		{
			StatusTile(g, r, t);
			return true;
		}
		using (GraphicsPath face = Imaging.Round(r, 22f))
		using (var fill = new SolidBrush(TileFace))
		{
			g.FillPath(fill, face);
		}
		switch (w.Text)
		{
		case "cpu":
			CpuTile(g, r, t);
			break;
		case "gpu":
			GpuTile(g, r, t);
			break;
		case "mem":
			MemTile(g, r, t);
			break;
		case "time":
			TimeTile(g, r, t);
			break;
		case "power":
			PowerTile(g, r, t);
			break;
		}
		return true;
	}

	/// <summary>A big number with everything after its whole part faded, as on a meter that has not settled.</summary>
	private void Reading(Graphics g, string value, string unit, float size, Color ink, Color faded, float x, float y)
	{
		int dot = value.IndexOf('.');
		string main = dot < 0 ? value : value.Substring(0, dot), rest = dot < 0 ? "" : value.Substring(dot);
		SizeF s = DeckText(g, main, size, ink, x, y, StringAlignment.Near, TileFont);
		float end = x + s.Width - 4f;
		if (rest.Length > 0)
		{
			end += DeckText(g, rest, size, faded, end, y, StringAlignment.Near, TileFont).Width - 4f;
		}
		if (unit.Length > 0)
		{
			// the unit smaller, sitting on the same baseline (Segoe's ascent is about 1.08 em)
			float us = size * 0.4f;
			DeckText(g, unit.Trim(), us, faded, end + size * 0.06f, y + 1.08f * (size - us), StringAlignment.Near, TileFont);
		}
	}

	private void TileTitle(Graphics g, RectangleF r, string label, string? note = null, Color? noteColor = null)
	{
		DeckText(g, label, 24f, TileInk, r.X + 26f, r.Y + 18f, StringAlignment.Near, TileLabel);
		if (note != null)
		{
			DeckText(g, note, 22f, noteColor ?? TileFade, r.Right - 26f, r.Y + 20f, StringAlignment.Far, TileLabel);
		}
	}

	/// <summary>Processor load, and a bar showing its two-minute range as a dark sweep with an orange block riding at the reading.</summary>
	private void CpuTile(Graphics g, RectangleF r, double t)
	{
		float load = Val("cpu.load");
		string state = load < 10f ? "idle" : load < 35f ? "light" : load < 65f ? "busy" : load < 90f ? "heavy" : "maxed";
		TileTitle(g, r, "Processor", state, load >= 65f ? TileOrange : TileFade);
		Reading(g, Fmt("cpu.load", "0.0"), "%", 100f, TileInk, TileFade, r.X + 20f, r.Y + 58f);
		float[] h = _sensors.History("cpu.load");
		float lo = Math.Clamp(h.Length > 0 ? Math.Min(h.Min(), load) : load, 0f, 100f), hi = Math.Clamp(h.Length > 0 ? Math.Max(h.Max(), load) : load, 0f, 100f);
		var bar = new RectangleF(r.X + 330f, r.Y + 78f, r.Width - 356f, 62f);
		float Pos(float v) => bar.X + Math.Clamp(v / 100f, 0f, 1f) * bar.Width;
		using (GraphicsPath track = Imaging.Round(bar, 8f))
		using (var under = new SolidBrush(Color.FromArgb(200, 200, 195)))
		{
			g.FillPath(under, track);
		}
		float a = Pos((float)DeckEase($"{r.X}|lo", lo, t)), b = Math.Max(a + 16f, Pos((float)DeckEase($"{r.X}|hi", hi, t)));
		using (GraphicsPath sweep = Imaging.Round(new RectangleF(a, bar.Y, b - a, bar.Height), 8f))
		using (var dark = new LinearGradientBrush(new RectangleF(a - 1f, bar.Y, b - a + 2f, bar.Height), TileInk, Color.FromArgb(120, 120, 116), 0f))
		{
			g.FillPath(dark, sweep);
		}
		// the block rides at the reading, ringed in the tile colour so it stands apart from the sweep
		const float bw = 64f;
		float at = Math.Clamp(Pos((float)DeckEase($"{r.X}|now", load, t)) - bw / 2f, bar.X, bar.Right - bw);
		using (GraphicsPath block = Imaging.Round(new RectangleF(at, bar.Y, bw, bar.Height), 8f))
		{
			using var ring = new Pen(TileFace, 8f);
			g.DrawPath(ring, block);
			using var orange = new SolidBrush(TileOrange);
			g.FillPath(orange, block);
		}
		DeckText(g, "L. " + lo.ToString("0", CultureInfo.InvariantCulture) + "%", 22f, TileInk, bar.X, bar.Bottom + 10f, StringAlignment.Near, TileLabel);
		DeckText(g, "H. " + hi.ToString("0", CultureInfo.InvariantCulture) + "%", 22f, TileInk, bar.Right, bar.Bottom + 10f, StringAlignment.Far, TileLabel);
	}

	/// <summary>Graphics load, and an orange dial at the tile's edge whose ticks turn with the graphics clock, read off by a needle.</summary>
	private void GpuTile(Graphics g, RectangleF r, double t)
	{
		TileTitle(g, r, "Graphics");
		Reading(g, Fmt("gpu.load", "0"), "%", 100f, TileInk, TileFade, r.X + 20f, r.Y + 58f);
		float rad = 190f, cx = r.Right + 52f, cy = r.Y + r.Height / 2f;
		float left = cx - rad;
		GraphicsState s = g.Save();
		using (GraphicsPath face = Imaging.Round(r, 22f))
		{
			g.SetClip(face);
		}
		using (var orange = new SolidBrush(TileOrange))
		{
			g.FillEllipse(orange, cx - rad, cy - rad, rad * 2f, rad * 2f);
		}
		double turn = DeckEase($"{r.X}|clock", Val("gpu.clock") / 3500.0 * 360.0, t);
		using (var tick = new Pen(TileInk, 2f))
		{
			for (int i = 0; i < 72; i++)
			{
				double ang = (i * 5.0 + turn) * Math.PI / 180.0;
				float len = i % 6 == 0 ? 22f : 12f;
				float c = (float)Math.Cos(ang), sn = (float)Math.Sin(ang);
				g.DrawLine(tick, cx + c * (rad - 6f), cy + sn * (rad - 6f), cx + c * (rad - 6f - len), cy + sn * (rad - 6f - len));
			}
		}
		g.Restore(s);
		using (var needle = new Pen(TileInk, 4f))
		{
			g.DrawLine(needle, left - 34f, cy, left + 26f, cy);
		}
		DeckText(g, Fmt("gpu.clock", "0"), 26f, TileInk, left - 44f, cy - 18f, StringAlignment.Far, TileLabel);
		DeckText(g, "MHz", 34f, TileInk, left + 40f, cy - 24f, StringAlignment.Near, TileFont);
		string name = _sensors.Text("gpu.name") ?? "";
		DeckText(g, name, 20f, TileFade, r.X + 26f, r.Bottom - 40f, StringAlignment.Near, TileLabel);
	}

	/// <summary>Memory in use, and a shallow orange arc with a black sweep and a knob at the share in use.</summary>
	private void MemTile(Graphics g, RectangleF r, double t)
	{
		float load = Val("ram.load");
		TileTitle(g, r, "Memory");
		Reading(g, Fmt("ram.used", "0.0"), " GB", 100f, TileInk, TileFade, r.X + 20f, r.Y + 58f);
		float cx = r.X + r.Width - 150f, cy = r.Y + 186f, rad = 130f;
		const float start = 205f, sweep = 130f;
		var box = new RectangleF(cx - rad, cy - rad, rad * 2f, rad * 2f);
		using (var track = new Pen(TileOrange, 24f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
		{
			g.DrawArc(track, box, start, sweep);
		}
		float f = (float)DeckEase($"{r.X}|mem", load / 100.0, t);
		if (f > 0.005f)
		{
			using var used = new Pen(TileInk, 14f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawArc(used, box, start, sweep * f);
		}
		double end = (start + sweep * f) * Math.PI / 180.0;
		float kx = cx + (float)Math.Cos(end) * rad, ky = cy + (float)Math.Sin(end) * rad;
		float k = 9f + 1.5f * (float)Math.Sin(t * 2.5);
		using (var knob = new SolidBrush(Color.FromArgb(30, 32, 30)))
		{
			g.FillEllipse(knob, kx - k, ky - k, k * 2f, k * 2f);
		}
		DeckText(g, "in use " + Fmt("ram.load", "0") + "%", 24f, TileInk, cx, cy - 74f, StringAlignment.Center, TileLabel);
		float total = load > 1f ? Val("ram.used") / load * 100f : 0f;
		DeckText(g, total > 0f ? "of " + total.ToString("0", CultureInfo.InvariantCulture) + " GB" : "", 20f, TileFade, r.X + 26f, r.Bottom - 40f, StringAlignment.Near, TileLabel);
	}

	/// <summary>The time and date, and a fine line for each second of the minute, filled to now with the current one in orange.</summary>
	private void TimeTile(Graphics g, RectangleF r, double t)
	{
		DateTime now = DateTime.Now;
		DeckText(g, now.ToString("HH:mm", CultureInfo.CurrentCulture), 120f, TileInk, r.X + 18f, r.Y + 22f, StringAlignment.Near, TileFont);
		DeckText(g, now.ToString("dddd", CultureInfo.CurrentCulture), 38f, TileInk, r.X + 350f, r.Y + 40f, StringAlignment.Near, TileLabel);
		DeckText(g, now.ToString("d MMMM", CultureInfo.CurrentCulture), 38f, TileInk, r.X + 350f, r.Y + 88f, StringAlignment.Near, TileLabel);
		var lines = new RectangleF(r.X + 26f, r.Y + 166f, r.Width - 52f, 26f);
		float step = lines.Width / 60f;
		for (int i = 0; i < 60; i++)
		{
			Color c = i == now.Second ? TileOrange : i < now.Second ? TileInk : Color.FromArgb(178, 178, 173);
			float hgt = i % 15 == 0 ? lines.Height : lines.Height * 0.7f;
			using var pen = new Pen(c, i == now.Second ? 3f : 1.6f);
			float x = lines.X + i * step + step / 2f;
			g.DrawLine(pen, x, lines.Bottom - hgt, x, lines.Bottom);
		}
	}

	/// <summary>Graphics power, with its temperature in the corner, as a row of bars lit black up to the reading; the leading bar orange.</summary>
	private void PowerTile(Graphics g, RectangleF r, double t)
	{
		float temp = Val("gpu.temp");
		TileTitle(g, r, "Graphics power", Fmt("gpu.temp", "0") + "°C", temp >= 75f ? TileOrange : TileFade);
		Reading(g, Fmt("gpu.power", "0"), " W", 100f, TileInk, TileFade, r.X + 20f, r.Y + 58f);
		const int n = 20;
		var area = new RectangleF(r.X + 340f, r.Y + 70f, r.Width - 366f, 120f);
		float step = area.Width / n;
		double f = DeckEase($"{r.X}|power", Math.Clamp(Val("gpu.power") / 450.0, 0.0, 1.0), t);
		int lit = (int)Math.Round(f * n);
		for (int i = 0; i < n; i++)
		{
			Color c = i < lit - 1 ? TileInk : i == lit - 1 ? TileOrange : Color.FromArgb(130, 130, 126);
			using var pen = new Pen(c, step * 0.45f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			float x = area.X + i * step + step / 2f;
			g.DrawLine(pen, x, area.Y + 4f, x, area.Bottom - 4f);
		}
		DeckText(g, "of 450 W", 20f, TileFade, r.X + 26f, r.Bottom - 40f, StringAlignment.Near, TileLabel);
	}

	/// <summary>
	/// Network download on a sage panel, with history as fine vertical lines and an arrow that turns with the trend.
	/// When something needs attention the panel warms to coral and its heading names the problem.
	/// </summary>
	private void StatusTile(Graphics g, RectangleF r, double t)
	{
		string? alert = Val("gpu.temp") >= 83f ? "GRAPHICS RUNNING HOT" : Val("cpu.load") >= 90f ? "PROCESSOR MAXED OUT" : Val("ram.load") >= 90f ? "MEMORY UNDER PRESSURE" : Val("gpu.load") >= 95f ? "GRAPHICS MAXED OUT" : null;
		float warm = (float)DeckEase($"{r.X}|alert", alert != null ? 1.0 : 0.0, t);
		Color face = Imaging.Mix(TileSage, TileCoral, warm);
		if (alert != null)
		{
			face = Imaging.Mix(face, Color.White, 0.06 * (0.5 + 0.5 * Math.Sin(t * 4.0)));
		}
		Color ink = Imaging.Mix(Color.FromArgb(34, 40, 30), Color.FromArgb(255, 240, 230), warm);
		Color fade = Imaging.Mix(Color.FromArgb(128, 134, 112), Color.FromArgb(246, 180, 156), warm);
		using (GraphicsPath p = Imaging.Round(r, 22f))
		using (var fill = new SolidBrush(face))
		{
			g.FillPath(fill, p);
		}
		// the "!" box and the heading
		using (var pen = new Pen(ink, 2f))
		{
			g.DrawRectangle(pen, r.X + 26f, r.Y + 24f, 22f, 22f);
		}
		DeckText(g, "!", 18f, ink, r.X + 37f, r.Y + 23f, StringAlignment.Center, TileFont);
		DeckText(g, alert ?? "ALL SYSTEMS NORMAL", 22f, ink, r.X + 62f, r.Y + 20f, StringAlignment.Near, TileLabel);
		// the arrow points up when traffic is rising, down when it is falling
		float[] h = _sensors.History("net.down");
		float down = Val("net.down"), before = h.Length > 10 ? h[^11] : down;
		double tilt = DeckEase($"{r.X}|arrow", down > before + 0.05f ? -45.0 : down < before - 0.05f ? 45.0 : 0.0, t);
		float ax = r.Right - 62f, ay = r.Y + 56f;
		using (var disc = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
		{
			g.FillEllipse(disc, ax - 36f, ay - 36f, 72f, 72f);
		}
		GraphicsState s = g.Save();
		g.TranslateTransform(ax, ay);
		g.RotateTransform((float)tilt);
		using (var arrow = new Pen(ink, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
		{
			g.DrawLine(arrow, -11f, 0f, 11f, 0f);
			g.DrawLines(arrow, new[] { new PointF(3f, -8f), new PointF(11f, 0f), new PointF(3f, 8f) });
		}
		g.Restore(s);
		Reading(g, Fmt("net.down", "0.0"), " MB/s", 88f, ink, fade, r.X + 20f, r.Y + 70f);
		DeckText(g, "NETWORK  ·  UP " + Fmt("net.up", "0.0") + " MB/s", 20f, ink, r.X + 26f, r.Bottom - 40f, StringAlignment.Near, TileLabel);
		// history as fine lines, the newest at the right
		const int n = 26;
		var lines = new RectangleF(r.Right - 250f, r.Y + 110f, 224f, 82f);
		float top = Math.Max(0.5f, h.Length > 0 ? h.Max() : 0f);
		float step = lines.Width / n;
		using var line = new Pen(ink, 1.6f);
		for (int j = 0; j < n; j++)
		{
			float v = j < h.Length ? h[^(j + 1)] : 0f;
			float hgt = Math.Max(6f, v / top * lines.Height);
			float x = lines.Right - j * step - step / 2f;
			g.DrawLine(line, x, lines.Bottom - hgt, x, lines.Bottom);
		}
	}
}
