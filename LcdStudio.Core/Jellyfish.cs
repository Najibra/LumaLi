using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Jellyfish: a living jellyfish in dark water, after a deep-sea album cover.
/// - The bell: dark glass with a glassy rim, an inner body seen through it, highlights on the crown, and a skirt of
///   ruffled lobes of glowing red tissue with specks of light. It pulses (a quick squeeze, a slow relax) faster the
///   busier the processor; each squeeze nudges it forward and flares its light.
/// - The red light: how bright and how hot its colour is follows the warmest temperature (processor or graphics).
/// - Long glossy tentacles (the far ones soft and out of focus) and four frilled oral arms trail on travelling waves;
///   beads of red light stream down five tentacles at a pace set by the graphics load.
/// - Faint shafts of light from above, marine snow, and a second jellyfish glowing far behind.
/// - Type in the manner of a field guide: thin labels with hairline leaders pointing at what each reading drives (PULSE
///   at the crown, GLOW at the skirt, CURRENT at the beads, MEMORY at the arms), and the time in thin white figures
///   with a soft glow of the jellyfish's light, floating gently as if seen through water. A changed figure sinks away
///   and the new one rises in with a few bubbles; a hairline under the time fills with the seconds.
/// Soft light is drawn on a quarter-size layer, blurred and added; nothing burns out to white.
/// </summary>
public sealed partial class ScreenRenderer
{
	private const int JfStrands = 16, JfArms = 4;

	private const string JfThin = "Montserrat ExtraLight", JfLabelFont = "Montserrat Light";

	private SKSurface? _skJelly;

	private double _jfAt = double.NaN, _jfPulse, _jfWave, _jfBead, _jfDrift;

	private float _jfPx = float.NaN, _jfPy, _jfVx, _jfVy, _jfHead, _jfSpeed, _jfTurn, _jfBeatStr = 0.9f;

	private double _jfBeatRate = 1.0;

	private long _jfBeatN = -1;

	private int _jfDart;

	private SKPoint _jfCrown, _jfFrill, _jfArmTip, _jfBeadAt;

	private readonly char[] _jfShown = new char[5];

	private readonly char[] _jfWas = new char[5];

	private readonly double[] _jfSince = { -100, -100, -100, -100, -100 };

