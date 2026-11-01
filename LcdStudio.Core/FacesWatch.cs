using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Three minimal watch faces, after a sheet of monochrome smart-watch designs: the time first, the fan's reading as a
/// quiet complication, one red accent.
/// - Dot Time: a field of dots where the hours and minutes swell out of the grid in 5×7 dot-matrix figures; a ring of
///   60 dots fills with the reading and a red dot walks round it with the seconds.
/// - Rolling Time: the hour in big figures, the minutes as a column that rolls up as each minute turns, a red hairline
///   that grows across the face with the seconds, the reading below it.
/// - Analog Dial: a light dial with thin black hands and a red sweep second hand, the reading as a red-to-orange arc
///   along the rim with its name written round it.
/// </summary>
public sealed partial class Faces
{
	/// <summary>Set by previews: the clock the watch faces read, as this time plus the frame's time.</summary>
	public static DateTime? PreviewClock;

	private static DateTime WatchNow(FaceContext x) => PreviewClock.HasValue ? PreviewClock.Value.AddSeconds(x.T) : DateTime.Now;

	// ---------- the 5×7 dot font ----------

	private static readonly Dictionary<char, string[]> DotFont = new Dictionary<char, string[]>
	{
		['0'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
		['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
		['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
		['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
		['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
		['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
		['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
		['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
		['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
		['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
		['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
		['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
		['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
		['D'] = new[] { "###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###.." },
		['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
		['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
		['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
		['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
		['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
		['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
		['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
		['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
		['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
		['N'] = new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" },
		['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
		['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
		['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
		['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
		['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
		['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
		['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
		['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
		['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." },
		['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
		['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
		['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
		['%'] = new[] { "##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##" },
		['°'] = new[] { ".##..", "#..#.", "#..#.", ".##..", ".....", ".....", "....." },
		[':'] = new[] { ".....", "..#..", "..#..", ".....", "..#..", "..#..", "....." },
		['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
		[','] = new[] { ".....", ".....", ".....", ".....", ".##..", "..#..", ".#..." },
		['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
		['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
		[' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." }
	};

	private static bool DotOn(char ch, int col, int row)
	{
		return DotFont.TryGetValue(char.ToUpperInvariant(ch), out string[]? g) && g[row][col] == '#';
	}

	/// <summary>A line of small dot-matrix text centred on cx, its top at y, one dot every pitch pixels.</summary>
	private static void DotText(SKCanvas c, string text, float cx, float y, float pitch, float r, SKColor color, SKColor? unlit = null)
	{
		text = text.ToUpperInvariant();
		float w = text.Length * 6f * pitch - pitch;
		float x0 = cx - w / 2f;
		using var on = new SKPaint { IsAntialias = true, Color = color };
		using var off = new SKPaint { IsAntialias = true, Color = unlit ?? SKColors.Transparent };
		for (int i = 0; i < text.Length; i++)
		{
			for (int row = 0; row < 7; row++)
			{
				for (int col = 0; col < 5; col++)
				{
					float px = x0 + (i * 6 + col) * pitch, py = y + row * pitch;
					if (DotOn(text[i], col, row))
					{
						c.DrawCircle(px, py, r, on);
					}
					else if (unlit.HasValue)
					{
						c.DrawCircle(px, py, r * 0.55f, off);
					}
				}
			}
		}
	}

	// ---------- Dot Time ----------

	private readonly Dictionary<string, float[]> _dotTime = new Dictionary<string, float[]>();

	private void DotTimeFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Dot Time");
		DateTime now = WatchNow(x);
		SKColor ink = SkiaKit.Sk(x.C1, 1f), accent = SkiaKit.Sk(x.C2, 1f);
		const float pitch = 13f, cx = 200f, cy = 186f;
		const int cols = 11, rows = 15;
		string hh = now.ToString("HH", CultureInfo.InvariantCulture), mm = now.ToString("mm", CultureInfo.InvariantCulture);
		if (!_dotTime.TryGetValue(x.Fan.Serial, out float[]? lit))
		{
			_dotTime[x.Fan.Serial] = lit = new float[cols * rows];
			dt = 10.0;
		}
		// each dot of the big figures eases on or off, so a new minute melts in rather than jumping
		for (int row = 0; row < rows; row++)
		{
			for (int col = 0; col < cols; col++)
			{
				bool on = false;
				if (row != 7 && col != 5)
				{
					string pair = row < 7 ? hh : mm;
					int digit = col < 5 ? 0 : 1, gc = col < 5 ? col : col - 6, gr = row < 7 ? row : row - 8;
					on = DotOn(pair[digit], gc, gr);
				}
				ref float b = ref lit[row * cols + col];
				// a gentle wave: dots further down and right follow a moment later
				float rate = 5.5f - 0.12f * (row + col);
				b += ((on ? 1f : 0f) - b) * Math.Clamp((float)dt * rate, 0f, 1f);
			}
		}

		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(7, 7, 8)));
			// the dot field, on the same grid as the figures
			using var dot = new SKPaint { IsAntialias = true };
			for (int gy = -9; gy <= 9; gy++)
			{
				for (int gx = -8; gx <= 8; gx++)
				{
					float px = cx + gx * pitch, py = cy + gy * pitch;
					float d = MathF.Sqrt((px - 200f) * (px - 200f) + (py - 200f) * (py - 200f));
					if (d > 168f)
					{
						continue;
					}
					int col = gx + 5, row = gy + 7;
					float b = col >= 0 && col < cols && row >= 0 && row < rows ? lit[row * cols + col] : 0f;
					float e = b * b * (3f - 2f * b);
					float fade = Math.Clamp((168f - d) / 30f, 0f, 1f);
					dot.Color = ink.WithAlpha((byte)Math.Clamp((26f + 229f * e) * (e > 0.02f ? 1f : fade), 0f, 255f));
					c.DrawCircle(px, py, 1.3f + 3.9f * e, dot);
				}
			}
			// the ring: 60 dots filling with the reading, and the red seconds dot walking round
			float sec = now.Second + now.Millisecond / 1000f;
			for (int i = 0; i < 60; i++)
			{
				float a = (i / 60f * 360f - 90f) * MathF.PI / 180f;
				float px = 200f + MathF.Cos(a) * 184f, py = 200f + MathF.Sin(a) * 184f;
				float fill = Math.Clamp(f * 60f - i, 0f, 1f);
				dot.Color = ink.WithAlpha((byte)(40 + 200 * fill));
				c.DrawCircle(px, py, i % 5 == 0 ? 2.6f : 1.8f, dot);
			}
			float sa = (sec / 60f * 360f - 90f) * MathF.PI / 180f;
			dot.Color = accent;
			c.DrawCircle(200f + MathF.Cos(sa) * 184f, 200f + MathF.Sin(sa) * 184f, 4.2f, dot);
			// the reading's name above, its value below, in small dots
			DotText(c, x.Label, 200f, 66f, 2.6f, 1.05f, ink.WithAlpha(150));
			DotText(c, x.Value, 200f, 300f, 4.4f, 1.8f, ink, ink.WithAlpha(22));
			DotText(c, now.ToString("ddd d", CultureInfo.InvariantCulture), 200f, 344f, 2.6f, 1.05f, accent);
		});
	}

	// ---------- Rolling Time ----------

	private readonly Dictionary<string, (int Minute, double At, int Hour, double HourAt)> _rollTime = new Dictionary<string, (int, double, int, double)>();

	private void RollingTimeFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Rolling Time");
		DateTime now = WatchNow(x);
		SKColor ink = SkiaKit.Sk(x.C1, 1f), accent = SkiaKit.Sk(x.C2, 1f);
		if (!_rollTime.TryGetValue(x.Fan.Serial, out var st))
		{
			st = (now.Minute, -100.0, now.Hour, -100.0);
		}
		if (st.Minute != now.Minute)
		{
			st = (now.Minute, x.T, st.Hour, st.HourAt);
		}
		if (st.Hour != now.Hour)
		{
			st = (st.Minute, st.At, now.Hour, x.T);
		}
		_rollTime[x.Fan.Serial] = st;
		// the column rolls up one place over 0.9 s when the minute turns, easing to rest
		double u = Math.Clamp((x.T - st.At) / 0.9, 0.0, 1.0);
		float roll = 1f - (float)(1.0 - Math.Pow(1.0 - u, 3.0));
		double uh = Math.Clamp((x.T - st.HourAt) / 0.9, 0.0, 1.0);
		float hroll = 1f - (float)(1.0 - Math.Pow(1.0 - uh, 3.0));

		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(7, 7, 8)));
			const string font = "Bahnschrift SemiBold";
			const float size = 92f, pitch = 86f, mx = 218f, line = 200f;
			SKRect zero = AfPath("00", font, size).TightBounds;
			float capH = zero.Height;
			c.Save();
			using (var clip = new SKPath())
			{
				clip.AddCircle(200f, 200f, 196f);
				c.ClipPath(clip, antialias: true);
			}
			// the minutes column: the current one sits on the line, its neighbours fade above and below
			for (int k = -3; k <= 3; k++)
			{
				int m = ((now.Minute + k) % 60 + 60) % 60;
				float y = line + (k + roll) * pitch;
				float dist = Math.Abs(y - line) / pitch;
				byte a = (byte)Math.Clamp(255f * (1f - 0.62f * Math.Min(dist, 1f)) * (1f - 0.28f * Math.Max(0f, dist - 1f)), 0f, 255f);
				AfText(c, m.ToString("00", CultureInfo.InvariantCulture), mx, y, size, ink.WithAlpha(a), bottom: true, font: font);
			}
			c.Restore();
			// the hour: rolls once each hour the same way
			c.Save();
			c.ClipRect(new SKRect(20f, line - capH - 6f, 200f, line + 6f));
			int prevHour = (now.Hour + 23) % 24;
			AfText(c, now.ToString("HH", CultureInfo.InvariantCulture), 196f, line + hroll * pitch, size, ink, align: 1, bottom: true, font: font);
			if (hroll > 0.001f)
			{
				AfText(c, prevHour.ToString("00", CultureInfo.InvariantCulture), 196f, line + (hroll - 1f) * pitch, size, ink, align: 1, bottom: true, font: font);
			}
			c.Restore();
			// the colon, blinking softly with the seconds
			float sec = now.Second + now.Millisecond / 1000f;
			float blink = 0.55f + 0.45f * MathF.Cos(sec * MathF.PI * 2f);
			using (var dot = new SKPaint { IsAntialias = true, Color = ink.WithAlpha((byte)(255 * blink)) })
			{
				c.DrawCircle(207f, line - capH * 0.68f, 4.2f, dot);
				c.DrawCircle(207f, line - capH * 0.26f, 4.2f, dot);
			}
			// the red hairline grows across with the seconds
			float lx0 = 22f, lx1 = 378f, ly = line + 10f;
			using (var hair = new SKPaint { IsAntialias = true, StrokeWidth = 1.4f, Color = ink.WithAlpha(40) })
			{
				c.DrawLine(lx0, ly, lx1, ly, hair);
				hair.Color = accent;
				float lx = lx0 + (lx1 - lx0) * sec / 60f;
				c.DrawLine(lx0, ly, lx, ly, hair);
				using var head = new SKPaint { IsAntialias = true, Color = accent };
				c.DrawCircle(lx, ly, 2.6f, head);
			}
			// the date top left, the reading below the line on the left
			AfText(c, now.ToString("ddd d", CultureInfo.InvariantCulture).ToUpperInvariant(), 196f, line - capH - 26f, 12f, accent, align: 1, track: 1.5f, font: "Bahnschrift SemiBold");
			AfText(c, x.Label.ToUpperInvariant(), 196f, ly + 22f, 11f, ink.WithAlpha(140), align: 1, track: 1.4f, font: "Bahnschrift");
			AfText(c, x.Value, 196f, ly + 40f, 30f, ink, align: 1, font: "Bahnschrift SemiBold");
			// a thin bar under it for the reading
			using (var bar = new SKPaint { IsAntialias = true, StrokeWidth = 2f, StrokeCap = SKStrokeCap.Round, Color = ink.WithAlpha(40) })
			{
				c.DrawLine(96f, ly + 84f, 196f, ly + 84f, bar);
				bar.Color = ink;
				c.DrawLine(196f - 100f * f, ly + 84f, 196f, ly + 84f, bar);
			}
		});
	}

	// ---------- Analog Dial ----------

	private void AnalogDialFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Analog Dial");
		DateTime now = WatchNow(x);
		SKColor dial = SkiaKit.Sk(x.C1, 1f), accent = SkiaKit.Sk(x.C2, 1f);
		// ink is the dial's opposite: black on a light dial, white on a dark one
		bool light = dial.Red * 0.3f + dial.Green * 0.59f + dial.Blue * 0.11f > 128f;
		SKColor ink = light ? new SKColor(18, 18, 20) : new SKColor(240, 240, 238);
		SKColor soft = ink.WithAlpha(light ? (byte)70 : (byte)90);
		var warm = new SKColor((byte)Math.Min(255, accent.Red + 20), (byte)Math.Min(255, accent.Green + 110), (byte)Math.Min(255, accent.Blue + 10));

		FaceSkia(x.G, c =>
		{
			c.Clear(dial);
			// a faint inner disc, a shade off the dial
			using (var inner = new SKPaint { IsAntialias = true, Color = light ? new SKColor(0, 0, 0, 10) : new SKColor(255, 255, 255, 8) })
			{
				c.DrawCircle(200f, 200f, 150f, inner);
			}
			// minute and hour marks
			using (var tick = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round })
			{
				for (int i = 0; i < 60; i++)
				{
					float a = (i * 6f - 90f) * MathF.PI / 180f;
					bool hour = i % 5 == 0;
					float r1 = hour ? 168f : 176f, r2 = 184f;
					tick.StrokeWidth = hour ? 3f : 1.1f;
					tick.Color = hour ? ink : soft;
					c.DrawLine(200f + MathF.Cos(a) * r1, 200f + MathF.Sin(a) * r1, 200f + MathF.Cos(a) * r2, 200f + MathF.Sin(a) * r2, tick);
				}
			}
			// the reading as an arc along the rim, from twelve round to four
			const float start = -88f, sweep = 116f, ar = 158f;
			var oval = new SKRect(200f - ar, 200f - ar, 200f + ar, 200f + ar);
			using (var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 9f, StrokeCap = SKStrokeCap.Round, Color = ink.WithAlpha(light ? (byte)18 : (byte)30) })
			{
				c.DrawArc(oval, start, sweep, false, track);
				if (f > 0.004f)
				{
					// red to orange along its length, drawn in short pieces (a sweep gradient's seam would cross the arc)
					float end = sweep * f;
					int steps = Math.Max(2, (int)(end / 3f));
					for (int i = 0; i < steps; i++)
					{
						float a0 = start + end * i / steps, a1 = start + end * (i + 1) / steps;
						float k = (i + 0.5f) / steps * f;
						track.Color = new SKColor((byte)(accent.Red + (warm.Red - accent.Red) * k), (byte)(accent.Green + (warm.Green - accent.Green) * k), (byte)(accent.Blue + (warm.Blue - accent.Blue) * k));
						track.StrokeCap = i == 0 || i == steps - 1 ? SKStrokeCap.Round : SKStrokeCap.Butt;
						c.DrawArc(oval, a0, a1 - a0 + (i < steps - 1 ? 0.4f : 0f), false, track);
					}
				}
			}
			// the reading's name written round the arc, just inside it
			string name = x.Label.ToUpperInvariant();
			float ang = start + 4f;
			foreach (char ch in name)
			{
				if (ch == ' ')
				{
					ang += 3.2f;
					continue;
				}
				SKPath p = AfPath(ch.ToString(), "Bahnschrift SemiBold", 11f);
				SKRect b = p.TightBounds;
				float w = Math.Max(b.Width, 3f);
				float mid = ang + (w / 2f) / 140f * 180f / MathF.PI;
				c.Save();
				c.Translate(200f, 200f);
				c.RotateDegrees(mid + 90f);
				c.Translate(-b.MidX, -140f - b.Top);
				using (var tp = new SKPaint { IsAntialias = true, Color = soft.WithAlpha(light ? (byte)150 : (byte)170) })
				{
					c.DrawPath(p, tp);
				}
				c.Restore();
				ang += (w + 2.4f) / 140f * 180f / MathF.PI;
			}
			// small digital time on top, the reading on the left, the date at the foot
			AfText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), 200f, 108f, 16f, ink, align: 0, font: "Bahnschrift SemiBold");
			SKRect v = AfText(c, x.Value, 116f, 188f, 26f, ink, align: 0, font: "Bahnschrift SemiBold");
			AfText(c, "NOW", 116f, v.Bottom + 7f, 9f, soft.WithAlpha(light ? (byte)150 : (byte)170), align: 0, track: 1.4f, font: "Bahnschrift");
			SKRect d = AfText(c, now.Day.ToString(CultureInfo.InvariantCulture), 196f, 282f, 16f, ink, align: 1, font: "Bahnschrift SemiBold");
			AfText(c, now.ToString("ddd", CultureInfo.InvariantCulture), 202f, 282f, 16f, accent, font: "Bahnschrift");

			// the hands: hour and minute thin black, the second a red sweep with a short tail
			float secs = now.Second + now.Millisecond / 1000f;
			float mins = now.Minute + secs / 60f;
			float hours = now.Hour % 12 + mins / 60f;
			void Hand(float deg, float len, float tail, float width, SKColor color, bool shadow)
			{
				float a = (deg - 90f) * MathF.PI / 180f;
				float ex = 200f + MathF.Cos(a) * len, ey = 200f + MathF.Sin(a) * len;
				float tx = 200f - MathF.Cos(a) * tail, ty = 200f - MathF.Sin(a) * tail;
				using var hp = new SKPaint { IsAntialias = true, StrokeWidth = width, StrokeCap = SKStrokeCap.Round };
				if (shadow)
				{
					hp.Color = new SKColor(0, 0, 0, light ? (byte)36 : (byte)90);
					hp.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f);
					c.DrawLine(tx + 1.5f, ty + 2.5f, ex + 1.5f, ey + 2.5f, hp);
					hp.MaskFilter = null;
				}
				hp.Color = color;
				c.DrawLine(tx, ty, ex, ey, hp);
			}
			Hand(hours * 30f, 96f, 14f, 6f, ink, true);
			Hand(mins * 6f, 148f, 16f, 4f, ink, true);
			Hand(secs * 6f, 166f, 30f, 1.6f, accent, true);
			using (var cap = new SKPaint { IsAntialias = true, Color = ink })
			{
				c.DrawCircle(200f, 200f, 6.5f, cap);
				cap.Color = accent;
				c.DrawCircle(200f, 200f, 3.2f, cap);
			}
		});
	}
}
