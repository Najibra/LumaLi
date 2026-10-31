using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace LcdStudio.Core;

public static class Lighting
{
	public static readonly string[] Effects = new string[22]
	{
		"Static", "Breathing", "Breathing gradient", "Rainbow spin", "Rainbow wave", "Colour cycle", "Comets", "Propeller", "Relay", "Fire",
		"Heartbeat", "Tide", "Lava lamp", "Thunderstorm", "Pendulum", "Ripple", "Tunnel spiral", "Fireflies", "Candy", "Aurora",
		"Comet trail", "Off"
	};

	public static readonly string[] StripEffects = Effects.Where(delegate(string e)
	{
		bool flag = ((e == "Propeller" || e == "Relay") ? true : false);
		return !flag;
	}).Where((string e) => e != "Off").Prepend("Rainbow").Concat(StripScenes.Names).Append("Off").Distinct()
		.ToArray();

	public const int MaxLoopBytes = 8192;

	public static EffectSettings DefaultsFor(string effect)
	{
		return effect switch
		{
			"Fire" => new EffectSettings
			{
				Color1 = "#ffb21e",
				Color2 = "#e8230a"
			}, 
			"Lava lamp" => new EffectSettings
			{
				Color1 = "#ff2a6d",
				Color2 = "#ffb000",
				Speed = 30
			}, 
			"Thunderstorm" => new EffectSettings
			{
				Color1 = "#bfe6ff",
				Color2 = "#2a3cff"
			}, 
			"Tide" => new EffectSettings
			{
				Color1 = "#00c2ff",
				Color2 = "#002b80"
			}, 
			"Heartbeat" => new EffectSettings
			{
				Color1 = "#ff1744",
				Color2 = "#400010"
			}, 
			"Candy" => new EffectSettings
			{
				Color1 = "#ffffff",
				Color2 = "#ff2050"
			}, 
			"Fireflies" => new EffectSettings
			{
				Color1 = "#d4ff3a",
				Color2 = "#00ff9c",
				Speed = 35
			}, 
			"Aurora" => new EffectSettings
			{
				Color1 = "#3affc8",
				Color2 = "#7a3cff",
				Speed = 35
			},
			"Static" => new EffectSettings
			{
				Color1 = "#3fa9ff",
				Color2 = "#3fa9ff"
			}, 
			_ => new EffectSettings(), 
		};
	}

	private static Color Lerp(Color a, Color b, double t)
	{
		return Imaging.Mix(a, b, t);
	}

	private static double Wrap(double x)
	{
		return (x % 1.0 + 1.0) % 1.0;
	}

	private static double Hash(int i)
	{
		int num = i * -1640531535;
		return (double)(((uint)num ^ ((uint)num >> 15)) & 0xFFFFu) / 65535.0;
	}