	private bool DrawJellyfish(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "jellyfish" || w.Kind != WidgetKind.Panel)
		{
			return DrawClearance(g, w, r, t);
		}
		Jellyfish(g, t);
		return true;
	}

	private static float JfSmooth(float x)
	{
		x = Math.Clamp(x, 0f, 1f);
		return x * x * (3f - 2f * x);
	}

	private void Jellyfish(Graphics g, double t)
	{
		double dt = double.IsNaN(_jfAt) ? 0.0 : Math.Clamp(t - _jfAt, 0.0, 0.25);
		_jfAt = t;
		float cpu = Math.Clamp(McEased("jf.cpu", Val("cpu.load") / 100f, dt, 1.4), 0f, 1f);
		float gpu = Math.Clamp(McEased("jf.gpu", Val("gpu.load") / 100f, dt, 1.4), 0f, 1f);
		// the warmest temperature sets the light; without temperatures the processor load stands in
		float tmax = Math.Max(McOnline("cpu.temp") ? Val("cpu.temp") : 0f, McOnline("gpu.temp") ? Val("gpu.temp") : 0f);
		float heatTarget = tmax > 0f ? Math.Clamp((tmax - 38f) / 50f, 0f, 1f) : cpu;
		float heat = Math.Clamp(McEased("jf.heat", heatTarget, dt, 1.0), 0f, 1f);

		// the pulse: a quick squeeze and a slow relax, its rate accumulating with the processor load. Every beat has
		// its own strength and pace, and now and then the jellyfish darts: a few quick, strong beats and a turn.
		double rate = (0.3 + 0.75 * cpu) * _jfBeatRate * (_jfDart > 0 ? 1.7 : 1.0);
		_jfPulse += dt * rate;
		long beat = (long)Math.Floor(_jfPulse);
		if (beat != _jfBeatN)
		{
			_jfBeatN = beat;
			int b = (int)(beat % 1000003);
			_jfBeatStr = 0.55f + 0.6f * (float)Hash01(b * 31 + 7);
			_jfBeatRate = 0.75 + 0.55 * Hash01(b * 37 + 3);
			if (_jfDart > 0)
			{
				_jfDart--;
				_jfBeatStr = 1.15f + 0.15f * (float)Hash01(b * 47 + 1);
			}
			else if (Hash01(b * 41 + 9) < 0.14 + 0.16 * cpu)
			{
				_jfDart = 2 + (int)(Hash01(b * 43 + 5) * 2.0);
				_jfTurn = (Hash01(b * 53 + 2) < 0.5 ? -1f : 1f) * (12f + 18f * (float)Hash01(b * 59 + 4));
			}
		}
		float ph = (float)(_jfPulse % 1.0);
		float squeeze = (ph < 0.3f ? JfSmooth(ph / 0.3f) : 1f - (1f - MathF.Pow(1f - (ph - 0.3f) / 0.7f, 3f))) * Math.Min(1.2f, _jfBeatStr);
		JfSwim(dt, ph, squeeze);
		_jfWave += dt * (0.55 + 0.9 * cpu + Math.Min(0.8, _jfSpeed / 160.0));
		_jfBead += dt * (40.0 + 280.0 * gpu);
		_jfDrift += dt;

		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;
		string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
		for (int i = 0; i < 5; i++)
		{
			if (_jfShown[i] != time[i])
			{
				_jfWas[i] = _jfShown[i];
				_jfSince[i] = _jfShown[i] == '\0' ? -100.0 : t;
				_jfShown[i] = time[i];
			}
		}

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_skJelly ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _skJelly.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);

			SKColor light = JfLight(heat);
			JfWater(c, q, t);
			JfFarJelly(q, t, light);

			// where the jellyfish is: wherever its swimming has taken it (JfSwim)
			float theta = _jfHead + 2.5f * squeeze;
			float rad = theta * MathF.PI / 180f;
			float bx = _jfPx, by = _jfPy;
			float glow = 0.55f + 0.45f * heat + 0.25f * squeeze;

			JfTentacles(c, q, bx, by, rad, squeeze, light, glow);
			JfArmsDraw(c, q, bx, by, rad, light, glow);
			JfBell(c, q, bx, by, theta, squeeze, light, glow, t);
			JfTimeGlow(q, t, light);

			q.Restore();
			using (SKImage sharp = _skJelly.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(2.0f, 2.0f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage soft = _skJelly.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(soft, pixels);
			}

			JfLabels(c, dt, rate, tmax);
			JfTime(c, t, now);
		});
	}

	/// <summary>
	/// Swimming: each squeeze pushes the bell along its axis by that beat's strength; the water drags it to a stop and
	/// a gentle current carries it back. Its heading wanders on slow overlapping waves, swings into a turn on each
	/// dart, and steers it back toward the middle of its patch of water. A soft wall keeps it clear of the labels.
	/// </summary>
	private void JfSwim(double dt, float ph, float squeeze)
	{
		const float homeX = 520f, homeY = 236f;
		if (float.IsNaN(_jfPx))
		{
			_jfPx = homeX;
			_jfPy = homeY;
			_jfHead = -68f;
		}
		float f = (float)dt;
		double d = _jfDrift;
		float wander = 14f * (float)Math.Sin(d * 0.23 + 1.3) + 9f * (float)Math.Sin(d * 0.57 + 4.1) + 5f * (float)Math.Sin(d * 1.31 + 0.7);
		float steer = Math.Clamp((_jfPy - homeY) * 0.35f, -24f, 24f) - Math.Clamp((_jfPx - homeX) * 0.06f, -8f, 8f);
		_jfTurn *= MathF.Exp(-f * 0.35f);
		float target = -68f + wander + steer + _jfTurn;
		_jfHead += (target - _jfHead) * (1f - MathF.Exp(-f * 1.1f));

		float rad = _jfHead * MathF.PI / 180f;
		float fx = MathF.Sin(rad), fy = -MathF.Cos(rad);
		float thrust = ph < 0.35f ? 210f * _jfBeatStr * MathF.Sin(ph / 0.35f * MathF.PI) : 0f;
		float ax = fx * thrust + (homeX - _jfPx) * 0.3f + 8f;
		float ay = fy * thrust + (homeY - _jfPy) * 0.45f;
		// soft walls: well inside the screen and clear of the labels on the left
		ax += Math.Max(0f, 450f - _jfPx) * 6f - Math.Max(0f, _jfPx - 680f) * 6f;
		ay += Math.Max(0f, 200f - _jfPy) * 6f - Math.Max(0f, _jfPy - 272f) * 6f;
		float drag = MathF.Exp(-f * 1.7f);
		_jfVx = (_jfVx + ax * f) * drag;
		_jfVy = (_jfVy + ay * f) * drag;
		_jfPx += _jfVx * f;
		_jfPy += _jfVy * f;
		_jfSpeed = MathF.Sqrt(_jfVx * _jfVx + _jfVy * _jfVy);
	}

	/// <summary>The red light: deep crimson-pink when cool, a hotter red-orange as the temperature climbs.</summary>
	private static SKColor JfLight(float heat)
	{
		SKColor cool = new SKColor(255, 38, 92), hot = new SKColor(255, 62, 36);
		return new SKColor((byte)(cool.Red + (hot.Red - cool.Red) * heat), (byte)(cool.Green + (hot.Green - cool.Green) * heat), (byte)(cool.Blue + (hot.Blue - cool.Blue) * heat));
	}

	// ---------- the water ----------

	private void JfWater(SKCanvas c, SKCanvas q, double t)
	{
		using (var shader = SKShader.CreateRadialGradient(new SKPoint(620f, 200f), 1350f,
			new[] { new SKColor(46, 52, 59), new SKColor(30, 34, 40), new SKColor(17, 19, 23), new SKColor(9, 10, 12) }, new[] { 0f, 0.3f, 0.62f, 1f }, SKShaderTileMode.Clamp))
		using (var bg = new SKPaint { Shader = shader })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, bg);
		}
		// faint shafts of light from above, swaying slowly
		using (var ray = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
		{
			for (int i = 0; i < 5; i++)
			{
				float x = 180f + i * 330f + 60f * (float)Math.Sin(t * 0.07 + i * 1.7);
				float w = 70f + 50f * (float)Hash01(i * 7 + 71);
				byte a = (byte)(12 + 10 * (0.5 + 0.5 * Math.Sin(t * 0.21 + i * 2.3)));
				using var path = new SKPath();
				path.MoveTo(x, -10f);
				path.LineTo(x + w, -10f);
				path.LineTo(x + w * 2.2f + 240f, 500f);
				path.LineTo(x + 240f, 500f);
				path.Close();
				using var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 480f), new[] { new SKColor(170, 190, 210, a), new SKColor(170, 190, 210, 0) }, null, SKShaderTileMode.Clamp);
				ray.Shader = sh;
				q.DrawPath(path, ray);
				ray.Shader = null;
			}
		}
		// marine snow: fine specks drifting slowly up and to the left, a few soft ones out of focus
		using var dot = new SKPaint { IsAntialias = true };
		for (int i = 0; i < 90; i++)
		{
			double sp = 6.0 + Hash01(i * 7 + 1) * 14.0;
			float x = (float)(((Hash01(i * 3 + 2) * 2100.0 - t * sp) % 2100.0 + 2100.0) % 2100.0) - 90f;
			float y = (float)(((Hash01(i * 5 + 3) * 560.0 - t * sp * 0.35) % 560.0 + 560.0) % 560.0) - 40f + 8f * (float)Math.Sin(t * 0.4 + i);
			if (i % 9 == 0)
			{
				SkiaKit.Glow(q, x, y, 26f, 26f, new SKColor(150, 170, 190, 26), SKBlendMode.Plus);
				continue;
			}
			float r = 0.7f + 1.5f * (float)Hash01(i * 11 + 4);
			dot.Color = new SKColor(200, 212, 224, (byte)(30 + 60 * Hash01(i * 13 + 5)));
			c.DrawCircle(x, y, r, dot);
		}
	}

	/// <summary>A second jellyfish far behind: only its soft light, on the blurred layer.</summary>
	private static void JfFarJelly(SKCanvas q, double t, SKColor l)
	{
		float x = 1190f + 50f * (float)Math.Sin(t * 0.05), y = 120f + 16f * (float)Math.Sin(t * 0.09 + 2.0);
		float beat = 0.5f + 0.5f * (float)Math.Sin(t * 1.3);
		q.Save();
		q.Translate(x, y);
		q.RotateDegrees(-40f);
		SkiaKit.Glow(q, 0f, -10f, 60f, 44f, new SKColor(120, 140, 160, 30), SKBlendMode.Plus);
		SkiaKit.Glow(q, 0f, 16f, 64f * (1f - 0.08f * beat), 18f, l.WithAlpha((byte)(60 + 40 * beat)), SKBlendMode.Plus);
		for (int i = 0; i < 6; i++)
		{
			float tx = -44f + i * 17f;
			SkiaKit.Glow(q, tx + 9f * (float)Math.Sin(t * 0.6 + i), 80f + 26f * (float)Hash01(i + 40), 5f, 54f, new SKColor(140, 150, 165, 20), SKBlendMode.Plus);
		}
		q.Restore();
	}

	// ---------- tentacles and arms ----------

	/// <summary>The centre line of a trailing strand: it leaves the bell fanned out along its axis and bends into the current.</summary>
	private static SKPoint[] JfStrand(float x0, float y0, float rad, float length, float amp, float lambda, float phase, int n, float spread = 0f)
	{
		var pts = new SKPoint[n];
		float tx = -MathF.Sin(rad + spread), ty = MathF.Cos(rad + spread);
		const float cx = 0.992f, cy = 0.126f;   // the current, mostly to the right
		float px = x0, py = y0, ds = length / (n - 1);
		for (int j = 0; j < n; j++)
		{
			float s = j * ds;
			float k = JfSmooth(s / length / (0.3f + 0.25f * MathF.Abs(spread)));
			float dx = tx + (cx - tx) * k, dy = ty + (cy - ty) * k;
			float m = MathF.Sqrt(dx * dx + dy * dy);
			dx /= m;
			dy /= m;
			float off = amp * MathF.Pow(s / length, 1.15f) * MathF.Sin(MathF.PI * 2f * s / lambda - phase);
			pts[j] = new SKPoint(px - dy * off, py + dx * off);
			px += dx * ds;
			py += dy * ds;
		}
		return pts;
	}

	/// <summary>A ribbon along a line of points; its width tapers from w0 to w1, times an optional ruffle.</summary>
	private static SKPath JfRibbon(SKPoint[] p, float w0, float w1, Func<int, float>? ruffle = null)
	{
		int n = p.Length;
		var left = new SKPoint[n];
		var right = new SKPoint[n];
		for (int j = 0; j < n; j++)
		{
			SKPoint a = p[Math.Max(0, j - 1)], b = p[Math.Min(n - 1, j + 1)];
			float dx = b.X - a.X, dy = b.Y - a.Y, m = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
			float nx = -dy / m, ny = dx / m;
			float w = (w0 + (w1 - w0) * j / (n - 1f)) / 2f * (ruffle?.Invoke(j) ?? 1f);
			left[j] = new SKPoint(p[j].X + nx * w, p[j].Y + ny * w);
			right[j] = new SKPoint(p[j].X - nx * w, p[j].Y - ny * w);
		}
		var path = new SKPath();
		path.MoveTo(left[0]);
		for (int j = 1; j < n; j++)
		{
			path.LineTo(left[j]);
		}
		for (int j = n - 1; j >= 0; j--)
		{
			path.LineTo(right[j]);
		}
		path.Close();
		return path;
	}

	private static SKPoint JfWorld(float bx, float by, float rad, float lx, float ly)
	{
		float cs = MathF.Cos(rad), sn = MathF.Sin(rad);
		return new SKPoint(bx + lx * cs - ly * sn, by + lx * sn + ly * cs);
	}

	private static SKPoint JfAlong(SKPoint[] pts, float k)
	{
		float fj = Math.Clamp(k, 0f, 1f) * (pts.Length - 1);
		int j0 = (int)fj;
		int j1 = Math.Min(pts.Length - 1, j0 + 1);
		float fr = fj - j0;
		return new SKPoint(pts[j0].X + (pts[j1].X - pts[j0].X) * fr, pts[j0].Y + (pts[j1].Y - pts[j0].Y) * fr);
	}

	private void JfTentacles(SKCanvas c, SKCanvas q, float bx, float by, float rad, float squeeze, SKColor light, float glow)
	{
		float wb = 165f * (1f - 0.13f * squeeze);
		using var body = new SKPaint { IsAntialias = true };
		using var hi = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
		using var bead = new SKPaint { IsAntialias = true };
		using var far = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
		for (int i = 0; i < JfStrands; i++)
		{
			float u = -0.95f + 1.9f * i / (JfStrands - 1f);
			SKPoint root = JfWorld(bx, by, rad, u * wb * 0.9f, 14f);
			float length = 760f + 760f * (float)Hash01(i * 17 + 3);
			float amp = (26f + 46f * (float)Hash01(i * 19 + 5)) * (1f + Math.Min(0.6f, _jfSpeed / 220f));
			float lambda = 340f + 260f * (float)Hash01(i * 23 + 7);
			float phase = (float)(_jfWave * Math.PI * 2.0 * (0.8 + 0.4 * Hash01(i * 29 + 11))) + (float)Hash01(i * 31) * 6.28f;
			float spread = u * 0.55f + 0.18f * (float)(Hash01(i * 41 + 1) - 0.5);
			SKPoint[] pts = JfStrand(root.X, root.Y, rad, length, amp, lambda, phase, 44, spread);
			if (i % 3 == 2)
			{
				// a far tentacle: soft, out of focus, only as light on the blurred layer
				using SKPath soft = JfRibbon(pts, 9f, 3f);
				using var shader = SKShader.CreateLinearGradient(pts[0], pts[^1], new[] { new SKColor(120, 136, 152, 70), new SKColor(120, 136, 152, 0) }, null, SKShaderTileMode.Clamp);
				far.Shader = shader;
				q.DrawPath(soft, far);
				far.Shader = null;
				continue;
			}
			// glossy grey glass, fading toward its end; a thin lit edge along one side
			using (SKPath rib = JfRibbon(pts, 6.2f, 0.8f))
			using (var shader = SKShader.CreateLinearGradient(pts[0], pts[^1], new[] { new SKColor(78, 88, 100, 225), new SKColor(58, 66, 76, 170), new SKColor(50, 56, 64, 0) }, new[] { 0f, 0.6f, 1f }, SKShaderTileMode.Clamp))
			{
				body.Shader = shader;
				c.DrawPath(rib, body);
				body.Shader = null;
			}
			using (var line = new SKPath())
			{
				int end = pts.Length * 3 / 4;
				line.MoveTo(pts[0].X, pts[0].Y - 1.4f);
				for (int j = 1; j < end; j++)
				{
					line.LineTo(pts[j].X, pts[j].Y - 1.4f);
				}
				using var shader = SKShader.CreateLinearGradient(pts[0], pts[end], new[] { new SKColor(225, 234, 242, 130), new SKColor(225, 234, 242, 0) }, null, SKShaderTileMode.Clamp);
				hi.Shader = shader;
				hi.StrokeWidth = 1.1f;
				c.DrawPath(line, hi);
				hi.Shader = null;
			}
			// beads of red light streaming down every third tentacle
			if (i % 3 == 1)
			{
				if (i == 7)
				{
					_jfBeadAt = JfAlong(pts, 0.52f);
				}
				const float gap = 22f;
				float shift = (float)(_jfBead % gap);
				for (float s = shift; s < length; s += gap)
				{
					float k = s / length;
					float env = JfSmooth((k - 0.12f) / 0.15f) * (1f - JfSmooth((k - 0.7f) / 0.25f));
					if (env <= 0.02f)
					{
						continue;
					}
					SKPoint p = JfAlong(pts, k);
					bead.Color = light.WithAlpha((byte)(230 * env * Math.Min(1f, glow)));
					c.DrawCircle(p.X, p.Y, 1.9f, bead);
					SkiaKit.Glow(q, p.X, p.Y, 9f, 9f, light.WithAlpha((byte)(120 * env * Math.Min(1f, glow))), SKBlendMode.Plus);
				}
			}
		}
	}

	private void JfArmsDraw(SKCanvas c, SKCanvas q, float bx, float by, float rad, SKColor light, float glow)
	{
		using var body = new SKPaint { IsAntialias = true };
		using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f, StrokeJoin = SKStrokeJoin.Round };
		using var core = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus };
		using var speck = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
		for (int i = 0; i < JfArms; i++)
		{
			float u = -0.36f + 0.24f * i;
			SKPoint root = JfWorld(bx, by, rad, u * 120f, -10f);
			float length = 210f + 110f * (float)Hash01(i * 37 + 2);
			float phase = (float)(_jfWave * Math.PI * 2.0 * 0.7) + i * 1.3f;
			SKPoint[] pts = JfStrand(root.X, root.Y, rad, length, 30f, 230f + 40f * i, phase, 40, u * 0.4f);
			if (i == 1)
			{
				_jfArmTip = JfAlong(pts, 0.8f);
			}
			// frilled edges: the width ripples along the arm, and the ripple travels
			float wph = (float)_jfWave * 5f + i;
			using SKPath rib = JfRibbon(pts, 36f - 5f * (i % 2), 6f, j => 0.78f + 0.22f * MathF.Sin(j * 0.9f - wph));
			using (var shader = SKShader.CreateLinearGradient(pts[0], pts[^1], new[] { new SKColor(10, 11, 13, 245), new SKColor(16, 18, 22, 225), new SKColor(16, 18, 22, 0) }, new[] { 0f, 0.75f, 1f }, SKShaderTileMode.Clamp))
			{
				body.Shader = shader;
				c.DrawPath(rib, body);
				body.Shader = null;
			}
			using (var shader = SKShader.CreateLinearGradient(pts[0], pts[^1], new[] { new SKColor(160, 172, 186, 100), new SKColor(160, 172, 186, 0) }, null, SKShaderTileMode.Clamp))
			{
				edge.Shader = shader;
				c.DrawPath(rib, edge);
				edge.Shader = null;
			}
			// a faint red light glowing inside the arm, as in the cover
			using (var line = new SKPath())
			{
				line.MoveTo(pts[0]);
				for (int j = 1; j < pts.Length * 2 / 3; j++)
				{
					line.LineTo(pts[j]);
				}
				using var shader = SKShader.CreateLinearGradient(pts[0], pts[pts.Length * 2 / 3], new[] { light.WithAlpha((byte)Math.Clamp(70f * glow, 0f, 110f)), light.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
				core.Shader = shader;
				core.StrokeWidth = 3f;
				c.DrawPath(line, core);
				core.Shader = null;
			}
			for (int k = 0; k < 10; k++)
			{
				float s = (float)Hash01(i * 50 + k) * 0.7f;
				SKPoint p = JfAlong(pts, s);
				float tw = 0.5f + 0.5f * MathF.Sin((float)_jfDrift * (1.5f + k * 0.3f) + k);
				speck.Color = light.WithAlpha((byte)(170 * tw * Math.Min(1f, glow) * (1f - s)));
				c.DrawCircle(p.X + 8f * (float)(Hash01(k * 3 + i) - 0.5), p.Y + 8f * (float)(Hash01(k * 5 + i) - 0.5), 1.6f, speck);
			}
		}
	}

	// ---------- the bell ----------

	/// <summary>The bell's outline in its own frame: the dome over the top, then the scalloped skirt back along the margin.</summary>
	private static SKPath JfBellPath(float wb, float hb, float squeeze, double t)
	{
		var p = new SKPath();
		const int n = 70;
		for (int i = 0; i <= n; i++)
		{
			float u = MathF.PI * (1f - i / (float)n);
			float s = Math.Max(0f, MathF.Sin(u));
			float wob = 1f + 0.025f * MathF.Sin(3f * u + (float)t * 0.9f);
			float x = wb * MathF.Cos(u) * (1f + 0.06f * s * s);
			float y = -hb * MathF.Pow(s, 0.78f) * wob;
			if (i == 0)
			{
				p.MoveTo(x, y);
			}
			else
			{
				p.LineTo(x, y);
			}
		}
		float pull = 0.97f - 0.09f * squeeze;
		const int m = 96;
		for (int j = 0; j <= m; j++)
		{
			float nx = 1f - 2f * j / m;
			float lobe = MathF.Pow(MathF.Abs(MathF.Sin(nx * MathF.PI * 4f + 0.2f * MathF.Sin((float)t * 0.7f))), 0.6f);
			float y = 6f + 16f * lobe * (1f - 0.35f * nx * nx) + 8f * squeeze;
			p.LineTo(wb * nx * pull, y);
		}
		p.Close();
		return p;
	}

	/// <summary>Half the bell's width at a height ny (0 at the margin, -1 at the crown).</summary>
	private static float JfHalfWidth(float wb, float ny) => wb * MathF.Sqrt(Math.Max(0f, 1f - MathF.Pow(Math.Max(0f, -ny), 2.2f)));

	private void JfBell(SKCanvas c, SKCanvas q, float bx, float by, float theta, float squeeze, SKColor light, float glow, double t)
	{
		float wb = 165f * (1f - 0.13f * squeeze), hb = 185f * (1f + 0.07f * squeeze);
		float rad = theta * MathF.PI / 180f;
		_jfCrown = JfWorld(bx, by, rad, -wb * 0.2f, -hb * 0.96f);
		_jfFrill = JfWorld(bx, by, rad, -wb * 0.72f, 8f);
		using SKPath bell = JfBellPath(wb, hb, squeeze, t);

		// the soft light: red under the skirt, a faint glow inside, a sheen on the crown, on the blurred layer
		q.Save();
		q.Translate(bx, by);
		q.RotateDegrees(theta);
		byte ga = (byte)Math.Clamp(150f * glow, 0f, 200f);
		SkiaKit.Glow(q, 0f, -6f, wb * 1.1f, 46f, light.WithAlpha(ga), SKBlendMode.Plus);
		SkiaKit.Glow(q, -wb * 0.62f, -22f, 62f, 58f, light.WithAlpha((byte)(ga * 0.7f)), SKBlendMode.Plus);
		SkiaKit.Glow(q, wb * 0.6f, -20f, 58f, 54f, light.WithAlpha((byte)(ga * 0.6f)), SKBlendMode.Plus);
		SkiaKit.Glow(q, 0f, -hb * 0.4f, wb * 0.6f, hb * 0.36f, light.WithAlpha((byte)(ga * 0.22f)), SKBlendMode.Plus);
		SkiaKit.Glow(q, -wb * 0.2f, -hb * 0.8f, wb * 0.5f, 30f, new SKColor(190, 210, 228, 40), SKBlendMode.Plus);
		q.Restore();

		c.Save();
		c.Translate(bx, by);
		c.RotateDegrees(theta);
		// dark glass lit from the top
		using (var shader = SKShader.CreateLinearGradient(new SKPoint(-wb * 0.3f, -hb), new SKPoint(wb * 0.2f, 20f),
			new[] { new SKColor(78, 92, 106, 240), new SKColor(28, 34, 41, 246), new SKColor(12, 14, 17, 250) }, new[] { 0f, 0.42f, 1f }, SKShaderTileMode.Clamp))
		using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
		{
			c.DrawPath(bell, paint);
		}
		c.Save();
		c.ClipPath(bell, SKClipOperation.Intersect, true);
		// the inner body seen through the glass: a darker dome with soft edges, lit red from below
		using (var inner = new SKPaint { IsAntialias = true, Color = new SKColor(4, 5, 7, 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12f) })
		{
			c.DrawOval(new SKRect(-wb * 0.55f, -hb * 0.62f, wb * 0.55f, hb * 0.05f), inner);
		}
		using (var innerLight = SKShader.CreateRadialGradient(new SKPoint(0f, -hb * 0.12f), wb * 0.5f, new[] { light.WithAlpha((byte)Math.Clamp(60f * glow, 0f, 90f)), light.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
		using (var ip = new SKPaint { IsAntialias = true, Shader = innerLight, BlendMode = SKBlendMode.Plus })
		{
			c.DrawOval(new SKRect(-wb * 0.6f, -hb * 0.5f, wb * 0.6f, hb * 0.1f), ip);
		}
		// the folds running down from the crown
		using (var fold = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12f, Color = new SKColor(0, 0, 0, 55), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f) })
		using (var foldHi = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(170, 188, 204, 18) })
		{
			for (int k = -3; k <= 3; k++)
			{
				float f = k / 3.6f;
				using var path = new SKPath();
				path.MoveTo(f * wb * 0.18f, -hb * 0.94f);
				path.QuadTo(f * wb * 0.95f, -hb * 0.55f, f * wb * 0.98f, 18f);
				c.DrawPath(path, fold);
				path.Offset(-4f, 0f);
				c.DrawPath(path, foldHi);
			}
		}
		// the glassy rim: the edge of the dome catches more light than its face
		c.Save();
		c.Translate(0f, -hb * 0.3f);
		c.Scale(wb * 1.04f, hb * 0.78f);
		using (var fres = SKShader.CreateRadialGradient(new SKPoint(0f, 0f), 1f, new[] { SKColors.Transparent, SKColors.Transparent, new SKColor(170, 192, 212, 40), new SKColor(200, 218, 232, 110) }, new[] { 0f, 0.66f, 0.9f, 1f }, SKShaderTileMode.Clamp))
		using (var fp = new SKPaint { IsAntialias = true, Shader = fres, BlendMode = SKBlendMode.Plus })
		{
			c.DrawRect(-1.3f, -1.3f, 2.6f, 2.6f, fp);
		}
		c.Restore();
		// a faint red wash up into the skirt
		using (var band = SKShader.CreateLinearGradient(new SKPoint(0f, -hb * 0.5f), new SKPoint(0f, 26f),
			new[] { light.WithAlpha(0), light.WithAlpha((byte)Math.Clamp(34f * glow, 0f, 70f)), light.WithAlpha((byte)Math.Clamp(90f * glow, 0f, 140f)) }, new[] { 0f, 0.6f, 1f }, SKShaderTileMode.Clamp))
		using (var bp = new SKPaint { IsAntialias = true, Shader = band })
		{
			c.DrawRect(-wb * 1.2f, -hb * 0.5f, wb * 2.4f, hb * 0.5f + 40f, bp);
		}
		c.Restore();

		// the skirt: two rows of ruffled lobes of red tissue, each lit from inside with a bright lower edge
		JfLobes(c, wb, hb, squeeze, light, glow, row: 1);
		JfLobes(c, wb, hb, squeeze, light, glow, row: 0);
		// specks of light in the tissue, twinkling; the squeeze makes them flare
		using (var sp = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
		{
			var hot = new SKColor(light.Red, (byte)Math.Min(255, light.Green + 80), (byte)Math.Min(255, light.Blue + 70));
			for (int i = 0; i < 200; i++)
			{
				float h1 = (float)Hash01(i * 7 + 101), h2 = (float)Hash01(i * 13 + 202);
				float ny = 0.06f - 0.42f * MathF.Pow(h2, 1.7f);
				float x = (h1 * 2f - 1f) * JfHalfWidth(wb, ny) * 0.95f, y = ny * hb + 6f;
				float tw = 0.45f + 0.55f * MathF.Sin((float)_jfDrift * (0.8f + 2.2f * (float)Hash01(i + 9)) + i);
				float near = 1f - Math.Clamp(-ny / 0.42f, 0f, 1f);
				float a = Math.Clamp((0.25f + 0.75f * tw) * near * glow, 0f, 1f);
				sp.Color = (i % 5 == 0 ? hot : light).WithAlpha((byte)(150 * a));
				c.DrawCircle(x, y, 0.8f + 2f * (float)Hash01(i * 3 + 7), sp);
			}
		}
		// the glossy outline and highlights on the crown: a soft window reflection and a crisp streak
		using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = new SKColor(196, 212, 226, 60) })
		{
			c.DrawPath(bell, rim);
		}
		using (var spec = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 9f, Color = new SKColor(226, 236, 246, 70), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) })
		{
			using var arc = new SKPath();
			arc.AddArc(new SKRect(-wb * 0.86f, -hb * 0.94f, wb * 0.86f, hb * 0.62f), 206f, 72f);
			c.DrawPath(arc, spec);
			spec.StrokeWidth = 2.2f;
			spec.MaskFilter = null;
			spec.Color = new SKColor(240, 246, 250, 150);
			using var small = new SKPath();
			small.AddArc(new SKRect(-wb * 0.72f, -hb * 0.86f, wb * 0.72f, hb * 0.5f), 220f, 30f);
			c.DrawPath(small, spec);
			spec.Color = new SKColor(240, 246, 250, 60);
			using var right = new SKPath();
			right.AddArc(new SKRect(-wb * 0.8f, -hb * 0.9f, wb * 0.8f, hb * 0.55f), 300f, 22f);
			c.DrawPath(right, spec);
		}
		c.Restore();
	}

	/// <summary>One row of the skirt's lobes: row 0 hangs at the margin, row 1 sits a little higher, smaller and dimmer.</summary>
	private void JfLobes(SKCanvas c, float wb, float hb, float squeeze, SKColor light, float glow, int row)
	{
		int n = row == 0 ? 11 : 9;
		float pull = 0.97f - 0.09f * squeeze;
		float ny = row == 0 ? 0.02f : -0.17f;
		float hw = JfHalfWidth(wb, ny) * (row == 0 ? pull : 0.94f);
		float rx0 = hw / n * 1.25f, ry0 = row == 0 ? 17f : 14f;
		float a = (row == 0 ? 0.85f : 0.55f) * Math.Min(1.25f, glow);
		var deep = new SKColor((byte)(light.Red * 0.45f), (byte)(light.Green * 0.3f), (byte)(light.Blue * 0.35f));
		var pale = new SKColor(255, (byte)Math.Min(255, light.Green + 120), (byte)Math.Min(255, light.Blue + 110));
		using var fill = new SKPaint { IsAntialias = true };
		using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.3f };
		for (int k = 0; k < n; k++)
		{
			float nx = -1f + (2f * k + 1f + 0.5f * (float)(Hash01(k * 13 + row * 7) - 0.5)) / n;
			float size = 0.75f + 0.5f * (float)Hash01(k * 17 + row * 3 + 5);
			float rx = rx0 * size, ry = ry0 * (0.8f + 0.4f * (float)Hash01(k * 19 + row));
			float wob = MathF.Sin((float)_jfDrift * (0.9f + 0.3f * k) + k * 1.7f + row);
			float x = nx * hw, y = ny * hb + 8f + 4f * wob + 6f * squeeze * (row == 0 ? 1f : 0.5f);
			float sx = rx * (1f + 0.08f * wob), sy = ry * (1f - 0.06f * wob);
			var oval = new SKRect(x - sx, y - sy, x + sx, y + sy);
			using (var sh = SKShader.CreateRadialGradient(new SKPoint(x, y + sy * 0.25f), Math.Max(sx, sy),
				new[] { light.WithAlpha((byte)Math.Clamp(150f * a, 0f, 190f)), light.WithAlpha((byte)Math.Clamp(80f * a, 0f, 120f)), deep.WithAlpha((byte)Math.Clamp(40f * a, 0f, 70f)) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp))
			{
				fill.Shader = sh;
				c.DrawOval(oval, fill);
				fill.Shader = null;
			}
			// the lower rim of the lobe catches the light
			edge.Color = pale.WithAlpha((byte)Math.Clamp(90f * a, 0f, 130f));
			using var arc = new SKPath();
			arc.AddArc(oval, 35f + 20f * (float)Hash01(k * 23 + row), 90f);
			c.DrawPath(arc, edge);
		}
	}

	// ---------- type ----------

	/// <summary>
	/// Field-guide labels: a small caption and a thin value, an underline, and a hairline leader to the part of the
	/// jellyfish that the reading drives, ending in a small ring.
	/// </summary>
	private void JfLabels(SKCanvas c, double dt, double rate, float tmax)
	{
		string Num(string key, string unit)
		{
			if (!McOnline(key))
			{
				return "--";
			}
			return Math.Round(Math.Max(0f, McEased("jf.t." + key, Val(key), dt, 2.6))).ToString("0", CultureInfo.InvariantCulture) + unit;
		}
		string temps = $"CPU {Num("cpu.temp", "°")}  ·  GPU {Num("gpu.temp", "°")}";
		string warm = tmax > 0f ? Math.Round(McEased("jf.t.max", tmax, dt, 2.6)).ToString("0", CultureInfo.InvariantCulture) + "°" : "--";
		JfLabel(c, 44f, 34f, "PULSE", rate.ToString("0.00", CultureInfo.InvariantCulture) + " HZ", "CPU " + Num("cpu.load", "%"), _jfCrown, right: true);
		JfLabel(c, 640f, 34f, "MEMORY", "IN USE", Num("ram.load", "%"), _jfArmTip, right: false);
		JfLabel(c, 44f, 356f, "GLOW", temps, warm, _jfFrill, right: true);
		JfLabel(c, 1010f, 34f, "CURRENT", "GPU LOAD", Num("gpu.load", "%"), _jfBeadAt, right: false);
	}

	private void JfLabel(SKCanvas c, float x, float y, string caption, string note, string value, SKPoint anchor, bool right)
	{
		var dim = new SKColor(255, 255, 255, 215);
		SKRect cap = McText(c, caption, x, y, 19f, dim, track: 4f, font: JfLabelFont, stroke: 0.6f);
		SKRect nt = McText(c, note, cap.Right + 16f, y + 2f, 16f, new SKColor(255, 255, 255, 175), track: 2.5f, font: JfLabelFont, stroke: 0.4f);
		SKRect v = McText(c, value, x - 2f, cap.Bottom + 14f, 46f, SKColors.White, track: 1f, font: JfLabelFont);
		float lineY = v.Bottom + 13f;
		float x0 = x, x1 = Math.Max(nt.Right + 24f, v.Right + 30f);
		using var line = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(255, 255, 255, 90) };
		c.DrawLine(x0, lineY, x1, lineY, line);
		// the leader: from the underline's end nearest the jellyfish, straight to the anchor
		SKPoint from = right ? new SKPoint(x1, lineY) : new SKPoint(x0, lineY);
		var dir = new SKPoint(anchor.X - from.X, anchor.Y - from.Y);
		float len = MathF.Max(1f, MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
		SKPoint end = new SKPoint(anchor.X - dir.X / len * 7f, anchor.Y - dir.Y / len * 7f);
		line.Color = new SKColor(255, 255, 255, 70);
		c.DrawLine(from, end, line);
		using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(255, 255, 255, 160) };
		c.DrawCircle(anchor, 7f, ring);
		ring.Style = SKPaintStyle.Fill;
		ring.Color = SKColors.White;
		c.DrawCircle(anchor, 1.8f, ring);
		c.DrawCircle(from, 1.6f, ring);
	}

	// the time: thin white figures in fixed cells, right-aligned at x 1862
	private const float JfTimeSize = 150f, JfTimeRight = 1862f, JfTimeTop = 64f;

	private (float X, float W)[] JfCells()
	{
		float digit = McPath("0", JfThin, JfTimeSize, 0f).TightBounds.Width + 22f;
		float colon = McPath(":", JfThin, JfTimeSize, 0f).TightBounds.Width + 34f;
		float[] w = { digit, digit, colon, digit, digit };
		var cells = new (float, float)[5];
		float x = JfTimeRight;
		for (int i = 4; i >= 0; i--)
		{
			x -= w[i];
			cells[i] = (x, w[i]);
		}
		return cells;
	}

	/// <summary>How far a figure floats, as if seen through moving water.</summary>
	private static float JfFloat(int i, double t) => 3.5f * (float)Math.Sin(t * 0.55 + i * 1.1) + 1.5f * (float)Math.Sin(t * 1.3 + i * 2.3);

	/// <summary>The figures again in the jellyfish's light on the blurred layer: their coloured glow.</summary>
	private void JfTimeGlow(SKCanvas q, double t, SKColor light)
	{
		var cells = JfCells();
		using var paint = new SKPaint { IsAntialias = true, Color = light.WithAlpha(150), BlendMode = SKBlendMode.Plus, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 6f };
		for (int i = 0; i < 5; i++)
		{
			float k = JfSmooth((float)(t - _jfSince[i]) / 0.9f);
			void One(char ch, float dy, float alpha)
			{
				if (ch == '\0' || alpha <= 0.004f)
				{
					return;
				}
				SKPath p = McPath(ch.ToString(), JfThin, JfTimeSize, 0f);
				SKRect b = p.TightBounds;
				paint.Color = light.WithAlpha((byte)(150 * alpha));
				q.Save();
				q.Translate(cells[i].X + (cells[i].W - b.Width) / 2f - b.Left, JfTimeTop - b.Top + JfFloat(i, t) + dy + (ch == ':' ? 18f : 0f));
				q.DrawPath(p, paint);
				q.Restore();
			}
			if (k < 1f)
			{
				One(_jfWas[i], 26f * k, 1f - k);
			}
			One(_jfShown[i], -30f * (1f - k), k);
		}
	}

	private void JfTime(SKCanvas c, double t, DateTime now)
	{
		var cells = JfCells();
		float digitH = McPath("0", JfThin, JfTimeSize, 0f).TightBounds.Height;
		using var paint = new SKPaint { IsAntialias = true };
		using var bubble = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
		void Figure(char ch, int i, float dy, float alpha)
		{
			if (ch == '\0' || alpha <= 0.004f)
			{
				return;
			}
			SKPath p = McPath(ch.ToString(), JfThin, JfTimeSize, 0f);
			SKRect b = p.TightBounds;
			paint.Color = SKColors.White.WithAlpha((byte)(255 * alpha));
			c.Save();
			c.Translate(cells[i].X + (cells[i].W - b.Width) / 2f - b.Left, JfTimeTop - b.Top + JfFloat(i, t) + dy + (ch == ':' ? 18f : 0f));
			c.DrawPath(p, paint);
			c.Restore();
		}
		for (int i = 0; i < 5; i++)
		{
			float since = (float)(t - _jfSince[i]);
			float k = JfSmooth(since / 0.9f);
			if (k < 1f)
			{
				// the old figure sinks and fades; the new one rises in; a few bubbles go up
				Figure(_jfWas[i], i, 26f * k, 1f - k);
				Figure(_jfShown[i], i, -30f * (1f - k), k);
				for (int b = 0; b < 6; b++)
				{
					float bk = Math.Clamp(since / 1.8f - b * 0.05f, 0f, 1f);
					if (bk <= 0f || bk >= 1f)
					{
						continue;
					}
					float bxp = cells[i].X + cells[i].W * (0.25f + 0.5f * (float)Hash01(b * 7 + i)) + 6f * MathF.Sin(bk * 9f + b);
					float byp = JfTimeTop + digitH * (0.9f - 1.2f * bk);
					bubble.Color = new SKColor(255, 255, 255, (byte)(150 * (1f - bk)));
					c.DrawCircle(bxp, byp, 1.5f + 2.5f * (float)Hash01(b * 3 + i * 5), bubble);
				}
			}
			else
			{
				Figure(_jfShown[i], i, 0f, 1f);
			}
		}
		// a hairline under the time that fills with the seconds, and the date
		float x0 = cells[0].X + 8f, y = JfTimeTop + digitH + 34f;
		float s = (now.Second + now.Millisecond / 1000f) / 60f;
		using var line = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(255, 255, 255, 60) };
		c.DrawLine(x0, y, JfTimeRight, y, line);
		line.Color = new SKColor(255, 255, 255, 190);
		float xs = x0 + (JfTimeRight - x0) * s;
		c.DrawLine(x0, y, xs, y, line);
		line.Style = SKPaintStyle.Fill;
		c.DrawCircle(xs, y, 2.2f, line);
		McText(c, now.ToString("dddd  d  MMMM", CultureInfo.CurrentCulture).ToUpperInvariant(), JfTimeRight, y + 18f, 20f, new SKColor(255, 255, 255, 220), align: 1, track: 4f, font: JfLabelFont, stroke: 0.6f);
	}
}
