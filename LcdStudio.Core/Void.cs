using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// The void: a sensor control room in LED dots. A triangular tunnel of glowing dots flies towards the viewer at the
/// centre, faster the busier the processor; in its mouth a scanner shows one sensor large, in dot-matrix digits with
/// its history as a dot graph, stepping through the channels and jumping to any that spikes or crosses its warning,
/// the tunnel surging each time it moves. Either side, eight channels each: a status light, a spaced label, the
/// reading in dot-matrix digits that re-scan when they change, and a meter of dots.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color VoidIce = Color.FromArgb(196, 240, 255), VoidPink = Color.FromArgb(255, 92, 128);

	private static readonly Color VoidGreen = Color.FromArgb(104, 214, 156);

	private const float VoidCx = 960f, VoidCy = 300f, VoidR0 = 270f, VoidQ = 1.12f;

	private const int VoidRings = 19, VoidPerEdge = 22;

	private sealed record VoidChannel(string Key, string Label, string Unit, string Format, float Max, float Warn, float Scale = 1f);

	private static readonly VoidChannel[] VoidChannels =
	{
		new("cpu.load", "CPU LOAD", "%", "0", 100f, 90f),
		new("cpu.busiest", "BUSIEST THREAD", "%", "0", 100f, 98f),
		new("cpu.clock", "CPU CLOCK", "GHz", "0.00", 6f, 0f, 0.001f),
		new("cpu.power", "CPU POWER", "W", "0", 200f, 0f),
		new("cpu.temp", "CPU TEMP", "°C", "0", 95f, 90f),
		new("ram.load", "MEMORY", "%", "0", 100f, 90f),
		new("ram.used", "MEMORY USED", "GB", "0.0", 0f, 0f),
		new("fans.rpm", "CASE FANS", "RPM", "0", 3000f, 0f),
		new("gpu.load", "GPU LOAD", "%", "0", 100f, 98f),
		new("gpu.temp", "GPU TEMP", "°C", "0", 95f, 83f),
		new("gpu.hotspot", "GPU HOT SPOT", "°C", "0", 110f, 100f),
		new("gpu.power", "GPU POWER", "W", "0", 450f, 0f),
		new("gpu.clock", "GPU CLOCK", "MHz", "0", 3500f, 0f),
		new("gpu.vram", "VIDEO MEMORY", "GB", "0.0", 32f, 0f),
		new("gpu.fan", "GPU FAN", "%", "0", 100f, 0f),
		new("net.down", "DOWNLOAD", "MB/s", "0.0", 0f, 0f)
	};

	// 5 × 7 dot-matrix glyphs; a point and a colon are a single column wide
	private static readonly Dictionary<char, string[]> VoidGlyphs = new Dictionary<char, string[]>
	{
		['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
		['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
		['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
		['3'] = new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
		['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
		['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
		['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
		['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
		['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
		['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
		['-'] = new[] { "000", "000", "000", "111", "000", "000", "000" },
		['.'] = new[] { "0", "0", "0", "0", "0", "0", "1" },
		[':'] = new[] { "0", "1", "0", "0", "0", "1", "0" }
	};

	private double _voidPhase, _voidAt = double.NaN;

	private readonly Dictionary<(int, int, int), Bitmap> _voidDots = new Dictionary<(int, int, int), Bitmap>();

	private readonly Dictionary<string, Bitmap> _voidLed = new Dictionary<string, Bitmap>();

	private sealed class VoidState
	{
		public string Shown = "";
		public Bitmap? Old, Now;
		public double Since = -10.0;
		public float Last = float.NaN;
		public double LastAt = double.NaN;
	}

	private readonly Dictionary<string, VoidState> _voidStates = new Dictionary<string, VoidState>();

	private int _voidFocus;

	private double _voidFocusSince = -10.0, _voidJumpAt = -10.0, _voidFocusAt = double.NaN;
	private bool _voidStill;

	private string _voidWhy = "SCAN";

	private Bitmap? _voidSpark;

	private string _voidSparkKey = "";

	private Bitmap? _voidConsole;

	private bool DrawVoid(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "voidtunnel" when w.Kind == WidgetKind.Panel:
			VoidScan(t);
			VoidTunnel(g, t);
			VoidHeart(g, t);
			return true;
		case "voidpanel" when w.Kind == WidgetKind.Panel:
			VoidScan(t);
			VoidConsole(g, w.Text == "right", t);
			return true;
		default:
			return DrawConstellation(g, w, r, t);
		}
	}

	// ---------- the channels and the scanner ----------

	private bool VoidOnline(VoidChannel c) => _sensors.Get(c.Key).HasValue && (c.Key == "net.down" || c.Key.EndsWith("fan") || Val(c.Key) > 0f);

	private float VoidMax(VoidChannel c)
	{
		if (c.Max > 0f)
		{
			return c.Max;
		}
		if (c.Key == "ram.used")
		{
			float load = Val("ram.load");
			return load > 1f ? Val("ram.used") / load * 100f : 64f;
		}
		float[] h = _sensors.History(c.Key);
		return Math.Max(1f, h.Length > 0 ? h.Max() * 1.1f : 1f);
	}

	private float VoidFraction(VoidChannel c) => Math.Clamp(Val(c.Key) * c.Scale / VoidMax(c), 0f, 1f);

	private bool VoidWarn(VoidChannel c) => c.Warn > 0f && Val(c.Key) * c.Scale >= c.Warn;

	private string VoidText(VoidChannel c) => _sensors.Get(c.Key) is float v ? (v * c.Scale).ToString(c.Format, CultureInfo.InvariantCulture) : "--";

	/// <summary>
	/// Moves the scanner once a frame: on to the next live channel every few seconds, but straight to any channel
	/// that crosses its warning or jumps by a sixth of its range, holding there a little longer.
	/// </summary>
	private void VoidScan(double t)
	{
		if (t == _voidFocusAt)
		{
			return;
		}
		// a still or a preview drawn now and then rather than a running animation: a move of the scanner shows its
		// reading whole, or the frame would catch the very start of the reveal and show the unit with no number
		_voidStill = double.IsNaN(_voidFocusAt) || t < _voidFocusAt || t - _voidFocusAt > 0.5;
		if (t < _voidFocusAt)
		{
			// the clock started again (a fresh preview): start the scanner afresh too
			_voidFocusSince = t;
			_voidJumpAt = -10.0;
		}
		_voidFocusAt = t;
		int jump = -1;
		string why = "";
		for (int i = 0; i < VoidChannels.Length; i++)
		{
			VoidChannel c = VoidChannels[i];
			VoidState st = VoidStateOf(c.Key);
			float v = Val(c.Key) * c.Scale;
			if (double.IsNaN(st.LastAt) || t < st.LastAt)
			{
				st.Last = v;
				st.LastAt = t;
				continue;
			}
			if (t - st.LastAt >= 1.0)
			{
				if (VoidOnline(c) && Math.Abs(v - st.Last) / VoidMax(c) > 0.16f && jump < 0)
				{
					jump = i;
					why = v > st.Last ? "SPIKE" : "DROP";
				}
				st.Last = v;
				st.LastAt = t;
			}
			if (VoidWarn(c) && i != _voidFocus && jump < 0 && t - _voidFocusSince > 3.0)
			{
				jump = i;
				why = "WARNING";
			}
		}
		if (jump >= 0 && jump != _voidFocus && t - _voidJumpAt > 2.5)
		{
			VoidFocus(jump, why, t);
			_voidJumpAt = t;
			return;
		}
		double hold = _voidWhy == "SCAN" ? 5.0 : 8.0;
		if (t - _voidFocusSince >= hold || t < _voidFocusSince || !VoidOnline(VoidChannels[_voidFocus]))
		{
			for (int step = 1; step <= VoidChannels.Length; step++)
			{
				int next = (_voidFocus + step) % VoidChannels.Length;
				if (VoidOnline(VoidChannels[next]))
				{
					VoidFocus(next, VoidWarn(VoidChannels[next]) ? "WARNING" : "SCAN", t);
					break;
				}
			}
		}
	}

	private void VoidFocus(int index, string why, double t)
	{
		_voidFocus = index;
		_voidWhy = why;
		_voidFocusSince = _voidStill ? t - 1.0 : t;
	}

	private VoidState VoidStateOf(string key)
	{
		if (!_voidStates.TryGetValue(key, out VoidState st))
		{
			_voidStates[key] = st = new VoidState();
		}
		return st;
	}

	// ---------- the tunnel ----------

	private void VoidTunnel(Graphics g, double t)
	{
		double dt = double.IsNaN(_voidAt) || t < _voidAt ? 0.0 : Math.Min(0.5, t - _voidAt);
		_voidAt = t;
		// cruising speed follows the processor; each move of the scanner sends a surge down the tunnel
		double surge = Math.Exp(-(t - _voidFocusSince) * 2.5);
		_voidPhase += dt * (0.1 + 1.1 * Val("cpu.load") / 100.0 + 3.0 * surge);
		float ph = (float)(_voidPhase % 1.0);
		VoidChannel focus = VoidChannels[_voidFocus];
		double f = DeckEase("void|focus", VoidFraction(focus), t);
		int tint = VoidWarn(focus) ? 7 : (int)Math.Round(Math.Pow(f, 1.5) * 6.0);
		float bright = 0.55f + 0.35f * (float)f + 0.25f * (float)surge;
		var corners = new[] { new PointF(0f, -1f), new PointF(0.866f, 0.5f), new PointF(-0.866f, 0.5f) };
		// drawn by Skia: every dot placed between pixels, sized and lit smoothly rather than in steps, its light added
		// to what is beneath, and faded in and out at the edges of the void instead of popping
		Color glowTint = VoidTint(tint), coreTint = Imaging.Mix(glowTint, Color.White, 0.7);
		SkiaDraw(g, new Rectangle(560, 0, 800, 480), c =>
		{
			using var halo = new SKPaint { BlendMode = SKBlendMode.Plus, ColorFilter = SKColorFilter.CreateBlendMode(SkiaKit.Sk(glowTint), SKBlendMode.Modulate) };
			using var core = new SKPaint { BlendMode = SKBlendMode.Plus, ColorFilter = SKColorFilter.CreateBlendMode(SkiaKit.Sk(coreTint), SKBlendMode.Modulate) };
			SKImage haloDot = VoidSprite(false), coreDot = VoidSprite(true);
			for (int i = 0; i < VoidRings; i++)
			{
				float depth = i + ph;
				float rad = VoidR0 * (float)Math.Pow(VoidQ, depth);
				float near = depth / VoidRings;
				float fade = i == 0 ? ph : 1f;
				// a band of light runs outward along the rings
				float wave = 0.75f + 0.25f * (float)Math.Sin(depth * 1.1 - t * 3.0);
				float level = Math.Min(1f, fade * bright * wave * (0.45f + 0.55f * (float)Math.Pow(near, 0.5)));
				if (level <= 0.005f)
				{
					continue;
				}
				float d = 1.6f + rad * 0.0068f, span = d * 1.4f;
				for (int e = 0; e < 3; e++)
				{
					PointF a = corners[e], b = corners[(e + 1) % 3];
					for (int k = 0; k < VoidPerEdge; k++)
					{
						float u = (k + 0.5f) / VoidPerEdge;
						float x = VoidCx + rad * (a.X + (b.X - a.X) * u), y = VoidCy + rad * (a.Y + (b.Y - a.Y) * u);
						float edge = Math.Clamp((x - 600f) / 40f, 0f, 1f) * Math.Clamp((1320f - x) / 40f, 0f, 1f);
						if (edge <= 0f || y < -10f || y > 490f)
						{
							continue;
						}
						var box = new SKRect(x - span, y - span, x + span, y + span);
						halo.Color = new SKColor(255, 255, 255, (byte)Math.Min(255f, 150f * level * edge));
						c.DrawImage(haloDot, box, SkiaKit.Linear, halo);
						core.Color = new SKColor(255, 255, 255, (byte)Math.Min(255f, (250f * level + 5f) * edge));
						c.DrawImage(coreDot, box, SkiaKit.Linear, core);
					}
				}
			}
		});
		using var mouth = new Pen(Color.FromArgb((int)(60 + 120 * surge), VoidTint(tint)), 1f);
		g.DrawPolygon(mouth, corners.Select(c => new PointF(VoidCx + VoidR0 * 0.97f * c.X, VoidCy + VoidR0 * 0.97f * c.Y)).ToArray());
	}

	/// <summary>The heart of the void: the time at the apex, then the scanned channel large with its history.</summary>
	private void VoidHeart(Graphics g, double t)
	{
		DateTime now = DateTime.Now;
		DeckText(g, now.ToString("HH:mm", CultureInfo.CurrentCulture), 30f, VoidIce, VoidCx, 98f, StringAlignment.Center, "Bahnschrift Light");
		DeckText(g, Spaced(now.ToString("d MMM", CultureInfo.CurrentCulture).ToUpperInvariant()), 11f, Color.FromArgb(170, VoidIce), VoidCx, 138f, StringAlignment.Center, "Bahnschrift Light");
		using (var pink = new SolidBrush(VoidPink))
		{
			g.FillRectangle(pink, VoidCx - 4f, 160f, 3f, 3f);
			g.FillRectangle(pink, VoidCx + 2f, 160f, 3f, 3f);
		}
		VoidChannel c = VoidChannels[_voidFocus];
		bool warn = VoidWarn(c);
		Color ink = warn ? VoidPink : VoidIce;
		double since = t - _voidFocusSince;
		// the heading: which channel, and why the scanner is here
		string index = (_voidFocus + 1).ToString("00") + " / " + VoidChannels.Length.ToString("00");
		Color tag = _voidWhy == "SCAN" ? Color.FromArgb(150, VoidIce) : VoidPink;
		DeckText(g, Spaced(_voidWhy) + "   " + index, 11f, tag, VoidCx, 178f, StringAlignment.Center, "Bahnschrift SemiLight");
		DeckText(g, Spaced(c.Label), 15f, ink, VoidCx, 198f, StringAlignment.Center, "Bahnschrift SemiLight");
		// the reading, revealed top to bottom when the scanner arrives
		Bitmap big = VoidLed(VoidText(c), 8, ink);
		Bitmap unit = TextImage(c.Unit, 18f, Color.FromArgb(200, ink), "Bahnschrift Light");
		float x = VoidCx - (big.Width + 6f + unit.Width) / 2f, y = 228f;
		float reveal = (float)Math.Clamp(since / 0.45, 0.0, 1.0);
		int shown = (int)(big.Height * reveal);
		if (shown > 0)
		{
			g.DrawImage(big, new Rectangle((int)x, (int)y, big.Width, shown), new Rectangle(0, 0, big.Width, shown), GraphicsUnit.Pixel);
		}
		if (reveal < 1f)
		{
			using var scan = new Pen(Color.FromArgb(220, VoidPink), 2f);
			g.DrawLine(scan, x - 8f, y + shown, x + big.Width + 8f, y + shown);
		}
		g.DrawImageUnscaled(unit, (int)(x + big.Width + 6f), (int)(y + big.Height - unit.Height + 4f));
		// its last half minute as a dot graph, the newest column brightest
		Bitmap spark = VoidSparkline(c);
		g.DrawImageUnscaled(spark, (int)(VoidCx - spark.Width / 2f), 312);
		// the room's state along the floor of the void
		int live = VoidChannels.Count(VoidOnline), warnings = VoidChannels.Count(VoidWarn);
		string state = live.ToString("00") + " / " + VoidChannels.Length.ToString("00") + " LIVE   ·   " + (warnings > 0 ? warnings + " WARNING" + (warnings > 1 ? "S" : "") : "ALL CLEAR") + "   ·   UP " + Resolve("{uptime}").ToUpperInvariant();
		DeckText(g, state, 11f, warnings > 0 ? VoidPink : Color.FromArgb(160, VoidIce), VoidCx, 390f, StringAlignment.Center, "Bahnschrift SemiLight");
	}

	private Bitmap VoidSparkline(VoidChannel c)
	{
		float[] h = _sensors.History(c.Key);
		const int cols = 34, rows = 7, pitch = 7;
		string key = $"{c.Key}|{(h.Length > 0 ? h[^1] : 0f)}|{(h.Length > 1 ? h[^2] : 0f)}|{VoidWarn(c)}";
		if (_voidSpark != null && _voidSparkKey == key)
		{
			return _voidSpark;
		}
		_voidSpark?.Dispose();
		_voidSpark = new Bitmap(cols * pitch + 4, rows * pitch + 4, PixelFormat.Format32bppPArgb);
		_voidSparkKey = key;
		float max = VoidMax(c);
		Color on = VoidWarn(c) ? VoidPink : VoidGreen;
		using Graphics g = Graphics.FromImage(_voidSpark);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		using var off = new SolidBrush(Color.FromArgb(28, VoidIce));
		for (int col = 0; col < cols; col++)
		{
			int j = cols - 1 - col;
			float v = j < h.Length ? h[^(j + 1)] * c.Scale : 0f;
			int lit = (int)Math.Round(Math.Clamp(v / max, 0f, 1f) * rows);
			float age = j / (float)cols;
			using var bright = new SolidBrush(Color.FromArgb((int)(255 * (1f - age * 0.75f)), j == 0 ? Imaging.Mix(on, Color.White, 0.5) : on));
			for (int row = 0; row < rows; row++)
			{
				float cx = 2f + col * pitch + pitch / 2f, cy = 2f + (rows - 1 - row) * pitch + pitch / 2f;
				g.FillEllipse(row < lit ? bright : off, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);
			}
		}
		return _voidSpark;
	}

	// ---------- the consoles ----------

	/// <summary>Eight channels in two columns of four, on black that fades out towards the tunnel.</summary>
	private void VoidConsole(Graphics g, bool right, double t)
	{
		_voidConsole ??= VoidConsoleBase();
		var area = right ? new Rectangle(1300, 0, 620, 480) : new Rectangle(0, 0, 620, 480);
		g.DrawImage(_voidConsole, area, area, GraphicsUnit.Pixel);
		int first = right ? 8 : 0;
		for (int i = 0; i < 8; i++)
		{
			float x = (right ? 1330f : 24f) + (i / 4) * 300f, y = 14f + (i % 4) * 117f;
			VoidChannelCell(g, first + i, x, y, t);
		}
	}

	private static Bitmap VoidConsoleBase()
	{
		var bmp = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		foreach (bool right in new[] { false, true })
		{
			var body = right ? new RectangleF(1300f, 0f, 620f, 480f) : new RectangleF(0f, 0f, 620f, 480f);
			using (var fill = new LinearGradientBrush(new RectangleF(body.X - 1f, 0f, body.Width + 2f, 480f), Color.Black, Color.Black, 0f))
			{
				fill.InterpolationColors = new ColorBlend
				{
					Colors = right ? new[] { Color.FromArgb(0, 0, 0, 0), Color.Black, Color.Black } : new[] { Color.Black, Color.Black, Color.FromArgb(0, 0, 0, 0) },
					Positions = right ? new[] { 0f, 0.07f, 1f } : new[] { 0f, 0.93f, 1f }
				};
				g.FillRectangle(fill, body);
			}
			// a faint LED board behind the channels
			using var dot = new SolidBrush(Color.FromArgb(22, 220, 235, 245));
			for (float x = body.X + (right ? 36f : 6f); x < body.Right - (right ? 0f : 30f); x += 10f)
			{
				for (float y = 5f; y < 480f; y += 10f)
				{
					g.FillEllipse(dot, x - 0.8f, y - 0.8f, 1.6f, 1.6f);
				}
			}
		}
		return bmp;
	}

	/// <summary>
	/// One channel: a status light (green live, pink warning, dim offline), the spaced label and number, the reading
	/// in dot-matrix digits that re-scan left to right when it changes, and a 28-dot meter; brackets mark the scanned one.
	/// </summary>
	private void VoidChannelCell(Graphics g, int index, float x, float y, double t)
	{
		VoidChannel c = VoidChannels[index];
		bool online = VoidOnline(c), warn = VoidWarn(c), focused = index == _voidFocus;
		Color ink = !online ? Color.FromArgb(90, VoidIce) : warn ? VoidPink : VoidIce;
		// the status light, blinking on a warning
		Color led = !online ? Color.FromArgb(60, VoidIce) : warn ? (t * 2.0 % 1.0 < 0.55 ? VoidPink : Color.FromArgb(60, VoidPink)) : VoidGreen;
		using (var halo = new SolidBrush(Color.FromArgb(online ? 60 : 0, led)))
		{
			g.FillEllipse(halo, x - 1f, y + 3f, 12f, 12f);
		}
		using (var core = new SolidBrush(led))
		{
			g.FillEllipse(core, x + 2f, y + 6f, 6f, 6f);
		}
		DeckText(g, Spaced(c.Label), 12f, Color.FromArgb(focused ? 255 : 200, ink), x + 18f, y, StringAlignment.Near, "Bahnschrift SemiLight");
		DeckText(g, (index + 1).ToString("00"), 11f, Color.FromArgb(110, VoidIce), x + 268f, y + 1f, StringAlignment.Far, "Bahnschrift Light");
		// the reading: when it changes the new digits scan in over the old behind a bright line
		VoidState st = VoidStateOf(c.Key);
		string text = online ? VoidText(c) : "--";
		Color digits = focused ? Imaging.Mix(ink, Color.White, 0.3) : ink;
		Bitmap led5 = VoidLed(text, 5, digits);
		if (text != st.Shown)
		{
			st.Old = st.Now;
			st.Since = st.Shown.Length == 0 ? -10.0 : t;
			st.Shown = text;
		}
		st.Now = led5;
		float vy = y + 26f;
		double since = t - st.Since;
		if (since < 0.4 && st.Old != null && _voidLed.ContainsValue(st.Old))
		{
			int scan = (int)(Math.Max(led5.Width, st.Old.Width) * since / 0.4);
			int keep = Math.Min(scan, led5.Width);
			if (keep > 0)
			{
				g.DrawImage(led5, new Rectangle((int)x, (int)vy, keep, led5.Height), new Rectangle(0, 0, keep, led5.Height), GraphicsUnit.Pixel);
			}
			if (scan < st.Old.Width)
			{
				g.DrawImage(st.Old, new Rectangle((int)x + scan, (int)vy, st.Old.Width - scan, st.Old.Height), new Rectangle(scan, 0, st.Old.Width - scan, st.Old.Height), GraphicsUnit.Pixel);
			}
			using var line = new Pen(Color.FromArgb(230, VoidPink), 2f);
			g.DrawLine(line, x + scan, vy - 3f, x + scan, vy + led5.Height + 3f);
		}
		else
		{
			g.DrawImageUnscaled(led5, (int)x, (int)vy);
		}
		DeckText(g, c.Unit, 15f, Color.FromArgb(online ? 190 : 80, ink), x + led5.Width + 8f, vy + led5.Height - 20f, StringAlignment.Near, "Bahnschrift Light");
		// the meter
		double f = online ? DeckEase("void|meter|" + c.Key, VoidFraction(c), t) : 0.0;
		float lit = (float)(f * 28.0);
		int tint = warn ? 7 : 0;
		for (int k = 0; k < 28; k++)
		{
			float on = Math.Clamp(lit - k, 0f, 1f);
			int alpha = on > 0f ? Math.Max(2, (int)Math.Round(on * (k < lit - 1f ? 7f : 10f))) : 1;
			Bitmap dot = VoidDot(7, alpha, k / 28f > 0.85f && on > 0f ? Math.Max(tint, 5) : tint);
			g.DrawImageUnscaled(dot, (int)(x + 4f + k * 9.5f - dot.Width / 2f), (int)(y + 88f - dot.Height / 2f));
		}
		if (focused)
		{
			// brackets round the scanned channel, closing in as it arrives
			float pull = (float)Math.Max(0.0, 1.0 - (t - _voidFocusSince) / 0.35) * 12f;
			var box = new RectangleF(x - 10f - pull, y - 6f - pull, 290f + pull * 2f, 106f + pull * 2f);
			using var pen = new Pen(VoidPink, 1.6f);
			const float arm = 12f;
			g.DrawLines(pen, new[] { new PointF(box.X, box.Y + arm), new PointF(box.X, box.Y), new PointF(box.X + arm, box.Y) });
			g.DrawLines(pen, new[] { new PointF(box.Right - arm, box.Y), new PointF(box.Right, box.Y), new PointF(box.Right, box.Y + arm) });
			g.DrawLines(pen, new[] { new PointF(box.X, box.Bottom - arm), new PointF(box.X, box.Bottom), new PointF(box.X + arm, box.Bottom) });
			g.DrawLines(pen, new[] { new PointF(box.Right - arm, box.Bottom), new PointF(box.Right, box.Bottom), new PointF(box.Right, box.Bottom - arm) });
		}
	}

	// ---------- dots ----------

	/// <summary>Text in 5 × 7 dot-matrix digits: every dot of each glyph shown faintly, the lit ones glowing.</summary>
	private Bitmap VoidLed(string text, int pitch, Color on)
	{
		string key = $"{text}|{pitch}|{on.ToArgb()}";
		if (_voidLed.TryGetValue(key, out Bitmap bmp))
		{
			return bmp;
		}
		if (_voidLed.Count > 500)
		{
			foreach (Bitmap old in _voidLed.Values)
			{
				old.Dispose();
			}
			_voidLed.Clear();
		}
		var glyphs = text.Select(ch => VoidGlyphs.TryGetValue(ch, out string[] gl) ? gl : VoidGlyphs['-']).ToArray();
		int cols = glyphs.Sum(gl => gl[0].Length + 1) - 1;
		bmp = new Bitmap(Math.Max(1, cols * pitch + 4), 7 * pitch + 4, PixelFormat.Format32bppPArgb);
		using (Graphics g = Graphics.FromImage(bmp))
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			float d = pitch * 0.72f;
			using var dim = new SolidBrush(Color.FromArgb(26, on));
			using var halo = new SolidBrush(Color.FromArgb(55, on));
			using var core = new SolidBrush(Imaging.Mix(on, Color.White, 0.25));
			int col0 = 0;
			foreach (string[] gl in glyphs)
			{
				for (int row = 0; row < 7; row++)
				{
					for (int col = 0; col < gl[row].Length; col++)
					{
						float cx = 2f + (col0 + col) * pitch + pitch / 2f, cy = 2f + row * pitch + pitch / 2f;
						if (gl[row][col] == '1')
						{
							g.FillEllipse(halo, cx - d * 0.75f, cy - d * 0.75f, d * 1.5f, d * 1.5f);
							g.FillEllipse(core, cx - d / 2f, cy - d / 2f, d, d);
						}
						else
						{
							g.FillEllipse(dim, cx - d / 2f, cy - d / 2f, d, d);
						}
					}
				}
				col0 += gl[0].Length + 1;
			}
		}
		return _voidLed[key] = bmp;
	}

	private static Color VoidTint(int step) => Imaging.Mix(VoidIce, VoidPink, step / 7.0);

	private static string Spaced(string s) => string.Join(" ", s.ToCharArray()).Replace("   ", "     ");

	private static SKImage? _voidHalo, _voidCore;

	/// <summary>
	/// A white dot for Skia to tint, scale and fade: the soft halo, or the bright core, each filling a square whose
	/// half-width is 1.4 times the dot's size.
	/// </summary>
	private static SKImage VoidSprite(bool core)
	{
		ref SKImage? slot = ref core ? ref _voidCore : ref _voidHalo;
		if (slot == null)
		{
			using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul));
			SKCanvas c = surface.Canvas;
			c.Clear(SKColors.Transparent);
			// a dot of size 1 has its halo out to 1.1 and its core out to 0.45, on a square of half-width 1.4
			float unit = 32f / 1.4f;
			var colors = core
				? new[] { SKColors.White, SKColors.White, new SKColor(255, 255, 255, 0) }
				: new[] { new SKColor(255, 255, 255, 255), new SKColor(255, 255, 255, 170), new SKColor(255, 255, 255, 0) };
			float[] stops = core ? new[] { 0f, 0.75f, 1f } : new[] { 0f, 0.55f, 1f };
			using var shader = SKShader.CreateRadialGradient(new SKPoint(32f, 32f), unit * (core ? 0.5f : 1.25f), colors, stops, SKShaderTileMode.Clamp);
			using var paint = new SKPaint { Shader = shader, IsAntialias = true };
			c.DrawRect(new SKRect(0f, 0f, 64f, 64f), paint);
			slot = surface.Snapshot();
		}
		return slot;
	}

	/// <summary>A glowing LED dot: a soft halo round a bright core, cached by size, brightness and tint.</summary>
	private Bitmap VoidDot(int twice, int alpha, int tint)
	{
		var key = (twice, alpha, tint);
		if (_voidDots.TryGetValue(key, out Bitmap bmp))
		{
			return bmp;
		}
		float d = twice / 2f;
		int size = (int)Math.Ceiling(d * 2.6f) + 2;
		bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		Color c = VoidTint(tint);
		float cx = size / 2f;
		using (var halo = new SolidBrush(Color.FromArgb((int)(alpha * 4.5), c)))
		{
			g.FillEllipse(halo, cx - d * 1.1f, cx - d * 1.1f, d * 2.2f, d * 2.2f);
		}
		using (var core = new SolidBrush(Color.FromArgb(Math.Min(255, alpha * 25 + 5), Imaging.Mix(c, Color.White, 0.7))))
		{
			g.FillEllipse(core, cx - d * 0.45f, cx - d * 0.45f, d * 0.9f, d * 0.9f);
		}
		return _voidDots[key] = bmp;
	}
}
