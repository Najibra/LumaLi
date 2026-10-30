using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// The Lightbeam face for the round fan LCDs, matching the Lightbeam screen layout: a bundle of light strands arching
/// over a black horizon, warm at the crest (C1) and cool on the flanks (C2), landing in a white-hot flare and mirrored
/// dimly in the floor. The strands are wound like a slowly turning cable, a swell travels along the beam stretching
/// them apart, and the fan breathes open and closed; the arch rises with the reading and everything moves faster the
/// busier it is. The reading sits under the arch in thin type whose digits roll like an odometer when it changes, with
/// its share as a hairline round the rim. Each fan keeps its own phase, so five fans side by side never move in step.
/// </summary>
public sealed partial class Faces
{
	private sealed class LightState
	{
		public double At = double.NaN, Lift, LiftV, Ring, RingV, Wave, Twist, Swell;

		public string From = "", To = "";

		public double RollAt = double.NegativeInfinity;
	}

	private const float LightHy = 252f, LightImpact = 345f;

	private const int LightStrands = 26;

	private readonly Dictionary<string, LightState> _lights = new Dictionary<string, LightState>();

	private readonly Dictionary<(char, float), float> _lightAdvance = new Dictionary<(char, float), float>();

	private Bitmap? _lightGlow, _lightBloom, _lightPage;

	private int _lightPageKey;

	private void Lightbeam(FaceContext x)
	{
		Graphics g = x.G;
		if (!_lights.TryGetValue(x.Fan.Serial, out LightState? s))
		{
			_lights[x.Fan.Serial] = s = new LightState { Swell = Hash01(x.Fan.Serial.Sum((char c) => c)) };
		}
		double dt = double.IsNaN(s.At) || x.T < s.At ? 0.0 : Math.Min(0.5, x.T - s.At);
		if (double.IsNaN(s.At))
		{
			s.Lift = s.Ring = x.Fraction;
		}
		s.At = x.T;
		// the arch and the rim follow the reading on critically damped springs: no jolt when it changes
		LightSpring(ref s.Lift, ref s.LiftV, x.Fraction, 2.4, dt);
		LightSpring(ref s.Ring, ref s.RingV, x.Fraction, 3.0, dt);
		float lift = (float)Math.Clamp(s.Lift, 0.0, 1.0);
		s.Wave += dt * (0.5 + 2.2 * lift);
		s.Twist += dt * (0.45 + 1.5 * lift);
		s.Swell = (s.Swell + dt * (0.075 + 0.1 * lift)) % 1.0;
		double t = x.T;
		float heat = Math.Clamp((lift - 0.75f) / 0.25f, 0f, 1f);
		Color warm = Imaging.Mix(x.C1, Color.FromArgb(255, 150, 96), heat * 0.8);
		Color flank = Imaging.Mix(x.C2, Color.FromArgb(214, 120, 150), heat * 0.5);
		// the crest sits right over the number, so the strands arch clear of it on both sides
		float cx = 200f + 10f * (float)Math.Sin(t * 0.11), arch = 128f + 46f * lift;
		// the swell runs from well off the left to well off the right, so where it wraps round it touches nothing
		float swellX = -240f + (float)s.Swell * 880f, twist = (float)s.Twist, wave = (float)s.Wave;
		float breathe = (float)Math.Sin(t * 0.29), reach = 1f + 0.2f * (float)Math.Sin(t * 0.21 + 1.3);
		var A = new float[LightStrands];
		var S = new float[LightStrands];
		var C = new float[LightStrands];
		var L = new float[LightStrands];
		var R = new float[LightStrands];
		var Amp = new float[LightStrands];
		var Fq = new float[LightStrands];
		var Ph = new float[LightStrands];
		for (int k = 0; k < LightStrands; k++)
		{
			float h1 = (float)Hash01(k * 17 + 3), h2 = (float)Hash01(k * 29 + 11), h3 = (float)Hash01(k * 41 + 5), h4 = (float)Hash01(k * 53 + 7);
			A[k] = arch * (1f + 0.035f * (float)Math.Sin(t * 0.7 + k));
			S[k] = h1 - 0.5f;
			C[k] = cx + (h2 - 0.5f) * 20f;
			L[k] = (130f + 170f * h3) * reach + 12f * (float)Math.Sin(t * 0.23 + k * 0.7);
			R[k] = LightImpact - 4f + 8f * h4;
			Amp[k] = 0.5f + 2f * h2;
			Fq[k] = 0.014f + 0.016f * h3;
			Ph[k] = h1 * 6.2832f;
		}
		float Y(int k, float px)
		{
			float h = px >= R[k] ? 0f
				: px >= C[k] ? 0.5f + 0.5f * (float)Math.Cos(Math.PI * (px - C[k]) / (R[k] - C[k]))
				: 0.5f + 0.5f * (float)Math.Cos(Math.PI * Math.Min(1f, (C[k] - px) / L[k]));
			// the swell lifts the beam and tears the fan open as it passes; the breathing opens and closes it
			float sw = (px - swellX) / 70f, bump = (float)Math.Exp(-sw * sw);
			float open = 1f + 0.45f * breathe + 1.7f * bump;
			float height = A[k] * (0.93f + S[k] * 0.3f * open) + 26f * bump;
			// the strands set round the bundle like the wires of a cable, turning as it runs
			float radius = (1.5f + 50f * h * (1f - h)) * (1f + 1.4f * bump);
			float turn = radius * (float)Math.Sin(k * 6.2832f / LightStrands + px * 0.024f - twist);
			float weave = Amp[k] * 0.6f * (float)Math.Sin(px * Fq[k] - wave + Ph[k]);
			return LightHy - height * h - h * (weave + turn) + (1f - h) * (k % 5 - 2) * 0.4f;
		}
		var strands = new PointF[LightStrands][];
		for (int k = 0; k < LightStrands; k++)
		{
			var pts = new PointF[43];
			for (int i = 0; i < pts.Length; i++)
			{
				float px = -10f + i * 10f;
				pts[i] = new PointF(px, Y(k, px));
			}
			strands[k] = pts;
		}
		float flare = 0.6f + 0.35f * lift + 0.08f * (float)Math.Sin(t * 0.9);

		// the page: near-black with a cold haze along the horizon and the floor falling to black, composed once per colour
		if (_lightPage == null || _lightPageKey != x.C2.ToArgb())
		{
			_lightPage?.Dispose();
			_lightPage = LightPage(x.C2);
			_lightPageKey = x.C2.ToArgb();
		}
		CompositingMode mode = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_lightPage, 0, 0);
		g.CompositingMode = mode;

