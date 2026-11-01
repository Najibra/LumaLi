using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Mission Control: the front wall of a flight-operations room.
/// - Centre: a world map from NASA's Blue Marble (day) and Black Marble (night city lights). The real day/night line
///   is worked out from the clock, and the sun's glow sits over the point where it is overhead.
/// - On the map, a spacecraft flies an inclined orbit with its ground track: the orbit behind it lit, the one ahead
///   dashed. Its footprint pulses, and a link runs to any ground station inside the footprint.
/// - Left: the mission clocks (local time, UTC, mission elapsed time = uptime), the communications rates and the
///   system status.
/// - Right: the subsystem console, five readings, each with a status light, a white value and a trace of its last
///   minute that glides as new samples come in.
/// Everything is drawn by Skia. Soft light goes on a quarter-size layer that is blurred and added over the frame.
/// </summary>
public sealed partial class ScreenRenderer
{
	private sealed record McRow(string Label, string Key, string Unit, string Format, float Lo, float Hi, float Warn);

	private static readonly McRow[] McRows =
	{
		new("PROCESSOR LOAD", "cpu.load", "%", "0", 0f, 100f, 95f),
		new("GRAPHICS LOAD", "gpu.load", "%", "0", 0f, 100f, 99f),
		new("PROCESSOR TEMP", "cpu.temp", "°C", "0", 25f, 100f, 90f),
		new("GRAPHICS TEMP", "gpu.temp", "°C", "0", 25f, 95f, 83f),
		new("MEMORY", "ram.load", "%", "0", 0f, 100f, 90f)
	};

	private sealed record McStation(string Code, float Lat, float Lon);

	/// <summary>Ground stations: Dubai first, then the three deep-space sites and two polar and ocean stations.</summary>
	private static readonly McStation[] McStations =
	{
		new("DXB", 25.2f, 55.3f), new("MAD", 40.4f, -4.2f), new("GDS", 35.4f, -116.9f), new("CBR", -35.4f, 149.0f),
		new("SVB", 78.2f, 15.4f), new("HAW", 21.3f, -157.9f), new("SCL", -33.4f, -70.7f)
	};

	private const float McMapX = 480f, McMapW = 960f, McMapH = 480f;

	/// <summary>Seconds per orbit on the screen (a real one takes about 92 minutes), and the inclination.</summary>
	private const double McPeriod = 75.0, McIncl = 51.6;

	private static readonly SKColor McCyan = new SKColor(90, 214, 255), McGreen = new SKColor(96, 232, 168), McAmber = new SKColor(255, 181, 71), McDim = new SKColor(150, 166, 192);

	private SKSurface? _skMission;

	private SKImage? _mcMap;

	private DateTime _mcMapAt = DateTime.MinValue;

	private readonly Dictionary<string, SKPath> _mcPaths = new Dictionary<string, SKPath>();

	private readonly Dictionary<string, (double X, double V)> _mcEase = new Dictionary<string, (double, double)>();

	private double _mcAt = double.NaN;

