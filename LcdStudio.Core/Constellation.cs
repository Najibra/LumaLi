using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// Constellation: every sensor as a glossy orb on a tilted ring round a golden core, spokes carrying coloured data
/// ticks and a tan value bar, comets streaming out along them as fast as each reading is busy. Every so often the
/// ring folds into a network: five category hubs tied to the core by golden beams, each sensor orbiting its hub,
/// with blue energy bursting off the orbs as they travel; a warning pulls it into the network at once and turns its
/// orb red. A timeline of processor load runs down the left edge; a legend and the live count sit at the foot.
/// </summary>
public sealed partial class ScreenRenderer
{
	private sealed record StarSensor(string Key, string Label, string Unit, string Format, float Max, float Warn, int Cat, float Scale = 1f);

	private static readonly StarSensor[] StarSensors =
	{
		new("cpu.load", "CPU Load", "%", "0", 100f, 90f, 0),
		new("cpu.busiest", "Busiest Thread", "%", "0", 100f, 98f, 0),
		new("cpu.clock", "CPU Clock", "GHz", "0.00", 6f, 0f, 0, 0.001f),
		new("cpu.power", "CPU Power", "W", "0", 200f, 0f, 0),
		new("cpu.temp", "CPU Temp", "°C", "0", 95f, 90f, 0),
		new("gpu.load", "GPU Load", "%", "0", 100f, 98f, 1),
		new("gpu.temp", "GPU Temp", "°C", "0", 95f, 83f, 1),
		new("gpu.hotspot", "Hot Spot", "°C", "0", 110f, 100f, 1),
		new("gpu.power", "GPU Power", "W", "0", 450f, 0f, 1),
		new("gpu.clock", "GPU Clock", "MHz", "0", 3500f, 0f, 1),
		new("gpu.memclock", "VRAM Clock", "MHz", "0", 16000f, 0f, 1),
		new("gpu.vram", "VRAM Used", "GB", "0.0", 32f, 0f, 1),
		new("gpu.fan", "GPU Fan", "%", "0", 100f, 0f, 1),
		new("ram.load", "Memory", "%", "0", 100f, 90f, 2),
		new("ram.used", "Memory Used", "GB", "0.0", 0f, 0f, 2),
		new("net.down", "Download", "MB/s", "0.0", 0f, 0f, 3),
		new("net.up", "Upload", "MB/s", "0.0", 0f, 0f, 3),
		new("fans.rpm", "Case Fans", "RPM", "0", 3000f, 0f, 4),
		new("fans.duty", "Fan Duty", "%", "0", 100f, 0f, 4)
	};

	private static readonly string[] StarCats = { "Processor", "Graphics", "Memory", "Network", "Cooling" };

	private static readonly Color[] StarCatColors =
	{
		Color.FromArgb(236, 150, 182), Color.FromArgb(152, 138, 232), Color.FromArgb(92, 214, 162), Color.FromArgb(255, 154, 74), Color.FromArgb(84, 204, 226)
	};

	// where each category's hub sits on the network ellipse, in degrees
	private static readonly float[] StarHubAngles = { 196f, 344f, 140f, 268f, 52f };

	private static readonly Color StarGold = Color.FromArgb(232, 200, 140), StarRed = Color.FromArgb(246, 82, 82), StarText = Color.FromArgb(216, 224, 226);

	private const float StarCx = 960f, StarCy = 244f;

	private Bitmap? _starPage;

	private readonly Dictionary<(int, int), Bitmap> _starOrbs = new Dictionary<(int, int), Bitmap>();

	private double _starSpin, _starMorph, _starAt = double.NaN;

	private readonly Dictionary<string, (float Last, double Flare)> _starFlares = new Dictionary<string, (float, double)>();