		// the soft light at a quarter size: warm smoke behind the crest, the strands wide and faint, and the landing
		_lightGlow ??= new Bitmap(100, 100, PixelFormat.Format32bppPArgb);
		_lightBloom ??= new Bitmap(25, 25, PixelFormat.Format32bppPArgb);
		using (Graphics gg = Graphics.FromImage(_lightGlow))
		{
			gg.Clear(Color.Transparent);
			gg.ScaleTransform(0.25f, 0.25f);
			gg.SmoothingMode = SmoothingMode.AntiAlias;
			using (var smoke = new GraphicsPath())
			{
				smoke.AddEllipse(cx - 150f, LightHy - arch * 0.8f - 80f, 300f, 160f);
				using var sb = new PathGradientBrush(smoke) { CenterColor = Color.FromArgb(110, Imaging.Mix(warm, Color.Black, 0.6)), SurroundColors = new[] { Color.FromArgb(0, warm) } };
				gg.FillPath(sb, smoke);
			}
			using (LinearGradientBrush faint = LightBrush(cx, warm, flank, 26))
			using (var pen = new Pen(faint, 9f))
			{
				foreach (PointF[] pts in strands)
				{
					gg.DrawLines(pen, pts);
				}
			}
			using var spot = new SolidBrush(Color.FromArgb((int)Math.Min(255f, 90f * flare), 230, 236, 255));
			gg.FillEllipse(spot, LightImpact - 30f, LightHy - 8f, 80f, 16f);
		}
		using (Graphics gb = Graphics.FromImage(_lightBloom))
		{
			gb.Clear(Color.Transparent);
			gb.InterpolationMode = InterpolationMode.HighQualityBilinear;
			gb.DrawImage(_lightGlow, new Rectangle(0, 0, 25, 25));
		}
		InterpolationMode interp = g.InterpolationMode;
		PixelOffsetMode offset = g.PixelOffsetMode;
		g.InterpolationMode = InterpolationMode.Bilinear;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		// mirrored in the floor, squashed and dim, fading toward the foot
		GraphicsState floor = g.Save();
		g.SetClip(new RectangleF(0f, LightHy + 1f, 400f, 400f - LightHy));
		PointF[] mirror = { new PointF(0f, LightHy * 1.5f), new PointF(400f, LightHy * 1.5f), new PointF(0f, LightHy - (400f - LightHy) * 0.5f) };
		LightBlit(g, _lightBloom, mirror, 0.7f);
		LightBlit(g, _lightGlow, mirror, 0.55f);
		using (var fade = new LinearGradientBrush(new PointF(0f, LightHy), new PointF(0f, 401f), Color.FromArgb(0, 1, 2, 5), Color.FromArgb(215, 1, 2, 5)))
		{
			g.FillRectangle(fade, 0f, LightHy + 1f, 400f, 400f - LightHy);
		}
		g.Restore(floor);
		PointF[] whole = { new PointF(0f, 0f), new PointF(400f, 0f), new PointF(0f, 400f) };
		LightBlit(g, _lightBloom, whole, 1f);
		LightBlit(g, _lightGlow, whole, 1f);
		g.InterpolationMode = interp;
		g.PixelOffsetMode = offset;

