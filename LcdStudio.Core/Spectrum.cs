using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Spectrum: a black page crossed by diagonal glass slats that split a band of light into a rainbow, with a dome of
/// glowing blue rings rising from below the screen in the middle. The readings are heavy condensed slab letters,
/// each tilted its own way and casting a soft shadow on its neighbour; they jostle more as the reading climbs, and a
/// digit that changes drops in and settles. Sonar pulses run out through the rings faster the busier the processor.
/// </summary>
public sealed partial class ScreenRenderer
{
	// the dome's centre (below the screen), its innermost ring and the spacing of the rings
	private const float DomeX = 960f, DomeY = 560f, DomeR0 = 110f, DomeStep = 44f;

	private const int DomeRings = 15;

	private Bitmap? _spectrum;

	private string _spectrumKey = "";

	private sealed class SlabGlyph
	{
		public Bitmap Face = null!;
		public Bitmap Shadow = null!;
	}

	private sealed class SlabSet
	{
		public string Look = "";
		public string Text = "";
		public string[] Keys = Array.Empty<string>();
		public float[] X = Array.Empty<float>();
		public float[] Since = Array.Empty<float>();
		public float[] From = Array.Empty<float>();
		public float[] Lift = Array.Empty<float>();
		public float Height;
	}

	private sealed class SonarSet
	{
		public double Last = double.NaN;
		public double Acc;
		public readonly List<double> Pulses = new List<double>();
	}

	private readonly Dictionary<string, SlabGlyph> _slabGlyphs = new Dictionary<string, SlabGlyph>();

	private readonly Dictionary<(int, int, string), SlabSet> _slabs = new Dictionary<(int, int, string), SlabSet>();

	private readonly Dictionary<(int, int, string), SonarSet> _sonars = new Dictionary<(int, int, string), SonarSet>();

