using System;
using System.Collections.Generic;
using System.Drawing;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Mission Control on a fan: the screen layout's core, made for one reading. The reading sits in white inside a lit
/// photon ring, with an accretion disc crossing the ring's lower part that turns faster the higher the reading. Around
/// it, rings of light dashes flow outward. Each ring is coloured by the reading when it left the core, from the calm
/// colour through the hues between to the busy colour, so the last half minute reads as colour. The label sits above,
/// and a small mission mark and the time sit below.
/// </summary>
public sealed partial class Faces
{
	/// <summary>Per fan: the eased reading once a second, oldest first, and when it was last taken.</summary>
	private readonly Dictionary<string, (float[] Hist, double At)> _mcHist = new Dictionary<string, (float[], double)>();

	private SKSurface? _mcGlow;

	/// <summary>The hue between the calm and busy colours, turning the short way round the colour wheel, at full colour.</summary>
	private static SKColor McHue(Color calm, Color busy, float v, byte alpha)
	{
		static (float H, float S, float V) Hsv(Color c)
		{
			float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
			float h = d == 0f ? 0f : max == r ? 60f * ((g - b) / d % 6f) : max == g ? 60f * ((b - r) / d + 2f) : 60f * ((r - g) / d + 4f);
			return (h < 0f ? h + 360f : h, max == 0f ? 0f : d / max, max);
		}
		var a = Hsv(calm);
		var b = Hsv(busy);
		v = Math.Clamp(v, 0f, 1f);
		float dh = b.H - a.H;
		if (dh > 180f)
		{
			dh -= 360f;
		}
		else if (dh < -180f)
		{
			dh += 360f;
		}
		float h = (a.H + dh * v + 360f) % 360f, s = Math.Max(0.6f, a.S + (b.S - a.S) * v), val = Math.Max(0.85f, a.V + (b.V - a.V) * v);
		return SKColor.FromHsv(h, s * 100f, val * 100f, alpha);
	}

	/// <summary>
	/// Per globe radius: for each pixel of the globe's square, its latitude row in the map, its longitude from the
	/// centre (degrees), its shade (darker at the rim and softly under the reading) and its edge coverage.
	/// </summary>
	private sealed class GlobeTable
	{
		public int Size;
		public int[] Row = Array.Empty<int>();
		public float[] Lon = Array.Empty<float>(), Shade = Array.Empty<float>(), Cover = Array.Empty<float>();
		public byte[] Px = Array.Empty<byte>();
	}

	private GlobeTable? _globe;

	private const float GlobeR = 120f, GlobeLat0 = 22f;

	private static GlobeTable MakeGlobe(float rad)
	{
		int size = (int)Math.Ceiling(rad * 2f) + 4;
		var t = new GlobeTable { Size = size, Row = new int[size * size], Lon = new float[size * size], Shade = new float[size * size], Cover = new float[size * size], Px = new byte[size * size * 4] };
		float half = size / 2f;
		double s0 = Math.Sin(GlobeLat0 * Math.PI / 180.0), c0 = Math.Cos(GlobeLat0 * Math.PI / 180.0);
		for (int py = 0; py < size; py++)
		{
			for (int px = 0; px < size; px++)
			{
				int i = py * size + px;
				float dx = px + 0.5f - half, dy = py + 0.5f - half;
				float d = MathF.Sqrt(dx * dx + dy * dy);
				t.Cover[i] = Math.Clamp(rad - d + 0.5f, 0f, 1f);
				if (t.Cover[i] <= 0f)
				{
					continue;
				}
				double xr = Math.Min(1.0, dx / rad), yr = Math.Min(1.0, -dy / rad);
				double rho2 = Math.Min(1.0, xr * xr + yr * yr), cosc = Math.Sqrt(1.0 - rho2);
				double lat = Math.Asin(Math.Clamp(cosc * s0 + yr * c0, -1.0, 1.0)) * 180.0 / Math.PI;
				double lon = Math.Atan2(xr, cosc * c0 - yr * s0) * 180.0 / Math.PI;
				t.Row[i] = Math.Clamp((int)((90.0 - lat) / 180.0 * EarthMap.H), 0, EarthMap.H - 1) * EarthMap.W;
				t.Lon[i] = (float)lon;
				// darker toward the rim, and a soft shade (no edge) behind the label and the reading
				float limb = 0.45f + 0.55f * (float)Math.Pow(cosc, 0.6);
				float ex = dx / 92f, ey = (dy + 6f) / 66f;
				float u = Math.Clamp((MathF.Sqrt(ex * ex + ey * ey) - 0.45f) / 0.85f, 0f, 1f);
				t.Shade[i] = limb * (0.42f + 0.58f * u * u * (3f - 2f * u));
			}
		}
		return t;
	}

