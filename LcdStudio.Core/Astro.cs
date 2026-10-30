using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace LcdStudio.Core;

/// <summary>
/// The cyber-HUD pieces: a sci-fi targeting ring, wide "cyber display" lettering with a teal and coral offset
/// extrude and circuit cuts, outlined label tags with circuit leads, running chevrons, a ruler-tick history,
/// corner brackets, and a dark grid with drifting bokeh lights.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color HudCyan = Color.FromArgb(70, 200, 220);

	private static readonly Color HudYellow = Color.FromArgb(255, 212, 0);

	private static readonly Color HudCoral = Color.FromArgb(240, 90, 70);

	private readonly Dictionary<(int, int, string), (string Text, double Since)> _glitch = new Dictionary<(int, int, string), (string, double)>();

	private bool DrawAstro(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "hud" when w.Kind == WidgetKind.Ring:
			HudRing(g, w, r, t);
			return true;
		case "cyber" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			CyberText(g, w, r, t);
			return true;
		case "circuittag" when w.Kind == WidgetKind.Text:
			CircuitTag(g, w, r);
			return true;
		case "chevrons" when w.Kind == WidgetKind.Text:
			Chevrons(g, w, r, t);
			return true;
		case "ruler" when w.Kind == WidgetKind.Graph:
			Ruler(g, w, r);
			return true;
		case "brackets" when w.Kind == WidgetKind.Panel:
			Brackets(g, w, r);
			return true;
		default:
			return DrawMech(g, w, r, t);
		}
	}

	// ---------- background ----------

	/// <summary>A deep navy field with a faint grid and soft out-of-focus lights in teal, amber and white drifting slowly.</summary>
	private static void Hud(Graphics g, Color top, Color bottom, double t)
	{
		var full = new RectangleF(0f, 0f, 1920f, 480f);
		using (var wash = new LinearGradientBrush(full, top, bottom, 90f))
		{
			g.FillRectangle(wash, full);
		}
		using (var grid = new Pen(Color.FromArgb(12, 120, 200, 230), 1f))
		{
			for (int x = 0; x <= 1920; x += 40)
			{
				g.DrawLine(grid, x, 0, x, 480);
			}
			for (int y = 0; y <= 480; y += 40)
			{
				g.DrawLine(grid, 0, y, 1920, y);
			}
		}
		Color[] tones = { HudCyan, Color.FromArgb(230, 170, 60), Color.FromArgb(210, 225, 235) };
		for (int i = 0; i < 28; i++)
		{
			double hx = Hash01(i * 7 + 1), hy = Hash01(i * 13 + 5), hs = Hash01(i * 3 + 9);
			float x = (float)((hx * 1920 + t * (6 + 10 * hs)) % 1980 - 30);
			float y = (float)(hy * 480 + Math.Sin(t * 0.3 + i) * 8);
			float rad = (float)(6 + 22 * hs * hs);
			int alpha = (int)(35 + 45 * (0.5 + 0.5 * Math.Sin(t * (0.4 + hs) + i * 1.3)));
			using var path = new GraphicsPath();
			path.AddEllipse(x - rad, y - rad, rad * 2, rad * 2);
			using var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, tones[i % 3]), SurroundColors = new[] { Color.FromArgb(0, tones[i % 3]) } };
			g.FillPath(b, path);
		}
	}

	// ---------- the HUD ring ----------

	/// <summary>
	/// A targeting ring: ticks round the rim, a cyan circle with gaps, yellow arc segments that sweep slowly
	/// round, a yellow value arc on an inner track, white index dashes, a bracketed centre square with a
	/// flickering dot grid, drifting constellation lines, and the reading in cyber type with its label (Print).
	/// </summary>
	private void HudRing(Graphics g, Widget w, RectangleF r, double t)
	{
		float side = Math.Min(r.Width, r.Height);
		float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, R = side / 2f;
		RectangleF Circle(float radius) => new RectangleF(cx - radius, cy - radius, radius * 2f, radius * 2f);

		// rim ticks
		using (var tick = new Pen(Color.FromArgb(70, HudCyan), 1.2f))
		using (var major = new Pen(Color.FromArgb(150, HudCyan), 1.6f))
		{
			for (int i = 0; i < 120; i++)
			{
				double a = i * Math.PI * 2 / 120 + t * 0.02;
				bool big = i % 10 == 0;
				float r1 = R * 0.95f, r2 = R * (big ? 1f : 0.975f);
				g.DrawLine(big ? major : tick, cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1, cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
			}
		}
		// cyan circle with gaps
		using (var cyan = new Pen(Color.FromArgb(170, HudCyan), 1.8f))
		{
			for (int k = 0; k < 4; k++)
			{
				g.DrawArc(cyan, Circle(R * 0.9f), k * 90f + 8f, 74f);
			}
		}
		// sweeping yellow segments and their thin amber companions
		float spin = (float)(t * 9.0 % 360.0);
		using (var yellow = new Pen(HudYellow, Math.Max(4f, R * 0.035f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
		using (var amber = new Pen(Color.FromArgb(170, 230, 160, 40), 2f))
		{
			foreach (float start in new[] { 200f, 20f })
			{
				g.DrawArc(yellow, Circle(R * 0.82f), start + spin, 38f);
				g.DrawArc(amber, Circle(R * 0.86f), start + spin + 44f, 30f);
			}
			g.DrawArc(yellow, Circle(R * 0.82f), 110f - spin * 0.6f, 24f);
		}
		// the value arc on its track
		double f = Eased(w, Fraction(w), t);
		using (var track = new Pen(Color.FromArgb(45, HudCyan), 3f))
		{
			g.DrawEllipse(track, Circle(R * 0.72f));
		}
		if (f > 0.002)
		{
			bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
			using var value = new Pen(warn ? HudCoral : HudYellow, 4f);
			g.DrawArc(value, Circle(R * 0.72f), -90f, (float)(f * 360.0));
		}
		using (var inner = new Pen(Color.FromArgb(80, HudCyan), 1f))
		{
			g.DrawEllipse(inner, Circle(R * 0.6f));
		}
		// white index dashes
		using (var dash = new Pen(Color.FromArgb(220, 235, 240, 245), 3f))
		{
			for (int k = 0; k < 4; k++)
			{
				double a = k * Math.PI / 2 - Math.PI / 2;
				float d = R * 0.66f, len = R * (k % 2 == 0 ? 0.08f : 0.04f);
				float ux = (float)Math.Cos(a), uy = (float)Math.Sin(a), px = -uy, py = ux;
				g.DrawLine(dash, cx + ux * d - px * len, cy + uy * d - py * len, cx + ux * d + px * len, cy + uy * d + py * len);
			}
		}
		// constellation: nodes drifting inside the ring, joined by fine lines
		var nodes = new PointF[9];
		for (int i = 0; i < nodes.Length; i++)
		{
			double a = Hash01(i * 17 + 3) * Math.PI * 2 + Math.Sin(t * 0.15 + i) * 0.25;
			double d = R * (0.25 + 0.6 * Hash01(i * 29 + 11));
			nodes[i] = new PointF(cx + (float)(Math.Cos(a) * d), cy + (float)(Math.Sin(a) * d));
		}
		using (var link = new Pen(Color.FromArgb(40, 220, 230, 240), 1f))
		using (var node = new SolidBrush(Color.FromArgb(170, 220, 230, 240)))
		{
			for (int i = 0; i < nodes.Length; i++)
			{
				g.DrawLine(link, nodes[i], nodes[(i + 3) % nodes.Length]);
				g.FillEllipse(node, nodes[i].X - 2f, nodes[i].Y - 2f, 4f, 4f);
			}
		}
		// the bracketed centre square
		float half = R * 0.36f;
		var square = new RectangleF(cx - half, cy - half, half * 2f, half * 2f);
		using (var bracket = new Pen(Color.FromArgb(200, 230, 170, 60), 1.6f))
		{
			float l = half * 0.25f;
			foreach (var (px, py, sx, sy) in new[] { (square.Left, square.Top, 1, 1), (square.Right, square.Top, -1, 1), (square.Left, square.Bottom, 1, -1), (square.Right, square.Bottom, -1, -1) })
			{
				g.DrawLine(bracket, px, py, px + sx * l, py);
				g.DrawLine(bracket, px, py, px, py + sy * l);
			}
		}
		// a little flickering dot grid above the reading
		int frame = (int)(t * 2.0);
		float gs = R * 0.03f;
		using (var on = new SolidBrush(Color.FromArgb(220, 235, 240, 245)))
		using (var off = new SolidBrush(Color.FromArgb(50, 235, 240, 245)))
		{
			for (int gy = 0; gy < 3; gy++)
			{
				for (int gx = 0; gx < 7; gx++)
				{
					bool lit = Hash01(frame * 131 + gy * 7 + gx) > 0.45;
					g.FillRectangle(lit ? on : off, cx - 3.5f * gs * 1.6f + gx * gs * 1.6f, square.Top + half * 0.22f + gy * gs * 1.6f, gs, gs);
				}
			}
		}
		// the reading and its label
		string text = Formatted(w) + (Sensors.Def(w.Sensor).Unit == "%" ? "%" : "");
		DrawCyber(g, w, text, new RectangleF(square.X - half * 0.4f, cy - half * 0.28f, square.Width + half * 0.8f, w.Size * 1.1f), StringAlignment.Center, w.Size, 0f);
		if (w.Print.Length > 0)
		{
			Text(g, w, new RectangleF(square.X, square.Bottom - half * 0.42f, square.Width, w.Size * 0.5f), string.Join(" ", w.Print.ToCharArray()), Color.FromArgb(200, 120, 210, 225), w.Size * 0.24f);
		}
	}

	// ---------- cyber lettering ----------

	/// <summary>
	/// Wide square lettering: Color for the face, Color2 (teal) and Track (coral) for two offset extrude layers,
	/// and two thin cuts through the letters in the page colour (WarnColor) like circuit traces. Thickness is
	/// the horizontal stretch in percent. When a reading changes, the layers jump apart briefly like a glitch.
	/// </summary>
	private void CyberText(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = w.Kind == WidgetKind.Value ? Formatted(w) : Resolve(w.Text);
		string unit = w.Kind == WidgetKind.Value && w.Sensor != "time" ? (w.Text.Length > 0 ? w.Text : Sensors.Def(w.Sensor).Unit) : "";
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_glitch.TryGetValue(key, out var st) || st.Text != text)
		{
			_glitch[key] = st = (text, st.Text == null ? -10.0 : t);
		}
		double dt = t - st.Since;
		float glitch = dt >= 0 && dt < 0.45 ? (float)((1 - dt / 0.45) * (Hash01((int)(t * 30)) - 0.5) * w.Size * 0.18) : 0f;
		if (unit.Length > 0)
		{
			// the number at full size, the unit small and hanging from its top right
			SizeF numSize = MeasureCyber(w, text, w.Size);
			SizeF unitSize = MeasureCyber(w, unit, w.Size * 0.42f);
			float total = numSize.Width + w.Size * 0.08f + unitSize.Width;
			float x = Align(w.Align) switch { StringAlignment.Center => r.X + (r.Width - total) / 2f, StringAlignment.Far => r.Right - total, _ => r.X };
			DrawCyber(g, w, text, new RectangleF(x, r.Y, numSize.Width + 4f, r.Height), StringAlignment.Near, w.Size, glitch);
			DrawCyber(g, w, unit, new RectangleF(x + numSize.Width + w.Size * 0.08f, r.Y, unitSize.Width + 4f, r.Height * 0.62f), StringAlignment.Near, w.Size * 0.42f, glitch);
			return;
		}
		DrawCyber(g, w, text, r, Align(w.Align), w.Size, glitch);
	}

	private GraphicsPath CyberPath(Widget w, string text, float size)
	{
		var path = new GraphicsPath(FillMode.Winding);
		FontFamily family;
		try
		{
			family = new FontFamily(w.Font);
		}
		catch
		{
			family = new FontFamily("Bahnschrift");
		}
		using (family)
		{
			FontStyle style = w.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
			path.AddString(text.ToUpperInvariant(), family, (int)style, size, PointF.Empty, StringFormat.GenericTypographic);
		}
		float stretch = w.Thickness > 10f ? w.Thickness / 100f : 1.3f;
		using var m = new Matrix();
		m.Scale(stretch, 1f);
		path.Transform(m);
		return path;
	}

	private SizeF MeasureCyber(Widget w, string text, float size)
	{
		using GraphicsPath path = CyberPath(w, text, size);
		RectangleF b = path.GetBounds();
		return new SizeF(b.Right, size);
	}

	private void DrawCyber(Graphics g, Widget w, string text, RectangleF r, StringAlignment align, float size, float glitch)
	{
		using GraphicsPath path = CyberPath(w, text, size);
		RectangleF b = path.GetBounds();
		if (b.Width < 1f)
		{
			return;
		}
		float x = align switch { StringAlignment.Center => r.X + (r.Width - b.Right) / 2f, StringAlignment.Far => r.Right - b.Right, _ => r.X };
		float y = r.Y + (r.Height - b.Bottom - b.Top) / 2f;
		Color face = w.Kind == WidgetKind.Value ? ValueColor(w) : Imaging.Hex(w.Color, Color.White);
		Color teal = Imaging.Hex(w.Color2, Color.FromArgb(40, 210, 200));
		Color coral = Imaging.Hex(w.Track, HudCoral);
		Color page = Imaging.Hex(w.WarnColor, Color.FromArgb(6, 9, 14));
		if (w.Kind == WidgetKind.Value && w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt)
		{
			face = Imaging.Hex(w.Print.StartsWith("#") ? w.Print : "#ff5a46", Color.Red);
		}
		float depth = Math.Max(2f, size * 0.045f);
		void Layer(Color c, float dx, float dy)
		{
			GraphicsState s = g.Save();
			g.TranslateTransform(x + dx, y + dy);
			using var b2 = new SolidBrush(c);
			g.FillPath(b2, path);
			g.Restore(s);
		}
		Layer(coral, depth * 1.6f + glitch, depth * 1.2f);
		Layer(teal, depth * 0.8f - glitch * 0.6f, depth * 0.6f);
		Layer(face, 0f, 0f);
		// the circuit cuts across the face, in the page colour
		using var cut = new Pen(page, Math.Max(1.2f, size * 0.035f));
		float h = b.Bottom - b.Top;
		g.DrawLine(cut, x + b.Left, y + b.Top + h * 0.4f, x + b.Left + b.Width * 0.46f, y + b.Top + h * 0.4f);
		g.DrawLine(cut, x + b.Left + b.Width * 0.58f, y + b.Top + h * 0.66f, x + b.Right, y + b.Top + h * 0.66f);
	}

	// ---------- tags, chevrons, ruler and brackets ----------

	/// <summary>A small outlined label in letter-spaced caps (Color), with a circuit lead of Thickness px ending in a ring.</summary>
	private void CircuitTag(Graphics g, Widget w, RectangleF r)
	{
		Color c = Imaging.Hex(w.Color, HudCyan);
		string text = string.Join(" ", Resolve(w.Text).ToUpperInvariant().ToCharArray());
		Font font = GetFont(w, w.Size);
		SizeF size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
		var box = new RectangleF(r.X, r.Y + (r.Height - w.Size * 1.8f) / 2f, size.Width + w.Size * 1.4f, w.Size * 1.8f);
		using (var pen = new Pen(c, 1.4f))
		{
			g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
			if (w.Thickness > 0f)
			{
				float ly = box.Y + box.Height / 2f;
				g.DrawLine(pen, box.Right, ly, box.Right + w.Thickness, ly);
				g.DrawEllipse(pen, box.Right + w.Thickness, ly - 3.5f, 7f, 7f);
			}
		}
		using var b = new SolidBrush(c);
		g.DrawString(text, font, b, box.X + w.Size * 0.7f, box.Y + (box.Height - size.Height) / 2f, StringFormat.GenericTypographic);
	}

	/// <summary>A run of chevrons fading from coral to teal with a light running along them, then a letter-spaced label.</summary>
	private void Chevrons(Graphics g, Widget w, RectangleF r, double t)
	{
		const int count = 12;
		float h = r.Height * 0.55f, step = h * 0.55f;
		float y = r.Y + (r.Height - h) / 2f;
		double run = t * 6.0 % (count + 6);
		for (int i = 0; i < count; i++)
		{
			Color c = Imaging.Mix(HudCoral, HudCyan, i / (double)(count - 1));
			int alpha = (int)(110 + 145 * Math.Max(0.0, 1.0 - Math.Abs(i - run) / 2.5));
			using var pen = new Pen(Color.FromArgb(Math.Min(255, alpha), c), Math.Max(2f, h * 0.18f));
			float x = r.X + i * step;
			g.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + h * 0.4f, y + h / 2f), new PointF(x, y + h) });
		}
		string text = string.Join(" ", Resolve(w.Text).ToUpperInvariant().ToCharArray());
		Text(g, w, new RectangleF(r.X + count * step + h, r.Y, r.Width - count * step - h, r.Height), text);
	}

	/// <summary>
	/// History as a ruler: a coral baseline with a tick per reading, every fifth one longer, each tick's height
	/// set by its reading; the newest tick and the highest on screen are drawn in white.
	/// </summary>
	private void Ruler(Graphics g, Widget w, RectangleF r)
	{
		float[] data = HistoryCopy(w.Sensor);
		Color coral = Imaging.Hex(w.Color, HudCoral);
		float pitch = Math.Max(4f, w.Thickness);
		int n = (int)(r.Width / pitch);
		int peak = -1;
		float peakValue = float.MinValue;
		for (int i = 0; i < n; i++)
		{
			int idx = data.Length - n + i;
			if (idx >= 0 && data[idx] > peakValue)
			{
				peakValue = data[idx];
				peak = i;
			}
		}
		SmoothingMode mode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.None;
		using (var base0 = new Pen(coral, 2f))
		{
			g.DrawLine(base0, r.X, r.Bottom, r.Right, r.Bottom);
		}
		using var pen = new Pen(coral, 2f);
		using var white = new Pen(Color.FromArgb(240, 240, 245), 2f);
		for (int i = 0; i < n; i++)
		{
			int idx = data.Length - n + i;
			float f = idx >= 0 ? (float)Math.Clamp((data[idx] - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0) : 0f;
			float minimum = i % 5 == 0 ? 0.22f : 0.1f;
			float h = r.Height * Math.Max(minimum, f);
			float x = (float)Math.Round(r.X + i * pitch);
			g.DrawLine(i == n - 1 || i == peak ? white : pen, x, r.Bottom, x, r.Bottom - h);
		}
		g.SmoothingMode = mode;
	}

	/// <summary>Four corner brackets (Color, arms Thickness px long) framing the widget box.</summary>
	private static void Brackets(Graphics g, Widget w, RectangleF r)
	{
		using var pen = new Pen(Imaging.Hex(w.Color, HudCoral), 2f);
		float l = Math.Max(8f, w.Thickness);
		g.DrawLines(pen, new[] { new PointF(r.Left, r.Top + l), new PointF(r.Left, r.Top), new PointF(r.Left + l, r.Top) });
		g.DrawLines(pen, new[] { new PointF(r.Right - l, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + l) });
		g.DrawLines(pen, new[] { new PointF(r.Left, r.Bottom - l), new PointF(r.Left, r.Bottom), new PointF(r.Left + l, r.Bottom) });
		g.DrawLines(pen, new[] { new PointF(r.Right - l, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - l) });
	}
}
