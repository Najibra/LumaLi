using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace LcdStudio.Core;

/// <summary>
/// The paper-art pieces: a paper-cut sea of nested layers that undulate and cast soft shadows on the layers
/// beneath, paper bubbles, a little boat rowing at the crest, a paper grain over everything, and a video
/// panel that plays a folder of frames inside a paper frame.
/// </summary>
public sealed partial class ScreenRenderer
{
	private Bitmap? _paperGrain;

	private readonly Dictionary<string, VideoFrames> _panelVideos = new Dictionary<string, VideoFrames>();

	private bool DrawPaper(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "videopanel" when w.Kind == WidgetKind.Image:
			VideoPanel(g, w, r, t);
			return true;
		case "paperwaves" when w.Kind == WidgetKind.Panel:
			PaperWaves(g, w, r, t, Palette(w.Text, Color.SteelBlue), w.Print == "swirl");
			return true;
		default:
			return DrawSlices(g, w, r, t);
		}
	}

	/// <summary>A soft speckled paper grain, made once and laid over the scene at low strength.</summary>
	private Bitmap PaperGrain()
	{
		if (_paperGrain != null)
		{
			return _paperGrain;
		}
		_paperGrain = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
		var rnd = new Random(7);
		for (int y = 0; y < 256; y++)
		{
			for (int x = 0; x < 256; x++)
			{
				int v = rnd.Next(100);
				_paperGrain.SetPixel(x, y, v < 50 ? Color.FromArgb(10, 0, 0, 0) : (v < 90 ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(14, 255, 255, 255)));
			}
		}
		return _paperGrain;
	}

	private void Grain(Graphics g, RectangleF r)
	{
		using var tex = new TextureBrush(PaperGrain(), WrapMode.Tile);
		g.FillRectangle(tex, r);
	}

	/// <summary>
	/// The paper-cut sea background: a pale paper sky (Color1) with grain, the nested water layers from
	/// Color2 at the edge to Accent in the deepest hollow, paper bubbles, and the little boat at the crest.
	/// </summary>
	private void PaperSea(Graphics g, Color sky, Color shallow, Color deep, double t)
	{
		// the paper moves slowly, so the sea is recomposed eight times a second and the boat drawn live on top
		if (_seaFrame == null || Math.Abs(t - _seaTime) >= 0.125)
		{
			_seaTime = t;
			_seaFrame ??= new Bitmap(1920, 480, PixelFormat.Format32bppArgb);
			using Graphics sg = Graphics.FromImage(_seaFrame);
			sg.SmoothingMode = SmoothingMode.AntiAlias;
			_seaCrest = ComposeSea(sg, sky, shallow, deep, t);
		}
		CompositingMode copy = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_seaFrame, 0, 0);
		g.CompositingMode = copy;
		Boat(g, _seaCrest.X + 34f, _seaCrest.Y - 22f, t);
	}

	private Bitmap? _seaFrame;

	private double _seaTime = double.MinValue;

	private PointF _seaCrest;

	private PointF ComposeSea(Graphics g, Color sky, Color shallow, Color deep, double t)
	{
		var full = new RectangleF(0f, 0f, 1920f, 480f);
		using (var wash = new LinearGradientBrush(full, Imaging.Mix(sky, Color.White, 0.12), sky, 90f))
		{
			g.FillRectangle(wash, full);
		}
		var palette = new Color[7];
		for (int i = 0; i < palette.Length; i++)
		{
			palette[i] = Imaging.Mix(shallow, deep, i / (double)(palette.Length - 1));
		}
		PointF crest = PaperLayers(g, full, t, palette, swirl: false);
		// paper bubbles drifting up to the left of the crest
		(float X, float Y, float W, float H)[] bubbles = { (0.62f, 0.32f, 54f, 34f), (0.655f, 0.22f, 30f, 20f), (0.67f, 0.14f, 16f, 11f), (0.58f, 0.5f, 62f, 38f), (0.61f, 0.47f, 26f, 18f) };
		for (int i = 0; i < bubbles.Length; i++)
		{
			var b = bubbles[i];
			float bx = 1920f * b.X + (float)Math.Sin(t * 0.5 + i) * 6f, by = 480f * b.Y + (float)Math.Cos(t * 0.6 + i * 1.3) * 5f;
			using (var shadow = new SolidBrush(Color.FromArgb(60, 10, 30, 70)))
			{
				g.FillEllipse(shadow, bx + 4f, by + 6f, b.W, b.H);
			}
			using var paper = new LinearGradientBrush(new RectangleF(bx, by, b.W, b.H), Imaging.Mix(palette[0], Color.White, 0.25), palette[1], 90f);
			g.FillEllipse(paper, bx, by, b.W, b.H);
		}
		Grain(g, full);
		return crest;
	}

	/// <summary>
	/// Nested paper layers rising toward a crest on the right. Each layer's edge is a slow wave; the deeper
	/// layers sit lower and darker, and a soft shadow is cast just inside each layer's edge, so the layers above
	/// read as cut paper. With swirl, the edges carry curling scallops like quilled waves. Returns the crest.
	/// </summary>
	private static PointF PaperLayers(Graphics g, RectangleF area, double t, Color[] palette, bool swirl)
	{
		int layers = palette.Length;
		float crestX = area.X + area.Width * 0.78f;
		PointF crest = PointF.Empty;
		for (int k = 0; k < layers; k++)
		{
			float baseY = area.Y + area.Height * (0.42f + k * 0.07f);
			float rise = area.Height * (0.27f - k * 0.022f);
			float width = area.Width * (0.17f - k * 0.012f);
			float cx = crestX - k * area.Width * 0.035f;
			var edge = new List<PointF>();
			for (float x = area.X - 20f; x <= area.Right + 20f; x += 12f)
			{
				double wave = Math.Sin(x * 0.006 + t * (0.35 + k * 0.05) + k * 0.9) * area.Height * 0.035
					+ Math.Sin(x * 0.0137 - t * 0.27 + k) * area.Height * 0.015;
				if (swirl)
				{
					wave += Math.Sin(x * 0.045 + t * 0.6 + k * 2.1) * area.Height * 0.012;
				}
				double hump = rise * Math.Exp(-Math.Pow((x - cx) / width, 2));
				// the far side of the crest falls away more steeply, like the water pouring down in the picture
				if (x > cx)
				{
					hump *= Math.Exp(-(x - cx) / (width * 1.6));
				}
				float y = (float)(baseY + wave - hump);
				edge.Add(new PointF(x, y));
				if (k == 0 && (crest.IsEmpty || y < crest.Y))
				{
					crest = new PointF(x, y);
				}
			}
			using var path = new GraphicsPath();
			path.AddCurve(edge.ToArray(), 0.4f);
			path.AddLine(area.Right + 20f, area.Bottom + 20f, area.X - 20f, area.Bottom + 20f);
			path.CloseFigure();
			using (var fill = new LinearGradientBrush(new RectangleF(area.X, area.Y - 1f, area.Width, area.Height + 2f), Imaging.Mix(palette[k], Color.White, 0.06), Imaging.Mix(palette[k], Color.Black, 0.12), 90f))
			{
				g.FillPath(fill, path);
			}
			// the shadow of the layer above, cast just inside this layer's edge, and a pale cut edge
			GraphicsState s = g.Save();
			g.SetClip(path, CombineMode.Intersect);
			using (var edgePath = new GraphicsPath())
			{
				edgePath.AddCurve(edge.ToArray(), 0.4f);
				float[] widths = { 26f, 14f, 6f };
				int[] alphas = { 22, 30, 40 };
				for (int i = 0; i < widths.Length; i++)
				{
					using var pen = new Pen(Color.FromArgb(alphas[i], 5, 15, 40), widths[i]);
					g.TranslateTransform(0f, widths[i] * 0.3f);
					g.DrawPath(pen, edgePath);
					g.TranslateTransform(0f, -widths[i] * 0.3f);
				}
				using var rim = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f);
				g.DrawPath(rim, edgePath);
			}
			g.Restore(s);
		}
		return crest;
	}

	/// <summary>A little wooden rowing boat seen from above, rocking gently, its oars sweeping.</summary>
	private static void Boat(Graphics g, float x, float y, double t)
	{
		GraphicsState s = g.Save();
		g.TranslateTransform(x, y);
		g.RotateTransform((float)(-38.0 + Math.Sin(t * 1.2) * 4.0));
		float oar = (float)(Math.Sin(t * 2.2) * 18.0);
		using (var oarPen = new Pen(Color.FromArgb(90, 50, 25), 3f) { StartCap = LineCap.Round })
		using (var blade = new SolidBrush(Color.FromArgb(110, 60, 30)))
		{
			foreach (int side in new[] { -1, 1 })
			{
				GraphicsState o = g.Save();
				g.RotateTransform(side * (90f + oar));
				g.DrawLine(oarPen, 0f, 0f, 0f, -44f);
				g.FillEllipse(blade, -4f, -54f, 8f, 16f);
				g.Restore(o);
			}
		}
		using var hull = new GraphicsPath();
		hull.AddBezier(0f, -34f, 16f, -20f, 15f, 18f, 11f, 26f);
		hull.AddLine(11f, 26f, -11f, 26f);
		hull.AddBezier(-11f, 26f, -15f, 18f, -16f, -20f, 0f, -34f);
		hull.CloseFigure();
		using (var shadow = new SolidBrush(Color.FromArgb(70, 10, 20, 50)))
		{
			GraphicsState sh = g.Save();
			g.TranslateTransform(4f, 6f);
			g.FillPath(shadow, hull);
			g.Restore(sh);
		}
		using (var wood = new LinearGradientBrush(new RectangleF(-16f, -34f, 32f, 60f), Color.FromArgb(214, 120, 52), Color.FromArgb(150, 72, 30), 0f))
		{
			g.FillPath(wood, hull);
		}
		using (var inside = new SolidBrush(Color.FromArgb(120, 58, 26)))
		{
			g.FillEllipse(inside, -9f, -22f, 18f, 40f);
		}
		using (var rower = new SolidBrush(Color.FromArgb(240, 228, 205)))
		{
			g.FillEllipse(rower, -6f, -4f, 12f, 12f);
		}
		using (var head = new SolidBrush(Color.FromArgb(60, 35, 20)))
		{
			g.FillEllipse(head, -3.5f, -1.5f, 7f, 7f);
		}
		g.Restore(s);
	}

	/// <summary>Paper-cut layers drawn inside the widget box (Text lists the colours, outer to deepest), for scenes that are not full-screen.</summary>
	private void PaperWaves(Graphics g, Widget w, RectangleF r, double t, Color[] palette, bool swirl)
	{
		GraphicsState s = g.Save();
		g.SetClip(r, CombineMode.Intersect);
		PaperLayers(g, r, t, palette, swirl);
		Grain(g, r);
		g.Restore(s);
	}

	/// <summary>A video playing from a folder of frames (Image) inside a paper frame with a soft shadow.</summary>
	private void VideoPanel(Graphics g, Widget w, RectangleF r, double t)
	{
		using (GraphicsPath shadow = Imaging.Round(new RectangleF(r.X + 6f, r.Y + 8f, r.Width, r.Height), w.Radius))
		using (var sb = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
		{
			g.FillPath(sb, shadow);
		}
		if (w.Image != null && Directory.Exists(w.Image))
		{
			if (!_panelVideos.TryGetValue(w.Image, out VideoFrames video))
			{
				_panelVideos[w.Image] = video = new VideoFrames();
			}
			GraphicsState s = g.Save();
			using GraphicsPath clip = Imaging.Round(r, w.Radius);
			g.SetClip(clip, CombineMode.Intersect);
			video.Draw(g, w.Image, w.Thickness > 0f ? (int)w.Thickness : 24, t, r, 1f);
			g.Restore(s);
		}
		using GraphicsPath frame = Imaging.Round(r, w.Radius);
		using var pen = new Pen(Imaging.Hex(w.Color, Color.FromArgb(235, 225, 210)), Math.Max(2f, w.Size));
		g.DrawPath(pen, frame);
	}
}
