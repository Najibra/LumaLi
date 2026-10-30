using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LcdStudio.Core;

/// <summary>The Headline face for the round fan LCDs, matching the Headline screen layout's newsprint collage.</summary>
public sealed partial class Faces
{
	private Bitmap? _headlinePage;

	private string _headlineKey = "";

	private sealed class HeadlinePress
	{
		public string Value = "";
		public Bitmap? Now, Old;
		public float Pad;
		public RectangleF Ink;
		public double Since = -10.0;
	}

	private readonly Dictionary<string, HeadlinePress> _headlinePress = new Dictionary<string, HeadlinePress>();

	/// <summary>
	/// Newsprint clippings round the rim and a hatched ink sweep across the middle (paint C1, ink C2), the
	/// reading in big engraved press type that an inking roller reprints on every change, the label and the
	/// time on torn paper strips that stir gently, and the reading's share as a printed rule round the rim.
	/// </summary>
	private void Headline(FaceContext x)
	{
		Graphics g = x.G;
		Color red = x.C1, navy = x.C2;
		string key = $"{red.ToArgb()}|{navy.ToArgb()}";
		if (_headlinePage == null || _headlineKey != key)
		{
			_headlinePage?.Dispose();
			_headlinePage = HeadlinePage(red, navy);
			_headlineKey = key;
		}
		CompositingMode mode = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_headlinePage, 0, 0);
		g.CompositingMode = mode;

		// the reading's share as a printed rule round the rim
		using (var track = new Pen(Color.FromArgb(70, navy), 10f))
		{
			g.DrawEllipse(track, 15f, 15f, 370f, 370f);
		}
		float sweep = 360f * Math.Clamp(x.Fraction, 0f, 1f);
		if (sweep > 0.5f)
		{
			using var fill = new Pen(red, 10f);
			g.DrawArc(fill, 15f, 15f, 370f, 370f, -90f, sweep);
		}
		using (var rule = new Pen(navy, 2f))
		{
			g.DrawEllipse(rule, 9f, 9f, 382f, 382f);
			g.DrawEllipse(rule, 21f, 21f, 358f, 358f);
		}

		string serial = x.Fan.Serial;
		float wobble = (float)Math.Sin(x.T * 0.7 + serial.Length);
		Strip(g, x.Label.ToUpperInvariant(), "Bahnschrift SemiBold", 25f, red, navy, new PointF(200f, 92f), -3f + wobble * 0.6f);

		// the reading in press type, reprinted by the roller when it changes
		if (!_headlinePress.TryGetValue(serial, out HeadlinePress press))
		{
			_headlinePress[serial] = press = new HeadlinePress();
		}
		if (press.Now == null || press.Value != x.Value)
		{
			press.Old?.Dispose();
			press.Old = press.Now;
			press.Now = ScreenRenderer.PressImage(x.Value, "Impact", false, 150f, StringAlignment.Center, new SizeF(290f, 156f), Color.FromArgb(251, 248, 241), navy, out press.Pad, out press.Ink);
			press.Since = press.Old == null ? -10.0 : x.T;
			press.Value = x.Value;
		}
		int ix = (int)Math.Round(55f - press.Pad), iy = (int)Math.Round(124f - press.Pad);
		double p = (x.T - press.Since) / 0.42;
		if (p >= 1.0 || press.Old == null || press.Old.Size != press.Now.Size)
		{
			g.DrawImageUnscaled(press.Now, ix, iy);
		}
		else
		{
			p = 1.0 - (1.0 - p) * (1.0 - p);
			int edge = (int)Math.Round(press.Ink.X + press.Ink.Width * p), h = press.Now.Height, w = press.Now.Width;
			if (edge > 0)
			{
				g.DrawImage(press.Now, new Rectangle(ix, iy, edge, h), new Rectangle(0, 0, edge, h), GraphicsUnit.Pixel);
			}
			if (edge < w)
			{
				g.DrawImage(press.Old, new Rectangle(ix + edge, iy, w - edge, h), new Rectangle(edge, 0, w - edge, h), GraphicsUnit.Pixel);
			}
			float top = iy + press.Ink.Y - 6f, height = press.Ink.Height + 12f;
			using (var roller = new SolidBrush(Color.FromArgb(225, navy)))
			{
				g.FillRectangle(roller, ix + edge - 4f, top, 8f, height);
			}
			using var shine = new Pen(Color.FromArgb(200, 255, 250, 240), 1.5f);
			g.DrawLine(shine, ix + edge - 1.5f, top + 2f, ix + edge - 1.5f, top + height - 2f);
		}

