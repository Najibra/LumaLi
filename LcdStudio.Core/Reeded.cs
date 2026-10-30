using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LcdStudio.Core;

/// <summary>
/// Reeded glass: a deep red page seen through vertical glass ribs, with dark lettering behind the glass. Each
/// rib shows its own magnified slice of the letters, shifted up or down by a slow wave, and the letters' lower
/// edges run down in jagged smears like paint dragged through the glass.
/// </summary>
public sealed partial class ScreenRenderer
{
	private const int Reed = 24;

	private Bitmap? _reed;

	private string _reedKey = "";

	private sealed class ReedSet
	{
		public string Look = "";
		public Bitmap? Image;
		public float PadX, PadTop;
		public double Since = -10.0;
	}

	private readonly Dictionary<(int, int, string), ReedSet> _reeds = new Dictionary<(int, int, string), ReedSet>();

	private bool DrawReeded(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "reeded" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			ReededText(g, w, r, t);
			return true;
		case "reedfigure" when w.Kind == WidgetKind.Panel:
			ReededText(g, w, r, t);
			return true;
		case "reedlines" when w.Kind == WidgetKind.Panel:
			ReedLines(g, r);
			return true;
		default:
			return DrawSpectrum(g, w, r, t);
		}
	}

	/// <summary>The page behind the glass: red (Color1) glowing from the top, darkening to Color2 below and at the sides, with the ribs' shading. Composed once.</summary>
	private void Reeded(Graphics g, Color red, Color deep)
	{
		string key = $"{red.ToArgb()}|{deep.ToArgb()}";
		if (_reed == null || _reedKey != key)
		{
			_reed?.Dispose();
			_reed = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
			_reedKey = key;
			using Graphics rg = Graphics.FromImage(_reed);
			var full = new RectangleF(0f, 0f, 1920f, 480f);
			using (var wash = new LinearGradientBrush(new RectangleF(0f, -1f, 1920f, 482f), red, deep, 90f))
			{
				wash.InterpolationColors = new ColorBlend
				{
					Colors = new[] { Imaging.Mix(red, Color.White, 0.05), red, Imaging.Mix(red, deep, 0.45), deep },
					Positions = new[] { 0f, 0.4f, 0.68f, 1f }
				};
				rg.FillRectangle(wash, full);
			}
			using (var sides = new LinearGradientBrush(new RectangleF(-1f, 0f, 1922f, 480f), Color.Black, Color.Black, 0f))
			{
				sides.InterpolationColors = new ColorBlend
				{
					Colors = new[] { Color.FromArgb(110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(110, 0, 0, 0) },
					Positions = new[] { 0f, 0.22f, 0.78f, 1f }
				};
				rg.FillRectangle(sides, full);
			}
			// each rib darkens toward its left edge and catches a soft highlight toward its right
			using var rib = new LinearGradientBrush(new RectangleF(0f, 0f, Reed, 480f), Color.Black, Color.Black, 0f)
			{
				InterpolationColors = new ColorBlend
				{
					Colors = new[] { Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 255, 120, 110), Color.FromArgb(46, 255, 130, 120), Color.FromArgb(60, 0, 0, 0) },
					Positions = new[] { 0f, 0.25f, 0.6f, 0.86f, 1f }
				}
			};
			for (int x = 0; x < 1920; x += Reed)
			{
				rib.ResetTransform();
				rib.TranslateTransform(x, 0f);
				rg.FillRectangle(rib, x, 0, Reed, 480);
			}
		}
		CompositingMode copy = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_reed, 0, 0);
		g.CompositingMode = copy;
	}

	/// <summary>The front of the glass, laid over everything: a fine light line and a dark seam on every rib.</summary>
	private static void ReedLines(Graphics g, RectangleF r)
	{
		using var light = new SolidBrush(Color.FromArgb(70, 255, 150, 140));
		using var seam = new SolidBrush(Color.FromArgb(80, 20, 0, 0));
		int first = (int)Math.Floor(r.X / Reed) * Reed;
		for (int x = first; x < r.Right; x += Reed)
		{
			g.FillRectangle(light, x + Reed - 4, r.Y, 1, r.Height);
			g.FillRectangle(seam, x + Reed - 1, r.Y, 1, r.Height);
		}
	}

	/// <summary>
	/// The text (a value with its unit) in Color behind the glass. Its lower edges smear downward by up to
	/// Thickness pixels in jagged runs; each rib shows a magnified slice, waving slowly up and down, and when
	/// the text changes a ripple runs through the ribs and settles.
	/// </summary>
	private void ReededText(Graphics g, Widget w, RectangleF r, double t)
	{
		string unit = w.Kind == WidgetKind.Value ? Sensors.Def(w.Sensor).Unit : "";
		string text = w.Kind == WidgetKind.Value ? Formatted(w) + (unit == "%" ? "%" : unit == "°C" ? "°" : unit == "W" ? "W" : "") : Resolve(w.Text);
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		bool figure = w.Kind == WidgetKind.Panel;
		// the figure moves, so it is redrawn twelve times a second; text only when it changes
		string look = figure ? $"figure|{(int)Math.Floor(t * 12.0)}|{w.W}|{w.H}|{w.Color}" : $"{text}|{warn}|{w.W}|{w.H}|{w.Size}|{w.Font}|{w.Align}|{w.Color}|{w.Thickness}";
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_reeds.TryGetValue(key, out ReedSet st))
		{
			_reeds[key] = st = new ReedSet();
		}
		if (st.Look != look)
		{
			if (st.Image != null && !figure)
			{
				st.Since = t;
			}
			st.Image?.Dispose();
			using (Bitmap flat = figure ? BuildFigure(w, r, Math.Floor(t * 12.0) / 12.0, out st.PadX, out st.PadTop) : BuildReeded(w, r, text, warn, out st.PadX, out st.PadTop))
			{
				st.Image = Refract(flat, (int)Math.Round(r.X - st.PadX));
			}
			st.Look = look;
		}
		Bitmap img = st.Image!;
		int ox = (int)Math.Round(r.X - st.PadX);
		float oy = r.Y - st.PadTop;
		double since = t - st.Since;
		float ripple = since < 1.0 ? (float)((1.0 - since) * (1.0 - since)) * w.Size * 0.1f : 0f;
		// the ribs are already magnified, so each frame only moves them up and down
		int k0 = (int)Math.Ceiling((ox + Reed) / (double)Reed), k1 = (int)Math.Floor((ox + img.Width - Reed) / (double)Reed);
		for (int k = k0; k < k1; k++)
		{
			int dx = k * Reed;
			float dy = (float)(Math.Sin(k * 0.38 + t * 0.9) * w.Size * 0.022 + Math.Sin(k * 1.7 - t * 1.3) * w.Size * 0.008);
			if (ripple > 0f)
			{
				dy += (float)Math.Sin(k * 0.6 - since * 14.0) * ripple;
			}
			g.DrawImage(img, new Rectangle(dx, (int)Math.Round(oy + dy), Reed, img.Height), new Rectangle(dx - ox, 0, Reed, img.Height), GraphicsUnit.Pixel);
		}
	}

	/// <summary>Each rib's magnified slice of the flat letters, laid side by side on the screen's rib grid (ox is the bitmap's screen x).</summary>
	private static Bitmap Refract(Bitmap flat, int ox)
	{
		var bmp = new Bitmap(flat.Width, flat.Height, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.InterpolationMode = InterpolationMode.HighQualityBilinear;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		const float slice = Reed * 0.74f;
		for (int k = (int)Math.Ceiling(ox / (double)Reed); k * Reed < ox + flat.Width; k++)
		{
			int dx = k * Reed - ox;
			float centre = dx + Reed / 2f + (float)Math.Sin(k * 0.7) * 3f;
			g.DrawImage(flat, new RectangleF(dx, 0f, Reed, flat.Height), new RectangleF(centre - slice / 2f, 0f, slice, flat.Height), GraphicsUnit.Pixel);
		}
		return bmp;
	}

	/// <summary>
	/// A caped figure seen from the front, filling the box in Color, at moment t: the cape billows in waves that
	/// run down from the shoulders and swings out in an occasional gust, its glossy folds moving with it and its
	/// dripping hem swaying; the figure breathes, and the light along the shoulders swells and fades.
	/// </summary>
	private static Bitmap BuildFigure(Widget w, RectangleF r, double t, out float padX, out float padTop)
	{
		padX = Reed * 2f;
		padTop = 0f;
		var bmp = new Bitmap((int)Math.Ceiling(r.Width + padX * 2f), (int)Math.Ceiling(r.Height), PixelFormat.Format32bppPArgb);
		Color ink = Imaging.Hex(w.Color, Color.FromArgb(8, 2, 2));
		using Graphics g = Graphics.FromImage(bmp);
		Setup(g);
		// drawn in a 400 × 450 design space, then fitted to the box
		g.TranslateTransform(padX, 0f);
		g.ScaleTransform(r.Width / 400f, r.Height / 450f);
		using var fill = new SolidBrush(ink);
		double gust = Math.Pow(Math.Max(0.0, Math.Sin(t * 0.45)), 4.0);
		float amp = (float)(7.0 + 18.0 * gust), sway = (float)(gust * 18.0 * Math.Sin(t * 0.9));
		// how far down the cape a point is (0 at the shoulders, 1 at the hem), and its billow there
		float Down(float y) => Math.Clamp((y - 122f) / 288f, 0f, 1f);
		float Billow(float y, int side) => (float)Math.Pow(Down(y), 1.2) * amp * (float)Math.Sin(t * 1.7 - y * 0.025 + side * 1.3);
		float Edge(float y, int side)
		{
			float d = Down(y);
			float x = side < 0 ? 110f - 96f * (float)Math.Pow(d, 0.85) : 290f + 96f * (float)Math.Pow(d, 0.85);
			return x + side * Billow(y, side) + sway * d;
		}
		var cape = new List<PointF> { new PointF(290f, 122f), new PointF(110f, 122f) };
		for (float y = 140f; y <= 410f; y += 18f)
		{
			cape.Add(new PointF(Edge(y, -1), y));
		}
		// the hem breaks into drips of different lengths, swaying as the cape moves
		var rnd = new Random(23);
		float left = Edge(410f, -1), right = Edge(410f, 1);
		int i = 0;
		for (float x = left; x <= right; x += 8f, i++)
		{
			bool drip = rnd.Next(3) > 0;
			float wave = (float)Math.Sin(t * 2.4 + i * 0.7) * 5f;
			cape.Add(new PointF(x, (drip ? 450f : 450f - 14f - (float)rnd.NextDouble() * 36f) + wave));
			cape.Add(new PointF(x + 4f, (drip ? 450f - (float)rnd.NextDouble() * 12f : 450f - 10f - (float)rnd.NextDouble() * 26f) + wave));
		}
		for (float y = 410f; y >= 140f; y -= 18f)
		{
			cape.Add(new PointF(Edge(y, 1), y));
		}
		using (var cp = new GraphicsPath())
		{
			cp.AddPolygon(cape.ToArray());
			g.FillPath(fill, cp);
			// glossy folds running down the cape, swinging with it
			GraphicsState fs = g.Save();
			g.SetClip(cp, CombineMode.Intersect);
			using var fold = new Pen(Color.FromArgb(70, 130, 18, 16), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			foreach (float x0 in new[] { 128f, 168f, 232f, 272f })
			{
				int side = x0 < 200f ? -1 : 1;
				var pts = new List<PointF>();
				for (float y = 160f; y <= 440f; y += 28f)
				{
					float d = Down(y);
					pts.Add(new PointF(x0 + (x0 - 200f) * 0.9f * d + side * Billow(y + 30f, side) * 0.8f + sway * d, y));
				}
				g.DrawCurve(fold, pts.ToArray(), 0.5f);
			}
			g.Restore(fs);
		}
		// the figure breathes: head and shoulders rise and settle
		GraphicsState bs = g.Save();
		g.TranslateTransform(0f, -(float)(Math.Sin(t * 1.25) * 2.2));
		using (var body = new GraphicsPath())
		{
			body.AddClosedCurve(new[]
			{
				new PointF(186f, 94f), new PointF(150f, 108f), new PointF(110f, 122f), new PointF(88f, 148f), new PointF(80f, 210f),
				new PointF(84f, 300f), new PointF(100f, 380f), new PointF(300f, 380f), new PointF(316f, 300f), new PointF(320f, 210f),
				new PointF(312f, 148f), new PointF(290f, 122f), new PointF(250f, 108f), new PointF(214f, 94f)
			}, 0.3f);
			g.FillPath(fill, body);
		}
		g.FillRectangle(fill, 183f, 72f, 34f, 28f);
		g.FillEllipse(fill, 167f, 10f, 66f, 82f);
		// short swept hair, a little fuller on one side
		g.FillEllipse(fill, 170f, 5f, 60f, 44f);
		g.FillEllipse(fill, 202f, 9f, 26f, 24f);
		// the light along the shoulders swells and fades, brightest in a gust
		int glow = (int)Math.Min(255.0, 50.0 + 70.0 * (0.5 + 0.5 * Math.Sin(t * 0.8)) + 90.0 * gust);
		using var rim = new Pen(Color.FromArgb(glow, 255, 70, 60), 2.4f) { LineJoin = LineJoin.Round };
		g.DrawCurve(rim, new[] { new PointF(92f, 144f), new PointF(112f, 122f), new PointF(150f, 108f), new PointF(186f, 95f) }, 0.4f);
		g.DrawCurve(rim, new[] { new PointF(214f, 95f), new PointF(250f, 108f), new PointF(288f, 122f), new PointF(308f, 144f) }, 0.4f);
		g.Restore(bs);
		return bmp;
	}

	private Bitmap BuildReeded(Widget w, RectangleF r, string text, bool warn, out float padX, out float padTop)
	{
		padX = Reed * 2f;
		padTop = w.Size * 0.12f;
		float smear = w.Thickness > 0f ? w.Thickness : w.Size * 0.6f;
		int bw = (int)Math.Ceiling(r.Width + padX * 2f), bh = (int)Math.Ceiling(r.Height + padTop + smear);
		var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
		Color ink = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(255, 230, 220)) : Imaging.Hex(w.Color, Color.FromArgb(8, 2, 2));
		using (Graphics g = Graphics.FromImage(bmp))
		{
			Setup(g);
			using FontFamily family = Family(w.Font);
			using var path = new GraphicsPath(FillMode.Winding);
			FontStyle style = w.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
			path.AddString(text, family, (int)style, w.Size, PointF.Empty, StringFormat.GenericTypographic);
			RectangleF b = path.GetBounds();
			if (b.Width < 1f)
			{
				return bmp;
			}
			float fit = Math.Min(1f, Math.Min(r.Width / b.Width, r.Height / b.Height));
			float x = Align(w.Align) switch { StringAlignment.Center => padX + (r.Width - b.Width * fit) / 2f, StringAlignment.Far => padX + r.Width - b.Width * fit, _ => padX };
			float y = padTop + (r.Height - b.Height * fit) / 2f;
			g.TranslateTransform(x, y);
			g.ScaleTransform(fit, fit);
			g.TranslateTransform(-b.X, -b.Y);
			// a soft rim so the letters read as a shape behind glass rather than crisp print
			using (var halo = new Pen(Color.FromArgb(70, ink), w.Size * 0.035f) { LineJoin = LineJoin.Round })
			{
				g.DrawPath(halo, path);
			}
			using var fill = new SolidBrush(ink);
			g.FillPath(fill, path);
		}
		// the smear: from the lowest inked pixel of every column, run down in a jagged, fading streak
		var rnd = new Random(text.Length * 977 + w.X);
		BitmapData data = bmp.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		unsafe
		{
			byte* scan = (byte*)data.Scan0;
			int textBottom = Math.Min(bh - 1, (int)Math.Ceiling(padTop + r.Height));
			float jag = 0f, drip = 0f;
			for (int x = 0; x < bw; x++)
			{
				int yb = -1;
				for (int y = textBottom; y >= 0; y--)
				{
					if (scan[y * data.Stride + x * 4 + 3] > 120)
					{
						yb = y;
						break;
					}
				}
				// the streak lengths wander, with sudden long drips
				jag = jag * 0.6f + (float)rnd.NextDouble() * 0.4f;
				drip = rnd.NextDouble() < 0.035 ? 1f : drip * 0.86f;
				if (yb < 0)
				{
					continue;
				}
				double n = 0.5 + 0.5 * Math.Sin(x * 0.09 + w.Y) * Math.Cos(x * 0.031);
				float len = smear * (float)(0.25 + 0.4 * n + 0.3 * jag + 0.45 * drip);
				for (int d = 1; d < len && yb + d < bh; d++)
				{
					double fade = 1.0 - d / len;
					int a = (int)(fade * 240.0);
					byte* p = scan + (yb + d) * data.Stride + x * 4;
					if (a > p[3])
					{
						p[0] = (byte)(ink.B * a / 255);
						p[1] = (byte)(ink.G * a / 255);
						p[2] = (byte)(ink.R * a / 255);
						p[3] = (byte)a;
					}
				}
			}
		}
		bmp.UnlockBits(data);
		return bmp;
	}
}
