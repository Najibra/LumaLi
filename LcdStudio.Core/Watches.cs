using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Two watch layouts.
/// - Station Clock (effect "station"): a big white railway-style clock on light grey, with black bar markers, tapered
///   black hands and a slim red second hand that sweeps round in 58.5 s, waits at twelve, and lets the minute hand
///   jump on. Beside it five smaller white dials (processor, graphics, both temperatures, memory) whose needles ease
///   on a spring, with the reading printed in the gap at the foot of each dial.
/// - Sport Dial (effect "sportdial"): a wide capsule split black and cream like a sports smartwatch. In the middle a
///   watch with a cream bezel (minute scale, 12 / 3 / 6, words round the ring), lume hands, a date window at nine,
///   load bars with red upright lettering, the digital time and a seconds arc. Out on the capsule: the loads (black
///   side), memory and network (cream side), and a temperature arc round each rounded end.
/// The still parts are drawn once and kept.
/// </summary>
public sealed partial class ScreenRenderer
{
	private sealed record WtRow(string Label, string Key, string Unit, float Hi, float Warn);

	private static readonly WtRow[] StRows =
	{
		new("PROCESSOR", "cpu.load", "%", 100f, 90f),
		new("GRAPHICS", "gpu.load", "%", 100f, 90f),
		new("CPU TEMP", "cpu.temp", "°", 100f, 85f),
		new("GPU TEMP", "gpu.temp", "°", 100f, 80f),
		new("MEMORY", "ram.load", "%", 100f, 90f)
	};

	private static readonly SKColor StInk = new SKColor(22, 22, 24), StRed = new SKColor(222, 38, 28), StGrey = new SKColor(52, 53, 56);

	private static readonly SKColor SdCream = new SKColor(236, 232, 207), SdBlack = new SKColor(17, 17, 18), SdRed = new SKColor(232, 71, 44), SdLume = new SKColor(244, 240, 218);

	private const float StCx = 250f, StCy = 240f, StRf = 196f, StSmallR = 90f, StSmallY = 254f;

	private static readonly float[] StSmallX = { 660f, 935f, 1210f, 1485f, 1760f };

	private const float SdCx = 960f, SdCy = 240f;

	private SKImage? _stBase, _sdBase;

	private double _wtAt = double.NaN;