		Strip(g, x.Time, "Impact", 38f, navy, navy, new PointF(200f, 318f), 2.5f - wobble * 0.6f);
	}

	/// <summary>A torn paper strip sized to its text, with a soft shadow, tilted by angle degrees about its centre.</summary>
	private void Strip(Graphics g, string text, string font, float size, Color ink, Color edgeInk, PointF centre, float angle)
	{
		using var f = new Font(Family(font), size, FontStyle.Regular, GraphicsUnit.Pixel);
		SizeF ts = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic);
		float w = ts.Width + size * 1.1f, h = size * 1.55f;
		GraphicsState s = g.Save();
		g.TranslateTransform(centre.X, centre.Y);
		g.RotateTransform(angle);
		var sheet = new RectangleF(-w / 2f, -h / 2f, w, h);
		// the same tear every frame for the same text
		using GraphicsPath torn = ScreenRenderer.Torn(sheet, new Random(text.Length * 131 + (int)centre.Y), 2.6f);
		using (var shadow = new SolidBrush(Color.FromArgb(60, 25, 18, 10)))
		{
			GraphicsState sh = g.Save();
			g.TranslateTransform(2.5f, 4f);
			g.FillPath(shadow, torn);
			g.Restore(sh);
		}
		using (var paper = new LinearGradientBrush(new RectangleF(sheet.X, sheet.Y - 1f, sheet.Width, sheet.Height + 2f), Color.FromArgb(251, 248, 241), Color.FromArgb(236, 228, 212), 90f))
		{
			g.FillPath(paper, torn);
		}
		using (var edge = new Pen(Color.FromArgb(70, edgeInk), 1f))
		{
			g.DrawPath(edge, torn);
		}
		using (var b = new SolidBrush(ink))
		{
			g.DrawString(text, f, b, -ts.Width / 2f, -ts.Height / 2f, StringFormat.GenericTypographic);
		}
		g.Restore(s);
	}

	/// <summary>The face's still page: paper, clippings round the rim, and the ink sweep across the middle.</summary>
	private static Bitmap HeadlinePage(Color red, Color navy)
	{
		var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
		g.InterpolationMode = InterpolationMode.HighQualityBilinear;
		Color paper = Color.FromArgb(246, 243, 236);
		g.Clear(paper);
		var rnd = new Random(5);
		for (int i = 0; i < 26; i++)
		{
			float cw = 44f + (float)rnd.NextDouble() * 110f, ch = 22f + (float)rnd.NextDouble() * 70f;
			double a = rnd.NextDouble() * Math.PI * 2.0;
			float r = 120f + (float)rnd.NextDouble() * 90f;
			float cx = 200f + (float)Math.Cos(a) * r, cy = 200f + (float)Math.Sin(a) * r;
			ScreenRenderer.NewsClipping(g, rnd, new RectangleF(cx - cw / 2f, cy - ch / 2f, cw, ch), (float)(rnd.NextDouble() * 8.0 - 4.0), red, navy);
		}
		var sweep = new ScreenRenderer.InkSweepShape(new PointF(70f, 470f), new PointF(330f, -70f), 104f, 24f);
		ScreenRenderer.InkSweep(g, rnd, sweep, red, navy, paper, 42f);
		return bmp;
	}
}