	public static Color Pixel(string effect, int ring, int rings, double pos, double phase, Color c1, Color c2)
	{
		double num = ((double)ring + pos) / (double)Math.Max(1, rings);
		switch (effect)
		{
		case "Off":
			return Color.Black;
		case "Static":
			return c1;
		case "Breathing":
			return Scale(c1, 0.08 + 0.92 * (0.5 - 0.5 * Math.Cos(phase * Math.PI * 2.0)));
		case "Breathing gradient":
			return Scale(Lerp(c1, c2, 0.5 + 0.5 * Math.Sin((pos + phase) * Math.PI * 2.0)), 0.15 + 0.85 * (0.5 - 0.5 * Math.Cos(phase * Math.PI * 2.0)));
		case "Rainbow spin":
		case "Rainbow":
			return Imaging.Hue(pos - phase);
		case "Rainbow wave":
			return Imaging.Hue(num - phase);
		case "Colour cycle":
			return Imaging.Hue(phase);
		case "Comets":
		{
			double num2 = Wrap(phase - pos);
			return Scale(Lerp(c1, c2, num2 * 3.0), Math.Exp((0.0 - num2) * 9.0));
		}
		case "Propeller":
			if (!(Wrap(pos * 3.0 - phase * 3.0) < 0.22))
			{
				return Scale(c2, 0.08);
			}
			return c1;
		case "Relay":
		{
			int num9 = (int)(phase * (double)rings) % Math.Max(1, rings);
			double num10 = Wrap(phase * (double)rings);
			if (ring != num9)
			{
				return Color.Black;
			}
			return Scale(Lerp(c1, c2, pos), Math.Sin(num10 * Math.PI));
		}
		case "Fire":
		{
			double num7 = 0.55 + 0.45 * Math.Sin(phase * Math.PI * 2.0 * 3.0 + Hash((int)(pos * 26.0) + ring * 31) * 6.28);
			double num8 = 1.0 - Math.Abs(pos - 0.5) * 2.0;
			return Scale(Lerp(c2, c1, num8 * num7), 0.25 + 0.75 * num7);
		}
		case "Heartbeat":
		{
			double num6 = phase * 2.0 % 1.0;
			double val = Math.Exp(0.0 - Math.Pow((num6 - 0.1) * 18.0, 2.0)) + 0.7 * Math.Exp(0.0 - Math.Pow((num6 - 0.32) * 18.0, 2.0));
			return Lerp(Scale(c2, 0.3), c1, Math.Min(1.0, val));
		}
		case "Tide":
		{
			double t4 = 0.5 + 0.5 * Math.Sin((num - phase) * Math.PI * 2.0);
			return Lerp(c2, c1, t4);
		}
		case "Lava lamp":
		{
			double t3 = 0.5 + 0.5 * Math.Sin(pos * Math.PI * 4.0 + Math.Sin(phase * Math.PI * 2.0) * 2.5 + (double)ring);
			return Lerp(c1, c2, t3);
		}
		case "Thunderstorm":
		{
			double t2 = Math.Pow(Math.Max(0.0, Math.Sin(phase * Math.PI * 6.0 + (double)ring * 1.7)), 30.0);
			return Lerp(Scale(c2, 0.25), c1, t2);
		}
		case "Pendulum":
		{
			double num5 = 0.5 + 0.45 * Math.Sin(phase * Math.PI * 2.0);
			return Scale(Lerp(c1, c2, num5), Math.Exp(0.0 - Math.Pow((num - num5) * 8.0, 2.0)));
		}
		case "Ripple":
		{
			double t = 0.5 + 0.5 * Math.Sin((Math.Abs(pos - 0.5) * 4.0 - phase * 2.0) * Math.PI * 2.0);
			return Lerp(Scale(c2, 0.1), c1, t);
		}
		case "Tunnel spiral":
			return Scale(Imaging.Hue(pos * 2.0 - phase), 0.1 + 0.9 * (0.5 + 0.5 * Math.Sin((pos * 2.0 - phase) * Math.PI * 4.0)));
		case "Fireflies":
		{
			int num3 = (int)(pos * 26.0) + ring * 26;
			double num4 = Wrap(phase * 2.0 + Hash(num3));
			double f = ((Hash(num3 * 7) > 0.7) ? Math.Pow(Math.Sin(num4 * Math.PI), 6.0) : 0.0);
			return Scale(Lerp(c1, c2, Hash(num3 * 3)), f);
		}
		case "Candy":
			if (!(Wrap(pos * 6.0 - phase) < 0.5))
			{
				return c2;
			}
			return c1;
		case "Aurora":
			return AuroraRing(ring, pos, phase, c1, c2);
		case "Comet trail":
			return CometRing(ring, pos, phase, c1, c2);
		default:
			return c1;
		}
	}

	/// <summary>How far apart two places around a ring are, either way round, as a share of the ring (0 to 0.5).</summary>
	private static double Apart(double a, double b)
	{
		double d = Wrap(a - b);
		return Math.Min(d, 1.0 - d);
	}

	private static Color Light(double r, double g, double b)
	{
		return Color.FromArgb((int)Math.Clamp(r, 0.0, 255.0), (int)Math.Clamp(g, 0.0, 255.0), (int)Math.Clamp(b, 0.0, 255.0));
	}

	/// <summary>The colour pushed further from grey, as LEDs wash colours out.</summary>
	private static Color Vivid(Color c, double by)
	{
		double grey = (c.R + c.G + c.B) / 3.0;
		return Light(grey + (c.R - grey) * by, grey + (c.G - grey) * by, grey + (c.B - grey) * by);
	}