	/// <summary>Where a place on the globe lands, relative to the centre, and whether it faces the viewer.</summary>
	private static (float X, float Y, bool Front) GlobePoint(double lat, double lon, double lon0, float rad)
	{
		double r = Math.PI / 180.0, s0 = Math.Sin(GlobeLat0 * r), c0 = Math.Cos(GlobeLat0 * r);
		double cp = Math.Cos(lat * r), sp = Math.Sin(lat * r), dl = (lon - lon0) * r;
		bool front = s0 * sp + c0 * cp * Math.Cos(dl) > 0.0;
		return ((float)(rad * cp * Math.Sin(dl)), (float)(-rad * (c0 * sp - s0 * cp * Math.Cos(dl))), front);
	}

	/// <summary>
	/// Mission Control on a fan: the Earth turning (NASA's day and night maps, lit by the real day/night line), a
	/// spacecraft circling it on a tilted orbit whose trail is coloured by the reading over the last laps (Calm to
	/// Busy), a link to Dubai while the craft passes over it, and the reading in white over the globe.
	/// </summary>
	private void MissionFace(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Mission Control");
		double lapRate = 0.42 + 0.9 * f;
		s.Orbit += dt * lapRate;
		s.Beat += dt * 0.9;
		s.Travel += dt * 5.0;

		// the reading once a second, kept for 40 seconds
		if (!_mcHist.TryGetValue(x.Fan.Serial, out var h))
		{
			h = (new float[40], x.T);
			Array.Fill(h.Hist, f);
		}
		while (x.T - h.At >= 1.0)
		{
			Array.Copy(h.Hist, 1, h.Hist, 0, h.Hist.Length - 1);
			h.Hist[^1] = f;
			h.At += 1.0;
			if (x.T - h.At > 5.0)
			{
				h.At = x.T;
			}
		}
		if (x.T < h.At)
		{
			h.At = x.T;
		}
		_mcHist[x.Fan.Serial] = h;
		float[] hist = h.Hist;
		float Ago(double age)
		{
			double idx = hist.Length - 1 - age;
			if (idx <= 0)
			{
				return hist[0];
			}
			int i = Math.Min(hist.Length - 2, (int)Math.Floor(idx));
			float v = hist[i] + (hist[i + 1] - hist[i]) * (float)(idx - i);
			return age < 1.5 ? f + (v - f) * (float)(age / 1.5) : v;
		}

		const float cx = 200f, cy = 192f, rad = GlobeR, ro = rad * 1.32f;
		const float elev = 0.30f, roll = -0.33f;
		double lon0 = 55.0 - s.Travel;
		float breathe = 0.85f + 0.15f * (float)Math.Sin(s.Beat);
		_globe ??= MakeGlobe(rad);
		byte[] map = EarthMap.Now(DateTime.UtcNow);

		// a point of the orbit (angle u) on the face, and whether it is on the near side
		(float X, float Y, float Z) Orbit(double u)
		{
			float ox = ro * (float)Math.Cos(u), oy = -ro * (float)Math.Sin(u) * MathF.Sin(elev), oz = ro * (float)Math.Sin(u) * MathF.Cos(elev);
			float cr = MathF.Cos(roll), sr = MathF.Sin(roll);
			return (cx + ox * cr - oy * sr, cy + ox * sr + oy * cr, oz);
		}

		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(4, 6, 13)));
			_mcGlow ??= SKSurface.Create(new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _mcGlow.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);

			// star dust, each speck twinkling on its own slow beat
			using (var speck = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
			{
				for (int i = 0; i < 70; i++)
				{
					float tw = (float)(0.5 + 0.5 * Math.Sin(x.T * (0.3 + Hash01(i * 3) * 0.9) + i * 1.7));
					float big = (float)Hash01(i * 11 + 5);
					speck.Color = new SKColor(200, 214, 255, (byte)(20 + 90 * tw * tw * (0.4f + 0.6f * big)));
					c.DrawCircle((float)Hash01(i * 37 + 11) * 400f, (float)Hash01(i * 53 + 7) * 400f, 0.5f + 0.8f * big * big, speck);
				}
			}

			// the orbit: a faint full ring, and the trail behind the craft coloured by the reading when it passed
			void Trail(bool near)
			{
				const int steps = 160;
				double u0 = s.Orbit;
				using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus };
				using var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus, StrokeWidth = 9f };
				var prev = Orbit(u0);
				for (int k = 1; k <= steps; k++)
				{
					double back = k * Math.PI * 2.0 / steps;
					var p = Orbit(u0 - back);
					if ((p.Z >= 0f) == near)
					{
						float age = (float)(back / (Math.PI * 2.0));
						float fade = MathF.Pow(1f - age, 1.6f);
						float v = Ago(back / lapRate);
						SKColor col = McHue(x.C1, x.C2, v, 255);
						float depth = near ? 1f : 0.45f;
						line.StrokeWidth = 1.2f + 1.6f * fade;
						line.Color = col.WithAlpha((byte)((40 + 200 * fade) * depth));
						c.DrawLine(prev.X, prev.Y, p.X, p.Y, line);
						soft.Color = col.WithAlpha((byte)(150 * fade * depth));
						q.DrawLine(prev.X, prev.Y, p.X, p.Y, soft);
					}
					prev = p;
				}
			}
			Trail(near: false);

			// the atmosphere: a soft blue halo just outside the globe, in the calm colour
			SKColor air = McHue(x.C1, x.C1, 0f, 255);
			using (var halo = SKShader.CreateRadialGradient(new SKPoint(cx, cy), rad + 22f, new[] { air.WithAlpha(0), air.WithAlpha(0), air.WithAlpha((byte)(70 * breathe)), air.WithAlpha(0) }, new[] { 0f, rad / (rad + 22f) - 0.02f, rad / (rad + 22f) + 0.01f, 1f }, SKShaderTileMode.Clamp))
			using (var hp = new SKPaint { Shader = halo, BlendMode = SKBlendMode.Plus })
			{
				c.DrawCircle(cx, cy, rad + 22f, hp);
			}

			// the globe, from the map for now, turning
			GlobeTable gt = _globe!;
			int size = gt.Size;
			float shift = (float)(((-lon0 % 360.0) + 540.0) % 360.0 - 180.0);
			for (int i = 0; i < gt.Cover.Length; i++)
			{
				float cov = gt.Cover[i];
				int o = i * 4;
				if (cov <= 0f)
				{
					gt.Px[o] = gt.Px[o + 1] = gt.Px[o + 2] = gt.Px[o + 3] = 0;
					continue;
				}
				float lon = gt.Lon[i] - shift;
				int col = (int)((lon + 180f) / 360f * EarthMap.W);
				col %= EarthMap.W;
				if (col < 0)
				{
					col += EarthMap.W;
				}
				int m = (gt.Row[i] + col) * 4;
				float k = gt.Shade[i] * cov * 1.3f;
				gt.Px[o] = (byte)Math.Min(255f * cov, map[m] * k);
				gt.Px[o + 1] = (byte)Math.Min(255f * cov, map[m + 1] * k);
				gt.Px[o + 2] = (byte)Math.Min(255f * cov, map[m + 2] * k);
				gt.Px[o + 3] = (byte)(255f * cov);
			}
			unsafe
			{
				fixed (byte* p = gt.Px)
				{
					using var pix = new SKPixmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul), (IntPtr)p, size * 4);
					using SKImage img = SKImage.FromPixelCopy(pix);
					c.DrawImage(img, cx - size / 2f, cy - size / 2f);
				}
			}
			// a thin lit rim
			using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = air.WithAlpha((byte)(150 * breathe)), BlendMode = SKBlendMode.Plus })
			{
				c.DrawCircle(cx, cy, rad + 0.5f, rim);
				rim.StrokeWidth = 10f;
				rim.Color = air.WithAlpha((byte)(80 * breathe));
				q.DrawCircle(cx, cy, rad + 3f, rim);
			}

			// Dubai, and the link to the craft while it is over the near side
			var craft = Orbit(s.Orbit);
			var dxb = GlobePoint(25.2, 55.3, lon0, rad);
			if (dxb.Front)
			{
				float gx = cx + dxb.X, gy = cy + dxb.Y;
				SKColor green = new SKColor(96, 232, 168);
				using (var mark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f, Color = green.WithAlpha(220) })
				{
					c.DrawRect(gx - 3f, gy - 3f, 6f, 6f, mark);
				}
				if (craft.Z > 0f)
				{
					using var link = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = green.WithAlpha(190), PathEffect = SKPathEffect.CreateDash(new[] { 4f, 5f }, (float)(-x.T * 30.0 % 9.0)) };
					c.DrawLine(gx, gy, craft.X, craft.Y, link);
					SkiaKit.Glow(q, gx, gy, 14f, 14f, green.WithAlpha(170), SKBlendMode.Plus);
				}
			}

			Trail(near: true);

			// the craft: a white core in a halo of the reading's colour, hidden while it is behind the Earth
			float bx = craft.X - cx, by = craft.Y - cy;
			bool hidden = craft.Z < 0f && bx * bx + by * by < rad * rad;
			if (!hidden)
			{
				SKColor glow = McHue(x.C1, x.C2, f, 255);
				SkiaKit.Glow(q, craft.X, craft.Y, 30f, 30f, glow.WithAlpha(200), SKBlendMode.Plus);
				SkiaKit.Glow(c, craft.X, craft.Y, 11f, 11f, glow.WithAlpha(150), SKBlendMode.Plus);
				using var dot = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeWidth = 1.3f };
				c.DrawCircle(craft.X, craft.Y, 3f, dot);
				c.DrawLine(craft.X - 9f, craft.Y, craft.X - 4.5f, craft.Y, dot);
				c.DrawLine(craft.X + 4.5f, craft.Y, craft.X + 9f, craft.Y, dot);
				// a pulse of signal leaving it every two seconds
				float life = (float)(x.T * 0.5 % 1.0);
				using var pulse = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = glow.WithAlpha((byte)(170 * (1f - life))), BlendMode = SKBlendMode.Plus };
				c.DrawCircle(craft.X, craft.Y, 4f + 20f * life, pulse);
			}

			q.Restore();
			using (SKImage sharp = _mcGlow.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.8f, 1.8f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glowImg = _mcGlow.Snapshot())
			using (SKPixmap pixels = _faceSurface!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glowImg, pixels);
			}

			// the reading in white over the globe, with a soft glow in the reading's own colour
			GlowNumber(c, x.Value, "Bahnschrift SemiBold", FontStyle.Regular, new RectangleF(cx - 58f, cy - 26f, 116f, 58f), Imaging.Mix(McHueColor(x, f), Color.Black, 0.35));
		});
		Small(x.G, x.Label, Color.FromArgb(240, 255, 255, 255), cy - 46f, 13f, bold: true, 1f);
		Small(x.G, x.Time, Color.FromArgb(210, 220, 232, 248), 352f, 14f, bold: false);
	}

	private static Color McHueColor(FaceContext x, float f)
	{
		SKColor k = McHue(x.C1, x.C2, f, 255);
		return Color.FromArgb(k.Red, k.Green, k.Blue);
	}
}
