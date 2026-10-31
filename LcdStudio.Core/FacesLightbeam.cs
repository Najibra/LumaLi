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
		public double At = double.NaN, Lift, LiftV, Ring, RingV, Wave, Twist, Swell, Flow2, Flow3, Cable, Surge, Kick, KickV;

		public float Last = float.NaN;

		public string From = "", To = "";

		public double RollAt = double.NegativeInfinity;
	}

	private const float LightHy = 252f, LightImpact = 345f;

	private const int LightStrands = 26;

	private readonly Dictionary<string, LightState> _lights = new Dictionary<string, LightState>();

	private readonly Dictionary<(char, float), float> _lightAdvance = new Dictionary<(char, float), float>();

	private Bitmap? _lightPage;

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
		// a real jump in the reading sends a ripple through the strands, swelling in on a spring rather than snapping on;
		// the small changes every reading makes each second are left alone, so the light never twitches
		if (!float.IsNaN(s.Last) && Math.Abs(x.Fraction - s.Last) > 0.1f)
		{
			s.Surge = Math.Min(1.2, s.Surge + Math.Abs(x.Fraction - s.Last) * 3f);
		}
		s.Last = x.Fraction;
		s.Surge *= Math.Exp(-dt * 1.4);
		LightSpring(ref s.Kick, ref s.KickV, s.Surge, 3.5, dt);
		float lift = (float)Math.Clamp(s.Lift, 0.0, 1.0), kick = (float)s.Kick;
		s.Wave += dt * (0.5 + 2.2 * lift);
		// the three flowing waves each keep their own pace, so their pattern never visibly repeats, and the rope's turn
		// drifts in pace a little, as a rope eases and tightens: the same motion as the screen layout
		double t = x.T;
		s.Twist += dt * (0.22 + 0.5 * lift);
		s.Flow2 += dt * (0.31 + 0.6 * lift);
		s.Flow3 += dt * (0.5 + 0.9 * lift);
		s.Cable += dt * (0.35 + 0.9 * lift) * (1.0 + 0.3 * Math.Sin(t * 0.17));
		s.Swell = (s.Swell + dt * (0.075 + 0.1 * lift)) % 1.0;
		float heat = Math.Clamp((lift - 0.75f) / 0.25f, 0f, 1f);
		Color warm = Imaging.Mix(x.C1, Color.FromArgb(255, 150, 96), heat * 0.8);
		Color flank = Imaging.Mix(x.C2, Color.FromArgb(214, 120, 150), heat * 0.5);
		// the crest sits right over the number, so the strands arch high and clear of it on both sides
		float cx = 200f + 10f * (float)Math.Sin(t * 0.11), arch = 150f + 60f * lift;
		// the swell runs from well off the left to well off the right, so where it wraps round it touches nothing
		float swellX = -240f + (float)s.Swell * 880f, wave = (float)s.Wave;
		float flow1 = (float)s.Twist, flow2 = (float)s.Flow2, flow3 = (float)s.Flow3, cable = (float)s.Cable, wind = (float)(t * 0.05);
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
			// each strand's place across the bundle, in order with a little scatter, so neighbours move together
			S[k] = (k + 0.5f) / LightStrands - 0.5f + (h1 - 0.5f) * 0.15f;
			C[k] = cx + (h2 - 0.5f) * 30f;
			L[k] = (150f + 190f * h3) * reach + 14f * (float)Math.Sin(t * 0.23 + k * 0.7);
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
			// each strand wanders in and out along its length and over time, so they cross and part like filaments
			float wander = 0.05f * (float)Math.Sin(px * 0.019f + k * 1.37f + t * 0.21) + 0.03f * (float)Math.Sin(px * 0.041f - k * 2.11f - t * 0.17);
			float height = A[k] * (0.93f + S[k] * 0.3f * open) * (1f + wander) + 26f * bump;
			// the flow, like strands of silk in a current: three slow waves of different lengths and speeds shifting
			// gradually across the bundle, strongest on the flanks and still alive over the crest
			float u = S[k] + 0.5f, env = (0.3f + 2.8f * h * (1f - h)) * (1f + 0.6f * bump);
			float turn = env * 0.5f * (16f * (float)Math.Sin(px * 0.0144f + 5f * u - flow1)
				+ 8f * (float)Math.Sin(px * 0.0275f - 9.5f * u + flow2 + 1.3f)
				+ 3.2f * (float)Math.Sin(px * 0.057f + 19f * u - flow3 + 0.7f));
			// and the rope over it: the strands laid round the bundle in order, the lay looser and tighter in stretches
			// that wander along the beam, widest on the flanks and gathered in over the crest
			float lay = px * 0.02f + 0.9f * (float)Math.Sin(px * 0.0068f + wind);
			float strand = 0.65f + 0.25f * (Amp[k] - 0.5f) + 0.15f * (float)Math.Sin(t * 0.4 + k * 1.7);
			float rope = (2.5f + 38f * h * (1f - h)) * (1f + 1.2f * bump) * strand;
			turn += rope * (float)Math.Sin(u * 6.2832f + lay - cable);
			float weave = Amp[k] * 0.6f * (float)Math.Sin(px * Fq[k] - wave + Ph[k]) + kick * 7f * (float)Math.Sin(px * 0.05f - t * 3.0 + k * 0.2);
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

		LightSkia(g, Y, strands, cx, arch, lift, flare, warm, flank, t, x.C2);
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