		// the sharp strands in three weights, each shimmering on its own slow beat
		for (int tier = 0; tier < 3; tier++)
		{
			float shimmer = 0.85f + 0.15f * (float)Math.Sin(t * 1.1 + tier * 2.1);
			using LinearGradientBrush b = LightBrush(cx, warm, flank, (int)(new[] { 80, 150, 235 }[tier] * shimmer));
			using var pen = new Pen(b, tier == 2 ? 1.3f : 1f);
			for (int k = tier; k < LightStrands; k += 3)
			{
				g.DrawLines(pen, strands[k]);
			}
		}
		// the horizon, brightening to the landing and burning on to the edge, and the landing flare breathing on it
		using (var line = new LinearGradientBrush(new PointF(-1f, 0f), new PointF(401f, 0f), Color.Black, Color.Black))
		{
			line.InterpolationColors = new ColorBlend
			{
				Colors = new[] { Color.FromArgb(40, flank), Color.FromArgb(110, flank), Color.FromArgb(255, 240, 244, 255), Color.FromArgb(200, Imaging.Mix(flank, Color.White, 0.6)) },
				Positions = new[] { 0f, 0.5f, LightImpact / 402f, 1f }
			};
			using var pen = new Pen(line, 1.2f);
			g.DrawLine(pen, 0f, LightHy, 400f, LightHy);
		}
		LightFlare(g, new RectangleF(LightImpact - 70f, LightHy - 20f, 140f, 40f), Color.FromArgb(150, 170, 255), 120f * flare);
		LightFlare(g, new RectangleF(LightImpact - 150f, LightHy - 3f, 300f, 6f), Color.FromArgb(220, 230, 255), 190f * flare);
		LightFlare(g, new RectangleF(LightImpact - 24f, LightHy - 6f, 48f, 12f), Color.White, 240f * flare);
		// embers lifting off the crest, more under load, each born and dying at nothing so the drift loops without a seam
		for (int i = 0; i < 16; i++)
		{
			float share = Math.Clamp(3f + 13f * lift - i, 0f, 1f);
			if (share <= 0f)
			{
				continue;
			}
			float e1 = (float)Hash01(i * 13 + 1), e2 = (float)Hash01(i * 19 + 2), e3 = (float)Hash01(i * 23 + 3);
			float life = (float)((t * (0.16 + 0.12 * e1) + e2) % 1.0);
			float x0 = cx + (e3 - 0.5f) * 170f;
			float ex = x0 + (e1 - 0.5f) * 20f * life, ey = Y(i % LightStrands, x0) - 22f * life * (0.5f + e2);
			float size = 1.2f + 1.6f * e1;
			using var ember = new SolidBrush(Color.FromArgb((int)(share * Math.Sin(Math.PI * life) * (110f + 120f * lift)), Imaging.Mix(warm, Color.White, 0.3)));
			g.FillEllipse(ember, ex - size / 2f, ey - size / 2f, size, size);
		}
		// the reading's share as a hairline round the rim
		using (var track = new Pen(Color.FromArgb(30, 255, 255, 255), 2f))
		{
			g.DrawEllipse(track, 12f, 12f, 376f, 376f);
		}
		float ring = (float)Math.Clamp(s.Ring, 0.0, 1.0) * 360f;
		if (ring > 0.3f)
		{
			using var arc = new Pen(Color.FromArgb(180, warm), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawArc(arc, 12f, 12f, 376f, 376f, -90f, ring);
		}
		Color soft = Color.FromArgb(170, 176, 190);
		LightNumber(g, s, x.Value, t, Color.FromArgb(236, 238, 246), soft);
		Small(g, x.Label, Color.FromArgb(150, soft), 292f, 11f, bold: true, 1f);
		Small(g, x.Time, Color.FromArgb(200, 232, 234, 244), 322f, 19f, bold: false);
	}

