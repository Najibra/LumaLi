using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LcdStudio.Core;

/// <summary>
/// The newsprint collage: a white page scattered with torn newspaper clippings, a great sweep of red and navy
/// hatched ink cells crossing it like a brushed poster, readings printed on torn clippings or as big
/// engraved press type that a roller prints anew on every change, and a scrolling news ticker.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly string[] NewsWords =
	{
		"EDITION", "REPORT", "LATE NEWS", "SIGNAL", "FRONT PAGE", "DAILY", "BULLETIN", "EXTRA", "PRESS", "HEADLINE",
		"WIRE", "SPECIAL", "MORNING", "CITY", "VOLUME", "INDEX", "TODAY", "EXCLUSIVE", "WEEKEND", "FEATURE", "OPINION"
	};

	private static readonly StringFormat TickerFormat = MakeTickerFormat();

	private static StringFormat MakeTickerFormat()
	{
		var f = (StringFormat)StringFormat.GenericTypographic.Clone();
		f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
		return f;
	}

	private bool DrawHeadline(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "clipping" when w.Kind == WidgetKind.Text:
			Clipping(g, w, r, t);
			return true;
		case "presstype" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			PressType(g, w, r, t);
			return true;
		case "ticker" when w.Kind == WidgetKind.Text:
			Ticker(g, w, r, t);
			return true;
		default:
			return DrawReeded(g, w, r, t);
		}
	}

	private static FontFamily Family(string name)
	{
		try
		{
			return new FontFamily(name);
		}
		catch
		{
			return new FontFamily("Segoe UI");
		}
	}

	// ---------- the newsprint page ----------

	private Bitmap? _news;

	private string _newsKey = "";

	/// <summary>The collage page: paper (Color1), the ink sweep in red (Color2) and navy (Accent). Composed once.</summary>
	private void Newsprint(Graphics g, Color paper, Color red, Color navy)
	{
		string key = $"{paper.ToArgb()}|{red.ToArgb()}|{navy.ToArgb()}";
		if (_news == null || _newsKey != key)
		{
			_news?.Dispose();
			_news = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
			_newsKey = key;
			using Graphics ng = Graphics.FromImage(_news);
			Setup(ng);
			ComposeNewsprint(ng, paper, red, navy);
		}
		CompositingMode copy = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_news, 0, 0);
		g.CompositingMode = copy;
	}

	private void ComposeNewsprint(Graphics g, Color paper, Color red, Color navy)
	{
		var full = new RectangleF(0f, 0f, 1920f, 480f);
		g.Clear(paper);
		var rnd = new Random(11);
		// clippings collect at the left and right edges of the page, leaving white space between
		for (int i = 0; i < 74; i++)
		{
			bool left = i % 5 < 3;
			float cw = 50f + (float)rnd.NextDouble() * 190f, ch = 26f + (float)rnd.NextDouble() * 120f;
			float x = left ? -30f + (float)rnd.NextDouble() * (640f - cw * 0.5f) : 1430f + (float)rnd.NextDouble() * 520f - cw * 0.5f;
			float y = -20f + (float)rnd.NextDouble() * 500f - ch * 0.5f;
			NewsClipping(g, rnd, new RectangleF(x, y, cw, ch), (float)(rnd.NextDouble() * 6.0 - 3.0), red, navy);
		}
		var sweep = new InkSweepShape(new PointF(520f, 540f), new PointF(1180f, -60f), 212f, 60f);
		InkSweep(g, rnd, sweep, red, navy, paper, 84f);
		// a few clippings caught on the sweep's edges, as if pasted over the paint
		for (int i = 0; i < 12; i++)
		{
			float s = 0.08f + (float)rnd.NextDouble() * 0.84f;
			int side = i % 2 == 0 ? -1 : 1;
			PointF e = sweep.Edge(s, side);
			float cw = 60f + (float)rnd.NextDouble() * 120f, ch = 24f + (float)rnd.NextDouble() * 60f;
			NewsClipping(g, rnd, new RectangleF(e.X - cw * 0.5f + side * 30f, e.Y - ch * 0.5f, cw, ch), (float)(rnd.NextDouble() * 8.0 - 4.0), red, navy);
		}
		Grain(g, full);
	}

	/// <summary>A torn newspaper clipping: columns of grey copy, a headline word, sometimes set vertically.</summary>
	internal static void NewsClipping(Graphics g, Random rnd, RectangleF r, float angle, Color red, Color navy)
	{
		GraphicsState s = g.Save();
		g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
		g.RotateTransform(angle);
		var local = new RectangleF(-r.Width / 2f, -r.Height / 2f, r.Width, r.Height);
		using GraphicsPath torn = Torn(local, rnd, 2.6f);
		using (var shadow = new SolidBrush(Color.FromArgb(34, 30, 25, 20)))
		{
			GraphicsState sh = g.Save();
			g.TranslateTransform(1.5f, 2.5f);
			g.FillPath(shadow, torn);
			g.Restore(sh);
		}
		Color[] tones = { Color.FromArgb(252, 251, 248), Color.FromArgb(241, 236, 224), Color.FromArgb(236, 240, 245), Color.FromArgb(246, 242, 232) };
		using (var fill = new SolidBrush(tones[rnd.Next(tones.Length)]))
		{
			g.FillPath(fill, torn);
		}
		g.SetClip(torn, CombineMode.Intersect);
		Color[] inks = { navy, red, Color.FromArgb(150, 135, 105), Color.FromArgb(60, 64, 78) };
		Color ink = inks[rnd.Next(inks.Length)];
		Color copy = Color.FromArgb(130 + rnd.Next(60), 70, 72, 84);
		var inner = RectangleF.Inflate(local, -5f, -5f);
		float lineH = 1.8f + (float)rnd.NextDouble() * 1.4f;
		string word = NewsWords[rnd.Next(NewsWords.Length)];
		switch (rnd.Next(5))
		{
		case 0:
			Greek(g, rnd, inner, copy, lineH, inner.Width > 130f ? 2 : 1);
			break;
		case 1:
		case 2:
		{
			float hh = Math.Min(inner.Height * 0.45f, 34f);
			FitWord(g, word, rnd.Next(2) == 0 ? "Impact" : "Georgia", true, new RectangleF(inner.X, inner.Y, inner.Width, hh), ink, vertical: false);
			Greek(g, rnd, new RectangleF(inner.X, inner.Y + hh + 4f, inner.Width, inner.Height - hh - 4f), copy, lineH, inner.Width > 130f ? 2 : 1);
			break;
		}
		case 3:
			FitWord(g, word, "Impact", false, inner, ink, vertical: inner.Height > inner.Width * 1.3f);
			break;
		default:
		{
			using (var bar = new SolidBrush(ink))
			{
				g.FillRectangle(bar, inner.X, inner.Y, inner.Width, Math.Min(6f, inner.Height * 0.12f));
			}
			Greek(g, rnd, new RectangleF(inner.X, inner.Y + 10f, inner.Width, inner.Height - 10f), copy, lineH, inner.Width > 130f ? 2 : 1);
			break;
		}
		}
		g.Restore(s);
	}

	/// <summary>A rectangle with a ragged, torn edge on every side.</summary>
	internal static GraphicsPath Torn(RectangleF r, Random rnd, float rough)
	{
		var pts = new List<PointF>();
		const float step = 5f;
		for (float x = r.Left; x < r.Right; x += step)
		{
			pts.Add(new PointF(x, r.Top + (float)rnd.NextDouble() * rough));
		}
		for (float y = r.Top; y < r.Bottom; y += step)
		{
			pts.Add(new PointF(r.Right - (float)rnd.NextDouble() * rough, y));
		}
		for (float x = r.Right; x > r.Left; x -= step)
		{
			pts.Add(new PointF(x, r.Bottom - (float)rnd.NextDouble() * rough));
		}
		for (float y = r.Bottom; y > r.Top; y -= step)
		{
			pts.Add(new PointF(r.Left + (float)rnd.NextDouble() * rough, y));
		}
		var path = new GraphicsPath();
		path.AddPolygon(pts.ToArray());
		return path;
	}

	/// <summary>Columns of newspaper copy suggested by rows of short grey word-bars, with ragged paragraph ends.</summary>
	internal static void Greek(Graphics g, Random rnd, RectangleF area, Color ink, float lineH, int cols)
	{
		float gap = Math.Max(6f, lineH * 2.5f);
		float colW = (area.Width - gap * (cols - 1)) / cols;
		if (colW < 12f || area.Height < lineH)
		{
			return;
		}
		using var brush = new SolidBrush(ink);
		for (int c = 0; c < cols; c++)
		{
			float left = area.X + c * (colW + gap), right = left + colW;
			bool indent = false;
			for (float y = area.Y; y + lineH <= area.Bottom; y += lineH * 1.9f)
			{
				bool last = rnd.Next(8) == 0;
				float end = last ? left + colW * (0.3f + (float)rnd.NextDouble() * 0.5f) : right;
				float x = left + (indent ? lineH * 3f : 0f);
				while (x < end - 2f)
				{
					float word = lineH * (1.5f + (float)rnd.NextDouble() * 5f);
					g.FillRectangle(brush, x, y, Math.Min(word, end - x), lineH);
					x += word + lineH * 1.1f;
				}
				indent = last;
			}
		}
	}

	/// <summary>A headline word stretched to fill its area like condensed newspaper type, optionally set vertically.</summary>
	private static void FitWord(Graphics g, string word, string font, bool bold, RectangleF area, Color ink, bool vertical)
	{
		if (area.Width < 4f || area.Height < 4f)
		{
			return;
		}
		using FontFamily family = Family(font);
		using var path = new GraphicsPath();
		FontStyle style = bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
		path.AddString(word, family, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF b = path.GetBounds();
		if (b.Width < 1f)
		{
			return;
		}
		float aw = vertical ? area.Height : area.Width, ah = vertical ? area.Width : area.Height;
		float sx = aw / b.Width, sy = Math.Min(ah / b.Height, sx * 2.2f);
		if (sy < sx * 0.7f)
		{
			sx = sy / 0.7f;
		}
		GraphicsState s = g.Save();
		g.TranslateTransform(area.X + area.Width / 2f, area.Y + area.Height / 2f);
		if (vertical)
		{
			g.RotateTransform(-90f);
		}
		g.ScaleTransform(sx, sy);
		g.TranslateTransform(-b.X - b.Width / 2f, -b.Y - b.Height / 2f);
		using (var brush = new SolidBrush(ink))
		{
			g.FillPath(brush, path);
		}
		g.Restore(s);
	}

	/// <summary>The sweep's shape: a wide brushed band from the bottom left to the top right, with a slight bow.</summary>
	internal sealed class InkSweepShape
	{
		private readonly PointF A, B;

		private readonly float HalfWidth, Bow;

		public readonly PointF Dir, Normal;

		public readonly float Length;

		/// <summary>A band from a to b, half as wide as halfWidth across, bowed sideways by bow at its middle.</summary>
		public InkSweepShape(PointF a, PointF b, float halfWidth, float bow)
		{
			A = a;
			B = b;
			HalfWidth = halfWidth;
			Bow = bow;
			float dx = B.X - A.X, dy = B.Y - A.Y;
			Length = (float)Math.Sqrt(dx * dx + dy * dy);
			Dir = new PointF(dx / Length, dy / Length);
			Normal = new PointF(-Dir.Y, Dir.X);
		}

		public PointF Centre(float s) => new PointF(A.X + (B.X - A.X) * s + (float)Math.Sin(s * Math.PI) * Bow, A.Y + (B.Y - A.Y) * s);

		public float Half(float s, int side) => HalfWidth * (1f + (float)(Math.Sin(s * 9.0 + side * 2.0) * 0.104 + Math.Sin(s * 23.0 + side) * 0.047));

		public PointF Edge(float s, int side)
		{
			PointF c = Centre(s);
			float h = Half(s, side) * side;
			return new PointF(c.X + Normal.X * h, c.Y + Normal.Y * h);
		}

		/// <summary>Where a point lies: along the band (0 to 1) and across it (-1 and 1 at the edges).</summary>
		public (float S, float Across) Locate(PointF p)
		{
			float s = ((p.X - A.X) * Dir.X + (p.Y - A.Y) * Dir.Y) / Length;
			PointF c = Centre(s);
			float off = (p.X - c.X) * Normal.X + (p.Y - c.Y) * Normal.Y;
			return (s, off / Half(s, Math.Sign(off) >= 0 ? 1 : -1));
		}

		public GraphicsPath Outline()
		{
			var pts = new List<PointF>();
			for (int i = 0; i <= 80; i++)
			{
				pts.Add(Edge(i / 80f, -1));
			}
			for (int i = 80; i >= 0; i--)
			{
				pts.Add(Edge(i / 80f, 1));
			}
			var path = new GraphicsPath();
			path.AddPolygon(pts.ToArray());
			return path;
		}
	}

	/// <summary>
	/// The ink sweep: the band cut into a mosaic of cells, each hatched with fine strokes in red, or navy along
	/// the edges and one dark ridge, a few cells holding newsprint or a headline fragment, all outlined in dark
	/// ink; the edges fray into dry-brush strokes and a few pale streaks run along the band.
	/// </summary>
	internal static void InkSweep(Graphics g, Random rnd, InkSweepShape shape, Color red, Color navy, Color paper, float cw)
	{
		using GraphicsPath band = shape.Outline();
		float scale = cw / 84f;
		// dry-brush fray beyond the edges, drawn first so the cells sit on top of it
		for (int i = 0; i < (int)shape.Length; i++)
		{
			float s = (float)rnd.NextDouble() * 1.1f - 0.05f;
			int side = rnd.Next(2) == 0 ? -1 : 1;
			PointF e = shape.Edge(s, side);
			float inset = ((float)rnd.NextDouble() * 24f - 6f) * scale, len = (14f + (float)rnd.NextDouble() * 70f) * scale;
			float ang = (float)(rnd.NextDouble() - 0.5) * 0.5f;
			float dx = shape.Dir.X * (float)Math.Cos(ang) - shape.Dir.Y * (float)Math.Sin(ang), dy = shape.Dir.X * (float)Math.Sin(ang) + shape.Dir.Y * (float)Math.Cos(ang);
			float x0 = e.X - shape.Normal.X * side * inset, y0 = e.Y - shape.Normal.Y * side * inset;
			Color c = rnd.Next(3) switch { 0 => navy, 1 => Imaging.Mix(navy, Color.FromArgb(90, 120, 170), 0.5), _ => red };
			using var pen = new Pen(Color.FromArgb(70 + rnd.Next(130), c), 0.8f + (float)rnd.NextDouble() * 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawLine(pen, x0 - dx * len * 0.5f, y0 - dy * len * 0.5f, x0 + dx * len * 0.5f, y0 + dy * len * 0.5f);
		}
		GraphicsState s0 = g.Save();
		g.SetClip(band, CombineMode.Intersect);
		float ch = cw * 0.786f;
		RectangleF area = RectangleF.Inflate(band.GetBounds(), cw, ch);
		int cols = (int)Math.Ceiling(area.Width / cw), rows = (int)Math.Ceiling(area.Height / ch);
		var lattice = new PointF[cols + 1, rows + 1];
		for (int i = 0; i <= cols; i++)
		{
			for (int j = 0; j <= rows; j++)
			{
				lattice[i, j] = new PointF(area.X + i * cw + (float)(rnd.NextDouble() - 0.5) * cw * 0.6f, area.Y + j * ch + (float)(rnd.NextDouble() - 0.5) * ch * 0.6f);
			}
		}
		Color deep = Imaging.Mix(navy, Color.Black, 0.35);
		float bandAngle = (float)(Math.Atan2(shape.Dir.Y, shape.Dir.X) * 180.0 / Math.PI);
		var outlines = new List<PointF[]>();
		for (int i = 0; i < cols; i++)
		{
			for (int j = 0; j < rows; j++)
			{
				PointF[] quad = { lattice[i, j], lattice[i + 1, j], lattice[i + 1, j + 1], lattice[i, j + 1] };
				var centre = new PointF((quad[0].X + quad[1].X + quad[2].X + quad[3].X) / 4f, (quad[0].Y + quad[1].Y + quad[2].Y + quad[3].Y) / 4f);
				(float along, float across) = shape.Locate(centre);
				if (Math.Abs(across) > 1.25f)
				{
					continue;
				}
				outlines.Add(quad);
				float ridge = Math.Abs(across - 0.1f - 0.32f * (float)Math.Sin(along * 5.0));
				bool blue = across > 0.86f || across < -0.9f || ridge < 0.07f;
				Color baseColor = blue
					? Imaging.Mix(navy, Color.FromArgb(70, 100, 150), rnd.NextDouble() * 0.6)
					: Imaging.Mix(red, rnd.Next(2) == 0 ? Color.White : Color.Black, rnd.NextDouble() * 0.16);
				using var cell = new GraphicsPath();
				cell.AddPolygon(quad);
				GraphicsState cs = g.Save();
				g.SetClip(cell, CombineMode.Intersect);
				int special = rnd.Next(100);
				if (!blue && special < 7)
				{
					// a scrap of newsprint inside the mask of paint
					using (var p = new SolidBrush(Imaging.Mix(paper, red, 0.08)))
					{
						g.FillPath(p, cell);
					}
					GraphicsState ts = g.Save();
					g.TranslateTransform(centre.X, centre.Y);
					g.RotateTransform(bandAngle + (float)(rnd.NextDouble() * 30.0 - 15.0));
					Greek(g, rnd, new RectangleF(-cw * 0.8f, -ch * 0.8f, cw * 1.6f, ch * 1.6f), Color.FromArgb(170, Imaging.Mix(red, Color.Black, 0.2)), 2.4f, 2);
					g.Restore(ts);
				}
				else
				{
					using (var fill = new SolidBrush(baseColor))
					{
						g.FillPath(fill, cell);
					}
					// fine hatching, mostly along the band, alternating light and dark strokes
					float a = (float)((bandAngle + rnd.NextDouble() * 60.0 - 30.0) * Math.PI / 180.0);
					float hx = (float)Math.Cos(a), hy = (float)Math.Sin(a);
					using var light = new Pen(Color.FromArgb(190, Imaging.Mix(baseColor, Color.White, blue ? 0.42 : 0.5)), 1.3f);
					using var dark = new Pen(Color.FromArgb(130, Imaging.Mix(baseColor, Color.Black, 0.32)), 1.1f);
					float reach = cw * 1.2f;
					int k = 0;
					for (float o = -reach; o <= reach; o += 4.2f, k++)
					{
						float px = centre.X - hy * o, py = centre.Y + hx * o;
						float jitter = (float)rnd.NextDouble() * 10f;
						g.DrawLine(k % 2 == 0 ? light : dark, px - hx * (reach - jitter), py - hy * (reach - jitter), px + hx * (reach - jitter), py + hy * (reach - jitter));
					}
					if (!blue && special >= 93)
					{
						// a fragment of a white headline caught in the paint
						string frag = NewsWords[rnd.Next(NewsWords.Length)];
						frag = frag.Substring(rnd.Next(Math.Max(1, frag.Length - 3)), Math.Min(frag.Length, 4)).Trim();
						if (frag.Length > 0)
						{
							RectangleF fb = cell.GetBounds();
							FitWord(g, frag, "Impact", false, RectangleF.Inflate(fb, -fb.Width * 0.2f, -fb.Height * 0.25f), Color.FromArgb(235, 250, 246, 240), vertical: false);
						}
					}
				}
				g.Restore(cs);
			}
		}
		// the dark ink lines between the cells
		using (var line = new Pen(Color.FromArgb(235, deep), Math.Max(2f, 3.4f * scale)) { LineJoin = LineJoin.Round })
		{
			foreach (PointF[] quad in outlines)
			{
				g.DrawPolygon(line, quad);
			}
		}
		// pale streaks where the brush dragged
		for (int i = 0; i < (int)(shape.Length / 20f); i++)
		{
			float s = (float)rnd.NextDouble();
			PointF c = shape.Centre(s);
			float across = ((float)rnd.NextDouble() * 2f - 1f) * shape.Half(s, 1) * 0.9f;
			float x0 = c.X + shape.Normal.X * across, y0 = c.Y + shape.Normal.Y * across, len = (60f + (float)rnd.NextDouble() * 180f) * scale;
			using var pen = new Pen(Color.FromArgb(40 + rnd.Next(60), 255, 250, 245), 0.8f + (float)rnd.NextDouble() * 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawLine(pen, x0, y0, x0 + shape.Dir.X * len, y0 + shape.Dir.Y * len);
		}
		g.Restore(s0);
		using var rim = new Pen(Color.FromArgb(150, deep), 2.2f) { LineJoin = LineJoin.Round };
		g.DrawPath(rim, band);
	}

	// ---------- clippings ----------

	private sealed class ClipState
	{
		public string Look = "";
		public string Value = "";
		public Bitmap? Image;
		public double Since = -10.0;
	}

	private readonly Dictionary<(int, int, string), ClipState> _clips = new Dictionary<(int, int, string), ClipState>();

	/// <summary>
	/// A torn newspaper clipping (Color2 paper) holding a reading: Text is "kicker|value" (or just a value) —
	/// a small red kicker (Track, size Thickness) over a rule, the value in headline type (Font, Size, Color)
	/// with a faint red misregistration, and columns of copy beside it where there is room. Radius tilts the
	/// clipping in degrees; a floating clipping stirs gently, and pops when its value changes.
	/// </summary>
	private void Clipping(Graphics g, Widget w, RectangleF r, double t)
	{
		string resolved = Resolve(w.Text);
		int bar = resolved.IndexOf('|');
		string kicker = bar >= 0 ? resolved.Substring(0, bar) : "";
		string value = bar >= 0 ? resolved.Substring(bar + 1) : resolved;
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		string look = $"{kicker}|{value}|{warn}|{w.W}|{w.H}|{w.Size}|{w.Font}|{w.Color}|{w.Color2}|{w.Track}|{w.Thickness}|{w.Align}";
		var key = (w.X, w.Y, w.Text);
		if (!_clips.TryGetValue(key, out ClipState st))
		{
			_clips[key] = st = new ClipState();
		}
		if (st.Look != look)
		{
			if (st.Image != null && st.Value != value)
			{
				st.Since = t;
			}
			st.Image?.Dispose();
			st.Image = BuildClipping(w, kicker, value, warn);
			st.Look = look;
			st.Value = value;
		}
		float pop = (float)Math.Clamp((t - st.Since) / 0.3, 0.0, 1.0);
		float scale = 1f + 0.06f * (1f - pop) * (1f - pop);
		float angle = w.Radius + (w.Float ? (float)Math.Sin(t * 0.7 + w.X * 0.013 + w.Y * 0.007) * 0.5f : 0f);
		GraphicsState s = g.Save();
		g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
		g.RotateTransform(angle);
		g.ScaleTransform(scale, scale);
		g.DrawImage(st.Image, -st.Image.Width / 2f, -st.Image.Height / 2f, st.Image.Width, st.Image.Height);
		g.Restore(s);
	}

	private Bitmap BuildClipping(Widget w, string kicker, string value, bool warn)
	{
		const float pad = 16f;
		var bmp = new Bitmap((int)Math.Ceiling(w.W + pad * 2f), (int)Math.Ceiling(w.H + pad * 2f), PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		Setup(g);
		// the same tear every time this clipping is printed
		var rnd = new Random(w.X * 7919 + w.Y * 31 + w.W);
		var sheet = new RectangleF(pad, pad, w.W, w.H);
		using GraphicsPath torn = Torn(sheet, rnd, 3f);
		for (int i = 3; i >= 1; i--)
		{
			using var sp = new Pen(Color.FromArgb(16, 25, 18, 10), i * 3f) { LineJoin = LineJoin.Round };
			GraphicsState sh = g.Save();
			g.TranslateTransform(2.5f, 4f);
			g.DrawPath(sp, torn);
			g.Restore(sh);
		}
		using (var sb = new SolidBrush(Color.FromArgb(46, 25, 18, 10)))
		{
			GraphicsState sh = g.Save();
			g.TranslateTransform(2.5f, 4f);
			g.FillPath(sb, torn);
			g.Restore(sh);
		}
		Color paper = Imaging.Hex(w.Color2, Color.FromArgb(250, 247, 240));
		using (var fill = new LinearGradientBrush(new RectangleF(sheet.X, sheet.Y - 1f, sheet.Width, sheet.Height + 2f), paper, Imaging.Mix(paper, Color.FromArgb(226, 216, 196), 0.35), 90f))
		{
			g.FillPath(fill, torn);
		}
		g.SetClip(torn, CombineMode.Intersect);
		using (var grain = new TextureBrush(PaperGrain(), WrapMode.Tile))
		{
			g.FillRectangle(grain, sheet);
		}
		Color ink = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(200, 36, 58)) : Imaging.Hex(w.Color, Color.FromArgb(29, 43, 82));
		Color accent = Imaging.Hex(w.Track, Color.FromArgb(200, 36, 58));
		float m = Math.Max(10f, Math.Min(w.W, w.H) * 0.09f);
		float y = pad + m * 0.8f;
		if (kicker.Length > 0)
		{
			float ks = w.Thickness > 0f ? w.Thickness : 18f;
			using FontFamily kf = Family("Bahnschrift SemiBold");
			using var font = new Font(kf, ks, FontStyle.Regular, GraphicsUnit.Pixel);
			using var ab = new SolidBrush(accent);
			g.FillRectangle(ab, pad + m, y + ks * 0.22f, ks * 0.55f, ks * 0.55f);
			g.DrawString(kicker.ToUpperInvariant(), font, ab, pad + m + ks * 0.85f, y - ks * 0.08f, StringFormat.GenericTypographic);
			y += ks * 1.2f;
			using var rule = new Pen(accent, 2f);
			g.DrawLine(rule, pad + m, y, pad + w.W - m, y);
			y += 4f;
		}
		if (value.Length > 0)
		{
			using FontFamily family = Family(w.Font);
			using var path = new GraphicsPath(FillMode.Winding);
			FontStyle style = w.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
			path.AddString(value, family, (int)style, w.Size, PointF.Empty, StringFormat.GenericTypographic);
			RectangleF b = path.GetBounds();
			if (b.Width >= 1f)
			{
				float availW = w.W - m * 2f, availH = pad + w.H - m * 0.8f - y;
				float scale = Math.Min(1f, Math.Min(availW / b.Width, availH / b.Height));
				float used = b.Width * scale;
				float vx = Align(w.Align) switch { StringAlignment.Center => pad + (w.W - used) / 2f, StringAlignment.Far => pad + w.W - m - used, _ => pad + m };
				float vy = y + (availH - b.Height * scale) / 2f;
				GraphicsState vs = g.Save();
				g.TranslateTransform(vx, vy);
				g.ScaleTransform(scale, scale);
				g.TranslateTransform(-b.X, -b.Y);
				// the red plate printed a hair out of register
				using (var reg = new SolidBrush(Color.FromArgb(110, accent)))
				{
					GraphicsState rs = g.Save();
					g.TranslateTransform(2.4f / scale, 1.6f / scale);
					g.FillPath(reg, path);
					g.Restore(rs);
				}
				using (var vb = new SolidBrush(ink))
				{
					g.FillPath(vb, path);
				}
				g.Restore(vs);
				float free = availW - used - m;
				if (Align(w.Align) == StringAlignment.Near && free > 46f && availH > 24f)
				{
					Greek(g, rnd, new RectangleF(pad + m + used + m, y + 6f, free, availH - 6f), Color.FromArgb(120, 62, 66, 82), 2.4f, free > 150f ? 2 : 1);
				}
			}
		}
		g.ResetClip();
		using var edge = new Pen(Color.FromArgb(60, 120, 110, 95), 1f);
		g.DrawPath(edge, torn);
		return bmp;
	}

	// ---------- press type ----------

	private sealed class PressState
	{
		public string Look = "";
		public Bitmap? Now, Old;
		public float Pad;
		public RectangleF Ink;
		public double Since = -10.0;
	}

	private readonly Dictionary<(int, int, string), PressState> _press = new Dictionary<(int, int, string), PressState>();

	/// <summary>
	/// Big engraved press type: paper-white letters (Color) with fine engraving lines, a dark ink outline and a
	/// stepped block of ink beneath (Color2). When the reading changes, a roller sweeps across and prints the
	/// new value over the old one.
	/// </summary>
	private void PressType(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = w.Kind == WidgetKind.Value ? Formatted(w) + (Sensors.Def(w.Sensor).Unit == "%" ? "%" : Sensors.Def(w.Sensor).Unit == "°C" ? "°" : "") : Resolve(w.Text);
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		string look = $"{text}|{warn}|{w.W}|{w.H}|{w.Size}|{w.Font}|{w.Color}|{w.Color2}|{w.Align}";
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_press.TryGetValue(key, out PressState st))
		{
			_press[key] = st = new PressState();
		}
		if (st.Look != look)
		{
			st.Old?.Dispose();
			st.Old = st.Now;
			st.Now = BuildPress(w, r, text, warn, out st.Pad, out st.Ink);
			st.Since = st.Old == null ? -10.0 : t;
			st.Look = look;
		}
		float bob = w.Float ? (float)(Math.Sin(t * 0.8 + w.X * 0.01) * w.Size * 0.008) : 0f;
		int ix = (int)Math.Round(r.X - st.Pad), iy = (int)Math.Round(r.Y - st.Pad + bob);
		Bitmap now = st.Now!;
		double p = (t - st.Since) / 0.42;
		if (p >= 1.0 || st.Old == null || st.Old.Size != now.Size)
		{
			g.DrawImageUnscaled(now, ix, iy);
			return;
		}
		p = 1.0 - (1.0 - p) * (1.0 - p);
		int edge = (int)Math.Round(st.Ink.X + st.Ink.Width * p);
		int h = now.Height;
		if (edge > 0)
		{
			g.DrawImage(now, new Rectangle(ix, iy, edge, h), new Rectangle(0, 0, edge, h), GraphicsUnit.Pixel);
		}
		if (edge < now.Width)
		{
			g.DrawImage(st.Old, new Rectangle(ix + edge, iy, now.Width - edge, h), new Rectangle(edge, 0, now.Width - edge, h), GraphicsUnit.Pixel);
		}
		// the inking roller at the print line
		float top = iy + st.Ink.Y - 6f, height = st.Ink.Height + 12f;
		using (var roller = new SolidBrush(Color.FromArgb(225, Imaging.Hex(w.Color2, Color.FromArgb(29, 43, 82)))))
		{
			g.FillRectangle(roller, ix + edge - 4f, top, 8f, height);
		}
		using var shine = new Pen(Color.FromArgb(200, 255, 250, 240), 1.5f);
		g.DrawLine(shine, ix + edge - 1.5f, top + 2f, ix + edge - 1.5f, top + height - 2f);
	}

	private Bitmap BuildPress(Widget w, RectangleF r, string text, bool warn, out float pad, out RectangleF ink)
	{
		Color face = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(255, 214, 120)) : Imaging.Hex(w.Color, Color.FromArgb(251, 248, 241));
		Color navy = Imaging.Hex(w.Color2, Color.FromArgb(29, 43, 82));
		return PressImage(text, w.Font, w.Bold, w.Size, Align(w.Align), r.Size, face, navy, out pad, out ink);
	}

	/// <summary>Press type drawn into a bitmap of box plus padding; the text is shrunk to fit the box if need be.</summary>
	internal static Bitmap PressImage(string text, string font, bool bold, float size, StringAlignment align, SizeF box, Color face, Color navy, out float pad, out RectangleF ink)
	{
		pad = size * 0.22f;
		var bmp = new Bitmap((int)Math.Ceiling(box.Width + pad * 2f), (int)Math.Ceiling(box.Height + pad * 2f), PixelFormat.Format32bppPArgb);
		ink = RectangleF.Empty;
		using Graphics g = Graphics.FromImage(bmp);
		Setup(g);
		using FontFamily family = Family(font);
		using var path = new GraphicsPath(FillMode.Winding);
		FontStyle style = bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
		path.AddString(text, family, (int)style, size, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF b = path.GetBounds();
		if (b.Width < 1f)
		{
			return bmp;
		}
		float fit = Math.Min(1f, Math.Min(box.Width / b.Width, box.Height / b.Height));
		using (var place = new Matrix())
		{
			float x = align switch { StringAlignment.Center => pad + (box.Width - b.Width * fit) / 2f, StringAlignment.Far => pad + box.Width - b.Width * fit, _ => pad };
			float y = pad + (box.Height - b.Height * fit) / 2f;
			place.Translate(x, y);
			place.Scale(fit, fit);
			place.Translate(-b.X, -b.Y);
			path.Transform(place);
		}
		size *= fit;
		b = path.GetBounds();
		float step = Math.Max(1.5f, size * 0.011f);
		const int steps = 7;
		using (var block = new SolidBrush(Imaging.Mix(navy, Color.Black, 0.25)))
		{
			for (int k = steps; k >= 1; k--)
			{
				GraphicsState s = g.Save();
				g.TranslateTransform(k * step, k * step * 1.15f);
				g.FillPath(block, path);
				g.Restore(s);
			}
		}
		using (var fill = new SolidBrush(face))
		{
			g.FillPath(fill, path);
		}
		// engraving lines across the face
		GraphicsState es = g.Save();
		g.SetClip(path, CombineMode.Intersect);
		using (var engrave = new Pen(Color.FromArgb(46, navy), 1.1f))
		{
			for (float o = b.X - b.Height; o < b.Right; o += 5f)
			{
				g.DrawLine(engrave, o, b.Bottom, o + b.Height * 0.9f, b.Top);
			}
		}
		using (var lip = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1f, b.Width, b.Height + 2f), Color.FromArgb(0, navy), Color.FromArgb(40, navy), 90f))
		{
			g.FillRectangle(lip, b);
		}
		g.Restore(es);
		using var outline = new Pen(navy, Math.Max(2f, size * 0.02f)) { LineJoin = LineJoin.Round };
		g.DrawPath(outline, path);
		ink = RectangleF.Inflate(b, 4f, 4f);
		return bmp;
	}

	// ---------- news ticker ----------

	private readonly Dictionary<(int, int), float> _tickerSpan = new Dictionary<(int, int), float>();

	/// <summary>
	/// A news ticker: a band (Color2) with a red rule (Track), a red LIVE box with a blinking dot, and the text
	/// (Color) scrolling past at Thickness pixels a second.
	/// </summary>
	private void Ticker(Graphics g, Widget w, RectangleF r, double t)
	{
		Color band = Imaging.Hex(w.Color2, Color.FromArgb(29, 43, 82));
		Color accent = Imaging.Hex(w.Track, Color.FromArgb(200, 36, 58));
		Color ink = Imaging.Hex(w.Color, Color.White);
		using (var bb = new SolidBrush(band))
		{
			g.FillRectangle(bb, r);
		}
		using var ab = new SolidBrush(accent);
		g.FillRectangle(ab, r.X, r.Y, r.Width, 3f);
		Font font = GetFont(w, w.Size);
		using var ib = new SolidBrush(ink);
		var live = new RectangleF(r.X, r.Y, Math.Max(90f, w.Size * 5.4f), r.Height);
		g.FillRectangle(ab, live);
		float th = font.GetHeight(g);
		float ty = r.Y + 1.5f + (r.Height - th) / 2f;
		float dot = w.Size * 0.5f;
		using (var db = new SolidBrush(Color.FromArgb(t % 1.0 < 0.6 ? 255 : 70, ink)))
		{
			g.FillEllipse(db, live.X + 14f, r.Y + 1.5f + (r.Height - dot) / 2f, dot, dot);
		}
		g.DrawString("LIVE", font, ib, live.X + 22f + dot, ty, TickerFormat);
		string text = Resolve(w.Text) + "      •      ";
		float width = g.MeasureString(text, font, PointF.Empty, TickerFormat).Width;
		var key = (w.X, w.Y);
		// the cycle only ever grows, so the scroll does not jump as the readings change length
		float span = Math.Max(width, _tickerSpan.TryGetValue(key, out float old) ? old : 0f);
		_tickerSpan[key] = span;
		float speed = w.Thickness > 0f ? w.Thickness : 80f;
		float offset = (float)(t * speed % span);
		GraphicsState s = g.Save();
		g.SetClip(new RectangleF(live.Right, r.Y, r.Right - live.Right, r.Height), CombineMode.Intersect);
		for (float x = live.Right + 16f - offset; x < r.Right; x += span)
		{
			g.DrawString(text, font, ib, x, ty, TickerFormat);
		}
		g.Restore(s);
	}
}
