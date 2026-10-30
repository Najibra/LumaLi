using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LcdStudio.Core;

/// <summary>
/// The dotted-dashboard pieces: dark cards with soft shadows on a warm gradient, LED dot-matrix readouts and
/// a status word, dotted rings, dotted bar charts, a dotted waveform, an ECG-style pulse trace, a dotted line
/// chart, pills and bullets. Grey dots carry the data and a red-orange accent marks what is newest or notable.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color DotGrey = Color.FromArgb(150, 150, 150);

	private static readonly Color DotDim = Color.FromArgb(58, 58, 58);

	private bool DrawDeer(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "card" when w.Kind == WidgetKind.Panel:
			Card(g, w, r);
			return true;
		case "status" when w.Kind == WidgetKind.Text:
			PixoText(g, w, r, t, StatusWord(w));
			return true;
		case "dotring" when w.Kind == WidgetKind.Ring:
			DotRing(g, w, r, t);
			return true;
		case "dotbars" when w.Kind == WidgetKind.Graph:
			DotBars(g, w, r);
			return true;
		case "wave" when w.Kind == WidgetKind.Graph:
			DotWave(g, w, r, t);
			return true;
		case "pulse" when w.Kind == WidgetKind.Graph:
			Pulse(g, w, r, t);
			return true;
		case "dotline" when w.Kind == WidgetKind.Graph:
			DotLine(g, w, r, t);
			return true;
		case "pill" when w.Kind == WidgetKind.Text:
			Pill(g, w, r, t);
			return true;
		case "bullet" when w.Kind == WidgetKind.Text:
			Bullet(g, w, r);
			return true;
		default:
			return DrawAstro(g, w, r, t);
		}
	}

	/// <summary>
	/// The warm studio backdrop: peach light from the top left falling off to warm grey and charcoal.
	/// Color1 is the light, Color2 the shadow and Accent the glow.
	/// </summary>
	private static void Warm(Graphics g, Color light, Color shadow, Color glow)
	{
		var full = new RectangleF(0f, 0f, 1920f, 480f);
		using (var wash = new LinearGradientBrush(full, light, shadow, 18f))
		{
			wash.InterpolationColors = new ColorBlend
			{
				Colors = new[] { light, Imaging.Mix(light, shadow, 0.55), shadow },
				Positions = new[] { 0f, 0.35f, 1f }
			};
			g.FillRectangle(wash, full);
		}
		using var spot = new GraphicsPath();
		spot.AddEllipse(-420f, -520f, 1300f, 1000f);
		using var brush = new PathGradientBrush(spot) { CenterColor = Color.FromArgb(120, glow), SurroundColors = new[] { Color.FromArgb(0, glow) } };
		g.FillPath(brush, spot);
	}

	/// <summary>A dark rounded card with a soft drop shadow, a faint lit top edge and an optional "01  TITLE" header.</summary>
	private void Card(Graphics g, Widget w, RectangleF r)
	{
		for (int i = 6; i >= 1; i--)
		{
			var shade = new RectangleF(r.X - i * 1.5f + 10f, r.Y + i * 2.5f + 6f, r.Width + i * 3f - 6f, r.Height + i * 2f - 6f);
			using GraphicsPath sp = Imaging.Round(shade, w.Radius + i * 2f);
			using var sb = new SolidBrush(Color.FromArgb(16, 0, 0, 0));
			g.FillPath(sb, sp);
		}
		using (GraphicsPath path = Imaging.Round(r, w.Radius))
		{
			using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Imaging.Hex(w.Color, Color.FromArgb(30, 30, 31)), Imaging.Hex(w.Color2, Color.FromArgb(22, 22, 23)), 90f))
			{
				g.FillPath(fill, path);
			}
			using var edge = new Pen(Color.FromArgb(26, 255, 255, 255), 1f);
			g.DrawPath(edge, path);
		}
		if (w.Text.Length > 0)
		{
			Text(g, w, new RectangleF(r.X + 22f, r.Y + 16f, r.Width - 44f, w.Size * 1.6f), Resolve(w.Text), Imaging.Hex(w.WarnColor, Color.FromArgb(215, 215, 215)), w.Size);
		}
	}

	/// <summary>A one-word state from the reading: Print holds "low|middle|high" words, split at 15 and 60.</summary>
	private string StatusWord(Widget w)
	{
		string[] words = w.Print.Split('|');
		float v = _sensors.Get(w.Sensor) ?? 0f;
		int i = v < 15f ? 0 : (v < 60f ? 1 : 2);
		return words[Math.Min(i, words.Length - 1)];
	}

	/// <summary>
	/// A ring of short radial ticks: lit ticks up to the reading in Color, the rest dim, and the last few lit
	/// ticks in the accent (Color2) with a slow pulse.
	/// </summary>
	private void DotRing(Graphics g, Widget w, RectangleF r, double t)
	{
		float side = Math.Min(r.Width, r.Height);
		float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, outer = side / 2f, inner = outer - Math.Max(6f, w.Thickness);
		const int ticks = 72;
		double f = Eased(w, Fraction(w), t);
		int lit = (int)Math.Round(f * ticks);
		Color on = Imaging.Hex(w.Color, Color.FromArgb(235, 235, 235));
		Color accent = Imaging.Hex(w.Color2, Color.FromArgb(255, 75, 43));
		int glow = (int)(190 + 65 * Math.Sin(t * 2.5));
		using var dim = new Pen(DotDim, 2f);
		using var bright = new Pen(on, 2f);
		using var hot = new Pen(Color.FromArgb(glow, accent), 2.4f);
		for (int i = 0; i < ticks; i++)
		{
			double a = -Math.PI / 2 + i * Math.PI * 2 / ticks;
			float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
			Pen pen = i >= lit ? dim : (i >= lit - 4 ? hot : bright);
			g.DrawLine(pen, cx + c * inner, cy + s * inner, cx + c * outer, cy + s * outer);
		}
	}

	private float Normal(Widget w, float v) => (float)Math.Clamp((v - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0);

	/// <summary>History as columns of dots (Thickness apart): grey, with the newest columns in the accent.</summary>
	private void DotBars(Graphics g, Widget w, RectangleF r)
	{
		float pitch = Math.Max(5f, w.Thickness);
		float dot = pitch * 0.62f;
		int cols = (int)(r.Width / (pitch * 1.6f));
		int rows = (int)(r.Height / pitch);
		float[] data = HistoryCopy(w.Sensor);
		Color accent = Imaging.Hex(w.Color2, Color.FromArgb(255, 75, 43));
		using var grey = new SolidBrush(Imaging.Hex(w.Color, DotGrey));
		using var dim = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
		using var red = new SolidBrush(accent);
		for (int c = 0; c < cols; c++)
		{
			int idx = data.Length - cols + c;
			float v = idx >= 0 ? data[idx] : w.Min;
			int h = Math.Max(1, (int)Math.Round(Normal(w, v) * rows));
			float x = r.Right - (cols - c) * pitch * 1.6f + pitch * 0.3f;
			bool newest = c >= cols - 3;
			for (int k = 0; k < rows; k++)
			{
				Brush b = k < h ? (newest ? red : grey) : (k == 0 ? dim : null);
				if (b != null)
				{
					g.FillEllipse(b, x, r.Bottom - (k + 1) * pitch, dot, dot);
				}
			}
		}
	}

	/// <summary>
	/// A dotted audio-style waveform across the card: columns of dots mirrored about a centre line, their
	/// heights from recent readings plus a gentle shimmer, with the middle stretch in the accent.
	/// </summary>
	private void DotWave(Graphics g, Widget w, RectangleF r, double t)
	{
		float pitch = Math.Max(4f, w.Thickness);
		float dot = pitch * 0.6f;
		int cols = (int)(r.Width / pitch);
		float mid = r.Y + r.Height / 2f;
		int half = (int)(r.Height / 2f / pitch);
		float[] data = HistoryCopy(w.Sensor);
		float level = data.Length > 0 ? Normal(w, data[data.Length - 1]) : 0f;
		Color accent = Imaging.Hex(w.Color2, Color.FromArgb(255, 75, 43));
		using var grey = new SolidBrush(Imaging.Hex(w.Color, Color.FromArgb(185, 185, 185)));
		using var red = new SolidBrush(accent);
		using var line = new SolidBrush(Color.FromArgb(120, 185, 185, 185));
		for (int c = 0; c < cols; c++)
		{
			double u = c / (double)(cols - 1);
			int idx = data.Length - cols + c;
			float v = idx >= 0 ? Normal(w, data[idx]) : 0f;
			double envelope = Math.Pow(Math.Sin(Math.PI * u), 1.5);
			double shimmer = 0.5 + 0.5 * Math.Sin(c * 1.7 + t * 6.0) * Math.Sin(c * 0.43 - t * 2.3);
			int h = (int)Math.Round(half * envelope * Math.Clamp(0.32 + 0.68 * (0.4 * v + 0.6 * level) + 0.28 * shimmer, 0.0, 1.0));
			float x = r.X + c * pitch;
			bool centre = u > 0.36 && u < 0.52;
			Brush b = centre ? red : grey;
			g.FillEllipse(h == 0 ? line : b, x, mid - dot / 2f, dot, dot);
			for (int k = 1; k <= h; k++)
			{
				g.FillEllipse(b, x, mid - k * pitch - dot / 2f, dot, dot);
				g.FillEllipse(b, x, mid + k * pitch - dot / 2f, dot, dot);
			}
		}
	}

	/// <summary>
	/// An ECG-style dotted trace scrolling left: a flat line with heartbeat spikes whose height follows the
	/// reading; the spike nearest the middle is in the accent.
	/// </summary>
	private void Pulse(Graphics g, Widget w, RectangleF r, double t)
	{
		float level = 0.55f + 0.45f * Normal(w, _sensors.Get(w.Sensor) ?? w.Min);
		float mid = r.Y + r.Height * 0.55f;
		float step = Math.Max(1.6f, w.Thickness * 0.3f);
		float dot = Math.Max(2.4f, w.Thickness * 0.5f);
		const float beat = 150f;
		float scroll = (float)(t * 60.0 % beat);
		Color accent = Imaging.Hex(w.Color2, Color.FromArgb(255, 75, 43));
		using var grey = new SolidBrush(Imaging.Hex(w.Color, Color.FromArgb(200, 200, 200)));
		using var red = new SolidBrush(accent);
		for (float x = r.X; x <= r.Right; x += step)
		{
			float local = (x - r.X + scroll) % beat;
			// a small wave, a sharp spike up, a deeper dip, then a gentle bump
			double y = 0;
			y += 0.10 * Gauss(local, 30f, 7f);
			y += 1.00 * Gauss(local, 62f, 4.5f);
			y -= 0.45 * Gauss(local, 71f, 4.5f);
			y += 0.18 * Gauss(local, 100f, 10f);
			float py = mid - (float)(y * r.Height * 0.62 * level);
			int beatIndex = (int)((x - r.X + scroll) / beat);
			float beatCentre = r.X - scroll + beatIndex * beat + 62f;
			bool hot = Math.Abs(beatCentre - (r.X + r.Width / 2f)) < beat / 2f && Math.Abs(local - 62f) < 16f;
			g.FillEllipse(hot ? red : grey, x - dot / 2f, py - dot / 2f, dot, dot);
		}
	}

	private static double Gauss(float x, float centre, float width) => Math.Exp(-Math.Pow((x - centre) / width, 2));

	/// <summary>History as a line of dots, with an accent ring on the newest reading.</summary>
	private void DotLine(Graphics g, Widget w, RectangleF r, double t)
	{
		float[] data = HistoryCopy(w.Sensor);
		if (data.Length < 2)
		{
			return;
		}
		float pitch = Math.Max(5f, w.Thickness);
		int n = Math.Min(data.Length, (int)(r.Width / pitch) + 1);
		float max = w.Max;
		if (w.Format == "auto")
		{
			// scale to the busiest moment on screen, so a quiet network still draws a shape
			max = w.Min + 0.001f;
			for (int i = data.Length - n; i < data.Length; i++)
			{
				max = Math.Max(max, data[i] * 1.15f);
			}
		}
		Color accent = Imaging.Hex(w.Color2, Color.FromArgb(255, 75, 43));
		using var grey = new SolidBrush(Imaging.Hex(w.Color, Color.FromArgb(205, 205, 205)));
		PointF last = PointF.Empty;
		for (int i = 0; i < n; i++)
		{
			float v = data[data.Length - n + i];
			float f = (float)Math.Clamp((v - w.Min) / Math.Max(0.001, max - w.Min), 0.0, 1.0);
			float x = r.Right - (n - 1 - i) * pitch;
			float y = r.Bottom - f * r.Height;
			g.FillEllipse(grey, x - 1.8f, y - 1.8f, 3.6f, 3.6f);
			last = new PointF(x, y);
		}
		float ring = (float)(7.0 + 1.5 * Math.Sin(t * 2.6));
		using var pen = new Pen(accent, 2f);
		g.DrawEllipse(pen, last.X - ring, last.Y - ring, ring * 2f, ring * 2f);
		using var core = new SolidBrush(accent);
		g.FillEllipse(core, last.X - 3f, last.Y - 3f, 6f, 6f);
	}

	/// <summary>A rounded outline pill around its text, with an accent dot after it that breathes when Print is "dot".</summary>
	private void Pill(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = Resolve(w.Text);
		Color color = Imaging.Hex(w.Color, Color.FromArgb(215, 215, 215));
		Color accent = Imaging.Hex(w.Color2, Color.FromArgb(255, 75, 43));
		using (GraphicsPath path = Imaging.Round(RectangleF.Inflate(r, -1f, -1f), r.Height / 2f))
		using (var pen = new Pen(Color.FromArgb(90, color), 1.4f))
		{
			g.DrawPath(pen, path);
		}
		Font font = GetFont(w, w.Size);
		SizeF size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
		bool dot = w.Print == "dot";
		float total = size.Width + (dot ? w.Size * 0.9f : 0f);
		float x = r.X + (r.Width - total) / 2f, y = r.Y + (r.Height - size.Height) / 2f;
		using (var b = new SolidBrush(color))
		{
			g.DrawString(text, font, b, x, y, StringFormat.GenericTypographic);
		}
		if (dot)
		{
			int alpha = (int)(170 + 85 * Math.Sin(t * 3.0));
			using var db = new SolidBrush(Color.FromArgb(alpha, accent));
			float d = w.Size * 0.45f;
			g.FillEllipse(db, x + size.Width + w.Size * 0.4f, r.Y + (r.Height - d) / 2f, d, d);
		}
	}

	/// <summary>A small dot in Color2 followed by the text in Color, like a chart legend.</summary>
	private void Bullet(Graphics g, Widget w, RectangleF r)
	{
		float d = w.Size * 0.55f;
		using (var dot = new SolidBrush(Imaging.Hex(w.Color2, Color.White)))
		{
			g.FillEllipse(dot, r.X, r.Y + (r.Height - d) / 2f, d, d);
		}
		Text(g, w, new RectangleF(r.X + d + w.Size * 0.6f, r.Y, r.Width - d - w.Size * 0.6f, r.Height), Resolve(w.Text));
	}
}