	/// <summary>
	/// The fans' Aurora: three wide, soft curtains of light on each ring, two of the first colour and one of the second,
	/// swaying a little back and forth and gently swelling, each drifting only slightly in tint, over a faint glow of the
	/// second colour. The fans hold only a few dozen frames, so the loop is kept short and each frame changes very little,
	/// many small steps rather than a few large ones. Where curtains overlap their colours are blended, not added, so the
	/// colour stays deep instead of washing toward white. Every change is once a loop, so it flows on without a seam.
	/// </summary>
	private static Color AuroraRing(int ring, double pos, double phase, Color c1, Color c2)
	{
		double[] tints = { 0.08, 0.92, 0.3 };
		double weight = 0.0, light = 0.0, r = 0.0, g = 0.0, b = 0.0;
		for (int i = 0; i < 3; i++)
		{
			double centre = Wrap(i / 3.0 + ring * 0.29 + 0.05 * Math.Sin(Math.PI * 2.0 * (phase + i / 3.0 + ring * 0.13)));
			double d = Apart(pos, centre) / 0.17;
			double swell = 0.75 + 0.25 * (0.5 + 0.5 * Math.Sin(Math.PI * 2.0 * (phase + i * 0.33 + ring * 0.2)));
			Color tint = Vivid(Lerp(c1, c2, Math.Clamp(tints[i] + 0.12 * Math.Sin(Math.PI * 2.0 * (phase + i / 3.0)), 0.0, 1.0)), 1.45);
			double a = Math.Exp(0.0 - d * d) * swell;
			r += tint.R * a;
			g += tint.G * a;
			b += tint.B * a;
			weight += a;
			light = Math.Max(light, a) + Math.Min(light, a) * 0.35;
		}
		// the hue is the curtains' blend and the level their strongest with a little of the rest; between them a faint
		// glow of the second colour, so the ring is never quite dark
		Color glow = Vivid(c2, 1.45);
		double level = Math.Min(1.0, light), floor = 0.07 * (1.0 - level);
		weight = Math.Max(weight, 1e-6);
		return Light(r / weight * level + glow.R * floor, g / weight * level + glow.G * floor, b / weight * level + glow.B * floor);
	}

	/// <summary>
	/// The fans' Comet trail: one comet round each ring, a big white-hot head in a glow of the first colour, trailing a
	/// full tail that turns into the second colour as it fades; each ring's comet a quarter turn on from the last, so
	/// they chase across the fans. The head is wide and soft, lighting the LEDs either side of it in proportion, so it
	/// glides between them rather than hopping.
	/// </summary>
	private static Color CometRing(int ring, double pos, double phase, Color c1, Color c2)
	{
		Color bold = Vivid(c1, 1.25), end = Vivid(c2, 1.25), hot = Lerp(bold, Color.White, 0.65);
		double head = Wrap(phase + ring * 0.25);
		double behind = Wrap(head - pos);
		double r = 0.0, g = 0.0, b = 0.0;
		const double length = 0.55;
		if (behind < length)
		{
			double u = behind / length, tail = Math.Pow(1.0 - u, 1.6);
			Color tc = Lerp(bold, end, Math.Min(1.0, u * 1.3));
			r += tc.R * tail;
			g += tc.G * tail;
			b += tc.B * tail;
		}
		double h = Apart(pos, head) / 0.05, halo = Apart(pos, head) / 0.11;
		double core = Math.Exp(0.0 - h * h), wide = Math.Exp(0.0 - halo * halo) * 0.55;
		r += hot.R * core + bold.R * wide;
		g += hot.G * core + bold.G * wide;
		b += hot.B * core + bold.B * wide;
		return Light(r, g, b);
	}

	private static Color Scale(Color c, double f)
	{
		f = Math.Clamp(f, 0.0, 1.0);
		return Color.FromArgb((int)((double)(int)c.R * f), (int)((double)(int)c.G * f), (int)((double)(int)c.B * f));
	}