	/// <summary>
	/// The reading in thin type under the arch, its digits rolling like an odometer when it changes: only the characters
	/// that differ move, the old rolling up and out as the new rolls up from below, crossfading over about a third of a
	/// second while the width eases; drawn as outlines, so it sits between pixels and never ticks. Mirrored in the floor.
	/// </summary>
	private void LightNumber(Graphics g, LightState s, string value, double t, Color ink, Color soft)
	{
		if (t < s.RollAt || s.To.Length == 0)
		{
			s.From = s.To = value;
			s.RollAt = double.NegativeInfinity;
		}
		else if (s.To != value)
		{
			s.From = s.To;
			s.To = value;
			s.RollAt = t;
		}
		float p = (float)Math.Clamp((t - s.RollAt) / 0.35, 0.0, 1.0), e = p * p * (3f - 2f * p);
		(string a, string ua) = LightSplit(s.From);
		(string b, string ub) = LightSplit(s.To);
		const float size = 84f, baseline = 238f;
		FontFamily family = Family("Segoe UI Light");
		float ascent = size * family.GetCellAscent(FontStyle.Regular) / family.GetEmHeight(FontStyle.Regular);
		float Adv(char ch)
		{
			if (!_lightAdvance.TryGetValue((ch, size), out float w))
			{
				using var f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
				w = _lightAdvance[(ch, size)] = g.MeasureString(ch.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width;
			}
			return w;
		}
		float wa = a.Sum(Adv), wb = b.Sum(Adv), width = wa + (wb - wa) * e;
		// long readings shrink to fit under the arch, easing as they do
		float fit = Math.Min(1f, 190f / Math.Max(1f, width)), right = 200f + width * fit / 2f, travel = size * 0.5f * fit;
		var glyphs = new List<(GraphicsPath Path, float Alpha)>();
		void Glyph(char ch, float left, float dy, float alpha)
		{
			if (alpha < 0.01f)
			{
				return;
			}
			var path = new GraphicsPath();
			path.AddString(ch.ToString(), family, (int)FontStyle.Regular, size * fit, new PointF(left, baseline - ascent * fit + dy), StringFormat.GenericTypographic);
			glyphs.Add((path, alpha));
		}
		float xa = right, xb = right;
		for (int i = 1; i <= Math.Max(a.Length, b.Length); i++)
		{
			char? ca = i <= a.Length ? a[^i] : null, cb = i <= b.Length ? b[^i] : null;
			xa -= ca.HasValue ? Adv(ca.Value) * fit : 0f;
			xb -= cb.HasValue ? Adv(cb.Value) * fit : 0f;
			if (ca == cb && Math.Abs(xa - xb) < 0.5f)
			{
				Glyph(cb!.Value, xb, 0f, 1f);
				continue;
			}
			if (ca.HasValue)
			{
				Glyph(ca.Value, xa, -travel * e, 1f - e);
			}
			if (cb.HasValue)
			{
				Glyph(cb.Value, xb, travel * (1f - e), e);
			}
		}
		// the reflection first, then the type
		GraphicsState st = g.Save();
		g.SetClip(new RectangleF(0f, LightHy + 2f, 400f, 400f - LightHy));
		g.TranslateTransform(0f, LightHy);
		g.ScaleTransform(1f, -0.7f);
		g.TranslateTransform(0f, -LightHy);
		foreach (var (path, alpha) in glyphs)
		{
			using var mirror = new SolidBrush(Color.FromArgb((int)(24 * alpha), ink));
			g.FillPath(mirror, path);
		}
		g.Restore(st);
		foreach (var (path, alpha) in glyphs)
		{
			using var brush = new SolidBrush(Color.FromArgb((int)(255 * alpha), ink));
			g.FillPath(brush, path);
			path.Dispose();
		}
		// the unit, small after the number, crossfading if it changes
		using var unitFont = new Font(family, 26f, FontStyle.Regular, GraphicsUnit.Pixel);
		float unitTop = baseline - 26f * family.GetCellAscent(FontStyle.Regular) / family.GetEmHeight(FontStyle.Regular);
		foreach (var (unit, alpha) in ua == ub ? new[] { (ub, 1f) } : new[] { (ua, 1f - e), (ub, e) })
		{
			if (unit.Length > 0 && alpha > 0.01f)
			{
				using var brush = new SolidBrush(Color.FromArgb((int)(170 * alpha), soft));
				g.DrawString(unit, unitFont, brush, right + 5f, unitTop, StringFormat.GenericTypographic);
			}
		}
	}

	/// <summary>Splits a reading such as "67%" or "1.2G" into its figures and its unit.</summary>
	private static (string, string) LightSplit(string value)
	{
		int i = value.Length;
		while (i > 0 && !char.IsDigit(value[i - 1]) && value[i - 1] != '-')
		{
			i--;
		}
		return (value.Substring(0, i), value.Substring(i));
	}

	/// <summary>The strands' colour across the face: cool flanks, warm over the crest, white-hot at the landing.</summary>
	private static LinearGradientBrush LightBrush(float cx, Color warm, Color flank, int alpha)
	{
		var b = new LinearGradientBrush(new PointF(-11f, 0f), new PointF(411f, 0f), Color.Black, Color.Black);
		float P(float px) => Math.Clamp((px + 11f) / 422f, 0.001f, 0.999f);
		Color Ac(Color c, float m) => Color.FromArgb(Math.Clamp((int)(alpha * m), 0, 255), c);
		Color white = Color.FromArgb(236, 240, 255);
		b.InterpolationColors = new ColorBlend
		{
			Colors = new[] { Ac(flank, 0.35f), Ac(flank, 0.8f), Ac(warm, 1f), Ac(Imaging.Mix(warm, flank, 0.55), 0.85f), Ac(Imaging.Mix(flank, white, 0.5), 0.8f), Ac(white, 1f), Ac(Imaging.Mix(flank, white, 0.6), 0.75f) },
			Positions = new[] { 0f, P(cx - 110f), P(cx), P(cx + 70f), P(LightImpact - 40f), P(LightImpact), 1f }
		};
		return b;
	}

	private static void LightBlit(Graphics g, Bitmap bmp, PointF[] dest, float alpha)
	{
		using var ia = new ImageAttributes();
		ia.SetWrapMode(WrapMode.TileFlipXY);
		ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(alpha, 0f, 1f) });
		g.DrawImage(bmp, dest, new RectangleF(0f, 0f, bmp.Width, bmp.Height), GraphicsUnit.Pixel, ia);
	}

	private static void LightFlare(Graphics g, RectangleF r, Color c, float alpha)
	{
		using var p = new GraphicsPath();
		p.AddEllipse(r);
		using var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb((int)Math.Clamp(alpha, 0f, 255f), c), SurroundColors = new[] { Color.FromArgb(0, c) } };
		g.FillPath(b, p);
	}

	private static Bitmap LightPage(Color flank)
	{
		var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.Clear(Color.FromArgb(2, 3, 6));
		using (var haze = new GraphicsPath())
		{
			haze.AddEllipse(60f, LightHy - 70f, 520f, 140f);
			using var hb = new PathGradientBrush(haze) { CenterPoint = new PointF(LightImpact, LightHy), CenterColor = Imaging.Mix(flank, Color.FromArgb(2, 3, 6), 0.84), SurroundColors = new[] { Color.FromArgb(2, 3, 6) } };
			g.FillPath(hb, haze);
		}
		using (var floor = new LinearGradientBrush(new PointF(0f, LightHy), new PointF(0f, 401f), Color.FromArgb(0, 1, 2, 5), Color.FromArgb(200, 1, 2, 5)))
		{
			g.FillRectangle(floor, 0f, LightHy + 1f, 400f, 400f - LightHy);
		}
		return bmp;
	}

	/// <summary>A critically damped spring: sets off gently and settles without overshoot, with no kink when the target moves.</summary>
	private static void LightSpring(ref double x, ref double v, double target, double w, double dt)
	{
		for (double left = dt; left > 1e-9; left -= 0.02)
		{
			double h = Math.Min(0.02, left);
			v += (w * w * (target - x) - 2.0 * w * v) * h;
			x += v * h;
		}
	}
}