	private bool DrawMission(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "mission" || w.Kind != WidgetKind.Panel)
		{
			return DrawInterstellar(g, w, r, t);
		}
		Mission(g, t);
		return true;
	}

	// ---------- readings ----------

	private bool McOnline(string key) => _sensors.Get(key).HasValue && (Val(key) > 0f || key.EndsWith("load") || key.StartsWith("net."));

	/// <summary>A reading eased on a spring, so a new sample glides in instead of jumping.</summary>
	private float McEased(string id, float target, double dt, double stiffness = 3.2)
	{
		if (!_mcEase.TryGetValue(id, out var s))
		{
			s = (target, 0.0);
		}
		double x = s.X, v = s.V;
		BeamSpring(ref x, ref v, target, stiffness, dt);
		_mcEase[id] = (x, v);
		return (float)x;
	}

	private static float McSmooth(float a, float b, float x)
	{
		float u = Math.Clamp((x - a) / (b - a), 0f, 1f);
		return u * u * (3f - 2f * u);
	}

	// ---------- text ----------

	/// <summary>The outline of a line of text at the given pixel size, letters spread by track pixels, made once.</summary>
	private SKPath McPath(string text, string font, float size, float track)
	{
		string key = $"{font}|{size}|{track}|{text}";
		if (_mcPaths.TryGetValue(key, out SKPath? cached))
		{
			return cached;
		}
		if (_mcPaths.Count > 1500)
		{
			foreach (SKPath old in _mcPaths.Values)
			{
				old.Dispose();
			}
			_mcPaths.Clear();
		}
		// a name ending in "|B" asks for the bold style
		int style = font.EndsWith("|B", StringComparison.Ordinal) ? (int)FontStyle.Bold : 0;
		string name = style != 0 ? font[..^2] : font;
		using FontFamily family = BundledFonts.Family(name) ?? Family(name);
		using var gp = new GraphicsPath(FillMode.Winding);
		if (track == 0f)
		{
			gp.AddString(text, family, style, size, PointF.Empty, StringFormat.GenericTypographic);
		}
		else
		{
			float x = 0f;
			foreach (char ch in text)
			{
				if (ch == ' ')
				{
					x += size * 0.3f + track;
					continue;
				}
				using var one = new GraphicsPath(FillMode.Winding);
				one.AddString(ch.ToString(), family, style, size, new PointF(x, 0f), StringFormat.GenericTypographic);
				RectangleF b = one.GetBounds();
				gp.AddPath(one, false);
				x = (b.Width > 0f ? b.Right : x + size * 0.3f) + track;
			}
		}
		return _mcPaths[key] = SkiaKit.FromGdi(gp);
	}

	/// <summary>
	/// Draws text placed by its ink: x is its left edge, centre or right edge (align -1, 0, 1), and y is the top of the
	/// letters, or their foot when bottom is set. Returns the ink's bounds where it landed.
	/// </summary>
	private SKRect McText(SKCanvas c, string text, float x, float y, float size, SKColor color, int align = -1, float track = 0f, string font = "Bahnschrift SemiBold", bool bottom = false, float stroke = 0f)
	{
		SKPath p = McPath(text, font, size, track);
		SKRect b = p.TightBounds;
		if (b.IsEmpty)
		{
			return new SKRect(x, y, x, y);
		}
		float dx = x - (align < 0 ? b.Left : align == 0 ? b.MidX : b.Right);
		float dy = y - (bottom ? b.Bottom : b.Top);
		c.Save();
		c.Translate(dx, dy);
		using (var paint = new SKPaint { Color = color, IsAntialias = true, Style = stroke > 0f ? SKPaintStyle.StrokeAndFill : SKPaintStyle.Fill, StrokeWidth = stroke, StrokeJoin = SKStrokeJoin.Round })
		{
			c.DrawPath(p, paint);
		}
		c.Restore();
		return new SKRect(b.Left + dx, b.Top + dy, b.Right + dx, b.Bottom + dy);
	}

	// ---------- the Earth ----------

	/// <summary>Where the sun is overhead now: its latitude (declination) and longitude, in degrees.</summary>
	private static (double Lat, double Lon) McSun(DateTime utc) => EarthMap.Sun(utc);

	/// <summary>The map for now (<see cref="EarthMap.Now"/>) as an image, made again once a minute.</summary>
	private SKImage McMap(DateTime utc)
	{
		if (_mcMap != null && (utc - _mcMapAt).TotalSeconds < 60.0 && utc >= _mcMapAt)
		{
			return _mcMap;
		}
		var info = new SKImageInfo(960, 480, SKColorType.Bgra8888, SKAlphaType.Premul);
		byte[] outPx = EarthMap.Now(utc);
		_mcMap?.Dispose();
		unsafe
		{
			fixed (byte* p = outPx)
			{
				using var pix = new SKPixmap(info, (IntPtr)p, 960 * 4);
				_mcMap = SKImage.FromPixelCopy(pix);
			}
		}
		_mcMapAt = utc;
		return _mcMap;
	}

	private static float McX(double lon) => McMapX + (float)((lon + 180.0) / 360.0 * McMapW);

	private static float McY(double lat) => (float)((90.0 - lat) / 180.0 * McMapH);

	/// <summary>The spacecraft's latitude and longitude at screen time tau: an inclined circular orbit over an Earth that turns under it.</summary>
	private static (double Lat, double Lon) McOrbit(double tau)
	{
		double u = 2.0 * Math.PI * tau / McPeriod, inc = McIncl * Math.PI / 180.0;
		double lat = Math.Asin(Math.Sin(inc) * Math.Sin(u)) * 180.0 / Math.PI;
		// each orbit the track lands about 23 degrees further west, as the Earth turns beneath it
		double lon = 20.0 + Math.Atan2(Math.Cos(inc) * Math.Sin(u), Math.Cos(u)) * 180.0 / Math.PI - 22.9 * tau / McPeriod;
		lon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
		return (lat, lon);
	}

	/// <summary>The angle between two points on the globe, in degrees.</summary>
	private static double McArc(double lat1, double lon1, double lat2, double lon2)
	{
		double r = Math.PI / 180.0;
		double c = Math.Sin(lat1 * r) * Math.Sin(lat2 * r) + Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Cos((lon1 - lon2) * r);
		return Math.Acos(Math.Clamp(c, -1.0, 1.0)) / r;
	}

	// ---------- the frame ----------

	private void Mission(Graphics g, double t)
	{
		double dt = double.IsNaN(_mcAt) ? 0.0 : Math.Clamp(t - _mcAt, 0.0, 0.25);
		_mcAt = t;
		DateTime utc = DateTime.UtcNow;

		string? caution = null;
		foreach (McRow m in McRows)
		{
			if (m.Warn > 0f && McOnline(m.Key) && Val(m.Key) >= m.Warn)
			{
				caution = $"{m.Label} {Val(m.Key).ToString(m.Format, CultureInfo.InvariantCulture)}{m.Unit}";
				break;
			}
		}

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_skMission ??= SKSurface.Create(new SKImageInfo(480, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas q = _skMission.Canvas;
			q.Clear(SKColors.Transparent);
			q.Save();
			q.Scale(0.25f);

			using (var shader = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 480f), new[] { new SKColor(7, 11, 20), new SKColor(3, 5, 10) }, null, SKShaderTileMode.Clamp))
			using (var bg = new SKPaint { Shader = shader })
			{
				c.DrawRect(0f, 0f, 1920f, 480f, bg);
			}
			McEarth(c, q, utc, t);
			McPanels(c, q, dt, t, caution, glowOnly: true);

			q.Restore();
			using (SKImage sharp = _skMission.Snapshot())
			{
				q.Clear(SKColors.Transparent);
				using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.8f, 1.8f) };
				q.DrawImage(sharp, 0f, 0f, SkiaKit.Linear, blur);
			}
			using (SKImage glow = _skMission.Snapshot())
			using (SKPixmap pixels = _skDrawing!.PeekPixels())
			{
				SkiaKit.UpscaleAdd(glow, pixels);
			}
			McPanels(c, q, dt, t, caution, glowOnly: false);
		});
	}

	/// <summary>
	/// The map and everything on it: graticule, the sun, ground stations, the orbit track behind and ahead, the
	/// spacecraft with its pulsing footprint and its links, a slow scan line, and the orbit readout.
	/// </summary>
	private void McEarth(SKCanvas c, SKCanvas q, DateTime utc, double t)
	{
		c.DrawImage(McMap(utc), McMapX, 0f);
		// the map's top and bottom (the poles) sink into the dark, so the ice does not glare
		foreach (bool topEdge in new[] { true, false })
		{
			float y0 = topEdge ? 0f : 390f, y1 = topEdge ? 64f : 480f;
			var cols = topEdge ? new[] { new SKColor(5, 8, 15, 235), new SKColor(5, 8, 15, 0) } : new[] { new SKColor(5, 8, 15, 0), new SKColor(5, 8, 15, 225) };
			using var shade = SKShader.CreateLinearGradient(new SKPoint(0f, y0), new SKPoint(0f, y1), cols, null, SKShaderTileMode.Clamp);
			using var sp = new SKPaint { Shader = shade };
			c.DrawRect(McMapX, y0, McMapW, y1 - y0, sp);
		}

		using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
		// the graticule every 30 degrees, the equator a touch brighter
		for (int lon = -150; lon <= 150; lon += 30)
		{
			line.Color = new SKColor(150, 190, 230, (byte)(lon == 0 ? 60 : 32));
			c.DrawLine(McX(lon), 0f, McX(lon), 480f, line);
		}
		for (int lat = -60; lat <= 60; lat += 30)
		{
			line.Color = new SKColor(150, 190, 230, (byte)(lat == 0 ? 70 : 32));
			c.DrawLine(McMapX, McY(lat), McMapX + McMapW, McY(lat), line);
		}
		foreach (int lat in new[] { 60, 30, 0, -30, -60 })
		{
			McText(c, lat == 0 ? "EQ" : $"{Math.Abs(lat)}{(lat > 0 ? "N" : "S")}", McMapX + 6f, McY(lat) - 13f, 10f, new SKColor(170, 196, 225, 160), font: "Bahnschrift");
		}
		foreach (int lon in new[] { -120, -60, 0, 60, 120 })
		{
			McText(c, lon == 0 ? "0" : $"{Math.Abs(lon)}{(lon > 0 ? "E" : "W")}", McX(lon) + 4f, 466f, 10f, new SKColor(170, 196, 225, 160), font: "Bahnschrift");
		}

		// the sun, glowing over the point where it is overhead
		var (sLat, sLon) = McSun(utc);
		float sx = McX(sLon), sy = McY(sLat);
		float sb = 0.85f + 0.15f * (float)Math.Sin(t * 0.8);
		SkiaKit.Glow(c, sx, sy, 120f, 120f, new SKColor(255, 220, 160, (byte)(46 * sb)), SKBlendMode.Plus);
		SkiaKit.Glow(q, sx, sy, 60f, 60f, new SKColor(255, 210, 150, (byte)(150 * sb)), SKBlendMode.Plus);
		using (var sun = new SKPaint { IsAntialias = true, Color = new SKColor(255, 240, 210, 230) })
		{
			c.DrawCircle(sx, sy, 4f, sun);
		}

		var (cLat, cLon) = McOrbit(t);
		float cx = McX(cLon), cy = McY(cLat);

		// the ground track: one orbit behind, lit and fading into the past; one ahead, dashed
		void Track(double from, double to, bool ahead)
		{
			const int steps = 220;
			using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
			if (ahead)
			{
				p.PathEffect = SKPathEffect.CreateDash(new[] { 6f, 7f }, (float)(-t * 24.0 % 13.0));
			}
			PointF? last = null;
			using var path = new SKPath();
			var segs = new List<(SKPath Path, float Age)>();
			for (int i = 0; i <= steps; i++)
			{
				double tau = from + (to - from) * i / steps;
				var (la, lo) = McOrbit(tau);
				var pt = new PointF(McX(lo), McY(la));
				float age = (float)Math.Abs(t - tau) / (float)McPeriod;
				if (last != null && Math.Abs(pt.X - last.Value.X) < McMapW / 2f)
				{
					var seg = new SKPath();
					seg.MoveTo(last.Value.X, last.Value.Y);
					seg.LineTo(pt.X, pt.Y);
					segs.Add((seg, age));
				}
				last = pt;
			}
			foreach (var (seg, age) in segs)
			{
				float a = ahead ? 0.55f * (1f - age) : 1f - age;
				p.StrokeWidth = ahead ? 1.4f : 2f;
				p.Color = ahead ? new SKColor(200, 230, 255, (byte)(150 * a)) : McCyan.WithAlpha((byte)(235 * a * a));
				c.DrawPath(seg, p);
				if (!ahead)
				{
					using var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 9f, Color = McCyan.WithAlpha((byte)(120 * a * a)) };
					q.DrawPath(seg, soft);
				}
				seg.Dispose();
			}
		}
		Track(t - McPeriod, t, ahead: false);
		Track(t, t + McPeriod, ahead: true);

		// ground stations: a dim mark, lit green while the spacecraft is in reach, with the link drawn to it
		const double reach = 19.0;
		string? linked = null;
		foreach (McStation st in McStations)
		{
			float gx = McX(st.Lon), gy = McY(st.Lat);
			bool aos = McArc(st.Lat, st.Lon, cLat, cLon) < reach && Math.Abs(gx - cx) < McMapW / 2f;
			SKColor col = aos ? McGreen : new SKColor(200, 214, 235, 170);
			using (var mark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f, Color = col })
			{
				c.DrawRect(gx - 3.5f, gy - 3.5f, 7f, 7f, mark);
			}
			McText(c, st.Code, gx + 7f, gy - 12f, 10f, col.WithAlpha((byte)(aos ? 240 : 150)), track: 1f, font: "Bahnschrift");
			if (aos)
			{
				linked ??= st.Code;
				using var link = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = McGreen.WithAlpha(220), PathEffect = SKPathEffect.CreateDash(new[] { 4f, 5f }, (float)(-t * 30.0 % 9.0)) };
				c.DrawLine(gx, gy, cx, cy, link);
				SkiaKit.Glow(q, gx, gy, 16f, 16f, McGreen.WithAlpha(170), SKBlendMode.Plus);
			}
		}
		_mcLink = linked;

		// the footprint, breathing, and a ring of signal leaving the craft every two seconds
		double rad = Math.Cos(cLat * Math.PI / 180.0);
		float ry = (float)(reach / 180.0 * McMapH), rx = (float)(ry / Math.Max(0.35, rad));
		using (var fp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = McCyan.WithAlpha(150) })
		{
			c.DrawOval(cx, cy, rx, ry, fp);
			fp.Style = SKPaintStyle.Fill;
			fp.Color = McCyan.WithAlpha(18);
			c.DrawOval(cx, cy, rx, ry, fp);
			float life = (float)(t * 0.5 % 1.0);
			fp.Style = SKPaintStyle.Stroke;
			fp.Color = McCyan.WithAlpha((byte)(170 * (1f - life)));
			c.DrawOval(cx, cy, rx * life, ry * life, fp);
		}
		// the spacecraft: a bright core in a soft halo, with its call sign
		SkiaKit.Glow(q, cx, cy, 34f, 34f, new SKColor(110, 210, 255, 170), SKBlendMode.Plus);
		SkiaKit.Glow(c, cx, cy, 12f, 12f, new SKColor(120, 210, 255, 140), SKBlendMode.Plus);
		using (var craft = new SKPaint { IsAntialias = true, Color = SKColors.White })
		{
			c.DrawCircle(cx, cy, 3.4f, craft);
			craft.Style = SKPaintStyle.Stroke;
			craft.StrokeWidth = 1.2f;
			c.DrawLine(cx - 11f, cy, cx - 5f, cy, craft);
			c.DrawLine(cx + 5f, cy, cx + 11f, cy, craft);
		}
		McText(c, "LUMA-1", cx + 14f, cy + 6f, 11f, new SKColor(235, 245, 255, 235), track: 1.2f);

		// a slow scan line sweeping across the map
		float scan = McMapX + (float)(t / 9.0 % 1.0) * McMapW;
		using (var scanShader = SKShader.CreateLinearGradient(new SKPoint(scan - 70f, 0f), new SKPoint(scan + 70f, 0f), new[] { McCyan.WithAlpha(0), McCyan.WithAlpha(22), McCyan.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
		using (var sp = new SKPaint { Shader = scanShader, BlendMode = SKBlendMode.Plus })
		{
			c.DrawRect(scan - 70f, 0f, 140f, 480f, sp);
		}

		// the map fades into the side panels
		foreach (bool left in new[] { true, false })
		{
			float x0 = left ? McMapX : McMapX + McMapW - 60f;
			using var fade = SKShader.CreateLinearGradient(new SKPoint(x0, 0f), new SKPoint(x0 + 60f, 0f),
				left ? new[] { new SKColor(5, 8, 15, 255), new SKColor(5, 8, 15, 0) } : new[] { new SKColor(5, 8, 15, 0), new SKColor(5, 8, 15, 255) }, null, SKShaderTileMode.Clamp);
			using var fp = new SKPaint { Shader = fade };
			c.DrawRect(x0, 0f, 60f, 480f, fp);
		}

		// the orbit readout, on a dark tab at the top of the map
		int orbitNo = (int)Math.Floor(t / McPeriod) % 999 + 1;
		double alt = 418.0 + 6.0 * Math.Sin(2.0 * Math.PI * t / McPeriod * 2.0);
		string latText = $"{Math.Abs(cLat):00.0}°{(cLat >= 0 ? "N" : "S")}", lonText = $"{Math.Abs(cLon):000.0}°{(cLon >= 0 ? "E" : "W")}";
		string readout = $"ORBIT {orbitNo:000}   LAT {latText}   LON {lonText}   ALT {alt:0} KM   VEL 7.66 KM/S";
		using (var tab = new SKPaint { IsAntialias = true, Color = new SKColor(4, 7, 14, 205) })
		{
			c.DrawRoundRect(new SKRect(McMapX + 150f, 8f, McMapX + McMapW - 150f, 34f), 5f, 5f, tab);
		}
		McText(c, readout, McMapX + McMapW / 2f, 15f, 12f, new SKColor(222, 236, 252, 235), align: 0, track: 1.4f, font: "Bahnschrift");
	}

	private string? _mcLink;

	/// <summary>
	/// The two consoles either side of the map. glowOnly draws only their soft light on the glow layer; otherwise the
	/// panels, traces and type.
	/// </summary>
	private void McPanels(SKCanvas c, SKCanvas q, double dt, double t, string? caution, bool glowOnly)
	{
		DateTime now = DateTime.Now, utc = DateTime.UtcNow;
		string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);

		// ---- left: the clocks ----
		if (glowOnly)
		{
			McText(q, time, 36f, 92f, 108f, new SKColor(60, 150, 255, 110), stroke: 10f);
		}
		else
		{
			McText(c, "MISSION CONTROL", 38f, 30f, 15f, McCyan, track: 4.2f);
			McText(c, "LUMALI  ·  FLIGHT OPERATIONS", 38f, 56f, 11f, McDim, track: 2.2f, font: "Bahnschrift");
			SKRect tr = McText(c, time, 36f, 92f, 108f, SKColors.White);
			McText(c, now.ToString("ss", CultureInfo.InvariantCulture), tr.Right + 12f, tr.Bottom, 32f, new SKColor(170, 200, 235, 230), bottom: true, font: "Bahnschrift");
			McText(c, now.ToString("dddd  dd  MMMM", CultureInfo.InvariantCulture).ToUpperInvariant(), 38f, 222f, 14f, new SKColor(206, 216, 232, 235), track: 2.8f);

			// UTC and mission elapsed time in a two-column block
			TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
			void Field(string label, string value, float x, float y)
			{
				McText(c, label, x, y, 11f, McDim, track: 2.2f, font: "Bahnschrift");
				McText(c, value, x, y + 20f, 22f, SKColors.White, font: "Bahnschrift");
			}
			Field("UTC", utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 38f, 262f);
			Field("MET", $"{up.Days}d {up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}", 214f, 262f);

			// communications: the network as downlink and uplink, and the station in contact
			float down = Val("net.down"), upl = Val("net.up");
			Field("DOWNLINK", $"{down:0.0} MB/s", 38f, 326f);
			Field("UPLINK", $"{upl:0.0} MB/s", 214f, 326f);

			using (var rule = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(120, 150, 190, 70) })
			{
				c.DrawLine(38f, 392f, 430f, 392f, rule);
			}
			string link = _mcLink != null ? $"LINK  {_mcLink}  ·  AOS" : "LINK  ·  LOS  ·  NEXT PASS PENDING";
			McText(c, link, 38f, 404f, 11f, _mcLink != null ? McGreen : McDim, track: 2f, font: "Bahnschrift");
			float b = (float)(0.6 + 0.4 * Math.Sin(t * (caution != null ? 4.0 : 1.4)));
			SKColor sc = caution != null ? McAmber : McGreen;
			using (var dot = new SKPaint { IsAntialias = true, Color = sc.WithAlpha((byte)(255 * b)) })
			{
				c.DrawCircle(44f, 446f, 4.5f, dot);
			}
			McText(c, caution != null ? "CAUTION  ·  " + caution : "ALL SYSTEMS NOMINAL", 58f, 440f, 13f, sc, track: 2.4f);
		}

		// ---- right: the subsystem console ----
		const float x0 = 1462f, x1 = 1896f, rowH = 82f, top = 46f;
		if (!glowOnly)
		{
			McText(c, "SUBSYSTEMS", x0, 18f, 13f, McCyan, track: 3.6f);
			float live = (float)(0.55 + 0.45 * Math.Sin(t * 2.0));
			using (var dot = new SKPaint { IsAntialias = true, Color = McGreen.WithAlpha((byte)(255 * live)) })
			{
				c.DrawCircle(x1 - 44f, 24f, 3.5f, dot);
			}
			McText(c, "LIVE", x1, 18f, 11f, McGreen, align: 1, track: 2f, font: "Bahnschrift");
		}
		for (int i = 0; i < McRows.Length; i++)
		{
			McRow m = McRows[i];
			float y = top + i * rowH;
			bool online = McOnline(m.Key);
			float now0 = Val(m.Key);
			bool warn = online && m.Warn > 0f && now0 >= m.Warn;
			SKColor accent = !online ? McDim : warn ? McAmber : McCyan;
			float[] hist = _sensors.History(m.Key);

			// the trace: the last 60 seconds, each point gliding to its new height
			const int pts = 31;
			float gx0 = x0 + 196f, gx1 = x1, gy0 = y + 20f, gy1 = y + 64f;
			// the scale follows the last minute (at least a fifth of the full range), so small changes show
			float hMin = float.MaxValue, hMax = float.MinValue;
			for (int k = Math.Max(0, hist.Length - 61); k < hist.Length; k++)
			{
				hMin = Math.Min(hMin, hist[k]);
				hMax = Math.Max(hMax, hist[k]);
			}
			if (hMin > hMax)
			{
				hMin = hMax = now0;
			}
			float span = Math.Max(hMax - hMin, (m.Hi - m.Lo) * 0.2f);
			float mid = (hMin + hMax) / 2f;
			float lo = McEased("lo" + m.Key, Math.Clamp(mid - span * 0.6f, m.Lo - span, m.Hi), glowOnly ? dt : 0.0, 1.2);
			float hi = McEased("hi" + m.Key, mid + span * 0.6f, glowOnly ? dt : 0.0, 1.2);
			var pp = new SKPoint[pts];
			for (int k = 0; k < pts; k++)
			{
				int idx = hist.Length - 1 - (pts - 1 - k) * 2;
				float raw = hist.Length == 0 ? now0 : hist[Math.Clamp(idx, 0, hist.Length - 1)];
				float f = Math.Clamp((raw - lo) / Math.Max(0.001f, hi - lo), 0f, 1f);
				f = McEased($"t{m.Key}{k}", f, glowOnly ? dt : 0.0, 2.4);
				pp[k] = new SKPoint(gx0 + (gx1 - gx0) * k / (pts - 1), gy1 - (gy1 - gy0) * f);
			}
			var asPoints = Array.ConvertAll(pp, p => new PointF(p.X, p.Y));
			using SKPath curve = SkiaKit.Curve(asPoints);
			if (glowOnly)
			{
				using var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8f, Color = accent.WithAlpha(110) };
				q.DrawPath(curve, soft);
				SkiaKit.Glow(q, pp[^1].X, pp[^1].Y, 16f, 16f, accent.WithAlpha(200), SKBlendMode.Plus);
				continue;
			}
			using (var frame = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(120, 150, 190, 40) })
			{
				c.DrawLine(gx0, gy1, gx1, gy1, frame);
				c.DrawLine(gx0, gy0, gx1, gy0, frame);
			}
			using (SKPath fill = new SKPath(curve))
			using (var wash = SKShader.CreateLinearGradient(new SKPoint(0f, gy0), new SKPoint(0f, gy1), new[] { accent.WithAlpha(70), accent.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
			using (var wp = new SKPaint { Shader = wash, IsAntialias = true })
			{
				fill.LineTo(gx1, gy1);
				fill.LineTo(gx0, gy1);
				fill.Close();
				c.DrawPath(fill, wp);
			}
			using (var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, Color = accent })
			{
				c.DrawPath(curve, stroke);
			}
			using (var head = new SKPaint { IsAntialias = true, Color = SKColors.White })
			{
				c.DrawCircle(pp[^1].X, pp[^1].Y, 3f, head);
			}

			// status light, label and the value in white
			float blink = warn ? (float)(0.55 + 0.45 * Math.Sin(t * 4.0)) : 1f;
			SKColor light = !online ? McDim : warn ? McAmber : McGreen;
			using (var dot = new SKPaint { IsAntialias = true, Color = light.WithAlpha((byte)(255 * blink)) })
			{
				c.DrawCircle(x0 + 4f, y + 6f, 3.5f, dot);
			}
			McText(c, m.Label, x0 + 14f, y, 11f, new SKColor(206, 218, 236, 230), track: 1.8f);
			float shown = online ? McEased("v" + m.Key, now0, 0.0) : 0f;
			if (online)
			{
				McEased("v" + m.Key, now0, dt);
			}
			string value = online ? shown.ToString(m.Format, CultureInfo.InvariantCulture) : "--";
			SKRect vr = McText(c, value, x0, y + 22f, 40f, SKColors.White);
			McText(c, m.Unit, vr.Right + 6f, vr.Bottom, 16f, new SKColor(206, 218, 236, 220), bottom: true, font: "Bahnschrift");
			McText(c, !online ? "NO SIGNAL" : warn ? "CAUTION" : "NOMINAL", x1, y + 1f, 9f, light.WithAlpha((byte)(220 * blink)), align: 1, track: 1.6f, font: "Bahnschrift");
		}
	}
}