	private bool DrawConstellation(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "constellation" || w.Kind != WidgetKind.Panel)
		{
			return DrawLightbeam(g, w, r, t);
		}
		Constellation(g, t);
		return true;
	}

	/// <summary>The page: deep teal light pooled behind the core, falling to black at the edges. Composed once.</summary>
	private void StarPage(Graphics g)
	{
		if (_starPage == null)
		{
			_starPage = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
			using Graphics pg = Graphics.FromImage(_starPage);
			pg.Clear(Color.FromArgb(3, 6, 7));
			using var pool = new GraphicsPath();
			pool.AddEllipse(-200f, -420f, 2320f, 1320f);
			using (var light = new PathGradientBrush(pool) { CenterPoint = new PointF(StarCx, StarCy), CenterColor = Color.FromArgb(22, 52, 52), SurroundColors = new[] { Color.FromArgb(3, 6, 7) } })
			{
				light.Blend = new Blend { Factors = new[] { 0f, 0.55f, 0.9f, 1f }, Positions = new[] { 0f, 0.35f, 0.75f, 1f } };
				pg.FillPath(light, pool);
			}
			Grain(_starPage, 4);
		}
		CompositingMode copy = g.CompositingMode;
		g.CompositingMode = CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_starPage, 0, 0);
		g.CompositingMode = copy;
	}

	// ---------- readings ----------

	private bool StarOnline(StarSensor s) => _sensors.Get(s.Key).HasValue && (Val(s.Key) > 0f || s.Cat >= 3 || s.Key == "gpu.fan");

	private float StarMax(StarSensor s)
	{
		if (s.Max > 0f)
		{
			return s.Max;
		}
		if (s.Key == "ram.used")
		{
			float load = Val("ram.load");
			return load > 1f ? Val("ram.used") / load * 100f : 64f;
		}
		float[] h = _sensors.History(s.Key);
		return Math.Max(1f, h.Length > 0 ? h.Max() * 1.1f : 1f);
	}

	private float StarFraction(StarSensor s) => Math.Clamp(Val(s.Key) * s.Scale / StarMax(s), 0f, 1f);

	private bool StarWarn(StarSensor s) => s.Warn > 0f && Val(s.Key) * s.Scale >= s.Warn;

	private string StarValue(StarSensor s) => StarOnline(s) ? (Val(s.Key) * s.Scale).ToString(s.Format, CultureInfo.InvariantCulture) + (s.Unit == "%" ? "%" : " " + s.Unit) : "offline";

	// ---------- the scene ----------

	private void Constellation(Graphics g, double t)
	{
		double dt = double.IsNaN(_starAt) || t < _starAt ? 0.0 : Math.Min(0.5, t - _starAt);
		_starAt = t;
		float cpu = Val("cpu.load") / 100f;
		// the ring turns faster the busier the processor
		_starSpin += dt * (0.025 + 0.12 * cpu);
		bool alarm = StarSensors.Any(StarWarn);
		// ring for 13 seconds, network for 13, and straight to the network on a warning
		double target = alarm || t % 26.0 >= 13.0 ? 1.0 : 0.0;
		_starMorph += (target - _starMorph) * (1.0 - Math.Exp(-dt * 1.6));
		float m = (float)_starMorph, ms = m * m * (3f - 2f * m), burst = 4f * ms * (1f - ms);
		int n = StarSensors.Length;
		var sun = new PointF(StarCx, StarCy);
		var hubs = new PointF[StarCats.Length];
		for (int c = 0; c < hubs.Length; c++)
		{
			double a = StarHubAngles[c] * Math.PI / 180.0;
			hubs[c] = new PointF(StarCx + 560f * (float)Math.Cos(a), StarCy + 158f * (float)Math.Sin(a));
		}
		// every node's place: on the ring, or orbiting its hub, blended by the morph
		var pos = new PointF[n];
		var depth = new float[n];
		var mids = new PointF[n];
		for (int i = 0; i < n; i++)
		{
			StarSensor s = StarSensors[i];
			double th = _starSpin + i * Math.PI * 2.0 / n;
			var ring = new PointF(StarCx + 740f * (float)Math.Cos(th), StarCy + 190f * (float)Math.Sin(th));
			int count = StarSensors.Count(q => q.Cat == s.Cat), j = StarSensors.Take(i).Count(q => q.Cat == s.Cat);
			double ph = _starSpin * 1.6 + j * Math.PI * 2.0 / count + s.Cat;
			float rx = 92f + 9f * count, ry = 48f + 5f * count;
			var orbit = new PointF(hubs[s.Cat].X + rx * (float)Math.Cos(ph), Math.Clamp(hubs[s.Cat].Y + ry * (float)Math.Sin(ph), 30f, 450f));
			pos[i] = new PointF(ring.X + (orbit.X - ring.X) * ms, ring.Y + (orbit.Y - ring.Y) * ms);
			depth[i] = (0.78f + 0.32f * (float)(Math.Sin(th) + 1.0) / 2f) * (1f - ms) + 0.92f * ms;
			var half = new PointF((sun.X + pos[i].X) / 2f, (sun.Y + pos[i].Y) / 2f);
			mids[i] = new PointF(half.X + (hubs[s.Cat].X - half.X) * ms, half.Y + (hubs[s.Cat].Y - half.Y) * ms);
		}
		StarPage(g);
		StarTimeline(g);
		StarInner(g, 1f - ms, t);
		// golden beams from the core to each hub, and fine threads from hub to orb, as the network forms
		if (ms > 0.02f)
		{
			for (int c = 0; c < hubs.Length; c++)
			{
				bool hot = StarSensors.Any(q => q.Cat == c && StarWarn(q));
				Color beam = hot ? StarRed : StarGold;
				foreach (var (width, alpha) in new[] { (12f, 26), (5f, 90), (1.6f, 230) })
				{
					using var pen = new Pen(Color.FromArgb((int)(alpha * ms), beam), width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
					g.DrawLine(pen, sun, hubs[c]);
				}
			}
		}
		for (int i = 0; i < n; i++)
		{
			StarSensor s = StarSensors[i];
			bool online = StarOnline(s);
			Color c = StarCatColors[s.Cat];
			using (var thread = new Pen(Color.FromArgb((int)((online ? 70 : 30) * (1f - ms) + (online ? 110 : 40) * ms), ms > 0.5f ? Color.FromArgb(120, 200, 220) : StarGold), 1f))
			{
				g.DrawLines(thread, new[] { sun, mids[i], pos[i] });
			}
			if (online && ms < 0.98f)
			{
				StarSpoke(g, s, sun, pos[i], 1f - ms);
			}
		}
		// comets: more of them, and quicker, the busier the reading
		for (int i = 0; i < n; i++)
		{
			StarSensor s = StarSensors[i];
			if (!StarOnline(s))
			{
				continue;
			}
			float f = StarFraction(s);
			int comets = 1 + (int)(f * 3.99f);
			for (int k = 0; k < comets; k++)
			{
				double sp = (t * (0.18 + 0.32 * f) + Hash01(i * 37 + k * 101)) % 1.0;
				StarComet(g, sun, mids[i], pos[i], (float)sp, StarWarn(s) ? StarRed : Imaging.Mix(StarCatColors[s.Cat], Color.White, 0.6));
			}
		}
		// hubs, in the network
		if (ms > 0.02f)
		{
			for (int c = 0; c < hubs.Length; c++)
			{
				var members = StarSensors.Where(q => q.Cat == c && StarOnline(q)).ToArray();
				float avg = members.Length > 0 ? members.Average(StarFraction) : 0f;
				bool hot = StarSensors.Any(q => q.Cat == c && StarWarn(q));
				Bitmap orb = StarOrb(hot ? StarRed : StarCatColors[c], 15);
				var st = g.Save();
				using (var ia = new ImageAttributes())
				{
					ia.SetColorMatrix(new ColorMatrix { Matrix33 = ms });
					g.DrawImage(orb, new Rectangle((int)(hubs[c].X - orb.Width / 2f), (int)(hubs[c].Y - orb.Height / 2f), orb.Width, orb.Height), 0, 0, orb.Width, orb.Height, GraphicsUnit.Pixel, ia);
				}
				g.Restore(st);
				if (ms > 0.6f)
				{
					DeckText(g, StarCats[c], 15f, Color.FromArgb((int)(255 * (ms - 0.6f) / 0.4f), hot ? StarRed : Imaging.Mix(StarCatColors[c], Color.White, 0.55)), hubs[c].X, hubs[c].Y - 46f, StringAlignment.Center, "Segoe UI Semibold");
					StarPill(g, (avg * 100f).ToString("0") + "%", hubs[c].X - 16f, hubs[c].Y + 24f, StringAlignment.Near, StarCatColors[c], (ms - 0.6f) / 0.4f);
				}
			}
		}
		// the orbs, back to front
		foreach (int i in Enumerable.Range(0, n).OrderBy(i => pos[i].Y))
		{
			StarNode(g, i, pos[i], depth[i], t, burst);
		}
		StarSun(g, t, alarm);
		StarChrome(g, t, ms, alarm);
	}

	/// <summary>A spoke's data: ticks coloured by state, more of them the higher the reading, and a tan bar as long as the reading.</summary>
	private void StarSpoke(Graphics g, StarSensor s, PointF a, PointF b, float fade)
	{
		PointF At(float u) => new PointF(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
		float f = StarFraction(s);
		Color state = StarWarn(s) ? StarRed : f > 0.7f ? Color.FromArgb(255, 150, 50) : Color.FromArgb(80, 220, 160);
		int ticks = 1 + (int)Math.Round(f * 5f);
		using (var tick = new Pen(Color.FromArgb((int)(230 * fade), state), 3f))
		{
			for (int k = 0; k < ticks; k++)
			{
				float u = 0.36f + k * 0.04f;
				g.DrawLine(tick, At(u), At(u + 0.022f));
			}
		}
		if (f > 0.01f)
		{
			PointF p0 = At(0.62f), p1 = At(0.62f + 0.24f * f);
			using var glow = new Pen(Color.FromArgb((int)(34 * fade), StarGold), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			using var bar = new Pen(Color.FromArgb((int)(170 * fade), StarGold), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawLine(glow, p0, p1);
			g.DrawLine(bar, p0, p1);
		}
	}

	/// <summary>A comet at fraction u of the way along the core → bend → orb path, with a tail behind it.</summary>
	private void StarComet(Graphics g, PointF a, PointF mid, PointF b, float u, Color color)
	{
		float l1 = Dist(a, mid), l2 = Dist(mid, b), total = Math.Max(1f, l1 + l2);
		PointF At(float v)
		{
			float d = Math.Clamp(v, 0f, 1f) * total;
			return d <= l1 ? Lerp(a, mid, d / Math.Max(1f, l1)) : Lerp(mid, b, (d - l1) / Math.Max(1f, l2));
		}
		float fade = (float)Math.Sin(Math.PI * u);
		PointF head = At(u), tail = At(u - 0.08f);
		using (var wide = new Pen(Color.FromArgb((int)(60 * fade), color), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
		{
			g.DrawLine(wide, tail, head);
		}
		using (var core = new Pen(Color.FromArgb((int)(220 * fade), Color.White), 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
		{
			g.DrawLine(core, At(u - 0.04f), head);
		}
		Bitmap dot = VoidDot(6, Math.Max(1, (int)Math.Round(10 * fade)), 0);
		g.DrawImageUnscaled(dot, (int)(head.X - dot.Width / 2f), (int)(head.Y - dot.Height / 2f));
	}

	private static float Dist(PointF a, PointF b) => (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

	private static PointF Lerp(PointF a, PointF b, float u) => new PointF(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);

	/// <summary>
	/// One sensor's orb, sized by its reading, its label and value beside it facing outward; it flares when the
	/// reading jumps, beats red on a warning, sits grey when the sensor is silent, and throws off blue sparks mid-morph.
	/// </summary>
	private void StarNode(Graphics g, int i, PointF p, float depth, double t, float burst)
	{
		StarSensor s = StarSensors[i];
		bool online = StarOnline(s), warn = StarWarn(s);
		float f = online ? (float)DeckEase("star|" + s.Key, StarFraction(s), t) : 0f;
		Color c = !online ? Color.FromArgb(90, 100, 104) : warn ? StarRed : StarCatColors[s.Cat];
		int rad = (int)Math.Round((8f + 10f * f) * depth);
		// a jump of a twentieth of the range sets off a flare
		float now = Val(s.Key) * s.Scale;
		if (!_starFlares.TryGetValue(s.Key, out var fl))
		{
			fl = (now, -10.0);
		}
		if (Math.Abs(now - fl.Last) / StarMax(s) > 0.05f)
		{
			fl = (now, t);
		}
		_starFlares[s.Key] = fl;
		double since = t - fl.Flare;
		if (since >= 0.0 && since < 0.9)
		{
			float grow = (float)since / 0.9f;
			using var ring = new Pen(Color.FromArgb((int)(200 * (1f - grow)), Imaging.Mix(c, Color.White, 0.5)), 2f);
			float rr = rad + 6f + grow * 26f;
			g.DrawEllipse(ring, p.X - rr, p.Y - rr, rr * 2f, rr * 2f);
		}
		if (warn)
		{
			float beat = (float)(0.5 + 0.5 * Math.Sin(t * 6.0));
			using var halo = new SolidBrush(Color.FromArgb((int)(40 + 70 * beat), StarRed));
			float hr = rad + 8f + 6f * beat;
			g.FillEllipse(halo, p.X - hr, p.Y - hr, hr * 2f, hr * 2f);
		}
		if (burst > 0.05f)
		{
			// blue energy thrown off as the orbs travel between views
			using var bloom = new Pen(Color.FromArgb((int)(70 * burst), 70, 170, 255), 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			using var spark = new Pen(Color.FromArgb((int)(230 * burst), 150, 220, 255), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			for (int k = 0; k < 7; k++)
			{
				double a = Hash01(i * 13 + k) * Math.PI * 2.0 + t * 1.5;
				float len = burst * (30f + 46f * (float)Hash01(i * 7 + k * 3));
				var root = new PointF(p.X + (float)Math.Cos(a) * rad, p.Y + (float)Math.Sin(a) * rad * 0.7f);
				var tip = new PointF(p.X + (float)Math.Cos(a) * (rad + len), p.Y + (float)Math.Sin(a) * (rad + len) * 0.7f);
				g.DrawLine(bloom, root, tip);
				g.DrawLine(spark, root, tip);
			}
		}
		Bitmap orb = StarOrb(c, Math.Max(4, rad));
		g.DrawImageUnscaled(orb, (int)(p.X - orb.Width / 2f), (int)(p.Y - orb.Height / 2f));
		bool right = p.X >= StarCx;
		float lx = p.X + (right ? rad + 10f : -rad - 10f);
		StringAlignment align = right ? StringAlignment.Near : StringAlignment.Far;
		Color label = !online ? Color.FromArgb(110, StarText) : warn ? StarRed : StarText;
		DeckText(g, s.Label, 13f * Math.Min(1.05f, depth + 0.1f), label, lx, p.Y - 19f, align, "Segoe UI Semibold");
		StarPill(g, StarValue(s), lx, p.Y + 1f, align, warn ? StarRed : c, online ? 1f : 0.5f);
	}

	/// <summary>A small dark pill holding a value, edged in the given colour.</summary>
	private void StarPill(Graphics g, string text, float x, float y, StringAlignment align, Color edge, float fade)
	{
		Bitmap label = TextImage(text, 11f, StarText, "Segoe UI");
		float w = label.Width + 8f, h = 17f;
		float left = align == StringAlignment.Far ? x - w : x;
		using GraphicsPath p = Imaging.Round(new RectangleF(left, y, w, h), 4f);
		using (var fill = new SolidBrush(Color.FromArgb((int)(200 * fade), 12, 26, 28)))
		{
			g.FillPath(fill, p);
		}
		using (var pen = new Pen(Color.FromArgb((int)(110 * fade), edge), 1f))
		{
			g.DrawPath(pen, p);
		}
		if (fade >= 0.99f)
		{
			g.DrawImageUnscaled(label, (int)(left + 3f), (int)(y + 1f));
		}
		else
		{
			using var ia = new ImageAttributes();
			ia.SetColorMatrix(new ColorMatrix { Matrix33 = fade });
			g.DrawImage(label, new Rectangle((int)(left + 3f), (int)(y + 1f), label.Width, label.Height), 0, 0, label.Width, label.Height, GraphicsUnit.Pixel, ia);
		}
	}

	/// <summary>A glossy orb: a lit sphere inside a thin ring with a soft glow, cached by colour and size.</summary>
	private Bitmap StarOrb(Color c, int rad)
	{
		var key = (c.ToArgb(), rad);
		if (_starOrbs.TryGetValue(key, out Bitmap bmp))
		{
			return bmp;
		}
		int size = rad * 2 + 16;
		bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		float cx = size / 2f;
		using (var glow = new GraphicsPath())
		{
			glow.AddEllipse(cx - rad - 7f, cx - rad - 7f, (rad + 7f) * 2f, (rad + 7f) * 2f);
			using var gb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(90, c), SurroundColors = new[] { Color.FromArgb(0, c) } };
			g.FillPath(gb, glow);
		}
		using (var ring = new Pen(Color.FromArgb(150, Imaging.Mix(c, Color.White, 0.3)), 1.2f))
		{
			g.DrawEllipse(ring, cx - rad - 3f, cx - rad - 3f, (rad + 3f) * 2f, (rad + 3f) * 2f);
		}
		using (var sphere = new GraphicsPath())
		{
			sphere.AddEllipse(cx - rad, cx - rad, rad * 2f, rad * 2f);
			using var sb = new PathGradientBrush(sphere)
			{
				CenterPoint = new PointF(cx - rad * 0.35f, cx - rad * 0.4f),
				CenterColor = Imaging.Mix(c, Color.White, 0.7),
				SurroundColors = new[] { Imaging.Mix(c, Color.Black, 0.35) }
			};
			g.FillPath(sb, sphere);
		}
		return _starOrbs[key] = bmp;
	}

	/// <summary>The golden core: a glossy sphere in a halo that beats with the overall load, ringed by turning crosses.</summary>
	private void StarSun(Graphics g, double t, bool alarm)
	{
		float load = (Val("cpu.load") + Val("gpu.load") + Val("ram.load")) / 300f;
		float beat = (float)(0.5 + 0.5 * Math.Sin(t * (1.2 + 4.0 * load)));
		Color tone = alarm ? StarRed : StarGold;
		using (var halo = new GraphicsPath())
		{
			float hr = 52f + 12f * beat;
			halo.AddEllipse(StarCx - hr, StarCy - hr, hr * 2f, hr * 2f);
			using var hb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb((int)(80 + 60 * beat), tone), SurroundColors = new[] { Color.FromArgb(0, tone) } };
			g.FillPath(hb, halo);
		}
		using (var plate = new SolidBrush(Color.FromArgb(150, 6, 14, 15)))
		{
			g.FillEllipse(plate, StarCx - 44f, StarCy - 44f, 88f, 88f);
		}
		using (var cross = new Pen(Color.FromArgb(150, 200, 210, 212), 1.6f))
		{
			for (int k = 0; k < 6; k++)
			{
				double a = -t * 0.3 + k * Math.PI / 3.0;
				float x = StarCx + 38f * (float)Math.Cos(a), y = StarCy + 38f * (float)Math.Sin(a);
				g.DrawLine(cross, x - 3.5f, y - 3.5f, x + 3.5f, y + 3.5f);
				g.DrawLine(cross, x - 3.5f, y + 3.5f, x + 3.5f, y - 3.5f);
			}
		}
		Bitmap sun = StarOrb(alarm ? Imaging.Mix(StarGold, StarRed, 0.6) : StarGold, 22);
		g.DrawImageUnscaled(sun, (int)(StarCx - sun.Width / 2f), (int)(StarCy - sun.Height / 2f));
		DeckText(g, "System", 14f, StarText, StarCx, StarCy + 30f, StringAlignment.Center, "Segoe UI Semibold");
		StarPill(g, "LOAD " + (load * 100f).ToString("0") + "%", StarCx - 30f, StarCy + 50f, StringAlignment.Near, tone, 1f);
	}

	/// <summary>The inner ring of tan dots and drifting green motes, seen only in the ring view.</summary>
	private void StarInner(Graphics g, float fade, double t)
	{
		if (fade < 0.02f)
		{
			return;
		}
		using (var tan = new SolidBrush(Color.FromArgb((int)(150 * fade), 210, 184, 150)))
		{
			for (int k = 0; k < 72; k++)
			{
				double a = k * Math.PI * 2.0 / 72 + _starSpin * 0.5;
				g.FillEllipse(tan, StarCx + 330f * (float)Math.Cos(a) - 2.5f, StarCy + 86f * (float)Math.Sin(a) - 2.5f, 5f, 5f);
			}
		}
		for (int k = 0; k < 14; k++)
		{
			double a = Hash01(k * 5) * Math.PI * 2.0 - _starSpin * 0.8;
			float rr = 0.45f + 0.4f * (float)Hash01(k * 9);
			float x = StarCx + 250f * rr * (float)Math.Cos(a), y = StarCy + 70f * rr * (float)Math.Sin(a);
			Bitmap mote = StarOrb(Color.FromArgb(92, 214, 162), 3);
			using var ia = new ImageAttributes();
			ia.SetColorMatrix(new ColorMatrix { Matrix33 = fade });
			g.DrawImage(mote, new Rectangle((int)(x - mote.Width / 2f), (int)(y - mote.Height / 2f), mote.Width, mote.Height), 0, 0, mote.Width, mote.Height, GraphicsUnit.Pixel, ia);
		}
	}

	/// <summary>Processor load for the last two minutes as a waveform down the left edge, with the time in a pill.</summary>
	private void StarTimeline(Graphics g)
	{
		float[] h = _sensors.History("cpu.load");
		var pts = new List<PointF> { new PointF(0f, 24f) };
		for (int k = 0; k < h.Length; k++)
		{
			float y = 24f + k * (432f / Math.Max(1, h.Length - 1));
			pts.Add(new PointF(2f + Math.Clamp(h[k], 0f, 100f) / 100f * 34f, y));
		}
		pts.Add(new PointF(0f, 456f));
		if (pts.Count > 3)
		{
			using var wave = new SolidBrush(Color.FromArgb(70, 200, 210, 212));
			g.FillPolygon(wave, pts.ToArray());
		}
		string[] marks = { "−2m", "−90s", "−60s", "−30s", "now" };
		for (int k = 0; k < marks.Length; k++)
		{
			DeckText(g, marks[k], 10f, Color.FromArgb(120, StarText), 44f, 18f + k * 108f, StringAlignment.Near, "Segoe UI");
		}
		using (GraphicsPath pill = Imaging.Round(new RectangleF(10f, 226f, 66f, 26f), 13f))
		{
			using var fill = new SolidBrush(Color.FromArgb(220, 16, 30, 32));
			g.FillPath(fill, pill);
			using var pen = new Pen(Color.FromArgb(150, StarGold), 1.2f);
			g.DrawPath(pen, pill);
		}
		DeckText(g, DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture), 14f, StarText, 43f, 229f, StringAlignment.Center, "Segoe UI Semibold");
	}

	/// <summary>The title, the view and live state at the top, the legend and the live count at the foot.</summary>
	private void StarChrome(Graphics g, double t, float ms, bool alarm)
	{
		using (var logo = new Pen(StarGold, 1.6f))
		{
			g.DrawEllipse(logo, 96f, 14f, 30f, 30f);
		}
		DeckText(g, "✦", 15f, StarGold, 111f, 18f, StringAlignment.Center, "Segoe UI Symbol");
		DeckText(g, "LumaLi", 15f, StarText, 136f, 12f, StringAlignment.Near, "Segoe UI Semibold");
		DeckText(g, "System Constellation", 12f, Color.FromArgb(150, StarText), 136f, 31f, StringAlignment.Near, "Segoe UI");
		string view = ms < 0.5f ? "RING VIEW" : "NETWORK VIEW";
		bool on = t % 1.2 < 0.7;
		DeckText(g, (on ? "● " : "○ ") + "LIVE   ·   " + view + (alarm ? "   ·   ALERT" : ""), 12f, alarm ? StarRed : StarText, 1896f, 18f, StringAlignment.Far, "Segoe UI Semibold");
		// legend
		var legend = new RectangleF(88f, 394f, 360f, 72f);
		StarPanel(g, legend);
		DeckText(g, "LEGEND", 10f, Color.FromArgb(140, StarText), legend.X + 12f, legend.Y + 6f, StringAlignment.Near, "Segoe UI Semibold");
		for (int c = 0; c < StarCats.Length; c++)
		{
			float x = legend.X + 12f + (c % 3) * 116f, y = legend.Y + 24f + (c / 3) * 22f;
			Bitmap orb = StarOrb(StarCatColors[c], 5);
			g.DrawImageUnscaled(orb, (int)(x - 4f), (int)(y + 8f - orb.Height / 2f));
			int live = StarSensors.Count(q => q.Cat == c && StarOnline(q));
			DeckText(g, live + "  " + StarCats[c], 12f, StarText, x + 22f, y, StringAlignment.Near, "Segoe UI");
		}
		// live count and alerts
		var stats = new RectangleF(1472f, 394f, 360f, 72f);
		StarPanel(g, stats);
		int online = StarSensors.Count(StarOnline), alerts = StarSensors.Count(StarWarn);
		StarStat(g, "SENSORS LIVE", online + " / " + StarSensors.Length, online / (float)StarSensors.Length, stats.X + 12f, stats.Y + 8f, Color.FromArgb(92, 214, 162));
		StarStat(g, "ALERTS", alerts.ToString(), Math.Min(1f, alerts / 3f), stats.X + 12f, stats.Y + 38f, alerts > 0 ? StarRed : Color.FromArgb(120, StarText));
		// the glowing action button in the corner, breathing
		float beat = (float)(0.5 + 0.5 * Math.Sin(t * 2.0));
		using (var glow = new SolidBrush(Color.FromArgb((int)(40 + 50 * beat), 84, 204, 226)))
		{
			g.FillEllipse(glow, 1846f, 410f, 56f, 56f);
		}
		using (var button = new SolidBrush(Color.FromArgb(230, 14, 34, 36)))
		{
			g.FillEllipse(button, 1856f, 420f, 36f, 36f);
		}
		DeckText(g, "✦", 15f, Color.FromArgb(220, 160, 240, 250), 1874f, 427f, StringAlignment.Center, "Segoe UI Symbol");
	}

	private static void StarPanel(Graphics g, RectangleF r)
	{
		using GraphicsPath p = Imaging.Round(r, 8f);
		using var fill = new SolidBrush(Color.FromArgb(170, 10, 26, 28));
		g.FillPath(fill, p);
		using var pen = new Pen(Color.FromArgb(50, 120, 200, 210), 1f);
		g.DrawPath(pen, p);
	}

	private void StarStat(Graphics g, string label, string value, float f, float x, float y, Color c)
	{
		DeckText(g, label, 11f, Color.FromArgb(170, StarText), x, y + 2f, StringAlignment.Near, "Segoe UI Semibold");
		using (var track = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
		{
			g.FillRectangle(track, x + 110f, y + 9f, 170f, 4f);
		}
		using (var fill = new SolidBrush(c))
		{
			g.FillRectangle(fill, x + 110f, y + 9f, 170f * Math.Clamp(f, 0f, 1f), 4f);
		}
		DeckText(g, value, 12f, StarText, x + 336f, y + 1f, StringAlignment.Far, "Segoe UI Semibold");
	}
}
