using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// Lightbeam: a bundle of fine light strands arching over a black horizon, warm white at the crest and cool lilac on
/// the flanks, landing in a hot flare at the right and running on along the horizon line, all of it mirrored dimly in
/// the floor. The arch rises with the reading in focus, light pulses race down the strands as fast as it is busy, a
/// jump in the reading or a change of channel sends a ripple through them, and embers drift off the crest under load.
/// One thin number sits under the arch, a gear-style selector at the foot slides between channels every few seconds
/// (straight to any channel that runs hot, which turns the light ember red), and a side reading, its two-minute trace
/// and the time sit either side.
/// </summary>
public sealed partial class ScreenRenderer
{
	private sealed record BeamReading(string Label, string Key, string Unit, string Format, float Warn = 0f);

	private sealed record BeamChannel(string Name, string Title, BeamReading Main, float Max, Color Tint, BeamReading[] Side);

	private static readonly BeamChannel[] BeamChannels =
	{
		new("CPU", "PROCESSOR", new("Load", "cpu.load", "%", "0", 95f), 100f, Color.FromArgb(146, 160, 240),
			new BeamReading[] { new("Temperature", "cpu.temp", "°C", "0", 90f), new("Busiest thread", "cpu.busiest", "%", "0") }),
		new("GPU", "GRAPHICS", new("Load", "gpu.load", "%", "0", 99f), 100f, Color.FromArgb(172, 146, 244),
			new BeamReading[] { new("Temperature", "gpu.temp", "°C", "0", 83f), new("Hot spot", "gpu.hotspot", "°C", "0", 100f) }),
		new("MEM", "MEMORY", new("In use", "ram.load", "%", "0", 90f), 100f, Color.FromArgb(124, 180, 236),
			new BeamReading[] { new("Used", "ram.used", "GB", "0.0") }),
		new("NET", "NETWORK", new("Download", "net.down", "MB/s", "0.0"), 0f, Color.FromArgb(110, 196, 240),
			new BeamReading[] { new("Upload", "net.up", "MB/s", "0.0") }),
		new("PWR", "POWER", new("Graphics power", "gpu.power", "W", "0"), 450f, Color.FromArgb(206, 150, 214),
			new BeamReading[] { new("Processor power", "cpu.power", "W", "0"), new("Graphics clock", "gpu.clock", "MHz", "0") }),
		new("FAN", "COOLING", new("Graphics fan", "gpu.fan", "%", "0"), 100f, Color.FromArgb(132, 212, 222),
			new BeamReading[] { new("Case fans", "fans.rpm", "rpm", "0"), new("Fan duty", "fans.duty", "%", "0") })
	};

	private const float BeamHy = 330f, BeamImpact = 1560f;

	private const int BeamStrands = 38;

	private static readonly Color BeamWarm = Color.FromArgb(255, 236, 220), BeamHot = Color.FromArgb(255, 164, 104), BeamAlarm = Color.FromArgb(255, 96, 78), BeamText = Color.FromArgb(232, 234, 244);

	private Bitmap? _beamPage, _beamHaze, _beamFlare, _beamGlow, _beamBloom, _beamFrame;

	private int _beamFocus, _beamPrev;

	private double _beamFocusAt = double.NaN, _beamAt = double.NaN, _beamWave, _beamSurge, _beamKick, _beamKickV, _beamSlide, _beamSlideV, _beamTarget, _beamCx, _beamLift = double.NaN, _beamLiftV, _beamHeat, _beamTwist, _beamFlow2, _beamFlow3, _beamCable, _beamSwell;

	private float _beamLast = float.NaN;

	private readonly float[] _bA = new float[BeamStrands], _bS = new float[BeamStrands], _bC = new float[BeamStrands], _bL = new float[BeamStrands], _bR = new float[BeamStrands], _bAmp = new float[BeamStrands], _bFq = new float[BeamStrands], _bPh = new float[BeamStrands];