	private bool DrawSpectrum(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "slabs" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			Slabs(g, w, r, t);
			return true;
		case "sonar" when w.Kind == WidgetKind.Panel:
			Sonar(g, w, t);
			return true;
		case "prismbar" when w.Kind == WidgetKind.Value:
			PrismBar(g, w, r, t);
			return true;
		case "ornament" when w.Kind == WidgetKind.Panel:
			Ornament(g, w, r);
			return true;
		default:
			return DrawDeck(g, w, r, t);
		}
	}

	// ---------- the page ----------

	/// <summary>The black page (Color1) with its rainbow slats and the dome of blue (Color2) rings, warming to amber (Color3) at the core. Composed once.</summary>
	private void Spectrum(Graphics g, Color black, Color blue, Color amber, bool dome = true)
	{
		string key = $"{black.ToArgb()}|{blue.ToArgb()}|{amber.ToArgb()}|{dome}";
		if (_spectrum == null || _spectrumKey != key)
		{
			_spectrum?.Dispose();
			_spectrum = ComposeSpectrum(black, blue, amber, dome);
			_spectrumKey = key;
		}
		CompositingMode copy = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_spectrum, 0, 0);
		g.CompositingMode = copy;
	}

	private static Bitmap ComposeSpectrum(Color black, Color blue, Color amber, bool dome)
	{
		var bmp = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
		var full = new RectangleF(0f, 0f, 1920f, 480f);
		using Graphics g = Graphics.FromImage(bmp);
		Setup(g);
		g.Clear(black);
		var rnd = new Random(41);
		// diagonal slats rising to the right; each holds its own grey sheen and its own slice of the rainbow
		const double theta = 50.0 * Math.PI / 180.0;
		var along = new PointF((float)Math.Cos(theta), -(float)Math.Sin(theta));
		var across = new PointF((float)Math.Sin(theta), (float)Math.Cos(theta));
		using var layer = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
		using Graphics lg = Graphics.FromImage(layer);
		Setup(lg);
		float u = -60f;
		for (int k = 0; u < 1900f; k++)
		{
			float width = 120f + (float)rnd.NextDouble() * 80f;
			lg.Clear(Color.Transparent);
			var c = new PointF(across.X * (u + width / 2f), across.Y * (u + width / 2f));
			int lum1 = 4 + rnd.Next(30), lum2 = 2 + rnd.Next(14), lum3 = 6 + rnd.Next(36);
			using (var sheen = new LinearGradientBrush(new PointF(c.X - along.X * 700f, c.Y - along.Y * 700f), new PointF(c.X + along.X * 700f, c.Y + along.Y * 700f), Color.Black, Color.Black))
			{
				sheen.InterpolationColors = new ColorBlend
				{
					Colors = new[] { Grey(lum2), Grey(lum1), Grey(lum2 / 2), Grey(lum3), Grey(lum2) },
					Positions = new[] { 0f, 0.3f, 0.5f, 0.72f, 1f }
				};
				lg.FillRectangle(sheen, full);
			}
			// the rainbow: blue above, a white seam, amber trailing off below; tilted and shifted a little in each slat
			float strength = 0.35f + (float)rnd.NextDouble() * 0.65f;
			float yc = 330f - 44f * (float)Math.Sin(k * 0.9) + (float)rnd.NextDouble() * 20f;
			float tilt = (float)(rnd.NextDouble() * 16.0 - 8.0);
			var band = new RectangleF(-200f, yc - 90f, 2320f, 200f);
			using (var bow = new LinearGradientBrush(band, Color.Black, Color.Black, 90f + tilt))
			{
				int A(int a) => (int)(a * strength);
				bow.InterpolationColors = new ColorBlend
				{
					Colors = new[]
					{
						Color.FromArgb(0, 0, 0, 0), Color.FromArgb(A(60), Imaging.Mix(blue, Color.Black, 0.6)), Color.FromArgb(A(170), blue),
						Color.FromArgb(A(220), Imaging.Mix(blue, Color.White, 0.55)), Color.FromArgb(A(235), 255, 255, 248), Color.FromArgb(A(210), Imaging.Mix(amber, Color.White, 0.3)),
						Color.FromArgb(A(110), amber), Color.FromArgb(A(36), Imaging.Mix(amber, Color.Black, 0.5)), Color.FromArgb(0, 0, 0, 0)
					},
					Positions = new[] { 0f, 0.3f, 0.43f, 0.48f, 0.5f, 0.53f, 0.62f, 0.8f, 1f }
				};
				lg.FillRectangle(bow, band);
			}
			// laid down through the slat's outline, so its long edges stay smooth
			using (var slat = new GraphicsPath())
			{
				var a = new PointF(across.X * u, across.Y * u);
				var b = new PointF(across.X * (u + width), across.Y * (u + width));
				slat.AddPolygon(new[]
				{
					new PointF(a.X - along.X * 2500f, a.Y - along.Y * 2500f), new PointF(a.X + along.X * 2500f, a.Y + along.Y * 2500f),
					new PointF(b.X + along.X * 2500f, b.Y + along.Y * 2500f), new PointF(b.X - along.X * 2500f, b.Y - along.Y * 2500f)
				});
				using var tex = new TextureBrush(layer, WrapMode.Clamp);
				g.FillPath(tex, slat);
			}
			u += width;
		}
		// the dome: discs stacked from the outermost in, so each shows as a band under the next one's rim; a band is
		// dark just under its own rim and brightens toward the rim below it, whitening at its sides. The outer
		// discs are faint, so the dome melts into the slats.
		var white = Color.FromArgb(222, 236, 255);
		for (int i = dome ? DomeRings - 1 : -1; i >= 0; i--)
		{
			float rr = DomeR0 + i * DomeStep;
			float b = 1f - i / (float)(DomeRings - 1), e = (float)Math.Pow(b, 1.4);
			float alpha = Math.Clamp(0.08f + 1.5f * b, 0f, 1f);
			var disc = new RectangleF(DomeX - rr, DomeY - rr, rr * 2f, rr * 2f);
			using var outline = new GraphicsPath();
			outline.AddEllipse(disc);
			float s = Math.Min(1f, DomeStep * 1.15f / rr);
			using (var shade = new PathGradientBrush(outline))
			{
				shade.CenterPoint = new PointF(DomeX, DomeY);
				shade.CenterColor = Color.FromArgb((int)(255 * alpha), Imaging.Mix(Color.FromArgb(4, 8, 24), blue, 0.3 + 0.65 * e));
				shade.SurroundColors = new[] { Color.FromArgb((int)(255 * alpha), Imaging.Mix(Color.FromArgb(1, 2, 8), blue, 0.06 + 0.22 * e)) };
				shade.Blend = new Blend { Factors = new[] { 0f, 0.12f, 1f, 1f }, Positions = new[] { 0f, s * 0.3f, s, 1f } };
				g.FillPath(shade, outline);
			}
			Color side = b > 0.72f ? Imaging.Mix(white, amber, (b - 0.72f) * 1.4f) : white;
			int wa = (int)((40 + 190 * Math.Pow(e, 1.2)) * alpha);
			using (var sides = new LinearGradientBrush(new RectangleF(disc.X - 1f, disc.Y, disc.Width + 2f, disc.Height), Color.Black, Color.Black, 0f))
			{
				sides.InterpolationColors = new ColorBlend
				{
					Colors = new[] { Color.FromArgb(wa, side), Color.FromArgb(wa / 3, side), Color.FromArgb(0, side), Color.FromArgb(0, side), Color.FromArgb(wa / 3, side), Color.FromArgb(wa, side) },
					Positions = new[] { 0f, 0.1f, 0.26f, 0.74f, 0.9f, 1f }
				};
				g.FillPath(sides, outline);
			}
			using var glow = new Pen(Color.FromArgb((int)((40 + 100 * e) * alpha), Imaging.Mix(blue, Color.White, 0.55)), 5f);
			g.DrawEllipse(glow, disc);
			using var rim = new Pen(Color.FromArgb((int)((60 + 195 * e) * alpha), 238, 246, 255), 1.5f);
			g.DrawEllipse(rim, disc);
		}
		// the top falls away into black
		using (var top = new LinearGradientBrush(new RectangleF(0f, -1f, 1920f, 220f), Color.FromArgb(150, black), Color.FromArgb(0, black), 90f))
		{
			g.FillRectangle(top, 0f, 0f, 1920f, 218f);
		}
		Grain(bmp, 9);
		return bmp;
	}

	private static Color Grey(int l) => Color.FromArgb(l, l, l + 2);

	/// <summary>Film grain over the whole bitmap.</summary>
	private static void Grain(Bitmap bmp, int amount)
	{
		var rnd = new Random(7);
		BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		unsafe
		{
			for (int y = 0; y < bmp.Height; y++)
			{
				byte* p = (byte*)data.Scan0 + y * data.Stride;
				for (int x = 0; x < bmp.Width; x++, p += 4)
				{
					int n = rnd.Next(-amount, amount + 1);
					for (int c = 0; c < 3; c++)
					{
						p[c] = (byte)Math.Clamp(p[c] + n, 0, p[3]);
					}
				}
			}
		}
		bmp.UnlockBits(data);
	}

	// ---------- the dome's sonar ----------

	/// <summary>Pulses running out through the dome's rings, sent more often the higher the reading, lighting each ring's rim as they pass.</summary>
	private void Sonar(Graphics g, Widget w, double t)
	{
		var key = (w.X, w.Y, w.Sensor);
		if (!_sonars.TryGetValue(key, out SonarSet st))
		{
			_sonars[key] = st = new SonarSet();
		}
		double rate = 0.2 + 1.3 * Fraction(w);
		const float speed = 240f, reach = DomeR0 + DomeRings * DomeStep;
		if (double.IsNaN(st.Last) || t < st.Last || t - st.Last > 2.0)
		{
			// a fresh start already has pulses on their way out
			st.Pulses.Clear();
			for (double s = t; (s - t) * -speed < reach - DomeR0; s -= 1.0 / rate)
			{
				st.Pulses.Add(s);
			}
			st.Acc = 0.0;
		}
		else
		{
			st.Acc += (t - st.Last) * rate;
			while (st.Acc >= 1.0)
			{
				st.Acc -= 1.0;
				st.Pulses.Add(t);
			}
		}
		st.Last = t;
		st.Pulses.RemoveAll(s => DomeR0 + (t - s) * speed > reach);
		Color blue = Imaging.Hex(w.Color2, Color.FromArgb(70, 140, 255));
		if (st.Pulses.Count == 0)
		{
			return;
		}
		// drawn by Skia, as light added to the dome: each pulse's wide glow at a quarter size, truly blurred and laid on
		// stretched, then its bright line and the flaring ring sharp over it
		SkiaDraw(g, new Rectangle(0, 0, 1920, 480), c =>
		{
			_sonarGlow ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			_sonarSoft ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _sonarGlow.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);
			using (var wide = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 36f, BlendMode = SKBlendMode.Plus })
			{
				foreach (double s in st.Pulses)
				{
					float rr = DomeR0 + (float)(t - s) * speed;
					float fade = (float)Math.Pow(1f - (rr - DomeR0) / (reach - DomeR0), 1.4);
					wide.Color = SkiaKit.Sk(Imaging.Mix(blue, Color.White, 0.15), 0.62f * fade);
					q.DrawArc(new SKRect(DomeX - rr, DomeY - rr, DomeX + rr, DomeY + rr), 180f, 180f, false, wide);
				}
			}
			q.Restore();
			using (SKImage glow = _sonarGlow.Snapshot())
			using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(3.5f, 3.5f) })
			{
				_sonarSoft.Canvas.Clear(SKColors.Transparent);
				_sonarSoft.Canvas.DrawImage(glow, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage soft = _sonarSoft.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(soft, pixels);
			}
			using var mid = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6f, BlendMode = SKBlendMode.Plus };
			using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, BlendMode = SKBlendMode.Plus };
			using var flare = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.2f) };
			foreach (double s in st.Pulses)
			{
				float rr = DomeR0 + (float)(t - s) * speed;
				float fade = (float)Math.Pow(1f - (rr - DomeR0) / (reach - DomeR0), 1.4);
				var box = new SKRect(DomeX - rr, DomeY - rr, DomeX + rr, DomeY + rr);
				mid.Color = SkiaKit.Sk(Imaging.Mix(blue, Color.White, 0.6), 0.5f * fade);
				c.DrawArc(box, 180f, 180f, false, mid);
				line.Color = new SKColor(248, 252, 255, (byte)(235f * fade));
				c.DrawArc(box, 180f, 180f, false, line);
				// the ring the pulse is crossing flares
				int ring = (int)Math.Round((rr - DomeR0) / DomeStep);
				float near = 1f - Math.Abs(rr - (DomeR0 + ring * DomeStep)) / (DomeStep / 2f);
				if (ring >= 0 && ring < DomeRings && near > 0f)
				{
					float rim = DomeR0 + ring * DomeStep;
					flare.Color = new SKColor(255, 255, 255, (byte)(200f * near * fade));
					c.DrawArc(new SKRect(DomeX - rim, DomeY - rim, DomeX + rim, DomeY + rim), 180f, 180f, false, flare);
				}
			}
		});
	}

	private SKSurface? _sonarGlow, _sonarSoft;

	// ---------- slab letters ----------

	/// <summary>
	/// The text (a value with its unit) as heavy slab letters in Color, each tilted its own way, overlapping a little
	/// and shadowing the one before it. They sway more, and faster, the higher the reading; a letter that changes
	/// drops in from above, bounces and settles at a new tilt.
	/// </summary>
	private void Slabs(Graphics g, Widget w, RectangleF r, double t, string? forced = null)
	{
		string unit = w.Kind == WidgetKind.Value ? Sensors.Def(w.Sensor).Unit : "";
		string text = forced ?? (w.Kind == WidgetKind.Value ? Formatted(w) + (unit == "%" ? "%" : unit == "°C" ? "°" : unit == "W" ? "W" : "") : Resolve(w.Text));
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		Color ink = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(255, 170, 90)) : Imaging.Hex(w.Color, Color.FromArgb(236, 231, 218));
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_slabs.TryGetValue(key, out SlabSet st))
		{
			_slabs[key] = st = new SlabSet();
		}
		string look = $"{text}|{ink.ToArgb()}|{w.W}|{w.H}|{w.Size}|{w.Font}|{w.Align}";
		if (st.Look != look)
		{
			LayoutSlabs(st, w, r, text, ink, (float)t);
			st.Look = look;
		}
		if (st.Keys.Length == 0)
		{
			return;
		}
		double level = Fraction(w);
		float h = st.Height;
		float cy = r.Y + r.Height / 2f;
		GraphicsState saved = g.Save();
		g.InterpolationMode = InterpolationMode.Bilinear;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		for (int i = 0; i < st.Keys.Length; i++)
		{
			if (st.Keys[i] == null)
			{
				continue;
			}
			SlabGlyph glyph = _slabGlyphs[st.Keys[i]];
			// where this letter rests: its own tilt and height, alternating in direction
			int seed = i * 7919 + st.Text[i] * 131 + w.X;
			float rest = (i % 2 == 0 ? -1f : 1f) * (2.5f + (float)Hash01(seed) * 6f);
			float restDy = ((float)Hash01(seed + 17) - 0.5f) * h * 0.12f;
			// restless in proportion to the reading
			double speed = 1.1 + 2.8 * level, sway = 0.5 + 3.2 * level * level;
			float angle = rest + (float)(Math.Sin(t * speed + i * 1.9) * sway);
			float dy = restDy + (float)(Math.Sin(t * (speed * 1.2) + i * 2.3) * h * (0.004 + 0.02 * level));
			float scale = 1f;
			double since = t - st.Since[i];
			if (since >= 0.0 && since < 2.0)
			{
				// a changed letter drops in, bounces, and swings from its old tilt to the new one
				float e = (float)Math.Exp(-4.5 * since);
				dy += -h * 0.55f * e * (float)Math.Cos(since * 10.0);
				angle += (st.From[i] - rest + (i % 2 == 0 ? 10f : -10f)) * e * (float)Math.Cos(since * 9.0);
				scale += 0.1f * (float)Math.Exp(-6.0 * since);
			}
			g.ResetTransform();
			g.TranslateTransform(r.X + st.X[i], cy + st.Lift[i] + dy);
			g.RotateTransform(angle);
			g.ScaleTransform(scale, scale);
			// light from the upper right: the shadow falls left and down, onto the letter before
			g.DrawImage(glyph.Shadow, -glyph.Shadow.Width / 2f - h * 0.05f, -glyph.Shadow.Height / 2f + h * 0.035f, glyph.Shadow.Width, glyph.Shadow.Height);
			g.DrawImage(glyph.Face, -glyph.Face.Width / 2f, -glyph.Face.Height / 2f, glyph.Face.Width, glyph.Face.Height);
		}
		g.Restore(saved);
	}

	/// <summary>Fits the text into the box, building any letters not yet cached, and marks the letters that changed so they drop in.</summary>
	private void LayoutSlabs(SlabSet st, Widget w, RectangleF r, string text, Color ink, float t)
	{
		using FontFamily family = Family(w.Font);
		FontStyle style = w.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
		// measure every letter at the widget's size, then shrink the lot to fit
		var bounds = new RectangleF[text.Length];
		float total = 0f, cap = 0f;
		for (int i = 0; i < text.Length; i++)
		{
			using var p = new GraphicsPath();
			p.AddString(text[i].ToString(), family, (int)style, w.Size, PointF.Empty, StringFormat.GenericTypographic);
			bounds[i] = p.GetBounds();
			cap = Math.Max(cap, bounds[i].Height);
		}
		// wide letters overlap their neighbours a little; narrow marks like a colon keep clear of them
		float Advance(RectangleF bb) => bb.Width < 1f ? w.Size * 0.28f : bb.Width < cap * 0.4f ? bb.Width + cap * 0.16f : bb.Width * 0.93f;
		foreach (RectangleF bb in bounds)
		{
			total += Advance(bb);
		}
		float fit = total < 1f ? 1f : Math.Min(1f, Math.Min(r.Width * 0.94f / total, r.Height * 0.76f / Math.Max(1f, cap)));
		float size = w.Size * fit;
		string font = w.Font + (style == FontStyle.Bold ? "|b" : "");
		var keys = new string[text.Length];
		var xs = new float[text.Length];
		float x = 0f;
		for (int i = 0; i < text.Length; i++)
		{
			float advance = Advance(bounds[i]) * fit;
			xs[i] = x + advance / 2f;
			x += advance;
			if (bounds[i].Width < 1f)
			{
				continue;
			}
			keys[i] = $"{font}|{text[i]}|{size:0.0}|{ink.ToArgb()}";
			if (!_slabGlyphs.ContainsKey(keys[i]))
			{
				_slabGlyphs[keys[i]] = BuildSlab(text[i], family, style, size, ink);
			}
		}
		float start = Align(w.Align) switch { StringAlignment.Center => (r.Width - x) / 2f, StringAlignment.Far => r.Width - x, _ => 0f };
		var since = new float[text.Length];
		var from = new float[text.Length];
		bool first = st.Text.Length == 0;
		for (int i = 0; i < text.Length; i++)
		{
			xs[i] += start;
			bool same = i < st.Text.Length && st.Text[i] == text[i] && st.Text.Length == text.Length;
			since[i] = first ? -10f : same ? st.Since[i] : t;
			from[i] = i < st.From.Length && !same ? (i % 2 == 0 ? 1f : -1f) * 6f : i < st.From.Length ? st.From[i] : 0f;
		}
		st.Text = text;
		st.Keys = keys;
		st.X = xs;
		st.Since = since;
		st.From = from;
		// each letter sits where the font puts it, so a point or comma stays on the line
		float top = float.MaxValue, bottom = float.MinValue;
		foreach (RectangleF bb in bounds)
		{
			if (bb.Width >= 1f)
			{
				top = Math.Min(top, bb.Y);
				bottom = Math.Max(bottom, bb.Bottom);
			}
		}
		st.Lift = Array.ConvertAll(bounds, bb => bb.Width < 1f ? 0f : (bb.Y + bb.Height / 2f - (top + bottom) / 2f) * fit);
		st.Height = cap * fit;
	}

	/// <summary>One slab letter: a warm off-white face shading slightly darker toward its foot, and a soft blurred shadow.</summary>
	private static SlabGlyph BuildSlab(char c, FontFamily family, FontStyle style, float size, Color ink)
	{
		using var path = new GraphicsPath();
		path.AddString(c.ToString(), family, (int)style, size, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF b = path.GetBounds();
		const float pad = 3f;
		var face = new Bitmap((int)Math.Ceiling(b.Width + pad * 2f), (int)Math.Ceiling(b.Height + pad * 2f), PixelFormat.Format32bppPArgb);
		using (Graphics g = Graphics.FromImage(face))
		{
			Setup(g);
			g.TranslateTransform(pad - b.X, pad - b.Y);
			using var fill = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1f, b.Width + 1f, b.Height + 2f), Imaging.Mix(ink, Color.White, 0.08), Imaging.Mix(ink, Color.Black, 0.14), 90f);
			g.FillPath(fill, path);
			using var edge = new Pen(Color.FromArgb(70, Imaging.Mix(ink, Color.Black, 0.5)), 1.2f) { LineJoin = LineJoin.Round };
			g.DrawPath(edge, path);
		}
		// the shadow, blurred by drawing it small and scaling it back up
		float spad = b.Height * 0.14f;
		int sw = (int)Math.Ceiling(b.Width + spad * 2f), sh = (int)Math.Ceiling(b.Height + spad * 2f);
		using var small = new Bitmap(Math.Max(1, sw / 6), Math.Max(1, sh / 6), PixelFormat.Format32bppPArgb);
		using (Graphics g = Graphics.FromImage(small))
		{
			Setup(g);
			g.ScaleTransform(small.Width / (float)sw, small.Height / (float)sh);
			g.TranslateTransform(spad - b.X, spad - b.Y);
			using var dark = new SolidBrush(Color.FromArgb(215, 0, 0, 0));
			g.FillPath(dark, path);
		}
		var shadow = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
		using (Graphics g = Graphics.FromImage(shadow))
		{
			g.InterpolationMode = InterpolationMode.HighQualityBilinear;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.DrawImage(small, new Rectangle(0, 0, sw, sh));
		}
		return new SlabGlyph { Face = face, Shadow = shadow };
	}

	// ---------- readouts ----------

	/// <summary>
	/// A small reading: the label (Text) above, the value with its unit at the right, and a thin bar beneath filled
	/// with the prism's spectrum, a bright head at its tip and a glint running along it, faster as the reading climbs.
	/// </summary>
	private void PrismBar(Graphics g, Widget w, RectangleF r, double t)
	{
		string unit = Sensors.Def(w.Sensor).Unit;
		string value = Formatted(w) + (unit == "%" ? "%" : unit == "°C" ? "°C" : string.IsNullOrEmpty(unit) ? "" : " " + unit);
		Color ink = ValueColor(w);
		Color label = Imaging.Hex(w.Color2, Color.FromArgb(150, 236, 231, 218));
		float f = (float)Eased(w, Fraction(w), t);
		float bar = Math.Max(2f, w.Thickness);
		float barY = r.Bottom - bar - 2f;
		TextRenderingHint hint = g.TextRenderingHint;
		g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		using (var small = new Font(w.Font, w.Size * 0.6f, FontStyle.Regular, GraphicsUnit.Pixel))
		using (var big = new Font(w.Font, w.Size, FontStyle.Regular, GraphicsUnit.Pixel))
		using (var lb = new SolidBrush(label))
		using (var vb = new SolidBrush(ink))
		{
			var line = new RectangleF(r.X, r.Y, r.Width, barY - r.Y - 4f);
			using var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };
			using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };
			g.DrawString(w.Text, small, lb, new RectangleF(line.X, line.Y, line.Width, line.Height - w.Size * 0.1f), left);
			g.DrawString(value, big, vb, line, right);
		}
		g.TextRenderingHint = hint;
		using (var track = new SolidBrush(Color.FromArgb(34, 236, 231, 218)))
		{
			g.FillRectangle(track, r.X, barY, r.Width, bar);
		}
		float fw = r.Width * f;
		if (fw < 1f)
		{
			return;
		}
		var fillRect = new RectangleF(r.X, barY, fw, bar);
		Color blue = Imaging.Hex(w.Track, Color.FromArgb(47, 107, 255));
		using (var spectrum = new LinearGradientBrush(new RectangleF(r.X - 1f, barY, fw + 2f, bar), Color.Black, Color.Black, 0f))
		{
			spectrum.InterpolationColors = new ColorBlend
			{
				Colors = new[] { Imaging.Mix(blue, Color.Black, 0.35), blue, Imaging.Mix(blue, Color.White, 0.6), Color.FromArgb(255, 253, 244), Color.FromArgb(255, 196, 120), Color.FromArgb(232, 128, 52) },
				Positions = new[] { 0f, 0.3f, 0.52f, 0.64f, 0.8f, 1f }
			};
			g.FillRectangle(spectrum, fillRect);
		}
		// a glint sliding along the filled part
		float gx = r.X + (float)((t * (60.0 + 260.0 * f)) % (fw + 120.0)) - 60f;
		using (var glint = new LinearGradientBrush(new RectangleF(gx - 60f, barY, 120f, bar), Color.Black, Color.Black, 0f))
		{
			glint.InterpolationColors = new ColorBlend
			{
				Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(170, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
				Positions = new[] { 0f, 0.5f, 1f }
			};
			GraphicsState s = g.Save();
			g.SetClip(fillRect);
			g.FillRectangle(glint, gx - 60f, barY, 120f, bar);
			g.Restore(s);
		}
		// the bright head, with a soft bloom
		float hx = r.X + fw;
		using (var bloom = new SolidBrush(Color.FromArgb(60, 255, 240, 220)))
		{
			g.FillEllipse(bloom, hx - bar * 2.6f, barY + bar / 2f - bar * 2.6f, bar * 5.2f, bar * 5.2f);
		}
		using (var head = new SolidBrush(Color.FromArgb(255, 255, 252, 244)))
		{
			g.FillEllipse(head, hx - bar * 1.1f, barY + bar / 2f - bar * 1.1f, bar * 2.2f, bar * 2.2f);
		}
	}

	/// <summary>A fine rule in Color, tapering to points at both ends, with a small diamond and two dots at its middle.</summary>
	private static void Ornament(Graphics g, Widget w, RectangleF r)
	{
		Color ink = Imaging.Hex(w.Color, Color.FromArgb(236, 231, 218));
		float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
		using var brush = new SolidBrush(ink);
		foreach (int side in new[] { -1, 1 })
		{
			g.FillPolygon(brush, new[]
			{
				new PointF(cx + side * r.Width / 2f, cy), new PointF(cx + side * 16f, cy - 1.6f), new PointF(cx + side * 9f, cy), new PointF(cx + side * 16f, cy + 1.6f)
			});
			g.FillEllipse(brush, cx + side * 9f - 1.8f, cy - 1.8f, 3.6f, 3.6f);
		}
		g.FillPolygon(brush, new[] { new PointF(cx, cy - 4.5f), new PointF(cx + 4.5f, cy), new PointF(cx, cy + 4.5f), new PointF(cx - 4.5f, cy) });
	}
}