	public static (byte R, byte G, byte B)[] Frame(string effect, EffectSettings s, int rings, int perRing, double phase)
	{
		Color c = Imaging.Hex(s.Color1, Color.DeepSkyBlue);
		Color c2 = Imaging.Hex(s.Color2, Color.HotPink);
		double num = (double)s.Brightness / 100.0;
		(byte, byte, byte)[] array = new(byte, byte, byte)[rings * perRing];
		for (int i = 0; i < rings; i++)
		{
			for (int j = 0; j < perRing; j++)
			{
				Color color = Pixel(effect, i, rings, (double)j / (double)perRing, phase, c, c2);
				array[i * perRing + j] = ((byte)((double)(int)color.R * num), (byte)((double)(int)color.G * num), (byte)((double)(int)color.B * num));
			}
		}
		return array;
	}

	/// <summary>
	/// The light strip's colours taken from a rendered screen frame, one per LED from left to right: each LED stands
	/// for a column of the screen, its hue weighted toward the column's brightest light, its level between the
	/// column's strong light and its average with the darks pressed down, so black stays off and the beam, glows and
	/// accents carry across. Coloured light counts for more than white, and the strong light is taken a little below
	/// the very brightest, so thin white type does not wash a stretch of the strip grey. Lightly smoothed between
	/// neighbours and more saturated, as LEDs wash out.
	/// </summary>
	public static unsafe (byte R, byte G, byte B)[] FromScreen(Bitmap frame, int count)
	{
		var raw = new (float R, float G, float B)[count];
		var levels = new double[count];
		int w = frame.Width, h = frame.Height;
		BitmapData data = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			for (int i = 0; i < count; i++)
			{
				int x0 = i * w / count, x1 = Math.Max(x0 + 1, (i + 1) * w / count);
				double r = 0, g = 0, b = 0, weight = 0, sum = 0;
				var lums = new List<double>(512);
				for (int y = 4; y < h; y += 12)
				{
					byte* row = (byte*)data.Scan0 + y * data.Stride;
					for (int x = x0; x < x1; x += 4)
					{
						byte* p = row + x * 4;
						double lum = 0.2126 * p[2] + 0.7152 * p[1] + 0.0722 * p[0];
						double chroma = Math.Max(p[2], Math.Max(p[1], p[0])) - Math.Min(p[2], Math.Min(p[1], p[0]));
						double k = (lum * lum + 1.0) * (0.15 + chroma / 255.0);
						r += p[2] * k;
						g += p[1] * k;
						b += p[0] * k;
						weight += k;
						sum += lum;
						lums.Add(lum);
					}
				}
				lums.Sort();
				double strong = lums.Count > 0 ? lums[Math.Min(lums.Count - 1, (int)(lums.Count * 0.96))] : 0.0;
				levels[i] = 0.7 * strong + 0.3 * sum / Math.Max(1, lums.Count);
				r /= weight;
				g /= weight;
				b /= weight;
				double lumAvg = 0.2126 * r + 0.7152 * g + 0.0722 * b, grey = (r + g + b) / 3.0;
				// a little more colour, then scaled to the column's level
				r = grey + (r - grey) * 1.6;
				g = grey + (g - grey) * 1.6;
				b = grey + (b - grey) * 1.6;
				raw[i] = ((float)(r / Math.Max(1.0, lumAvg)), (float)(g / Math.Max(1.0, lumAvg)), (float)(b / Math.Max(1.0, lumAvg)));
			}
		}
		finally
		{
			frame.UnlockBits(data);
		}
		// lift the whole strip so its brightest LED runs near full, within reason, then press the darks down
		double gain = Math.Min(2.5, 235.0 / Math.Max(1.0, levels.Max()));
		for (int i = 0; i < count; i++)
		{
			double level = levels[i] < 12.0 ? 0.0 : 255.0 * Math.Pow(Math.Min(1.0, levels[i] * gain / 255.0), 1.4);
			raw[i] = ((float)(raw[i].R * level), (float)(raw[i].G * level), (float)(raw[i].B * level));
		}
		var leds = new (byte R, byte G, byte B)[count];
		for (int i = 0; i < count; i++)
		{
			var l = raw[Math.Max(0, i - 1)];
			var c = raw[i];
			var rr = raw[Math.Min(count - 1, i + 1)];
			byte B8(float v) => (byte)Math.Clamp((int)v, 0, 255);
			leds[i] = (B8((l.R + 2f * c.R + rr.R) / 4f), B8((l.G + 2f * c.G + rr.G) / 4f), B8((l.B + 2f * c.B + rr.B) / 4f));
		}
		return leds;
	}

	/// <summary>How many steps the fan lights take from calm to hot when they follow a reading (each step is one upload).</summary>
	public const int ReactSteps = 4;

	/// <summary>Where a reading sits between the lighting's calm and hot levels, 0 to 1.</summary>
	public static double ReactShare(LightingSettings l, float v)
	{
		return Math.Clamp((v - l.ReactLow) / Math.Max(0.001, l.ReactHigh - l.ReactLow), 0.0, 1.0);
	}

	/// <summary>
	/// The effect's settings warmed by so many steps toward the hot colours: each colour turns round the colour wheel
	/// the short way to its hot one (cyan through green and yellow to amber, violet through magenta to red), so it stays
	/// vivid all the way instead of passing through grey, and the pace quickens. Brightness is left alone. Effects that
	/// make their own colours (the rainbows) only quicken.
	/// </summary>
	public static EffectSettings Warmed(EffectSettings s, LightingSettings l, int step)
	{
		double k = Math.Clamp(step / (double)ReactSteps, 0.0, 1.0);
		var w = new EffectSettings { Speed = s.Speed, Brightness = s.Brightness, Color1 = s.Color1, Color2 = s.Color2 };
		if (k <= 0.0)
		{
			return w;
		}
		w.Color1 = Imaging.Hex(HueMix(Imaging.Hex(s.Color1, Color.DeepSkyBlue), Imaging.Hex(l.ReactHot1, Color.Orange), k));
		w.Color2 = Imaging.Hex(HueMix(Imaging.Hex(s.Color2, Color.HotPink), Imaging.Hex(l.ReactHot2, Color.Red), k));
		w.Speed = (int)Math.Round(s.Speed + (Math.Min(100, s.Speed + 40) - s.Speed) * k);
		return w;
	}

	/// <summary>Between two colours by hue (the short way round), saturation and value, rather than by mixing red, green and blue.</summary>
	public static Color HueMix(Color a, Color b, double k)
	{
		if (k <= 0.0)
		{
			return a;
		}
		if (k >= 1.0)
		{
			return b;
		}
		var (ha, sa, va) = Hsv(a);
		var (hb, sb, vb) = Hsv(b);
		// a near-grey colour has no real hue, so it takes the other's
		if (sa < 0.08)
		{
			ha = hb;
		}
		if (sb < 0.08)
		{
			hb = ha;
		}
		double d = hb - ha;
		if (d > 0.5)
		{
			d -= 1.0;
		}
		else if (d < -0.5)
		{
			d += 1.0;
		}
		return Imaging.Hue(ha + d * k, sa + (sb - sa) * k, va + (vb - va) * k);
	}

	private static (double H, double S, double V) Hsv(Color c)
	{
		double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
		double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		double h = 0.0;
		if (d > 1e-9)
		{
			h = max == r ? (g - b) / d % 6.0 : max == g ? (b - r) / d + 2.0 : (r - g) / d + 4.0;
			h = (h / 6.0 % 1.0 + 1.0) % 1.0;
		}
		return (h, max <= 0.0 ? 0.0 : d / max, max);
	}

	public static bool IsAnimated(string effect)
	{
		bool flag = ((effect == "Static" || effect == "Off") ? true : false);
		return !flag;
	}

	public static double LoopSeconds(int speed)
	{
		return 8.0 - (double)Math.Clamp(speed, 0, 100) / 100.0 * 6.8;
	}

	/// <summary>
	/// How many frames a fan loop gets, at most the most that fit. Effects with a sharp head or blade that goes once round
	/// the ring per loop get a count that moves it the same whole number of LEDs every frame (or one LED every so many
	/// frames): with any other count it would advance one LED, then two, then one, and turn in a visible limp.
	/// </summary>
	private static int Steps(string effect, int perRing, int most)
	{
		// (Comet trail's head is soft, lighting the LEDs either side of it in proportion, so it glides on more frames instead)
		bool sweeps = effect == "Comets" || effect == "Propeller";
		if (!sweeps || perRing <= 0)
		{
			return most;
		}
		for (int f = most; f >= 2; f--)
		{
			if (f % perRing == 0 || perRing % f == 0)
			{
				return f;
			}
		}
		return most;
	}

	/// <summary>
	/// The order "2,1,3" as zero-based controller fan indices, one per position, or null if it isn't a full ordering of
	/// the group's fans (then the controller's own order is used).
	/// </summary>
	public static int[]? ParseOrder(string? text, int fans)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] parts = text.Split(new[] { ',', ' ', '-', '>' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != fans)
		{
			return null;
		}
		int[] order = new int[fans];
		for (int i = 0; i < fans; i++)
		{
			if (!int.TryParse(parts[i], out int n) || n < 1 || n > fans)
			{
				return null;
			}
			order[i] = n - 1;
		}
		return order.Distinct().Count() == fans ? order : null;
	}

	/// <summary>
	/// Moves each fan's ring so the effect's k-th fan lights the fan that sits k-th: the controller's fan order[k]
	/// shows ring k.
	/// </summary>
	public static List<(byte R, byte G, byte B)[]> Reorder(List<(byte R, byte G, byte B)[]> frames, int perRing, int[]? order)
	{
		if (order == null || order.Select((int v, int i) => v == i).All((bool same) => same))
		{
			return frames;
		}
		var result = new List<(byte, byte, byte)[]>(frames.Count);
		foreach ((byte, byte, byte)[] f in frames)
		{
			var o = new (byte, byte, byte)[f.Length];
			for (int k = 0; k < order.Length; k++)
			{
				int src = k * perRing, dst = order[k] * perRing;
				if (src + perRing <= f.Length && dst + perRing <= o.Length)
				{
					Array.Copy(f, src, o, dst, perRing);
				}
			}
			result.Add(o);
		}
		return result;
	}

	/// <summary>Colours that tell the fans of a group apart: controller fan 1 red, 2 green, 3 blue, 4 yellow.</summary>
	public static readonly (string Name, Color Color)[] IdentifyColors =
	{
		("red", Color.FromArgb(255, 0, 0)), ("green", Color.FromArgb(0, 255, 0)), ("blue", Color.FromArgb(0, 60, 255)), ("yellow", Color.FromArgb(255, 200, 0))
	};

	public static List<(byte R, byte G, byte B)[]> IdentifyFrame(int fans, int perRing)
	{
		var f = new (byte, byte, byte)[fans * perRing];
		for (int i = 0; i < fans; i++)
		{
			Color c = IdentifyColors[i % IdentifyColors.Length].Color;
			for (int j = 0; j < perRing; j++)
			{
				f[i * perRing + j] = (c.R, c.G, c.B);
			}
		}
		return new List<(byte, byte, byte)[]> { f };
	}

	public static (List<(byte R, byte G, byte B)[]> Frames, int Ticks) FanLoop(string effect, EffectSettings s, int fans, int perRing = 26)
	{
		int num = fans * perRing;
		// as many frames as the group's memory holds, up to 52: the controller was seen to take 52 (two fans' worth), and
		// more has not been tried
		int num2 = ((!IsAnimated(effect)) ? 1 : Steps(effect, perRing, Math.Clamp(8192 / (num * 3), 2, 52)));
		List<(byte, byte, byte)[]> list = new List<(byte, byte, byte)[]>(num2);
		for (int i = 0; i < num2; i++)
		{
			list.Add(Frame(effect, s, fans, perRing, (double)i / (double)num2));
		}
		// Aurora changes so little per loop that it can loop faster, each frame shown for less time: the same pace in
		// smaller, quicker steps
		double stretch = effect == "Aurora" ? 0.75 : 1.6;
		int val = ((num2 == 1) ? 8000 : ((int)(LoopSeconds(s.Speed) * 1000.0 * stretch / (double)num2)));
		return (Frames: list, Ticks: Math.Max(16, val));
	}
}