	private bool DrawWatches(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Kind != WidgetKind.Panel || (w.Effect != "station" && w.Effect != "sportdial"))
		{
			return DrawJellyfish(g, w, r, t);
		}
		double dt = double.IsNaN(_wtAt) ? 0.0 : Math.Clamp(t - _wtAt, 0.0, 0.25);
		_wtAt = t;
		// previews can fix the clock (--clock); the frame time then moves it on
		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;
		if (w.Effect == "station")
		{
			Station(g, dt, now, t);
		}
		else
		{
			SportDial(g, dt, now, t);
		}
		return true;
	}

	// ---------- shared pieces ----------

	private static float WtBack(float k)
	{
		k = Math.Clamp(k, 0f, 1f) - 1f;
		const float s = 2.2f;
		return 1f + k * k * ((s + 1f) * k + s);
	}

	/// <summary>A hand pointing at angle deg (0 = twelve, clockwise), tapering from wBase at -back to wTip at len.</summary>
	private static SKPath WtHand(float cx, float cy, float deg, float back, float len, float wBase, float wTip, float point = 0f)
	{
		var p = new SKPath();
		p.MoveTo(-wBase / 2f, back);
		p.LineTo(wBase / 2f, back);
		p.LineTo(wTip / 2f, -len + point);
		if (point > 0f)
		{
			p.LineTo(0f, -len);
		}
		p.LineTo(-wTip / 2f, -len + point);
		p.Close();
		p.Transform(SKMatrix.CreateRotationDegrees(deg).PostConcat(SKMatrix.CreateTranslation(cx, cy)));
		return p;
	}

	private static void WtShadow(SKCanvas c, SKPath p, float dx, float dy, float sigma, byte alpha)
	{
		using var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
		using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, alpha), MaskFilter = mf };
		c.Save();
		c.Translate(dx, dy);
		c.DrawPath(p, paint);
		c.Restore();
	}

	private static void WtTick(SKCanvas c, float cx, float cy, float deg, float r0, float r1, float width, SKPaint paint)
	{
		c.Save();
		c.Translate(cx, cy);
		c.RotateDegrees(deg);
		c.DrawRect(new SKRect(-width / 2f, -r1, width / 2f, -r0), paint);
		c.Restore();
	}

	private static SKPoint WtAt(float cx, float cy, float deg, float r)
	{
		float a = deg * MathF.PI / 180f;
		return new SKPoint(cx + MathF.Sin(a) * r, cy - MathF.Cos(a) * r);
	}

	/// <summary>A reading eased on a spring, or NaN when the sensor has nothing.</summary>
	private float WtValue(string key, double dt)
	{
		if (!McOnline(key))
		{
			return float.NaN;
		}
		return Math.Max(0f, McEased("wt." + key, Val(key), dt, 2.6));
	}

	/// <summary>Station-clock time: the second hand sweeps in 58.5 s and waits at twelve; the minute hand jumps on.</summary>
	private static (float Hour, float Minute, float Second) WtStation(DateTime now)
	{
		float s = now.Second + now.Millisecond / 1000f;
		float sec = Math.Min(s / 58.5f, 1f) * 360f;
		float jump = WtBack(s / 0.4f);
		float min = (now.Minute - 1 + jump) * 6f;
		float hour = (now.Hour % 12 + (now.Minute - 1 + jump) / 60f) * 30f;
		return (hour, min, sec);
	}

	// ---------- Station Clock ----------

	private void Station(Graphics g, double dt, DateTime now, double t)
	{
		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_stBase ??= StMakeBase();
			c.DrawImage(_stBase, 0f, 0f);

			// the header line: date on the left, uptime and the digital time on the right
			McText(c, now.ToString("dddd  d  MMMM", CultureInfo.CurrentCulture).ToUpperInvariant(), 556f, 40f, 22f, StGrey, track: 3.5f);
			TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
			SKRect tr = McText(c, now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 1890f, 40f, 22f, StInk, align: 1, track: 2f);
			McText(c, $"UP  {(int)up.TotalHours}:{up.Minutes:00}", tr.Left - 34f, 40f, 22f, new SKColor(120, 122, 126), align: 1, track: 3f);

			// the five dials: needle, reading, warning lamp
			for (int i = 0; i < StRows.Length; i++)
			{
				WtRow row = StRows[i];
				float cx = StSmallX[i], cy = StSmallY, rf = StSmallR;
				float v = WtValue(row.Key, dt);
				float k = float.IsNaN(v) ? 0f : Math.Clamp(v / row.Hi, 0f, 1.02f);
				float deg = -135f + 270f * k;
				using (SKPath needle = WtHand(cx, cy, deg, rf * 0.2f, rf * 0.84f, rf * 0.085f, rf * 0.026f))
				{
					WtShadow(c, needle, 2.5f, 5f, 3.5f, 70);
					using var ink = new SKPaint { IsAntialias = true, Color = StInk };
					c.DrawPath(needle, ink);
				}
				using (var cap = new SKPaint { IsAntialias = true, Color = StRed })
				{
					c.DrawCircle(cx, cy, rf * 0.07f, cap);
				}
				using (var pin = new SKPaint { IsAntialias = true, Color = new SKColor(250, 250, 250) })
				{
					c.DrawCircle(cx, cy, rf * 0.025f, pin);
				}
				string text = float.IsNaN(v) ? "--" : Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + row.Unit;
				McText(c, text, cx, cy + rf * 0.42f, 30f, StInk, align: 0, font: "Bahnschrift SemiBold");
				// a red lamp by the name blinks while the reading is past its warning level
				if (!float.IsNaN(v) && v >= row.Warn)
				{
					float b = 0.55f + 0.45f * MathF.Sin((float)t * 6f);
					using var lamp = new SKPaint { IsAntialias = true, Color = StRed.WithAlpha((byte)(255 * b)) };
					c.DrawCircle(cx, cy + rf + 52f, 5f, lamp);
				}
			}

			// the big clock's hands, with their shadows
			var (hour, minute, second) = WtStation(now);
			using SKPath hh = WtHand(StCx, StCy, hour, StRf * 0.24f, StRf * 0.62f, StRf * 0.105f, StRf * 0.085f);
			using SKPath mh = WtHand(StCx, StCy, minute, StRf * 0.24f, StRf * 0.92f, StRf * 0.085f, StRf * 0.06f);
			using var sh = new SKPath();
			sh.AddPath(hh);
			sh.AddPath(mh);
			WtShadow(c, sh, 4f, 9f, 5f, 80);
			using (var ink = new SKPaint { IsAntialias = true, Color = StInk })
			{
				c.DrawPath(hh, ink);
				c.DrawPath(mh, ink);
			}
			StSecondHand(c, second);
		});
	}

	/// <summary>The slim red second hand: a counterweight, a hollow ring near the tip, and a short point beyond it.</summary>
	private static void StSecondHand(SKCanvas c, float deg)
	{
		float w = StRf * 0.022f;
		float ringAt = StRf * 0.66f, ringR = StRf * 0.068f;
		using var shape = new SKPath();
		shape.AddRect(new SKRect(-w / 2f, -(ringAt - ringR), w / 2f, StRf * 0.12f));
		shape.AddRect(new SKRect(-w * 1.9f / 2f, StRf * 0.1f, w * 1.9f / 2f, StRf * 0.3f));
		shape.AddRect(new SKRect(-w / 2f, -StRf * 0.9f, w / 2f, -(ringAt + ringR)));
		shape.AddCircle(0f, 0f, StRf * 0.035f);
		using var ring = new SKPath();
		ring.AddCircle(0f, -ringAt, ringR);
		ring.AddCircle(0f, -ringAt, ringR - StRf * 0.026f, SKPathDirection.CounterClockwise);
		shape.AddPath(ring);
		shape.FillType = SKPathFillType.Winding;
		shape.Transform(SKMatrix.CreateRotationDegrees(deg).PostConcat(SKMatrix.CreateTranslation(StCx, StCy)));
		WtShadow(c, shape, 5f, 10f, 4f, 70);
		using var red = new SKPaint { IsAntialias = true, Color = StRed };
		c.DrawPath(shape, red);
		using var pin = new SKPaint { IsAntialias = true, Color = new SKColor(120, 20, 14) };
		c.DrawCircle(StCx, StCy, StRf * 0.012f, pin);
	}

	private SKImage StMakeBase()
	{
		using var surface = SKSurface.Create(new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		// soft light-grey card, lit from above
		using (var shader = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 480f), new[] { new SKColor(236, 237, 238), new SKColor(206, 208, 210) }, null, SKShaderTileMode.Clamp))
		using (var bg = new SKPaint { Shader = shader })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, bg);
		}
		using (var glow = SKShader.CreateRadialGradient(new SKPoint(StCx, StCy - 40f), 620f, new[] { new SKColor(255, 255, 255, 120), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
		using (var gp = new SKPaint { Shader = glow })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, gp);
		}

		// the big clock
		StFace(c, StCx, StCy, StRf, 15f);
		using (var ink = new SKPaint { IsAntialias = true, Color = StInk })
		{
			for (int i = 0; i < 60; i++)
			{
				if (i % 5 == 0)
				{
					WtTick(c, StCx, StCy, i * 6f, StRf * 0.70f, StRf * 0.94f, StRf * 0.075f, ink);
				}
				else
				{
					WtTick(c, StCx, StCy, i * 6f, StRf * 0.865f, StRf * 0.94f, StRf * 0.024f, ink);
				}
			}
		}

		// a hairline ruler under the header
		using (var rule = new SKPaint { IsAntialias = true, Color = new SKColor(150, 152, 156), StrokeWidth = 1.2f })
		{
			c.DrawLine(556f, 86f, 1890f, 86f, rule);
			for (int x = 0; x <= 60; x++)
			{
				float px = 556f + (1890f - 556f) * x / 60f;
				c.DrawLine(px, 86f, px, x % 5 == 0 ? 96f : 91f, rule);
			}
		}

		// the five small dials
		for (int i = 0; i < StRows.Length; i++)
		{
			WtRow row = StRows[i];
			float cx = StSmallX[i], cy = StSmallY, rf = StSmallR;
			StFace(c, cx, cy, rf, 8f);
			using (var ink = new SKPaint { IsAntialias = true, Color = StInk })
			{
				for (int k = 0; k <= 50; k++)
				{
					float deg = -135f + 270f * k / 50f;
					if (k % 5 == 0)
					{
						WtTick(c, cx, cy, deg, rf * 0.68f, rf * 0.93f, rf * 0.06f, ink);
					}
					else
					{
						WtTick(c, cx, cy, deg, rf * 0.84f, rf * 0.93f, rf * 0.022f, ink);
					}
				}
			}
			// the warning zone as a thin red arc inside the scale
			float w0 = -135f + 270f * row.Warn / row.Hi;
			using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = StRed })
			{
				float rr = rf * 0.6f;
				c.DrawArc(new SKRect(cx - rr, cy - rr, cx + rr, cy + rr), w0 - 90f, 135f - w0, false, arc);
			}
			McText(c, row.Label, cx, cy + rf + 26f, 19f, StGrey, align: 0, track: 3f, font: "Bahnschrift SemiBold");
		}
		return surface.Snapshot();
	}

	/// <summary>A white dial: a soft shadow, a brushed grey rim and a gently shaded white face.</summary>
	private static void StFace(SKCanvas c, float cx, float cy, float rf, float rim)
	{
		float ro = rf + rim;
		using (var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ro * 0.07f))
		using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 90), MaskFilter = mf })
		{
			c.DrawCircle(cx, cy + ro * 0.05f, ro, shadow);
		}
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(cx, cy - ro), new SKPoint(cx, cy + ro), new[] { new SKColor(250, 250, 251), new SKColor(196, 198, 201), new SKColor(150, 152, 156) }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp))
		using (var paint = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawCircle(cx, cy, ro, paint);
		}
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(cx, cy - ro), new SKPoint(cx, cy + ro), new[] { new SKColor(160, 162, 166), new SKColor(246, 246, 247) }, null, SKShaderTileMode.Clamp))
		using (var paint = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawCircle(cx, cy, rf + rim * 0.35f, paint);
		}
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(cx, cy - rf * 0.35f), rf * 1.4f, new[] { new SKColor(255, 255, 255), new SKColor(236, 236, 237) }, null, SKShaderTileMode.Clamp))
		using (var paint = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawCircle(cx, cy, rf, paint);
		}
		// a faint inner shadow along the top edge of the face
		c.Save();
		using (var clip = new SKPath())
		{
			clip.AddCircle(cx, cy, rf);
			c.ClipPath(clip, SKClipOperation.Intersect, true);
		}
		using (var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, rf * 0.04f))
		using (var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = rf * 0.05f, Color = new SKColor(0, 0, 0, 50), MaskFilter = mf })
		{
			c.DrawCircle(cx, cy + rf * 0.02f, rf + rf * 0.01f, inner);
		}
		c.Restore();
	}

	// ---------- Sport Dial ----------

	private static readonly SKRect SdBody = new SKRect(20f, 20f, 1900f, 460f);

	private const float SdR = 220f, SdIn = 174f;

	private void SportDial(Graphics g, double dt, DateTime now, double t)
	{
		float cpu = WtValue("cpu.load", dt), gpu = WtValue("gpu.load", dt);
		float ct = WtValue("cpu.temp", dt), gt = WtValue("gpu.temp", dt), ram = WtValue("ram.load", dt);
		float down = McOnline("net.down") ? Math.Max(0f, McEased("wt.down", Val("net.down"), dt, 2.6)) : float.NaN;
		float upl = McOnline("net.up") ? Math.Max(0f, McEased("wt.up", Val("net.up"), dt, 2.6)) : float.NaN;
		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_sdBase ??= SdMakeBase();
			c.DrawImage(_sdBase, 0f, 0f);

			// the rounded ends: a temperature arc each
			SdEndArc(c, 240f, ct, 85f, left: true);
			SdEndArc(c, 1680f, gt, 80f, left: false);
			SdBig(c, ct, "°", 168f, 178f, SdLume, 0);
			SdBig(c, gt, "°", 1752f, 178f, SdBlack, 0);

			// the black side: processor and graphics load
			SdLoad(c, 344f, "PROCESSOR", cpu);
			SdLoad(c, 548f, "GRAPHICS", gpu);
			// the cream side: memory and network
			SdBig(c, ram, "%", 1196f, 118f, SdBlack, -1);
			SdSegments(c, 1196f, 252f, 186f, 22f, float.IsNaN(ram) ? 0f : ram / 100f, 90f / 100f, onCream: true);
			SdBig(c, down, "", 1414f, 118f, SdBlack, -1, net: true);
			McText(c, float.IsNaN(upl) ? "UP  --" : $"UP  {upl:0.0}", 1414f, 262f, 22f, SdBlack, track: 2f, font: "Bahnschrift SemiBold");

			SdWatch(c, now, cpu, gpu, t);
		});
	}

	/// <summary>A big reading: the number in bold with a smaller unit after it.</summary>
	private void SdBig(SKCanvas c, float v, string unit, float x, float y, SKColor ink, int align, bool net = false)
	{
		string text = float.IsNaN(v) ? "--" : net ? v.ToString(v >= 100f ? "0" : "0.0", CultureInfo.InvariantCulture) : Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
		SKPath p = McPath(text, "Bahnschrift|B", 96f, 0f);
		float uw = unit.Length > 0 ? McPath(unit, "Bahnschrift|B", 48f, 0f).TightBounds.Width + 6f : 0f;
		float total = p.TightBounds.Width + uw;
		float left = align < 0 ? x : align == 0 ? x - total / 2f : x - total;
		SKRect r = McText(c, text, left, y, 96f, ink, font: "Bahnschrift|B");
		if (unit.Length > 0)
		{
			McText(c, unit, r.Right + 6f, r.Top, 48f, ink, font: "Bahnschrift|B");
		}
	}

	private void SdLoad(SKCanvas c, float x, string label, float v)
	{
		SdBig(c, v, "%", x, 118f, SdLume, -1);
		SdSegments(c, x, 252f, 180f, 22f, float.IsNaN(v) ? 0f : v / 100f, 0.9f, onCream: false);
	}

	/// <summary>A row of segments lit up to k; those past warn turn red.</summary>
	private static void SdSegments(SKCanvas c, float x, float y, float w, float h, float k, float warn, bool onCream)
	{
		const int n = 15;
		float gap = 3f, sw = (w - gap * (n - 1)) / n;
		using var paint = new SKPaint { IsAntialias = true };
		for (int i = 0; i < n; i++)
		{
			float u = (i + 0.5f) / n;
			float lit = Math.Clamp((k - i / (float)n) * n, 0f, 1f);
			SKColor off = onCream ? new SKColor(211, 207, 182) : new SKColor(44, 44, 43);
			SKColor on = u > warn ? SdRed : onCream ? SdBlack : SdLume;
			paint.Color = WtMix(off, on, lit);
			c.DrawRect(new SKRect(x + i * (sw + gap), y, x + i * (sw + gap) + sw, y + h), paint);
		}
	}

	private static SKColor WtMix(SKColor a, SKColor b, float k)
	{
		k = Math.Clamp(k, 0f, 1f);
		return new SKColor((byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k), (byte)(a.Alpha + (b.Alpha - a.Alpha) * k));
	}

	/// <summary>A segmented arc round a rounded end of the capsule, lit with the temperature (0–100 °C).</summary>
	private static void SdEndArc(SKCanvas c, float cx, float v, float warn, bool left)
	{
		const int n = 26;
		float r = 196f, sweep = 140f;
		float k = float.IsNaN(v) ? 0f : Math.Clamp(v / 100f, 0f, 1f);
		using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14f };
		var oval = new SKRect(cx - r, SdCy - r, cx + r, SdCy + r);
		for (int i = 0; i < n; i++)
		{
			// both fill from the bottom up. Skia angles: 0 = three o'clock, clockwise; the left arc runs 110°→250°
			// (bottom-left round to top-left), the right arc 70°→-70°
			float u = (i + 0.5f) / n;
			float lit = Math.Clamp((k - i / (float)n) * n, 0f, 1f);
			SKColor off = left ? new SKColor(44, 44, 43) : new SKColor(211, 207, 182);
			SKColor on = u * 100f > warn ? SdRed : left ? SdLume : SdBlack;
			paint.Color = WtMix(off, on, lit);
			float start = left ? 110f + i * sweep / n : 70f - (i + 1) * sweep / n;
			c.DrawArc(oval, start + 0.6f, sweep / n - 1.6f, false, paint);
		}
	}

	private void SdWatch(SKCanvas c, DateTime now, float cpu, float gpu, double t)
	{
		// load bars in the black half, with red upright lettering beside them
		SdUpright(c, 900f, 78f, cpu);
		SdUpright(c, 900f, 292f, gpu);

		// date window at nine
		var pill = new SKRect(772f, 214f, 902f, 266f);
		using (var cream = new SKPaint { IsAntialias = true, Color = SdCream })
		{
			c.DrawRoundRect(pill, 26f, 26f, cream);
		}
		McText(c, now.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant(), 792f, 220f, 18f, SdBlack, font: "Bahnschrift SemiBold");
		McText(c, now.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), 792f, 260f, 18f, SdBlack, font: "Bahnschrift SemiBold", bottom: true);
		McText(c, now.Day.ToString(CultureInfo.InvariantCulture), 888f, 240f - 17f, 34f, SdRed, align: 1, font: "Bahnschrift|B");

		// the cream half: digital time between two small fields
		TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
		McText(c, "UP", 992f, 132f, 14f, SdBlack, track: 2f, font: "Bahnschrift SemiBold");
		McText(c, $"{(int)up.TotalHours}:{up.Minutes:00}", 1074f, 128f, 22f, SdBlack, align: 1, font: "Bahnschrift SemiBold");
		SKRect tr = McText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), 990f, 196f, 50f, SdBlack, font: "Bahnschrift|B");
		McText(c, now.ToString("ss", CultureInfo.InvariantCulture), tr.Right + 6f, tr.Bottom, 20f, SdRed, bottom: true, font: "Bahnschrift SemiBold");
		int week = ISOWeek.GetWeekOfYear(now);
		McText(c, "WEEK", 992f, 300f, 14f, SdBlack, track: 2f, font: "Bahnschrift SemiBold");
		McText(c, week.ToString(CultureInfo.InvariantCulture), 1074f, 296f, 22f, SdBlack, align: 1, font: "Bahnschrift SemiBold");

		// the seconds arc on the cream side, filling over the minute
		float s = now.Second + now.Millisecond / 1000f;
		using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 9f })
		{
			const int n = 16;
			float rr = SdIn - 10f;
			var oval = new SKRect(SdCx - rr, SdCy - rr, SdCx + rr, SdCy + rr);
			for (int i = 0; i < n; i++)
			{
				float lit = Math.Clamp((s / 60f - i / (float)n) * n, 0f, 1f);
				arc.Color = WtMix(new SKColor(211, 207, 182), SdBlack, lit);
				c.DrawArc(oval, -40f + i * 80f / n + 0.6f, 80f / n - 1.4f, false, arc);
			}
		}

		// the hands: lume-filled with a dark edge, and a slim red seconds hand that sweeps
		float hour = (now.Hour % 12 + now.Minute / 60f + s / 3600f) * 30f;
		float minute = (now.Minute + s / 60f) * 6f;
		using SKPath hh = WtHand(SdCx, SdCy, hour, 20f, 118f, 17f, 15f, 18f);
		using SKPath mh = WtHand(SdCx, SdCy, minute, 20f, 168f, 15f, 12f, 18f);
		using var both = new SKPath();
		both.AddPath(hh);
		both.AddPath(mh);
		WtShadow(c, both, 4f, 8f, 5f, 110);
		using (var lume = new SKPaint { IsAntialias = true, Color = SdLume })
		using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = SdBlack, StrokeJoin = SKStrokeJoin.Round })
		{
			foreach (SKPath p in new[] { hh, mh })
			{
				c.DrawPath(p, lume);
				c.DrawPath(p, edge);
			}
		}
		// the hands' black centre line, like the reference's slot
		using (var slot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SKColor(40, 40, 40), StrokeCap = SKStrokeCap.Round })
		{
			SKPoint a = WtAt(SdCx, SdCy, hour, 30f), b = WtAt(SdCx, SdCy, hour, 96f);
			c.DrawLine(a, b, slot);
			a = WtAt(SdCx, SdCy, minute, 30f);
			b = WtAt(SdCx, SdCy, minute, 146f);
			c.DrawLine(a, b, slot);
		}
		using SKPath sh = WtHand(SdCx, SdCy, s * 6f, 42f, 190f, 4.5f, 2.5f);
		WtShadow(c, sh, 4f, 8f, 3f, 90);
		using (var red = new SKPaint { IsAntialias = true, Color = SdRed })
		{
			c.DrawPath(sh, red);
			using (var cap = new SKPaint { IsAntialias = true, Color = SdBlack }) { c.DrawCircle(SdCx, SdCy, 13f, cap); }
			c.DrawCircle(SdCx, SdCy, 7f, red);
		}

		// a soft crystal sheen over the dial
		using (var sheen = SKShader.CreateLinearGradient(new SKPoint(SdCx - 160f, SdCy - 220f), new SKPoint(SdCx + 60f, SdCy + 40f), new[] { new SKColor(255, 255, 255, 34), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
		using (var sp = new SKPaint { IsAntialias = true, Shader = sheen, BlendMode = SKBlendMode.Plus })
		{
			c.DrawCircle(SdCx, SdCy, SdR - 4f, sp);
		}
	}

	/// <summary>A vertical stack of load segments in the black half, lit from the bottom.</summary>
	private static void SdUpright(SKCanvas c, float x, float y, float v)
	{
		const int n = 9;
		float k = float.IsNaN(v) ? 0f : Math.Clamp(v / 100f, 0f, 1f);
		using var paint = new SKPaint { IsAntialias = true };
		for (int i = 0; i < n; i++)
		{
			float lit = Math.Clamp((k - i / (float)n) * n, 0f, 1f);
			float yy = y + (n - 1 - i) * 12.5f;
			// wider at the bottom, like the reference's stepped bars
			float w = 22f + 8f * (1f - i / (float)(n - 1));
			paint.Color = WtMix(new SKColor(52, 52, 50), i >= 8 ? SdRed : SdLume, lit);
			c.DrawRect(new SKRect(x - w, yy, x, yy + 8f), paint);
		}
	}

	private SKImage SdMakeBase()
	{
		using var surface = SKSurface.Create(new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		c.Clear(new SKColor(10, 10, 11));
		var body = new SKRoundRect(SdBody, SdR, SdR);
		// the capsule: black on the left, cream on the right
		c.Save();
		c.ClipRoundRect(body, SKClipOperation.Intersect, true);
		using (var black = new SKPaint { Color = SdBlack })
		{
			c.DrawRect(0f, 0f, SdCx, 480f, black);
		}
		using (var cream = SKShader.CreateLinearGradient(new SKPoint(0f, 20f), new SKPoint(0f, 460f), new[] { new SKColor(240, 236, 212), new SKColor(226, 222, 196) }, null, SKShaderTileMode.Clamp))
		using (var cp = new SKPaint { Shader = cream })
		{
			c.DrawRect(SdCx, 0f, 1920f - SdCx, 480f, cp);
		}
		c.Restore();
		using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = new SKColor(70, 70, 68) })
		{
			c.DrawRoundRect(body, rim);
		}

		// labels and fine print on the capsule
		McText(c, "CPU TEMP", 168f, 132f, 16f, SdRed, align: 0, track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "°C", 168f, 300f, 14f, new SKColor(150, 148, 136), align: 0, track: 2f, font: "Bahnschrift SemiBold");
		McText(c, "GPU TEMP", 1752f, 132f, 16f, SdRed, align: 0, track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "°C", 1752f, 300f, 14f, new SKColor(110, 108, 96), align: 0, track: 2f, font: "Bahnschrift SemiBold");
		McText(c, "PROCESSOR", 344f, 82f, 16f, SdRed, track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "GRAPHICS", 548f, 82f, 16f, SdRed, track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "MEMORY", 1196f, 82f, 16f, SdRed, track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "NETWORK  MB/S", 1414f, 82f, 16f, SdRed, track: 3f, font: "Bahnschrift SemiBold");
		using (var dim = new SKPaint { IsAntialias = true, StrokeWidth = 1.5f, Color = new SKColor(70, 70, 68) })
		{
			c.DrawLine(344f, 300f, 728f, 300f, dim);
			dim.Color = new SKColor(150, 146, 124);
			c.DrawLine(1196f, 300f, 1600f, 300f, dim);
		}
		McText(c, "LOAD  0 — 100", 344f, 316f, 13f, new SKColor(140, 138, 128), track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "IN USE  0 — 100", 1196f, 316f, 13f, new SKColor(110, 106, 90), track: 3f, font: "Bahnschrift SemiBold");
		McText(c, "LUMALI", 344f, 384f, 18f, new SKColor(120, 118, 110), track: 9f, font: "Bahnschrift SemiBold");
		McText(c, "SPORT DIAL", 1196f, 384f, 18f, new SKColor(120, 116, 98), track: 9f, font: "Bahnschrift SemiBold");

		// the watch: bezel ring with a minute scale over both halves
		using (var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12f))
		using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120), MaskFilter = mf })
		{
			c.DrawCircle(SdCx + 6f, SdCy + 4f, SdR - 2f, shadow);
		}
		using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = SdR - SdIn, Color = SdCream })
		{
			c.DrawCircle(SdCx, SdCy, (SdR + SdIn) / 2f - 2f, ring);
		}
		c.Save();
		using (var clip = new SKPath())
		{
			clip.AddCircle(SdCx, SdCy, SdIn);
			c.ClipPath(clip, SKClipOperation.Intersect, true);
		}
		using (var black = new SKPaint { Color = SdBlack })
		{
			c.DrawRect(0f, 0f, SdCx, 480f, black);
		}
		using (var cream = new SKPaint { Color = new SKColor(238, 234, 209) })
		{
			c.DrawRect(SdCx, 0f, 960f, 480f, cream);
		}
		c.Restore();
		using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = new SKColor(28, 28, 28) })
		{
			c.DrawCircle(SdCx, SdCy, SdR - 4f, edge);
			edge.StrokeWidth = 2f;
			c.DrawCircle(SdCx, SdCy, SdIn, edge);
		}
		using (var ink = new SKPaint { IsAntialias = true, Color = SdBlack })
		{
			for (int i = 0; i < 60; i++)
			{
				if (i % 15 == 0)
				{
					continue;
				}
				if (i % 5 == 0)
				{
					WtTick(c, SdCx, SdCy, i * 6f, SdR - 30f, SdR - 9f, 5f, ink);
				}
				else
				{
					WtTick(c, SdCx, SdCy, i * 6f, SdR - 17f, SdR - 9f, 2f, ink);
				}
			}
		}
		// 12, 3 and 6 standing on the ring (nine is the date window)
		McText(c, "12", SdCx, SdCy - SdR + 6f, 50f, SdBlack, align: 0, font: "Bahnschrift|B");
		SKPath three = McPath("3", "Bahnschrift|B", 50f, 0f);
		McText(c, "3", SdCx + SdR - 10f, SdCy - three.TightBounds.Height / 2f, 50f, SdBlack, align: 1, font: "Bahnschrift|B");
		McText(c, "6", SdCx, SdCy + SdR - 6f, 50f, SdBlack, align: 0, font: "Bahnschrift|B", bottom: true);
		// words round the ring, upright, like the reference's mode labels
		void Word(string text, float deg, SKColor ink)
		{
			SKPoint p = WtAt(SdCx, SdCy, deg, (SdR + SdIn) / 2f - 2f);
			bool top = deg < 90f || deg > 270f;
			c.Save();
			c.Translate(p.X, p.Y);
			c.RotateDegrees(top ? deg : deg + 180f);
			SKPath path = McPath(text, "Bahnschrift SemiBold", 17f, 1.5f);
			SKRect b = path.TightBounds;
			c.Translate(-b.MidX, -b.MidY);
			using var paint = new SKPaint { IsAntialias = true, Color = ink };
			c.DrawPath(path, paint);
			c.Restore();
		}
		Word("TIME", 330f, SdBlack);
		Word("LOAD", 300f, SdBlack);
		Word("TEMP", 240f, SdBlack);
		Word("FANS", 210f, SdBlack);
		Word("MAX", 30f, SdRed);
		Word("MIN", 150f, SdBlack);

		// the black half: a small wordmark above the date window
		McText(c, "LUMALI", 836f, 186f, 15f, SdLume, align: 0, track: 4f, font: "Bahnschrift SemiBold");
	// red upright lettering beside the load bars
		void Upright(string text, float x, float y)
		{
			for (int i = 0; i < text.Length; i++)
			{
				McText(c, text[i].ToString(), x, y + i * 26f, 19f, SdRed, align: 0, font: "Bahnschrift SemiBold");
			}
		}
		Upright("CPU", 922f, 92f);
		Upright("GPU", 922f, 306f);
		// rules between the fields in the cream half
		using (var rule = new SKPaint { IsAntialias = true, StrokeWidth = 2f, Color = SdBlack })
		{
			c.DrawLine(988f, 168f, 1076f, 168f, rule);
			c.DrawLine(988f, 282f, 1076f, 282f, rule);
		}
		return surface.Snapshot();
	}
}
