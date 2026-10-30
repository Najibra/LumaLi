using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// The music-player look: a fluted-glass background of drifting warm colour seen through vertical ribs, a 3D
/// carousel of glass cards (one per reading, each with a colourful cover) that turns every few seconds, and a
/// glass player bar with a "now playing" chip for the time.
/// </summary>
public sealed partial class ScreenRenderer
{
	private Bitmap? _flutedField;

	private bool DrawCarousel(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "carousel" when w.Kind == WidgetKind.Panel:
			CardCarousel(g, w, r, t);
			return true;
		case "player" when w.Kind == WidgetKind.Panel:
			PlayerBar(g, w, r, t);
			return true;
		default:
			return DrawPaper(g, w, r, t);
		}
	}

	// ---------- fluted glass ----------

	/// <summary>
	/// Soft blobs of amber, orange, navy, white and deep red drifting behind reeded glass: each vertical rib
	/// squeezes the colour behind it and is lit on one edge and shaded on the other.
	/// </summary>
	private void Fluted(Graphics g, double t)
	{
		// the colour drifts slowly, so the glass is recomposed four times a second and copied in between
		if (_flutedFull != null && Math.Abs(t - _flutedTime) < 0.25)
		{
			CompositingMode copy = g.CompositingMode;
			g.CompositingMode = CompositingMode.SourceCopy;
			g.DrawImageUnscaled(_flutedFull, 0, 0);
			g.CompositingMode = copy;
			return;
		}
		_flutedTime = t;
		_flutedFull ??= new Bitmap(1920, 480, PixelFormat.Format32bppArgb);
		using (Graphics full = Graphics.FromImage(_flutedFull))
		{
			ComposeFluted(full, t);
		}
		g.DrawImageUnscaled(_flutedFull, 0, 0);
	}

	private Bitmap? _flutedFull;

	private double _flutedTime = double.MinValue;

	private void ComposeFluted(Graphics g, double t)
	{
		const int fw = 480, fh = 120;
		_flutedField ??= new Bitmap(fw, fh, PixelFormat.Format32bppPArgb);
		using (Graphics fg = Graphics.FromImage(_flutedField))
		{
			fg.SmoothingMode = SmoothingMode.AntiAlias;
			using (var wash = new LinearGradientBrush(new Rectangle(0, 0, fw, fh), Color.FromArgb(232, 150, 52), Color.FromArgb(214, 84, 20), 90f))
			{
				fg.FillRectangle(wash, 0, 0, fw, fh);
			}
			(Color C, double X, double Y, double R, double Speed)[] blobs =
			{
				(Color.FromArgb(31, 40, 56), 0.28, 0.55, 0.42, 0.11),
				(Color.FromArgb(236, 236, 240), 0.62, 0.42, 0.34, 0.08),
				(Color.FromArgb(150, 30, 16), 0.72, 1.0, 0.45, 0.06),
				(Color.FromArgb(248, 176, 72), 0.12, 0.05, 0.4, 0.09),
				(Color.FromArgb(242, 106, 27), 0.92, 0.3, 0.38, 0.07)
			};
			for (int i = 0; i < blobs.Length; i++)
			{
				var b = blobs[i];
				float cx = (float)((b.X + 0.08 * Math.Sin(t * b.Speed + i * 1.7)) * fw);
				float cy = (float)((b.Y + 0.12 * Math.Cos(t * b.Speed * 1.3 + i)) * fh);
				float rad = (float)(b.R * fw * (0.9 + 0.1 * Math.Sin(t * 0.2 + i)));
				using var path = new GraphicsPath();
				path.AddEllipse(cx - rad, cy - rad * 0.55f, rad * 2f, rad * 1.1f);
				using var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(230, b.C), SurroundColors = new[] { Color.FromArgb(0, b.C) } };
				fg.FillPath(pb, path);
			}
		}
		// the ribs are composed at half size (the glass is soft anyway) and scaled up in one draw
		const float rib = 13f;
		_flutedFrame ??= new Bitmap(960, 240, PixelFormat.Format32bppPArgb);
		using (Graphics hg = Graphics.FromImage(_flutedFrame))
		{
			hg.InterpolationMode = InterpolationMode.Bilinear;
			hg.PixelOffsetMode = PixelOffsetMode.HighQuality;
			for (float x = 0f; x < 960f; x += rib)
			{
				// each rib shows a slightly wider strip of what is behind it, which reads as refraction
				float sx = x / 2f - rib / 4f * 0.6f;
				hg.DrawImage(_flutedField, new RectangleF(x, 0f, rib + 0.5f, 240f), new RectangleF(Math.Max(0f, sx), 0f, rib / 2f * 1.6f, fh), GraphicsUnit.Pixel);
			}
			using var shade = new LinearGradientBrush(new RectangleF(0f, 0f, rib, 240f), Color.FromArgb(46, 255, 255, 255), Color.FromArgb(60, 0, 0, 0), 0f)
			{
				WrapMode = WrapMode.Tile
			};
			shade.InterpolationColors = new ColorBlend
			{
				Colors = new[] { Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(60, 0, 0, 0) },
				Positions = new[] { 0f, 0.3f, 0.65f, 1f }
			};
			hg.FillRectangle(shade, 0f, 0f, 960f, 240f);
		}
		InterpolationMode mode = g.InterpolationMode;
		g.InterpolationMode = InterpolationMode.Bilinear;
		g.DrawImage(_flutedFrame, new RectangleF(-1f, -1f, 1922f, 482f));
		g.InterpolationMode = mode;
	}

	private Bitmap? _flutedFrame;

	// ---------- the card carousel ----------

	private readonly struct CardItem
	{
		public readonly string Sensor, Title, Format;
		public readonly Color A, B;

		public CardItem(string sensor, string title, string format, Color a, Color b)
		{
			Sensor = sensor;
			Title = title;
			Format = format;
			A = a;
			B = b;
		}
	}

	/// <summary>
	/// Glass cards in a turning carousel. Text lists the cards as "sensor|Title|#colour|#colour" separated by
	/// ';'. Every Thickness seconds (6 by default) the next card glides to the centre: side cards shrink, tilt,
	/// darken and slide off as new ones come in from the edges.
	/// </summary>
	private void CardCarousel(Graphics g, Widget w, RectangleF r, double t)
	{
		CardItem[] items = w.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select((string s) =>
		{
			string[] p = s.Split('|');
			string sensor = p[0].Trim();
			string format = sensor.StartsWith("net.") ? "0.0" : "0";
			return new CardItem(sensor, p.Length > 1 ? p[1] : sensor, format, Imaging.Hex(p.Length > 2 ? p[2] : "", Color.Orange), Imaging.Hex(p.Length > 3 ? p[3] : "", Color.Brown));
		}).ToArray();
		if (items.Length == 0)
		{
			return;
		}
		double period = w.Thickness > 1f ? w.Thickness : 6.0;
		double step = t / period;
		double whole = Math.Floor(step), frac = step - whole;
		// hold still most of the period, then glide over its last 0.9 s
		double glide = Math.Clamp((frac * period - (period - 0.9)) / 0.9, 0.0, 1.0);
		glide = glide * glide * (3.0 - 2.0 * glide);
		double position = whole + glide;
		float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
		float cardW = r.Height * 0.86f, cardH = r.Height * 0.96f;
		// the soft shadow under the centre card
		using (var shadowPath = new GraphicsPath())
		{
			shadowPath.AddEllipse(cx - cardW * 0.75f, cy + cardH * 0.18f, cardW * 1.5f, cardH * 0.5f);
			using var sb = new PathGradientBrush(shadowPath) { CenterColor = Color.FromArgb(140, 20, 8, 4), SurroundColors = new[] { Color.FromArgb(0, 20, 8, 4) } };
			g.FillPath(sb, shadowPath);
		}
		int n = items.Length;
		var order = Enumerable.Range(0, n).Select((int k) =>
		{
			double rel = k - position % n;
			if (rel > n / 2.0)
			{
				rel -= n;
			}
			if (rel < -n / 2.0)
			{
				rel += n;
			}
			return (k, rel);
		}).Where(((int k, double rel) e) => Math.Abs(e.rel) < 3.2).OrderByDescending(((int k, double rel) e) => Math.Abs(e.rel)).ToList();
		foreach (var (k, rel) in order)
		{
			double a = Math.Abs(rel);
			float offset = (float)(Interp(a, new[] { 0.0, 1.0, 2.0, 3.0 }, new[] { 0.0, cardW * 1.05, cardW * 1.9, cardW * 3.4 }) * Math.Sign(rel));
			float scale = (float)Interp(a, new[] { 0.0, 1.0, 2.0, 3.0 }, new[] { 1.0, 0.82, 0.68, 0.6 });
			float shear = (float)(0.07 * Math.Clamp(rel, -1.5, 1.5));
			float dim = (float)Interp(a, new[] { 0.0, 1.0, 2.0, 3.0 }, new[] { 0.0, 0.25, 0.45, 0.6 });
			GraphicsState state = g.Save();
			using (var m = new Matrix())
			{
				m.Translate(cx + offset, cy);
				m.Shear(0f, shear);
				m.Scale(scale, scale);
				m.Translate(-cardW / 2f, -cardH / 2f);
				g.MultiplyTransform(m);
			}
			// the card's content only changes with the readings, so it is drawn once per update and reused
			g.DrawImage(CardImage(w, items[k], k, cardW, cardH), 0f, 0f, cardW, cardH);
			if (a < 0.5)
			{
				LiveChip(g, w, cardW, cardH, t);
			}
			if (dim > 0.01f)
			{
				using GraphicsPath veilPath = Imaging.Round(new RectangleF(0f, 0f, cardW, cardH), cardW * 0.075f);
				using var veil = new SolidBrush(Color.FromArgb((int)(dim * 255), 40, 22, 12));
				g.FillPath(veil, veilPath);
			}
			g.Restore(state);
		}
	}

	private static double Interp(double x, double[] xs, double[] ys)
	{
		if (x <= xs[0])
		{
			return ys[0];
		}
		for (int i = 1; i < xs.Length; i++)
		{
			if (x <= xs[i])
			{
				double f = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
				return ys[i - 1] + (ys[i] - ys[i - 1]) * f;
			}
		}
		return ys[ys.Length - 1];
	}

	private readonly System.Collections.Generic.Dictionary<int, (Bitmap Image, int Version)> _cards = new System.Collections.Generic.Dictionary<int, (Bitmap, int)>();

	private Bitmap CardImage(Widget w, CardItem item, int index, float cw, float ch)
	{
		int version = _sensors.Version;
		if (_cards.TryGetValue(index, out var hit) && hit.Version == version && hit.Image.Width == (int)Math.Ceiling(cw))
		{
			return hit.Image;
		}
		hit.Image?.Dispose();
		var bmp = new Bitmap((int)Math.Ceiling(cw), (int)Math.Ceiling(ch), PixelFormat.Format32bppPArgb);
		using (Graphics cg = Graphics.FromImage(bmp))
		{
			Setup(cg);
			DrawCard(cg, w, item, cw, ch);
		}
		_cards[index] = (bmp, version);
		return bmp;
	}

	/// <summary>One glass card in its own coordinates: a gradient cover with the live value and sparkline, then the title and peak/average.</summary>
	private void DrawCard(Graphics g, Widget w, CardItem item, float cw, float ch)
	{
		var card = new RectangleF(0f, 0f, cw, ch);
		using (GraphicsPath path = Imaging.Round(card, cw * 0.075f))
		{
			using (var glass = new LinearGradientBrush(new RectangleF(0f, -1f, cw, ch + 2f), Color.FromArgb(110, 120, 104, 96), Color.FromArgb(200, 52, 46, 48), 90f))
			{
				g.FillPath(glass, path);
			}
			using var edge = new Pen(Color.FromArgb(120, 255, 236, 214), 1.6f);
			g.DrawPath(edge, path);
		}
		float pad = cw * 0.035f;
		var cover = new RectangleF(pad, pad, cw - pad * 2f, ch * 0.66f);
		using (GraphicsPath cp = Imaging.Round(cover, cw * 0.055f))
		{
			using (var cb = new LinearGradientBrush(cover, item.A, item.B, 55f))
			{
				g.FillPath(cb, cp);
			}
			GraphicsState s = g.Save();
			g.SetClip(cp, CombineMode.Intersect);
			// a soft light spot and the reading's recent history across the bottom of the cover
			using (var spot = new GraphicsPath())
			{
				spot.AddEllipse(cover.X - cover.Width * 0.2f, cover.Y - cover.Height * 0.4f, cover.Width * 0.9f, cover.Height * 0.9f);
				using var sb = new PathGradientBrush(spot) { CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } };
				g.FillPath(sb, spot);
			}
			float[] data = HistoryCopy(item.Sensor);
			SensorDef def = Sensors.Def(item.Sensor);
			if (data.Length >= 2)
			{
				int count = Math.Min(data.Length, 60);
				float max = item.Sensor.StartsWith("net.") ? Math.Max(1f, data.Skip(data.Length - count).Max() * 1.2f) : def.Max;
				var pts = new PointF[count];
				for (int i = 0; i < count; i++)
				{
					float f = Math.Clamp((data[data.Length - count + i] - def.Min) / Math.Max(0.001f, max - def.Min), 0f, 1f);
					pts[i] = new PointF(cover.X + cover.Width * i / (count - 1), cover.Bottom - cover.Height * 0.08f - f * cover.Height * 0.35f);
				}
				using var area = new GraphicsPath();
				area.AddLines(pts);
				area.AddLine(cover.Right, cover.Bottom, cover.X, cover.Bottom);
				using (var fill = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
				{
					g.FillPath(fill, area);
				}
				using var line = new Pen(Color.FromArgb(200, 255, 255, 255), 2f);
				g.DrawLines(line, pts);
			}
			g.Restore(s);
		}
		// the live value on the cover
		float? v = _sensors.Get(item.Sensor);
		SensorDef d = Sensors.Def(item.Sensor);
		string value = v.HasValue ? v.Value.ToString(item.Format, CultureInfo.InvariantCulture) : "--";
		string unit = d.Unit == "°C" ? "°" : d.Unit;
		Font big = GetFont(w, cw * 0.27f);
		Font unitFont = GetFont(w, cw * 0.1f);
		SizeF vs = g.MeasureString(value, big, PointF.Empty, StringFormat.GenericTypographic);
		SizeF us = g.MeasureString(unit, unitFont, PointF.Empty, StringFormat.GenericTypographic);
		float vx = cover.X + (cover.Width - vs.Width - us.Width - 4f) / 2f, vy = cover.Y + cover.Height * 0.12f;
		using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
		{
			g.DrawString(value, big, shadow, vx + 2f, vy + 3f, StringFormat.GenericTypographic);
		}
		using (var white = new SolidBrush(Color.White))
		{
			g.DrawString(value, big, white, vx, vy, StringFormat.GenericTypographic);
			g.DrawString(unit, unitFont, white, vx + vs.Width + 4f, vy + cw * 0.04f, StringFormat.GenericTypographic);
		}
		// title and peak / average under the cover
		float[] hist = HistoryCopy(item.Sensor);
		string sub = hist.Length > 0
			? $"peak {hist.Max().ToString(item.Format, CultureInfo.InvariantCulture)}{unit}  ·  avg {hist.Average().ToString(item.Format, CultureInfo.InvariantCulture)}{unit}"
			: "waiting for readings";
		using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
		using (var titleBrush = new SolidBrush(Color.White))
		using (var subBrush = new SolidBrush(Color.FromArgb(185, 255, 240, 228)))
		{
			g.DrawString(item.Title, GetFont(w, cw * 0.085f), titleBrush, new RectangleF(0f, cover.Bottom + ch * 0.045f, cw, cw * 0.12f), fmt);
			g.DrawString(sub, GetFont(w, cw * 0.052f), subBrush, new RectangleF(0f, cover.Bottom + ch * 0.045f + cw * 0.11f, cw, cw * 0.08f), fmt);
		}
	}

	/// <summary>A small glass "live" chip on the centre card's cover, its dot breathing.</summary>
	private void LiveChip(Graphics g, Widget w, float cw, float ch, double t)
	{
		float pad = cw * 0.035f;
		var cover = new RectangleF(pad, pad, cw - pad * 2f, ch * 0.66f);
		Font chipFont = GetFont(w, cw * 0.05f);
		var chip = new RectangleF(cover.Right - cw * 0.26f, cover.Y + cover.Height * 0.06f, cw * 0.22f, cw * 0.075f);
		using (GraphicsPath chp = Imaging.Round(chip, chip.Height / 2f))
		using (var cb2 = new SolidBrush(Color.FromArgb(120, 40, 30, 28)))
		{
			g.FillPath(cb2, chp);
		}
		using (var dot = new SolidBrush(Color.FromArgb((int)(160 + 90 * Math.Sin(t * 3.0)), 255, 90, 60)))
		{
			g.FillEllipse(dot, chip.X + chip.Height * 0.35f, chip.Y + chip.Height * 0.3f, chip.Height * 0.4f, chip.Height * 0.4f);
		}
		using var ct = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
		g.DrawString("LIVE", chipFont, ct, chip.X + chip.Height * 0.95f, chip.Y + (chip.Height - chipFont.Size * 1.2f) / 2f, StringFormat.GenericTypographic);
	}

	// ---------- the player bar ----------

	/// <summary>
	/// A frosted glass pill with playback symbols, a dark "now playing" chip (a small cover, LumaLi, the
	/// time and date, a waveform that moves with the processor, and a bar filling over each minute), and
	/// output, mixer and volume symbols.
	/// </summary>
	private void PlayerBar(Graphics g, Widget w, RectangleF r, double t)
	{
		using (GraphicsPath pill = Imaging.Round(r, r.Height / 2f))
		{
			using (var glass = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Color.FromArgb(70, 255, 236, 210), Color.FromArgb(40, 120, 70, 40), 90f))
			{
				g.FillPath(glass, pill);
			}
			using var edge = new Pen(Color.FromArgb(130, 255, 236, 210), 1.6f);
			g.DrawPath(edge, pill);
		}
		Color icon = Color.FromArgb(215, 255, 244, 232);
		using var ib = new SolidBrush(icon);
		using var ip = new Pen(icon, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
		float h = r.Height, cy = r.Y + h / 2f, s = h * 0.2f;
		// rewind, pause, forward
		float x = r.X + h * 0.9f;
		g.FillPolygon(ib, new[] { new PointF(x, cy), new PointF(x + s, cy - s), new PointF(x + s, cy + s) });
		g.FillPolygon(ib, new[] { new PointF(x + s, cy), new PointF(x + s * 2f, cy - s), new PointF(x + s * 2f, cy + s) });
		x += h * 1.15f;
		g.FillRectangle(ib, x, cy - s, s * 0.6f, s * 2f);
		g.FillRectangle(ib, x + s, cy - s, s * 0.6f, s * 2f);
		x += h * 1.0f;
		g.FillPolygon(ib, new[] { new PointF(x + s, cy), new PointF(x, cy - s), new PointF(x, cy + s) });
		g.FillPolygon(ib, new[] { new PointF(x + s * 2f, cy), new PointF(x + s, cy - s), new PointF(x + s, cy + s) });
		// the now-playing chip
		var chip = new RectangleF(r.X + r.Width * 0.31f, r.Y + h * 0.14f, r.Width * 0.38f, h * 0.72f);
		using (GraphicsPath cp = Imaging.Round(chip, h * 0.16f))
		using (var cb = new SolidBrush(Color.FromArgb(225, 36, 24, 20)))
		{
			g.FillPath(cb, cp);
		}
		var art = new RectangleF(chip.X + h * 0.08f, chip.Y + h * 0.07f, chip.Height - h * 0.2f, chip.Height - h * 0.2f);
		using (GraphicsPath ap = Imaging.Round(art, 6f))
		using (var ab = new LinearGradientBrush(art, Color.FromArgb(255, 160, 60), Color.FromArgb(150, 30, 16), 50f))
		{
			g.FillPath(ab, ap);
		}
		Font title = GetFont(w, h * 0.22f), sub = GetFont(w, h * 0.17f);
		using (var tb = new SolidBrush(Color.White))
		using (var sb = new SolidBrush(Color.FromArgb(185, 255, 240, 228)))
		{
			g.DrawString("LumaLi", title, tb, art.Right + h * 0.18f, chip.Y + h * 0.08f, StringFormat.GenericTypographic);
			g.DrawString(Resolve("{time:HH:mm}  ·  {date:dddd d MMMM}"), sub, sb, art.Right + h * 0.18f, chip.Y + h * 0.36f, StringFormat.GenericTypographic);
		}
		// waveform bars that dance with the processor, and the "more" dots
		float level = (_sensors.Get("cpu.load") ?? 0f) / 100f;
		float wx = chip.Right - h * 1.05f;
		using (var wave = new Pen(Color.FromArgb(200, 255, 240, 228), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
		{
			for (int i = 0; i < 7; i++)
			{
				double a = 0.25 + 0.75 * Math.Abs(Math.Sin(t * (3.0 + i * 0.7) + i)) * (0.35 + 0.65 * level);
				float bh = (float)(h * 0.28 * a);
				g.DrawLine(wave, wx + i * 5f, cy - bh, wx + i * 5f, cy + bh);
			}
		}
		for (int i = 0; i < 3; i++)
		{
			g.FillEllipse(ib, chip.Right - h * 0.42f + i * 7f, cy - 2f, 4f, 4f);
		}
		// the progress bar fills over each minute
		double sec = DateTime.Now.Second + DateTime.Now.Millisecond / 1000.0;
		using (var track = new Pen(Color.FromArgb(60, 255, 255, 255), 3f))
		using (var done = new Pen(Color.FromArgb(230, 255, 255, 255), 3f))
		{
			float y = chip.Bottom - 3f, x0 = chip.X + h * 0.12f, x1 = chip.Right - h * 0.12f;
			g.DrawLine(track, x0, y, x1, y);
			g.DrawLine(done, x0, y, x0 + (float)((x1 - x0) * sec / 60.0), y);
		}
		// output, mixer and volume
		x = r.Right - h * 3.2f;
		g.DrawArc(ip, x - s * 1.1f, cy - s * 1.1f, s * 2.2f, s * 2.2f, 200f, 140f);
		g.DrawArc(ip, x - s * 0.6f, cy - s * 0.6f, s * 1.2f, s * 1.2f, 200f, 140f);
		g.FillPolygon(ib, new[] { new PointF(x, cy + s * 0.2f), new PointF(x - s * 0.5f, cy + s), new PointF(x + s * 0.5f, cy + s) });
		x += h * 1.1f;
		g.DrawLine(ip, x - s, cy - s * 0.45f, x + s, cy - s * 0.45f);
		g.DrawLine(ip, x - s, cy + s * 0.45f, x + s, cy + s * 0.45f);
		g.FillEllipse(ib, x + s * 0.2f, cy - s * 0.75f, s * 0.6f, s * 0.6f);
		g.FillEllipse(ib, x - s * 0.8f, cy + s * 0.15f, s * 0.6f, s * 0.6f);
		x += h * 1.0f;
		g.FillPolygon(ib, new[] { new PointF(x - s, cy - s * 0.4f), new PointF(x - s * 0.4f, cy - s * 0.4f), new PointF(x + s * 0.2f, cy - s), new PointF(x + s * 0.2f, cy + s), new PointF(x - s * 0.4f, cy + s * 0.4f), new PointF(x - s, cy + s * 0.4f) });
		g.DrawArc(ip, x - s * 0.3f, cy - s * 0.7f, s * 1.4f, s * 1.4f, -50f, 100f);
	}
}
