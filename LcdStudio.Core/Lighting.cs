using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace LcdStudio.Core;

public static class Lighting
{
	public static readonly string[] Effects = new string[20]
	{
		"Static", "Breathing", "Breathing gradient", "Rainbow spin", "Rainbow wave", "Colour cycle", "Comets", "Propeller", "Relay", "Fire",
		"Heartbeat", "Tide", "Lava lamp", "Thunderstorm", "Pendulum", "Ripple", "Tunnel spiral", "Fireflies", "Candy", "Off"
	};

	public static readonly string[] StripEffects = Effects.Where(delegate(string e)
	{
		bool flag = ((e == "Propeller" || e == "Relay") ? true : false);
		return !flag;
	}).Prepend("Rainbow").Distinct()
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
		default:
			return c1;
		}
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

	public static bool IsAnimated(string effect)
	{
		bool flag = ((effect == "Static" || effect == "Off") ? true : false);
		return !flag;
	}

	public static double LoopSeconds(int speed)
	{
		return 8.0 - (double)Math.Clamp(speed, 0, 100) / 100.0 * 6.8;
	}

	public static (List<(byte R, byte G, byte B)[]> Frames, int Ticks) FanLoop(string effect, EffectSettings s, int fans, int perRing = 26)
	{
		int num = fans * perRing;
		int num2 = ((!IsAnimated(effect)) ? 1 : Math.Clamp(8192 / (num * 3), 2, 48));
		List<(byte, byte, byte)[]> list = new List<(byte, byte, byte)[]>(num2);
		for (int i = 0; i < num2; i++)
		{
			list.Add(Frame(effect, s, fans, perRing, (double)i / (double)num2));
		}
		int val = ((num2 == 1) ? 8000 : ((int)(LoopSeconds(s.Speed) * 1000.0 * 1.6 / (double)num2)));
		return (Frames: list, Ticks: Math.Max(16, val));
	}
}