	private bool DrawLightbeam(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "lightbeam" || w.Kind != WidgetKind.Panel)
		{
			return DrawStandby(g, w, r, t);
		}
		Lightbeam(g, t);
		return true;
	}

	// ---------- readings ----------

	private bool BeamOnline(BeamReading b) => _sensors.Get(b.Key).HasValue && (Val(b.Key) > 0f || b.Key.StartsWith("net.") || b.Key.EndsWith("fan"));

	private bool BeamWarn(BeamChannel c) => new[] { c.Main }.Concat(c.Side).Any(b => b.Warn > 0f && Val(b.Key) >= b.Warn);

	private float BeamMax(BeamChannel c)
	{
		if (c.Max > 0f)
		{
			return c.Max;
		}
		float[] h = _sensors.History(c.Main.Key);
		// eased, so the trace and the arch never snap when an old peak leaves the two-minute window
		return (float)DeckEase("beam|max|" + c.Name, Math.Max(1f, h.Length > 0 ? h.Max() * 1.1f : 1f), _beamAt);
	}

	private float BeamFraction(BeamChannel c) => Math.Clamp(Val(c.Main.Key) / BeamMax(c), 0f, 1f);

	/// <summary>
	/// On to the next live channel every seven seconds; while anything runs hot, only the hot channels take turns.
	/// </summary>
	private void BeamScan(double t)
	{
		if (double.IsNaN(_beamFocusAt) || t < _beamFocusAt)
		{
			_beamFocusAt = t;
		}
		List<int> alarms = Enumerable.Range(0, BeamChannels.Length).Where(i => BeamWarn(BeamChannels[i])).ToList();
		List<int> pool = alarms.Count > 0 ? alarms : Enumerable.Range(0, BeamChannels.Length).Where(i => BeamOnline(BeamChannels[i].Main)).ToList();
		if (pool.Count == 0)
		{
			return;
		}
		if (!pool.Contains(_beamFocus))
		{
			BeamMove(pool[0], t);
		}
		else if (t - _beamFocusAt >= 7.0)
		{
			BeamMove(pool.FirstOrDefault(i => i > _beamFocus, pool[0]), t);
		}
	}

	private void BeamMove(int i, double t)
	{
		if (i != _beamFocus)
		{
			_beamSurge = 1.0;
			_beamPrev = _beamFocus;
			// the selector turns forward round the ring, never back
			_beamTarget += ((i - _beamFocus) % BeamChannels.Length + BeamChannels.Length) % BeamChannels.Length;
		}
		_beamFocus = i;
		_beamFocusAt = t;
	}

	// ---------- the scene ----------

	private void Lightbeam(Graphics g, double t)
	{
		double dt = double.IsNaN(_beamAt) || t < _beamAt ? 0.0 : Math.Min(0.5, t - _beamAt);
		_beamAt = t;
		BeamScan(t);
		BeamChannel ch = BeamChannels[_beamFocus];
		float f = BeamFraction(ch);
		bool warn = BeamWarn(ch);
		if (double.IsNaN(_beamLift))
		{
			_beamLift = f;
			_beamTarget = _beamFocus;
			_beamSlide = _beamFocus;
			_beamPrev = _beamFocus;
		}
		// a jump in the reading sends a ripple down the strands, as a change of channel does; the ripple swells in
		// rather than snapping on
		// only a real jump counts: the small changes every reading makes each second would keep the light twitching
		if (!float.IsNaN(_beamLast) && Math.Abs(f - _beamLast) > 0.1f)
		{
			_beamSurge = Math.Min(1.2, _beamSurge + Math.Abs(f - _beamLast) * 3f);
		}
		_beamLast = f;
		_beamSurge *= Math.Exp(-dt * 1.4);
		// everything that follows a target does so on a critically damped spring, so it eases out of rest as well as into it
		BeamSpring(ref _beamKick, ref _beamKickV, _beamSurge, 3.5, dt);
		BeamSpring(ref _beamLift, ref _beamLiftV, f, 2.4, dt);
		BeamSpring(ref _beamSlide, ref _beamSlideV, _beamTarget, 3.6, dt);
		// the crest drifts with the selector round the ring, so it too comes round without a jump
		_beamCx = 650.0 + 90.0 * Math.Sin(_beamSlide * Math.PI * 2.0 / BeamChannels.Length);
		double heatTarget = warn ? 1.0 : Math.Clamp((f - 0.65) / 0.35, 0.0, 1.0) * 0.7;
		_beamHeat += (heatTarget - _beamHeat) * (1.0 - Math.Exp(-dt * 1.5));
		// the strands weave quicker, the bundle flows faster and the swell comes round sooner the busier the reading;
		// the three flowing waves each keep their own pace, so their pattern never visibly repeats
		double busy = Math.Clamp(_beamLift, 0.0, 1.0);
		_beamWave += dt * (0.5 + 2.2 * busy);
		_beamTwist += dt * (0.22 + 0.5 * busy);
		_beamFlow2 += dt * (0.31 + 0.6 * busy);
		_beamFlow3 += dt * (0.5 + 0.9 * busy);
		// the rope's turn, its pace itself drifting a little, as a rope eases and tightens
		_beamCable += dt * (0.35 + 0.9 * busy) * (1.0 + 0.3 * Math.Sin(t * 0.17));
		_beamSwell = (_beamSwell + dt * (0.075 + 0.1 * busy)) % 1.0;
		float lift = (float)busy, heat = (float)_beamHeat, kick = (float)_beamKick;
		// the swell runs from well off the left to well off the right, so where it wraps round it touches nothing
		float swellX = -800f + (float)_beamSwell * 3000f, flow1 = (float)_beamTwist, flow2 = (float)_beamFlow2, flow3 = (float)_beamFlow3, cable = (float)_beamCable, wind = (float)(t * 0.05);
		// the fan slowly opens and closes, and the arch stretches out to the left and relaxes back
		float breathe = (float)Math.Sin(t * 0.29), reach = 1f + 0.2f * (float)Math.Sin(t * 0.21 + 1.3);
		float cx = (float)_beamCx + 30f * (float)Math.Sin(t * 0.11);
		float arch = 220f + 95f * lift;
		Color warm = heat <= 0.7f ? Imaging.Mix(BeamWarm, BeamHot, heat / 0.7f) : Imaging.Mix(BeamHot, BeamAlarm, (heat - 0.7f) / 0.3f);
		Color tint = Color.FromArgb((int)DeckEase("beam|r", ch.Tint.R, t), (int)DeckEase("beam|g", ch.Tint.G, t), (int)DeckEase("beam|b", ch.Tint.B, t));
		Color flank = Imaging.Mix(tint, Color.FromArgb(214, 120, 150), heat * 0.5f);
		// every strand: its own height, crest, reach to the left, landing point and weave
		for (int k = 0; k < BeamStrands; k++)
		{
			float h1 = (float)Hash01(k * 17 + 3), h2 = (float)Hash01(k * 29 + 11), h3 = (float)Hash01(k * 41 + 5), h4 = (float)Hash01(k * 53 + 7);
			_bA[k] = arch * (1f + 0.035f * (float)Math.Sin(t * 0.7 + k));
			// each strand's place across the bundle, in order with a little scatter, so neighbours move together
			_bS[k] = (k + 0.5f) / BeamStrands - 0.5f + (h1 - 0.5f) * 0.15f;
			_bC[k] = cx + (h2 - 0.5f) * 90f;
			_bL[k] = (650f + 750f * h3) * reach + 60f * (float)Math.Sin(t * 0.23 + k * 0.7);
			_bR[k] = BeamImpact - 20f + 40f * h4;
			_bAmp[k] = 2f + 8f * h2;
			_bFq[k] = 0.0035f + 0.004f * h3;
			_bPh[k] = h1 * 6.283f;
		}
		float wave = (float)_beamWave;
		float Y(int k, float x)
		{
			float h;
			if (x >= _bR[k])
			{
				h = 0f;
			}
			else if (x >= _bC[k])
			{
				h = 0.5f + 0.5f * (float)Math.Cos(Math.PI * (x - _bC[k]) / (_bR[k] - _bC[k]));
			}
			else
			{
				h = 0.5f + 0.5f * (float)Math.Cos(Math.PI * Math.Min(1f, (_bC[k] - x) / _bL[k]));
			}
			// the swell: a broad, soft bulge travelling down the beam that lifts it and eases the strands apart
			float sw = (x - swellX) / 340f, bump = (float)Math.Exp(-sw * sw);
			// the fan: each strand's share of the height by its place across the bundle, opened and closed by the
			// breathing and eased wider by the swell
			float open = 1f + 0.4f * breathe + 0.9f * bump;
			// each strand wanders in and out along its length and over time, so they cross and part like filaments
			// instead of moving in lockstep
			float wander = 0.05f * (float)Math.Sin(x * 0.0042f + k * 1.37f + t * 0.21) + 0.03f * (float)Math.Sin(x * 0.0091f - k * 2.11f - t * 0.17);
			float height = _bA[k] * (0.93f + _bS[k] * 0.3f * open) * (1f + wander) + 40f * bump;
			// the flow: like strands of silk in a current rather than wires in a cable. Three slow waves of different
			// lengths and speeds, two running one way and one the other, each shifting gradually across the bundle so
			// neighbouring strands move nearly together and each trails its neighbour slightly. Strongest on the
			// flanks, still alive over the crest
			float u = _bS[k] + 0.5f, env = (0.3f + 2.8f * h * (1f - h)) * (1f + 0.6f * bump);
			float turn = env * 0.6f * (16f * (float)Math.Sin(x * 0.0032f + 5f * u - flow1)
				+ 8f * (float)Math.Sin(x * 0.0061f - 9.5f * u + flow2 + 1.3f)
				+ 3.2f * (float)Math.Sin(x * 0.0127f + 19f * u - flow3 + 0.7f));
			// the twist, on top of the flow: the strands laid round the bundle in order like the wires of a rope,
			// winding round as it runs. The lay is looser in some stretches and tighter in others, and those stretches
			// slowly wander along the beam, each strand sits a little in or out from the rest and breathes on its own,
			// and the turn is widest on the flanks and gathered in over the crest, so it reads as a rope, not a drill
			float lay = x * 0.0045f + 0.9f * (float)Math.Sin(x * 0.0015f + wind);
			float strand = 0.65f + 0.5f * (_bAmp[k] - 2f) / 8f + 0.15f * (float)Math.Sin(t * 0.4 + k * 1.7);
			float rope = (5f + 70f * h * (1f - h)) * (1f + 1.2f * bump) * strand;
			turn += rope * (float)Math.Sin(u * 6.2832f + lay - cable);
			float weave = _bAmp[k] * 0.4f * (float)Math.Sin(x * _bFq[k] - wave + _bPh[k]) + kick * 18f * (float)Math.Sin(x * 0.011f - t * 3.0 + k * 0.2);
			return BeamHy - height * h - h * (weave + turn) + (1f - h) * (k % 5 - 2) * 0.6f;
		}
		var strands = new PointF[BeamStrands][];
		for (int k = 0; k < BeamStrands; k++)
		{
			var pts = new PointF[49];
			for (int i = 0; i < pts.Length; i++)
			{
				float x = -40f + i * 2000f / 48f;
				pts[i] = new PointF(x, Y(k, x));
			}
			strands[k] = pts;
		}
		// the landing flare breathes slowly, brighter under load
		float flare = 0.6f + 0.35f * lift + 0.08f * (float)Math.Sin(t * 0.9);

		// the soft light, at a quarter size: the warm smoke behind the crest and every strand drawn wide and faint. The
		// page, this, a sixteenth-size smear of it for the wide bloom, and both again mirrored in the floor make the frame.
		_beamGlow ??= new Bitmap(480, 120, PixelFormat.Format32bppPArgb);
		_beamBloom ??= new Bitmap(120, 30, PixelFormat.Format32bppPArgb);
		using (Graphics gg = Graphics.FromImage(_beamGlow))
		{
			gg.Clear(Color.Transparent);
			gg.ScaleTransform(0.25f, 0.25f);
			float hw = 1200f * (0.8f + 0.2f * lift), hh = 640f * (0.7f + 0.3f * lift);
			gg.DrawImage(BeamHazeSprite(), new RectangleF(cx - hw / 2f, BeamHy - arch * 0.8f - hh / 2f, hw, hh));
			gg.SmoothingMode = SmoothingMode.AntiAlias;
			using (LinearGradientBrush soft = BeamBrush(cx, warm, flank, 22))
			using (var pen = new Pen(soft, 10f))
			{
				foreach (PointF[] pts in strands)
				{
					gg.DrawLines(pen, pts);
				}
			}
			using var spot = new SolidBrush(Color.FromArgb((int)Math.Min(255f, 90f * flare), 230, 236, 255));
			gg.FillEllipse(spot, BeamImpact - 70f, BeamHy - 15f, 180f, 30f);
		}
		// the page and the light added together pixel by pixel, the light scaled up fourfold on the way
		BeamCompose();
		// the rest is drawn onto the frame itself, so the moving type can be laid in by hand after it (see BeamSprites)
		Graphics screen = g;
		g = Graphics.FromImage(_beamFrame!);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		// clear the shared text cache between frames rather than letting it clear mid-frame under queued type
		if (_deckText.Count > 450)
		{
			foreach (Bitmap old in _deckText.Values)
			{
				old.Dispose();
			}
			_deckText.Clear();
		}
		// the sharp strands, in three weights
		for (int tier = 0; tier < 3; tier++)
		{
			// each weight shimmers on its own slow beat
			float shimmer = 0.85f + 0.15f * (float)Math.Sin(t * 1.1 + tier * 2.1);
			using LinearGradientBrush b = BeamBrush(cx, warm, flank, (int)(new[] { 80, 150, 235 }[tier] * shimmer));
			using var pen = new Pen(b, tier == 2 ? 1.4f : 1f);
			for (int k = tier; k < BeamStrands; k += 3)
			{
				g.DrawLines(pen, strands[k]);
			}
		}
		// the horizon, brightening toward the landing point and burning on to the right edge
		using (var line = new LinearGradientBrush(new PointF(-1f, 0f), new PointF(1921f, 0f), Color.Black, Color.Black))
		{
			line.InterpolationColors = new ColorBlend
			{
				Colors = new[] { Color.FromArgb(40, flank), Color.FromArgb(110, flank), Color.FromArgb(255, 240, 244, 255), Color.FromArgb(200, Imaging.Mix(flank, Color.White, 0.6f)) },
				Positions = new[] { 0f, 0.5f, BeamImpact / 1922f, 1f }
			};
			using var pen = new Pen(line, 1.3f);
			g.DrawLine(pen, 0f, BeamHy, 1920f, BeamHy);
		}
		Bitmap fl = BeamFlareSprite();
		float fw = fl.Width * (0.7f + 0.5f * flare), fh = fl.Height * (0.6f + 0.6f * flare);
		BeamBlit(g, fl, new[] { new PointF(BeamImpact + 20f - fw / 2f, BeamHy - fh / 2f), new PointF(BeamImpact + 20f + fw / 2f, BeamHy - fh / 2f), new PointF(BeamImpact + 20f - fw / 2f, BeamHy + fh / 2f) }, Math.Min(1f, flare));
		// embers lifting off the crest, more of them under load: each fades in and out with the load rather than
		// appearing mid-flight, and each is born and dies at nothing, so the drift loops without a seam
		for (int i = 0; i < 36; i++)
		{
			float share = Math.Clamp(6f + 30f * lift - i, 0f, 1f);
			if (share <= 0f)
			{
				continue;
			}
			float e1 = (float)Hash01(i * 13 + 1), e2 = (float)Hash01(i * 19 + 2), e3 = (float)Hash01(i * 23 + 3);
			float life = (float)((t * (0.16 + 0.12 * e1) + e2) % 1.0);
			float x0 = cx + (e3 - 0.5f) * 720f;
			float x = x0 + (e1 - 0.5f) * 70f * life, y = Y(i % BeamStrands, x0) - 56f * life * (0.5f + e2);
			float size = 1.4f + 2.2f * e1;
			using var ember = new SolidBrush(Color.FromArgb((int)(share * Math.Sin(Math.PI * life) * (110f + 120f * lift)), Imaging.Mix(warm, Color.White, 0.3f)));
			g.FillEllipse(ember, x - size / 2f, y - size / 2f, size, size);
		}
		BeamType(g, t, ch, warn, warm, flank);
		g.Dispose();
		BeamSprites(_beamFrame!);
		CompositingMode mode = screen.CompositingMode;
		screen.CompositingMode = CompositingMode.SourceCopy;
		screen.DrawImageUnscaled(_beamFrame!, 0, 0);
		screen.CompositingMode = mode;
	}

	/// <summary>
	/// Moves a value toward its target on a critically damped spring of the given stiffness: it sets off gently and
	/// settles without overshoot, with no kink in its speed when the target changes. Stepped finely for stability.
	/// </summary>
	private static void BeamSpring(ref double x, ref double v, double target, double w, double dt)
	{
		for (double left = dt; left > 1e-9; left -= 0.02)
		{
			double h = Math.Min(0.02, left);
			v += (w * w * (target - x) - 2.0 * w * v) * h;
			x += v * h;
		}
	}
	/// <summary>The strands' colour along the screen: lilac flanks, warm white over the crest, white-hot at the landing.</summary>
	private static LinearGradientBrush BeamBrush(float cx, Color warm, Color flank, int alpha)
	{
		var b = new LinearGradientBrush(new PointF(-41f, 0f), new PointF(1961f, 0f), Color.Black, Color.Black);
		float P(float x) => Math.Clamp((x + 41f) / 2002f, 0.001f, 0.999f);
		Color A(Color c, float m) => Color.FromArgb(Math.Clamp((int)(alpha * m), 0, 255), c);
		Color white = Color.FromArgb(236, 240, 255);
		b.InterpolationColors = new ColorBlend
		{
			Colors = new[] { A(flank, 0.35f), A(flank, 0.8f), A(warm, 1f), A(Imaging.Mix(warm, flank, 0.55f), 0.85f), A(Imaging.Mix(flank, white, 0.5f), 0.8f), A(white, 1f), A(Imaging.Mix(flank, white, 0.6f), 0.75f) },
			Positions = new[] { 0f, P(cx - 430f), P(cx), P(cx + 400f), P(BeamImpact - 160f), P(BeamImpact), 1f }
		};
		return b;
	}

	private static void BeamBlit(Graphics g, Bitmap bmp, PointF[] dest, float alpha)
	{
		using var ia = new ImageAttributes();
		ia.SetWrapMode(WrapMode.TileFlipXY);
		// always through the colour matrix, even when opaque: GDI+ places images a shade differently with and
		// without one, and switching paths as a fade completes would nudge the image by a pixel
		ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(alpha, 0f, 1f) });
		g.DrawImage(bmp, dest, new RectangleF(0f, 0f, bmp.Width, bmp.Height), GraphicsUnit.Pixel, ia);
	}

	/// <summary>Near-black, with a cold blue haze lying along the horizon toward the landing point. Composed once.</summary>
	private Bitmap BeamPage()
	{
		if (_beamPage == null)
		{
			_beamPage = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
			using Graphics pg = Graphics.FromImage(_beamPage);
			pg.Clear(Color.FromArgb(2, 3, 6));
			using var haze = new GraphicsPath();
			haze.AddEllipse(300f, BeamHy - 170f, 2000f, 340f);
			using (var hb = new PathGradientBrush(haze) { CenterPoint = new PointF(BeamImpact, BeamHy), CenterColor = Color.FromArgb(18, 26, 52), SurroundColors = new[] { Color.FromArgb(2, 3, 6) } })
			{
				pg.FillPath(hb, haze);
			}
			using (var floor = new LinearGradientBrush(new PointF(0f, BeamHy), new PointF(0f, 481f), Color.FromArgb(0, 1, 2, 5), Color.FromArgb(220, 1, 2, 5)))
			{
				pg.FillRectangle(floor, 0f, BeamHy + 1f, 1920f, 480f - BeamHy);
			}
			Grain(_beamPage, 3);
		}
		return _beamPage;
	}

	/// <summary>
	/// The frame's base, made by hand because GDI+ takes several times as long to stretch layers this size. At quarter
	/// size: the light, plus its sixteenth-size bloom, plus both mirrored and squashed below the horizon and fading
	/// toward the foot. That is stretched across first, then down, and added to the page, as light adds.
	/// </summary>
	private unsafe void BeamCompose()
	{
		_beamFrame ??= new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
		_beamQ ??= new int[120 * 480 * 3];
		_beamH ??= new int[120 * 1920 * 3];
		Bitmap page = BeamPage(), glow = _beamGlow!, bloom = _beamBloom!;
		var full = new Rectangle(0, 0, 1920, 480);
		BitmapData lit = glow.LockBits(new Rectangle(0, 0, 480, 120), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
		BitmapData wide = bloom.LockBits(new Rectangle(0, 0, 120, 30), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		try
		{
			// the bloom: each of its pixels the average of a four-by-four block of the light
			for (int y = 0; y < 30; y++)
			{
				byte* bp = (byte*)wide.Scan0 + y * wide.Stride;
				for (int x = 0; x < 120 * 4; x++)
				{
					int sum = 0;
					for (int j = 0; j < 4; j++)
					{
						byte* lp = (byte*)lit.Scan0 + (y * 4 + j) * lit.Stride + (x >> 2) * 16 + (x & 3);
						sum += lp[0] + lp[4] + lp[8] + lp[12];
					}
					bp[x] = (byte)(sum >> 4);
				}
			}
			fixed (int* q = _beamQ)
			{
				// the light plus the bloom stretched up to it
				for (int y = 0; y < 120; y++)
				{
					var (b0, b1, bf) = BeamRows(y, 4f, 30);
					byte* lp = (byte*)lit.Scan0 + y * lit.Stride, r0 = (byte*)wide.Scan0 + b0 * wide.Stride, r1 = (byte*)wide.Scan0 + b1 * wide.Stride;
					int* qp = q + y * 1440;
					for (int x = 0; x < 480; x++)
					{
						int x0 = BeamTapsQ[x * 3] * 4, x1 = BeamTapsQ[x * 3 + 1] * 4, fx = BeamTapsQ[x * 3 + 2];
						for (int c = 0; c < 3; c++)
						{
							int top = r0[x0 + c] * (256 - fx) + r0[x1 + c] * fx, bottom = r1[x0 + c] * (256 - fx) + r1[x1 + c] * fx;
							qp[x * 3 + c] = lp[x * 4 + c] + ((top * (256 - bf) + bottom * bf) >> 16);
						}
					}
				}
				// below the horizon, the light from twice as far above it, dimming toward the foot
				for (int y = (int)Math.Ceiling(BeamHy / 4f); y < 120; y++)
				{
					float sy = y * 4f + 1.5f, my = BeamHy - 2f * (sy - BeamHy);
					if (my < 0f)
					{
						continue;
					}
					int weight = (int)(256f * 0.6f * (1f - 0.9f * (sy - BeamHy) / (480f - BeamHy)));
					var (m0, m1, mf) = BeamRows(my, 4f, 120);
					int* qp = q + y * 1440, s0 = q + m0 * 1440, s1 = q + m1 * 1440;
					for (int i = 0; i < 1440; i++)
					{
						qp[i] += (s0[i] * (256 - mf) + s1[i] * mf >> 8) * weight >> 8;
					}
				}
				fixed (int* h = _beamH)
				{
					// across: every quarter-size row stretched to the full width
					for (int y = 0; y < 120; y++)
					{
						int* qp = q + y * 1440, hp = h + y * 5760;
						for (int x = 0; x < 1920; x++)
						{
							int x0 = BeamTaps4[x * 3] * 3, x1 = BeamTaps4[x * 3 + 1] * 3, fx = BeamTaps4[x * 3 + 2];
							hp[x * 3] = qp[x0] * (256 - fx) + qp[x1] * fx >> 8;
							hp[x * 3 + 1] = qp[x0 + 1] * (256 - fx) + qp[x1 + 1] * fx >> 8;
							hp[x * 3 + 2] = qp[x0 + 2] * (256 - fx) + qp[x1 + 2] * fx >> 8;
						}
					}
					// down, onto the page
					BitmapData dst = _beamFrame.LockBits(full, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
					BitmapData src = page.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
					try
					{
						for (int y = 0; y < 480; y++)
						{
							var (g0, g1, gf) = BeamRows(y, 4f, 120);
							int* h0 = h + g0 * 5760, h1 = h + g1 * 5760;
							byte* sp = (byte*)src.Scan0 + y * src.Stride, dp = (byte*)dst.Scan0 + y * dst.Stride;
							for (int x = 0; x < 1920; x++, sp += 4, dp += 4, h0 += 3, h1 += 3)
							{
								int v0 = sp[0] + (h0[0] * (256 - gf) + h1[0] * gf >> 8), v1 = sp[1] + (h0[1] * (256 - gf) + h1[1] * gf >> 8), v2 = sp[2] + (h0[2] * (256 - gf) + h1[2] * gf >> 8);
								dp[0] = (byte)(v0 > 255 ? 255 : v0);
								dp[1] = (byte)(v1 > 255 ? 255 : v1);
								dp[2] = (byte)(v2 > 255 ? 255 : v2);
								dp[3] = 255;
							}
						}
					}
					finally
					{
						page.UnlockBits(src);
						_beamFrame.UnlockBits(dst);
					}
				}
			}
		}
		finally
		{
			bloom.UnlockBits(wide);
			glow.UnlockBits(lit);
		}
	}

	private int[]? _beamQ, _beamH;

	// for each column of a layer stretched fourfold: its left and right source pixels and the right one's weight in 256ths
	private static readonly int[] BeamTaps4 = BeamTaps(1920, 480), BeamTapsQ = BeamTaps(480, 120);

	private static int[] BeamTaps(int width, int from)
	{
		var taps = new int[width * 3];
		for (int x = 0; x < width; x++)
		{
			float gx = Math.Max(0f, (x + 0.5f) / 4f - 0.5f);
			taps[x * 3] = Math.Min(from - 1, (int)gx);
			taps[x * 3 + 1] = Math.Min(from - 1, (int)gx + 1);
			taps[x * 3 + 2] = (int)((gx - (int)gx) * 256f);
		}
		return taps;
	}

	private static (int, int, int) BeamRows(float y, float scale, int height)
	{
		float gy = Math.Max(0f, (y + 0.5f) / scale - 0.5f);
		return (Math.Min(height - 1, (int)gy), Math.Min(height - 1, (int)gy + 1), (int)((gy - (int)gy) * 256f));
	}
	/// <summary>The warm smoke of light behind the crest.</summary>
	private Bitmap BeamHazeSprite()
	{
		if (_beamHaze == null)
		{
			_beamHaze = new Bitmap(600, 320, PixelFormat.Format32bppPArgb);
			using Graphics hg = Graphics.FromImage(_beamHaze);
			using var p = new GraphicsPath();
			p.AddEllipse(0f, 0f, 600f, 320f);
			using var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb(120, 92, 62, 52), SurroundColors = new[] { Color.FromArgb(0, 92, 62, 52) } };
			b.Blend = new Blend { Factors = new[] { 0f, 0.5f, 1f }, Positions = new[] { 0f, 0.5f, 1f } };
			hg.FillPath(b, p);
		}
		return _beamHaze;
	}

	/// <summary>The landing flare: a white-hot core in a flat blue bloom, with a long streak through it.</summary>
	private Bitmap BeamFlareSprite()
	{
		if (_beamFlare == null)
		{
			_beamFlare = new Bitmap(720, 160, PixelFormat.Format32bppPArgb);
			using Graphics fg = Graphics.FromImage(_beamFlare);
			fg.SmoothingMode = SmoothingMode.AntiAlias;
			void Glow(RectangleF r, Color c, int alpha)
			{
				using var p = new GraphicsPath();
				p.AddEllipse(r);
				using var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb(alpha, c), SurroundColors = new[] { Color.FromArgb(0, c) } };
				fg.FillPath(b, p);
			}
			Glow(new RectangleF(160f, 20f, 400f, 120f), Color.FromArgb(150, 170, 255), 120);
			Glow(new RectangleF(0f, 70f, 720f, 20f), Color.FromArgb(220, 230, 255), 200);
			Glow(new RectangleF(250f, 50f, 220f, 60f), Color.White, 230);
			Glow(new RectangleF(310f, 68f, 100f, 24f), Color.White, 255);
		}
		return _beamFlare;
	}

	// ---------- the type ----------

	private string BeamValue(BeamReading b, float v) => v.ToString(b.Format, CultureInfo.InvariantCulture);

	/// <summary>
	/// The number under the arch with its reflection, the selector at the foot, the side reading and its trace on the
	/// right, and the time at the bottom left. On a change of channel the old readings scroll up and shrink away while
	/// the new ones rise in from below a touch large and settle, three-quarters of a second eased out; the selector is a
	/// ring, so it always turns forward and comes round from the last channel to the first without a jump.
	/// </summary>
	private void BeamType(Graphics g, double t, BeamChannel ch, bool warn, Color warm, Color flank)
	{
		Color soft = Color.FromArgb(170, 176, 190);
		// staggered: the old readings are mostly gone before the new ones arrive, so the two never sit muddled together
		float p = (float)Math.Clamp((t - _beamFocusAt) / 0.9, 0.0, 1.0);
		// the old ones set off from rest (they are fully visible, so a sudden start would show); the new ones arrive
		// from nothing, so they can come in quick and slow to a stop
		float Out(float v) => 1f - (1f - v) * (1f - v) * (1f - v);
		float Smooth(float v) => v * v * (3f - 2f * v);
		float o = Smooth(Math.Min(1f, p / 0.5f)), e = Out(Math.Clamp((p - 0.3f) / 0.7f, 0f, 1f));
		BeamChannel? prev = p < 1f && _beamPrev != _beamFocus ? BeamChannels[_beamPrev] : null;
		// filtered and offset by half a pixel, so type that is moving slowly is placed between pixels instead of
		// ticking from one whole pixel to the next
		g.InterpolationMode = InterpolationMode.HighQualityBilinear;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		if (prev != null)
		{
			BeamNumber(g, t, prev, soft, -54f * o, 1f - 0.16f * o, 1f - o, false);
			BeamSide(g, t, prev, soft, -26f * o, 1f - 0.08f * o, 1f - o);
			BeamTrace(g, prev, warm, flank, -14f * o, 1f - o);
			BeamCaption(g, t, prev, soft, -8f * o, 1f - o);
		}
		BeamNumber(g, t, ch, soft, 54f * (1f - e), 1.16f - 0.16f * e, e, true);
		BeamSide(g, t, ch, soft, 26f * (1f - e), 1.08f - 0.08f * e, e);
		BeamTrace(g, ch, warm, flank, 14f * (1f - e), e);
		BeamCaption(g, t, ch, soft, 8f * (1f - e), e);
		// the selector: the channel in focus large and bright with a bar before it, the others smaller and dimmer the
		// further round the ring they are, fading out at its far side
		int n = BeamChannels.Length;
		for (int i = 0; i < n; i++)
		{
			float d = i - (float)_beamSlide;
			d = ((d + n / 2f) % n + n) % n - n / 2f;
			// every curve here is smoothstepped, so nothing changes speed with a kink as a name passes a threshold
			float ad = Math.Abs(d), k = 2f * BeamSmooth(Math.Min(1f, ad / 2f)), edge = BeamSmooth(Math.Clamp((2.9f - ad) / 0.7f, 0f, 1f));
			if (edge <= 0f)
			{
				continue;
			}
			bool hot = BeamWarn(BeamChannels[i]);
			Color c = hot ? Imaging.Mix(BeamText, BeamAlarm, 0.6f) : BeamText;
			float x = 960f + d * 118f, scale = (30f - 7f * k) / 30f, alpha = (1f - 0.335f * k) * edge, bold = BeamSmooth(Math.Max(0f, 1f - ad * 2f));
			var pivot = new PointF(x, 436f);
			Bitmap light = TextImage(BeamChannels[i].Name, 30f, c, "Segoe UI Light"), heavy = TextImage(BeamChannels[i].Name, 30f, c, "Segoe UI");
			BeamScaled(g, light, x - light.Width / 2f, 436f - 30f * 1.079f, pivot, scale, alpha * (1f - bold));
			BeamScaled(g, heavy, x - heavy.Width / 2f, 436f - 30f * 1.079f, pivot, scale, alpha * bold);
			if (bold > 0f)
			{
				float bx = x - heavy.Width * scale / 2f - 7f;
				using var bar = new Pen(Color.FromArgb((int)(220 * bold), c), 1.6f);
				g.DrawLine(bar, bx, 436f - 24f * scale, bx, 438f);
			}
		}
		// bottom left, in the dark of the floor: the time over a hairline, mirroring the trace on the right
		DeckText(g, DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture), 40f, BeamText, 62f, 370f, StringAlignment.Near, "Segoe UI Light");
		using (var hair = new Pen(Color.FromArgb(80, 200, 206, 220), 1f))
		{
			g.DrawLine(hair, 0f, 433f, 320f, 433f);
			g.DrawLine(hair, 1600f, 433f, 1920f, 433f);
		}
		DeckText(g, DateTime.Now.ToString("dddd d MMMM", CultureInfo.CurrentCulture).ToUpperInvariant(), 10f, Color.FromArgb(110, soft), 64f, 438f, StringAlignment.Near, "Segoe UI Semibold");
		DeckText(g, (_beamFocus + 1).ToString("00") + " / " + n.ToString("00"), 14f, Color.FromArgb(130, soft), 1880f, 26f, StringAlignment.Far, "Segoe UI Light");
		DeckText(g, "−2 MIN", 10f, Color.FromArgb(110, soft), 1640f, 438f, StringAlignment.Near, "Segoe UI Semibold");
		DeckText(g, "NOW", 10f, Color.FromArgb(110, soft), 1890f, 438f, StringAlignment.Far, "Segoe UI Semibold");
	}

	private readonly List<(Bitmap Bmp, float L, float T, float Scale, float Alpha)> _beamSprites = new List<(Bitmap, float, float, float, float)>();

	/// <summary>
	/// Queues an image whose top left would be at (x, y), scaled about a pivot and faded, to be laid into the frame by
	/// BeamSprites. GDI+ snaps images to whole pixels however it is set, so type gliding to a stop would tick.
	/// </summary>
	private void BeamScaled(Graphics g, Bitmap bmp, float x, float y, PointF pivot, float scale, float alpha)
	{
		if (alpha < 0.01f)
		{
			return;
		}
		_beamSprites.Add((bmp, pivot.X + (x - pivot.X) * scale, pivot.Y + (y - pivot.Y) * scale, scale, Math.Min(1f, alpha)));
	}

	/// <summary>
	/// Lays the queued type into the frame with true sub-pixel placement: every frame pixel samples the image
	/// bilinearly at exactly where it falls, then is blended over at the image's fade.
	/// </summary>
	private unsafe void BeamSprites(Bitmap frame)
	{
		if (_beamSprites.Count == 0)
		{
			return;
		}
		BitmapData dst = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		try
		{
			foreach (var (bmp, l, top, scale, alpha) in _beamSprites)
			{
				int sw = bmp.Width, sh = bmp.Height;
				BitmapData src = bmp.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
				try
				{
					byte* s0 = (byte*)src.Scan0;
					int ss = src.Stride, fade = (int)(alpha * 256f);
					int x0 = Math.Max(0, (int)Math.Floor(l)), x1 = Math.Min(frame.Width, (int)Math.Ceiling(l + sw * scale) + 1);
					int y0 = Math.Max(0, (int)Math.Floor(top)), y1 = Math.Min(frame.Height, (int)Math.Ceiling(top + sh * scale) + 1);
					int Texel(int x, int y, int c) => x < 0 || y < 0 || x >= sw || y >= sh ? 0 : s0[y * ss + x * 4 + c];
					for (int y = y0; y < y1; y++)
					{
						float sy = (y + 0.5f - top) / scale - 0.5f;
						int iy = (int)Math.Floor(sy), fy = (int)((sy - iy) * 256f);
						byte* dp = (byte*)dst.Scan0 + y * dst.Stride + x0 * 4;
						for (int x = x0; x < x1; x++, dp += 4)
						{
							float sx = (x + 0.5f - l) / scale - 0.5f;
							int ix = (int)Math.Floor(sx), fx = (int)((sx - ix) * 256f);
							int Sample(int c) => ((Texel(ix, iy, c) * (256 - fx) + Texel(ix + 1, iy, c) * fx) * (256 - fy) + (Texel(ix, iy + 1, c) * (256 - fx) + Texel(ix + 1, iy + 1, c) * fx) * fy) >> 16;
							int a = Sample(3) * fade >> 8;
							if (a == 0)
							{
								continue;
							}
							for (int c = 0; c < 3; c++)
							{
								int v = (Sample(c) * fade >> 8) + dp[c] * (255 - a) / 255;
								dp[c] = (byte)(v > 255 ? 255 : v);
							}
							dp[3] = 255;
						}
					}
				}
				finally
				{
					bmp.UnlockBits(src);
				}
			}
		}
		finally
		{
			frame.UnlockBits(dst);
			_beamSprites.Clear();
		}
	}

	private static float BeamSmooth(float v) => v * v * (3f - 2f * v);

	private readonly Dictionary<string, (string From, string To, double At)> _beamRolls = new Dictionary<string, (string, string, double)>();

	/// <summary>Where a rolling reading is: the text it is rolling from, the text it is rolling to, and how far, 0 to 1.</summary>
	private (string From, string To, float E) BeamRollState(string id, string text, double t)
	{
		if (!_beamRolls.TryGetValue(id, out var r) || t < r.At)
		{
			r = (text, text, double.NegativeInfinity);
		}
		else if (r.To != text)
		{
			// a new value rolls on from whichever text is showing more, so a roll interrupted by another hands over
			// from what the eye sees rather than snapping
			float shown = BeamSmooth((float)Math.Clamp((t - r.At) / 0.32, 0.0, 1.0));
			r = (shown < 0.5f ? r.From : r.To, text, t);
		}
		_beamRolls[id] = r;
		return (r.From, r.To, BeamSmooth((float)Math.Clamp((t - r.At) / 0.32, 0.0, 1.0)));
	}

	/// <summary>
	/// Draws a reading like an odometer: when its text changes, only the characters that differ move, the old one rolling
	/// up and out and the new one rolling up from below as they crossfade over a third of a second, while the width eases
	/// from the old to the new. x is the left edge, centre or right edge by align. Returns the width it took.
	/// </summary>
	private float BeamRoll(Graphics g, string id, string text, float size, Color color, string font, float x, float baseline, StringAlignment align, PointF pivot, float scale, float alpha, double t)
	{
		var (a, b, e) = BeamRollState(id, text, t);
		float Adv(char ch) => Math.Max(1, TextImage(ch.ToString(), size, color, font).Width - 4);
		float wa = a.Sum(Adv), wb = b.Sum(Adv), w = wa + (wb - wa) * e;
		float right = align == StringAlignment.Center ? x + w / 2f : align == StringAlignment.Far ? x : x + w;
		float top = baseline - size * 1.079f, travel = size * 0.5f, xa = right, xb = right;
		for (int i = 1; i <= Math.Max(a.Length, b.Length); i++)
		{
			char? ca = i <= a.Length ? a[^i] : null, cb = i <= b.Length ? b[^i] : null;
			xa -= ca.HasValue ? Adv(ca.Value) : 0f;
			xb -= cb.HasValue ? Adv(cb.Value) : 0f;
			if (ca == cb && Math.Abs(xa - xb) < 0.5f)
			{
				BeamScaled(g, TextImage(cb!.Value.ToString(), size, color, font), xb, top, pivot, scale, alpha);
				continue;
			}
			if (ca.HasValue && e < 1f)
			{
				BeamScaled(g, TextImage(ca.Value.ToString(), size, color, font), xa, top - travel * e, pivot, scale, alpha * (1f - e));
			}
			if (cb.HasValue && e > 0f)
			{
				BeamScaled(g, TextImage(cb.Value.ToString(), size, color, font), xb, top + travel * (1f - e), pivot, scale, alpha * e);
			}
		}
		return w;
	}

	/// <summary>A channel's number and unit, moved, scaled about the number's centre and faded; the incoming one is mirrored in the floor.</summary>
	private void BeamNumber(Graphics g, double t, BeamChannel c, Color soft, float dy, float scale, float alpha, bool mirror)
	{
		if (alpha < 0.01f)
		{
			return;
		}
		bool online = BeamOnline(c.Main);
		// the raw reading, which changes about once a second, so each roll finishes before the next begins
		string number = online ? BeamValue(c.Main, Val(c.Main.Key)) : "--";
		// the number stays white even when the channel runs hot; the light and the HOT label carry the warning
		Color ink = BeamText;
		float baseline = 312f + dy;
		var pivot = new PointF(960f, 262f + dy);
		string id = "num|" + c.Name;
		if (mirror)
		{
			// the reflection crossfades between the old and new text as the digits roll
			var (from, to, e) = BeamRollState(id, number, t);
			GraphicsState st = g.Save();
			g.SetClip(new RectangleF(0f, BeamHy + 2f, 1920f, 480f - BeamHy));
			foreach (var (text, share) in new[] { (from, 1f - e), (to, e) })
			{
				if (share < 0.01f)
				{
					continue;
				}
				Bitmap num = TextImage(text, 130f, ink, "Segoe UI Light");
				float top = baseline - 130f * 1.079f, nx = 960f - num.Width / 2f;
				BeamBlit(g, num, new[] { new PointF(nx, BeamHy + (BeamHy - top) * 0.7f), new PointF(nx + num.Width, BeamHy + (BeamHy - top) * 0.7f), new PointF(nx, BeamHy + (BeamHy - top - num.Height) * 0.7f) }, 0.09f * alpha * share);
			}
			g.Restore(st);
		}
		float w = BeamRoll(g, id, number, 130f, ink, "Segoe UI Light", 960f, baseline, StringAlignment.Center, pivot, scale, alpha, t);
		BeamScaled(g, TextImage(c.Main.Unit, 30f, soft, "Segoe UI Light"), 960f + w / 2f + 6f, baseline - 30f * 1.079f, pivot, scale, alpha * 0.67f);
	}

	/// <summary>The channel's name, its side reading's label and value, top right, scaled about the value's left edge.</summary>
	private void BeamSide(Graphics g, double t, BeamChannel c, Color soft, float dy, float scale, float alpha)
	{
		if (alpha < 0.01f)
		{
			return;
		}
		bool warn = BeamWarn(c);
		BeamReading side = c.Side.FirstOrDefault(BeamOnline) ?? c.Side[0];
		bool hot = side.Warn > 0f && Val(side.Key) >= side.Warn, online = BeamOnline(side);
		string value = online ? BeamValue(side, Val(side.Key)) : "--";
		var pivot = new PointF(1640f, 230f + dy);
		Color ink = hot ? BeamAlarm : BeamText;
		BeamScaled(g, TextImage(c.Title + (warn ? "   ·   HOT" : ""), 12f, warn ? BeamAlarm : Color.FromArgb(150, soft), "Segoe UI Semibold"), 1642f, 166f + dy, pivot, scale, alpha);
		BeamScaled(g, TextImage(side.Label, 34f, soft, "Segoe UI Light"), 1640f, 186f + dy, pivot, scale, alpha);
		float baseline = 230f + dy + 34f * 1.079f;
		float w = BeamRoll(g, "side|" + c.Name + "|" + side.Key, value, 34f, ink, "Segoe UI Light", 1640f, baseline, StringAlignment.Near, pivot, scale, alpha, t);
		if (online)
		{
			BeamScaled(g, TextImage(side.Unit, 34f, ink, "Segoe UI Light"), 1640f + w + 10f, 230f + dy, pivot, scale, alpha);
		}
	}

	/// <summary>
	/// The channel's last two minutes as a fine line above the right-hand hairline. Each new sample slides in from beyond
	/// the right edge over the second until the next, and the warm dot rides the line where it meets that edge, so
	/// neither the line nor the dot ever steps when a sample arrives.
	/// </summary>
	private void BeamTrace(Graphics g, BeamChannel c, Color warm, Color flank, float dy, float alpha)
	{
		float[] hist = _sensors.History(c.Main.Key);
		if (alpha < 0.01f || hist.Length < 2 || !BeamOnline(c.Main))
		{
			return;
		}
		float max = BeamMax(c), age = (float)Math.Clamp(_sensors.Age(c.Main.Key), 0.0, 1.0), step = 250f / (hist.Length - 1);
		var pts = new PointF[hist.Length];
		for (int i = 0; i < hist.Length; i++)
		{
			pts[i] = new PointF(1890f + (i - hist.Length + 2 - age) * step, 424f + dy - Math.Clamp(hist[i] / max, 0f, 1f) * 44f);
		}
		GraphicsState clip = g.Save();
		g.SetClip(new RectangleF(1640f, 360f, 250f, 70f));
		using (var pen = new Pen(Color.FromArgb((int)(170 * alpha), Imaging.Mix(flank, Color.White, 0.5f)), 1.3f))
		{
			g.DrawLines(pen, pts);
		}
		g.Restore(clip);
		PointF p0 = pts[^2], p1 = pts[^1];
		float u = Math.Clamp((1890f - p0.X) / Math.Max(0.001f, p1.X - p0.X), 0f, 1f);
		float y = p0.Y + (p1.Y - p0.Y) * u;
		using var dot = new SolidBrush(Color.FromArgb((int)(255 * alpha), warm));
		g.FillEllipse(dot, 1887f, y - 3f, 6f, 6f);
	}

	/// <summary>The channel's peak and average over the last two minutes, under the selector, their figures rolling as they change.</summary>
	private void BeamCaption(Graphics g, double t, BeamChannel c, Color soft, float dy, float alpha)
	{
		float[] hist = _sensors.History(c.Main.Key);
		if (alpha < 0.01f || hist.Length == 0 || !BeamOnline(c.Main))
		{
			return;
		}
		Color ink = Color.FromArgb(120, soft);
		const string font = "Segoe UI Semibold";
		float unitGap = c.Main.Unit == "%" ? 0f : 4f;
		var parts = new (string Text, string? Roll, float Gap)[]
		{
			("PEAK", null, 5f), (BeamValue(c.Main, hist.Max()), "peak|" + c.Name, unitGap), (c.Main.Unit, null, 20f), ("·", null, 20f),
			("AVERAGE", null, 5f), (BeamValue(c.Main, hist.Average()), "avg|" + c.Name, unitGap), (c.Main.Unit, null, 20f), ("·", null, 20f), ("LAST 2 MIN", null, 0f)
		};
		float Width((string Text, string? Roll, float Gap) p)
		{
			if (p.Roll == null)
			{
				return TextImage(p.Text, 11f, ink, font).Width - 4;
			}
			var (a, b, e) = BeamRollState(p.Roll, p.Text, t);
			float Adv(char ch) => Math.Max(1, TextImage(ch.ToString(), 11f, ink, font).Width - 4);
			return a.Sum(Adv) + (b.Sum(Adv) - a.Sum(Adv)) * e;
		}
		float x = 960f - parts.Sum(p => Width(p) + p.Gap) / 2f, top = 452f + dy;
		var pivot = new PointF(960f, 458f);
		foreach (var p in parts)
		{
			float w = Width(p);
			if (p.Roll == null)
			{
				BeamScaled(g, TextImage(p.Text, 11f, ink, font), x, top, pivot, 1f, alpha);
			}
			else
			{
				BeamRoll(g, p.Roll, p.Text, 11f, ink, font, x, top + 11f * 1.079f, StringAlignment.Near, pivot, 1f, alpha, t);
			}
			x += w + p.Gap;
		}
	}
}
