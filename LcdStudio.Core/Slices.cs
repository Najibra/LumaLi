using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LcdStudio.Core;

/// <summary>
/// Sliced lettering: big numbers cut into vertical strips like bent paper, each strip shifted up or down by a
/// travelling ripple, filled white to lilac with a soft fold shadow in it.
/// </summary>
public sealed partial class ScreenRenderer
{
	private sealed class SliceSet : IDisposable
	{
		public string Text = "";
		public double Since = -10.0;
		public Bitmap[] Variants = Array.Empty<Bitmap>();
		public RectangleF Bounds;
		public string Look = "";

		public void Dispose()
		{
			foreach (Bitmap b in Variants)
			{
				b.Dispose();
			}
			Variants = Array.Empty<Bitmap>();
		}
	}

	private readonly Dictionary<(int, int, string), SliceSet> _slices = new Dictionary<(int, int, string), SliceSet>();

	private bool DrawSlices(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "slices" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			SlicedText(g, w, r, t);
			return true;
		case "paperstack" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			PaperStack(g, w, r, t);
			return true;
		case "papertag" when w.Kind == WidgetKind.Text:
			PaperTag(g, w, r);
			return true;
		default:
			return DrawRelief(g, w, r, t);
		}
	}

	// ---------- layered paper-cut type ----------

	private readonly Dictionary<(int, int, string), (string Look, Bitmap Image, float Pad)> _paperType = new Dictionary<(int, int, string), (string, Bitmap, float)>();

	/// <summary>
	/// Lettering cut from stacked sheets of paper: a cream face with paper grain (Color), a red sheet (Color2)
	/// and a deep teal sheet (Track) stepping down and to the right beneath it, and a soft shadow under the
	/// stack. Drawn once per change and reused; a floating widget drifts gently like a paper cut-out.
	/// </summary>
	private void PaperStack(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = w.Kind == WidgetKind.Value ? Formatted(w) + (Sensors.Def(w.Sensor).Unit == "%" ? "%" : Sensors.Def(w.Sensor).Unit == "°C" ? "°" : "") : Resolve(w.Text);
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		string look = text + (warn ? "!" : "") + w.W + w.Size;
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_paperType.TryGetValue(key, out var hit) || hit.Look != look)
		{
			hit.Image?.Dispose();
			float pad = w.Size * 0.3f;
			var bmp = new Bitmap((int)Math.Ceiling(r.Width + pad * 2f), (int)Math.Ceiling(r.Height + pad * 2f), PixelFormat.Format32bppPArgb);
			using (Graphics pg = Graphics.FromImage(bmp))
			{
				Setup(pg);
				DrawPaperStack(pg, w, new RectangleF(pad, pad, r.Width, r.Height), text, warn);
			}
			_paperType[key] = hit = (look, bmp, pad);
		}
		float bob = w.Float ? (float)(Math.Sin(t * 0.9 + w.X * 0.004) * w.Size * 0.025) : 0f;
		g.DrawImageUnscaled(hit.Image, (int)Math.Round(r.X - hit.Pad), (int)Math.Round(r.Y - hit.Pad + bob));
	}

	private void DrawPaperStack(Graphics g, Widget w, RectangleF r, string text, bool warn)
	{
		using var path = new GraphicsPath(FillMode.Winding);
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
			path.AddString(text, family, (int)style, w.Size, PointF.Empty, StringFormat.GenericTypographic);
		}
		RectangleF b = path.GetBounds();
		if (b.Width < 1f)
		{
			return;
		}
		float x = Align(w.Align) switch { StringAlignment.Center => r.X + (r.Width - b.Width) / 2f, StringAlignment.Far => r.Right - b.Width, _ => r.X } - b.X;
		float y = r.Y + (r.Height - b.Height) / 2f - b.Y;
		float step = Math.Max(1.5f, w.Size * 0.035f);
		Color face = Imaging.Hex(w.Color, Color.FromArgb(246, 232, 212));
		Color red = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(255, 150, 60)) : Imaging.Hex(w.Color2, Color.FromArgb(150, 24, 18));
		Color teal = Imaging.Hex(w.Track, Color.FromArgb(24, 72, 84));
		void Layer(Brush brush, float dx, float dy)
		{
			GraphicsState s = g.Save();
			g.TranslateTransform(x + dx, y + dy);
			g.FillPath(brush, path);
			g.Restore(s);
		}
		// a soft shadow under the whole stack
		using (var shadowFill = new SolidBrush(Color.FromArgb(30, 10, 2, 2)))
		{
			for (int i = 4; i >= 1; i--)
			{
				using var shadow = new Pen(Color.FromArgb(22, 10, 2, 2), step * i * 1.6f) { LineJoin = LineJoin.Round };
				GraphicsState s = g.Save();
				g.TranslateTransform(x + step * 3.5f, y + step * 4.5f);
				g.DrawPath(shadow, path);
				g.FillPath(shadowFill, path);
				g.Restore(s);
			}
		}
		using (var tealBrush = new SolidBrush(teal))
		{
			Layer(tealBrush, step * 2.2f, step * 2.6f);
		}
		using (var redBrush = new SolidBrush(red))
		{
			Layer(redBrush, step * 1.1f, step * 1.3f);
		}
		using (var faceBrush = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1f, b.Width, b.Height + 2f), Imaging.Mix(face, Color.White, 0.25), Imaging.Mix(face, Color.FromArgb(200, 170, 140), 0.25), 90f))
		{
			Layer(faceBrush, 0f, 0f);
		}
		// paper grain and a pale cut edge on the face
		GraphicsState gs = g.Save();
		g.TranslateTransform(x, y);
		g.SetClip(path);
		g.ResetTransform();
		using (var grain = new TextureBrush(PaperGrain(), WrapMode.Tile))
		{
			g.FillRectangle(grain, r.X - w.Size, r.Y - w.Size, r.Width + w.Size * 2f, r.Height + w.Size * 2f);
		}
		g.Restore(gs);
		using var rim = new Pen(Color.FromArgb(90, 255, 255, 255), 1.2f);
		GraphicsState rs = g.Save();
		g.TranslateTransform(x - 0.6f, y - 0.6f);
		g.DrawPath(rim, path);
		g.Restore(rs);
	}

	/// <summary>A small paper tag: a rounded sheet (Color2) with a soft shadow and the text in Color.</summary>
	private void PaperTag(Graphics g, Widget w, RectangleF r)
	{
		string text = Resolve(w.Text);
		Font font = GetFont(w, w.Size);
		SizeF size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
		float padX = w.Size * 0.75f, height = w.Size * 1.75f;
		float width = size.Width + padX * 2f;
		float x = Align(w.Align) switch { StringAlignment.Center => r.X + (r.Width - width) / 2f, StringAlignment.Far => r.Right - width, _ => r.X };
		var tag = new RectangleF(x, r.Y + (r.Height - height) / 2f, width, height);
		using (GraphicsPath sp = Imaging.Round(new RectangleF(tag.X + 3f, tag.Y + 4f, tag.Width, tag.Height), height / 2f))
		using (var sb = new SolidBrush(Color.FromArgb(90, 10, 2, 2)))
		{
			g.FillPath(sb, sp);
		}
		using (GraphicsPath tp = Imaging.Round(tag, height / 2f))
		{
			using (var fill = new LinearGradientBrush(new RectangleF(tag.X, tag.Y - 1f, tag.Width, tag.Height + 2f), Imaging.Hex(w.Color2, Color.FromArgb(30, 84, 96)), Imaging.Mix(Imaging.Hex(w.Color2, Color.FromArgb(30, 84, 96)), Color.Black, 0.25), 90f))
			{
				g.FillPath(fill, tp);
			}
			using var grain = new TextureBrush(PaperGrain(), WrapMode.Tile);
			g.FillPath(grain, tp);
		}
		using var b = new SolidBrush(Imaging.Hex(w.Color, Color.FromArgb(246, 232, 212)));
		g.DrawString(text, font, b, tag.X + padX, tag.Y + (tag.Height - size.Height) / 2f, StringFormat.GenericTypographic);
	}

	/// <summary>
	/// The text (a value with its unit) cut into strips about a sixth of the letter height wide. A ripple runs
	/// along the strips and shifts each one; when the text changes the ripple swells, then settles. The letters
	/// are drawn once per change in six variants (white-to-lilac or lilac-to-white, each with a fold shadow at a
	/// different height, every third a little see-through) and the strips are copied from them, so the effect
	/// stays cheap. Color is the light tone, Color2 the lilac.
	/// </summary>
	private void SlicedText(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = w.Kind == WidgetKind.Value ? Formatted(w) + (Sensors.Def(w.Sensor).Unit == "%" ? "%" : Sensors.Def(w.Sensor).Unit == "°C" ? "°" : "") : Resolve(w.Text);
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		string look = text + (warn ? "!" : "");
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_slices.TryGetValue(key, out SliceSet set))
		{
			_slices[key] = set = new SliceSet();
		}
		if (set.Look != look)
		{
			if (set.Text.Length > 0 && set.Text != text)
			{
				set.Since = t;
			}
			set.Dispose();
			BuildSlices(set, w, text, warn);
			set.Text = text;
			set.Look = look;
		}
		if (set.Variants.Length == 0)
		{
			return;
		}
		double kick = Math.Max(0.0, 1.0 - (t - set.Since) / 1.2);
		RectangleF b = set.Bounds;
		float pad = w.Size * 0.3f;
		float x0 = Align(w.Align) switch { StringAlignment.Center => r.X + (r.Width - b.Width) / 2f, StringAlignment.Far => r.Right - b.Width, _ => r.X };
		float y0 = r.Y + (r.Height - b.Height) / 2f;
		float strip = Math.Max(6f, w.Size * 0.16f);
		int strips = (int)Math.Ceiling(b.Width / strip);
		float amplitude = (float)(w.Size * (0.045 + 0.12 * kick));
		InterpolationMode mode = g.InterpolationMode;
		g.InterpolationMode = InterpolationMode.NearestNeighbor;
		for (int i = 0; i < strips; i++)
		{
			float sx = i * strip, sw = Math.Min(strip, b.Width - sx);
			float dy = (float)(Math.Sin(i * 0.85 - t * 1.4) * amplitude);
			Bitmap variant = set.Variants[i % set.Variants.Length];
			g.DrawImage(variant, new RectangleF(x0 + sx, y0 + dy - pad, sw, b.Height + pad * 2f), new RectangleF(pad + sx, 0f, sw, b.Height + pad * 2f), GraphicsUnit.Pixel);
		}
		g.InterpolationMode = mode;
		// fine seams between the strips
		using var seam = new Pen(Color.FromArgb(55, 20, 30, 160), 1f);
		for (int i = 1; i < strips; i++)
		{
			float sx = (float)Math.Round(x0 + i * strip);
			float dy = (float)(Math.Sin(i * 0.85 - t * 1.4) * amplitude);
			g.DrawLine(seam, sx, y0 + dy + b.Height * 0.05f, sx, y0 + dy + b.Height * 0.95f);
		}
	}

	private void BuildSlices(SliceSet set, Widget w, string text, bool warn)
	{
		using var path = new GraphicsPath(FillMode.Winding);
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
			path.AddString(text, family, (int)style, w.Size, PointF.Empty, StringFormat.GenericTypographic);
		}
		RectangleF b = path.GetBounds();
		if (b.Width < 1f)
		{
			return;
		}
		float pad = w.Size * 0.3f;
		using (var m = new Matrix())
		{
			m.Translate(pad - b.X, pad - b.Y);
			path.Transform(m);
		}
		set.Bounds = new RectangleF(0f, 0f, b.Width, b.Height);
		Color light = Imaging.Hex(w.Color, Color.FromArgb(236, 238, 248));
		Color lilac = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(255, 110, 90)) : Imaging.Hex(w.Color2, Color.FromArgb(128, 132, 230));
		int width = (int)Math.Ceiling(b.Width + pad * 2f), height = (int)Math.Ceiling(b.Height + pad * 2f);
		var variants = new Bitmap[6];
		for (int v = 0; v < variants.Length; v++)
		{
			bool flip = v % 2 == 1;
			int alpha = v % 3 == 2 ? 205 : 248;
			float fold = 0.22f + 0.28f * (v % 3);
			var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
			using (Graphics bg = Graphics.FromImage(bmp))
			{
				bg.SmoothingMode = SmoothingMode.AntiAlias;
				// a soft shadow behind the letters keeps them readable over bright light
				using (var shadow = new Pen(Color.FromArgb(70, 10, 6, 30), w.Size * 0.05f) { LineJoin = LineJoin.Round })
				{
					bg.DrawPath(shadow, path);
				}
				using (var fill = new LinearGradientBrush(new RectangleF(pad, pad - 1f, b.Width, b.Height + 2f), Color.FromArgb(alpha, flip ? lilac : light), Color.FromArgb(alpha, flip ? light : lilac), 90f))
				{
					bg.FillPath(fill, path);
				}
				bg.SetClip(path);
				var band = new RectangleF(pad, pad + b.Height * fold - b.Height * 0.2f, b.Width, b.Height * 0.4f);
				using var shade = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, 90f)
				{
					InterpolationColors = new ColorBlend
					{
						Colors = new[] { Color.FromArgb(0, 20, 30, 210), Color.FromArgb(115, 20, 30, 210), Color.FromArgb(0, 20, 30, 210) },
						Positions = new[] { 0f, 0.5f, 1f }
					}
				};
				bg.FillRectangle(shade, band);
			}
			variants[v] = bmp;
		}
		set.Variants = variants;
	}
}
