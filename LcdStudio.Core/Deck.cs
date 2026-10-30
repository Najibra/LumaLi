using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

/// <summary>
/// The deck: dark glass cards on the prism page, each a live instrument. Text is "hero" (warm prism glass with the
/// time, the system's mood and the date) or a card: "cpu", "gpu", "mem", "therm" or "net". The busiest card is in the
/// spotlight, a light running round its rim; a card glows red while its part runs hot; big numbers are slab letters
/// that drop in as they change.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color DeckInk = Color.FromArgb(236, 231, 218), DeckDim = Color.FromArgb(120, 126, 142), DeckFaint = Color.FromArgb(30, 34, 46);

	private static readonly Color DeckBlue = Color.FromArgb(74, 140, 255), DeckAmber = Color.FromArgb(255, 164, 82), DeckRed = Color.FromArgb(255, 77, 94), DeckGreen = Color.FromArgb(76, 227, 154);

	private readonly Dictionary<string, Bitmap> _deckText = new Dictionary<string, Bitmap>();

	private readonly Dictionary<(int, int, string), Bitmap> _deckChrome = new Dictionary<(int, int, string), Bitmap>();

	private readonly Dictionary<string, Widget> _deckSlabs = new Dictionary<string, Widget>();

	private readonly Dictionary<string, (double Shown, double At)> _deckEase = new Dictionary<string, (double, double)>();

	private readonly Dictionary<string, (double Peak, double At, double Last)> _deckPeak = new Dictionary<string, (double, double, double)>();

	private readonly Dictionary<(int, int), PointF[]> _deckRims = new Dictionary<(int, int), PointF[]>();

	private readonly Dictionary<string, double> _activity = new Dictionary<string, double>();

	private string _focus = "";

	private double _focusSince, _focusAt = double.NaN;

	private bool DrawDeck(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect == "deck" && w.Kind == WidgetKind.Panel)
		{
			Card(g, w, r, t);
			return true;
		}
		return DrawInstrument(g, w, r, t);
	}

	private void Card(Graphics g, Widget w, RectangleF r, double t)
	{
		string kind = w.Text;
		var key = (w.X, w.Y, kind);
		if (!_deckChrome.TryGetValue(key, out Bitmap chrome) || chrome.Width != w.W || chrome.Height != w.H)
		{
			chrome?.Dispose();
			_deckChrome[key] = chrome = kind == "hero" ? HeroChrome(w.W, w.H) : CardChrome(kind, w.W, w.H);
		}
		g.DrawImageUnscaled(chrome, w.X, w.Y);
		if (kind != "hero")
		{
			Focus(t);
			bool hot = kind switch
			{
				"gpu" => Val("gpu.temp") >= 83f,
				"therm" => Val("gpu.temp") >= 83f || Val("cpu.temp") >= 90f,
				"cpu" => Val("cpu.temp") >= 90f,
				"mem" => Val("ram.load") >= 90f,
				_ => false
			};
			if (hot)
			{
				using var red = new Pen(Color.FromArgb((int)(120 + 100 * Math.Sin(t * 5.0)), DeckRed), 2.5f);
				using GraphicsPath edge = Imaging.Round(new RectangleF(r.X + 1f, r.Y + 1f, r.Width - 2f, r.Height - 2f), 18f);
				g.DrawPath(red, edge);
			}
			else if (_focus == kind)
			{
				Spotlight(g, r, t);
			}
		}
		switch (kind)
		{
		case "hero":
			HeroCard(g, r, t);
			break;
		case "cpu":
			CpuCard(g, r, t);
			break;
		case "gpu":
			GpuCard(g, r, t);
			break;
		case "mem":
			MemCard(g, r, t);
			break;
		case "therm":
			ThermCard(g, r, t);
			break;
		case "net":
			NetCard(g, r, t);
			break;
		}
	}

	// ---------- the spotlight ----------

	/// <summary>Picks the busiest part of the machine, once a frame, holding on to it until another is clearly busier.</summary>
	private void Focus(double t)
	{
		if (t == _focusAt)
		{
			return;
		}
		double dt = double.IsNaN(_focusAt) ? 10.0 : Math.Clamp(t - _focusAt, 0.0, 2.0);
		_focusAt = t;
		var raw = new Dictionary<string, double>
		{
			["cpu"] = Val("cpu.load") / 100.0,
			["gpu"] = Val("gpu.load") / 100.0,
			["mem"] = Math.Clamp((Val("ram.load") - 30.0) / 60.0, 0.0, 1.0),
			["therm"] = Math.Clamp((Math.Max(Val("cpu.temp"), Val("gpu.temp")) - 35.0) / 55.0, 0.0, 1.0),
			["net"] = Math.Clamp((Val("net.down") + Val("net.up")) / 20.0, 0.0, 1.0)
		};
		foreach (var (k, v) in raw)
		{
			_activity[k] = _activity.TryGetValue(k, out double a) ? a + (v - a) * (1.0 - Math.Exp(-dt * 1.5)) : v;
		}
		string best = _activity.OrderByDescending(p => p.Value).First().Key;
		double current = _focus.Length > 0 ? _activity[_focus] : -1.0;
		if (best != _focus && _activity[best] > current + 0.06)
		{
			_focus = best;
			_focusSince = t;
		}
	}

	/// <summary>A soft glow round the card's rim and a comet of light running round it.</summary>
	private void Spotlight(Graphics g, RectangleF r, double t)
	{
		float fadeIn = (float)Math.Clamp((t - _focusSince) * 1.5, 0.0, 1.0);
		using GraphicsPath edge = Imaging.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f), 18f);
		using (var glow = new Pen(Color.FromArgb((int)((70 + 40 * Math.Sin(t * 2.0)) * fadeIn), DeckBlue), 4f))
		{
			g.DrawPath(glow, edge);
		}
		PointF[] rim = Rim((int)r.Width, (int)r.Height);
		int n = rim.Length, head = (int)(t * 0.22 % 1.0 * n);
		const int tail = 46;
		for (int k = 0; k < tail; k++)
		{
			PointF a = rim[((head - k) % n + n) % n], b = rim[((head - k - 1) % n + n) % n];
			float f = 1f - k / (float)tail;
			using var pen = new Pen(Color.FromArgb((int)(240 * f * f * fadeIn), Imaging.Mix(DeckBlue, Color.White, 0.3 + 0.7 * f)), 1.5f + 2f * f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawLine(pen, r.X + a.X, r.Y + a.Y, r.X + b.X, r.Y + b.Y);
		}
		DeckText(g, "● FOCUS", 12f, Color.FromArgb((int)(255 * fadeIn), DeckBlue), r.Right - 22f, r.Y + 24f, StringAlignment.Far);
	}

	/// <summary>Evenly spaced points round a card's rounded outline.</summary>
	private PointF[] Rim(int w, int h)
	{
		if (_deckRims.TryGetValue((w, h), out PointF[] pts))
		{
			return pts;
		}
		using GraphicsPath p = Imaging.Round(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), 18f);
		p.Flatten();
		PointF[] raw = p.PathPoints;
		var lengths = new float[raw.Length + 1];
		for (int i = 1; i <= raw.Length; i++)
		{
			PointF a = raw[i - 1], b = raw[i % raw.Length];
			lengths[i] = lengths[i - 1] + (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
		}
		var list = new List<PointF>();
		float total = lengths[^1], step = total / 300f;
		int seg = 1;
		for (float d = 0f; d < total; d += step)
		{
			while (seg < raw.Length && lengths[seg] < d)
			{
				seg++;
			}
			PointF a = raw[seg - 1], b = raw[seg % raw.Length];
			float f = (d - lengths[seg - 1]) / Math.Max(0.001f, lengths[seg] - lengths[seg - 1]);
			list.Add(new PointF(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f));
		}
		return _deckRims[(w, h)] = list.ToArray();
	}

	// ---------- the cards ----------

	private void HeroCard(Graphics g, RectangleF r, double t)
	{
		float busy = Math.Max(Val("cpu.load"), Val("gpu.load"));
		bool hot = Val("gpu.temp") >= 83f || Val("cpu.temp") >= 90f;
		(string mood, Color dot) = hot ? ("Running hot", DeckRed) : busy < 15f ? ("All systems calm", DeckGreen) : busy < 50f ? ("Working steadily", DeckBlue) : busy < 85f ? ("Working hard", DeckAmber) : ("Flat out", DeckRed);
		// the dot beats faster the busier the machine
		float beat = (float)(0.5 + 0.5 * Math.Sin(t * (2.0 + busy / 12.0)));
		using (var halo = new SolidBrush(Color.FromArgb((int)(40 + 80 * beat), dot)))
		{
			g.FillEllipse(halo, r.X + 24f, r.Y + 113f, 14f, 14f);
		}
		using (var core = new SolidBrush(dot))
		{
			g.FillEllipse(core, r.X + 27.5f, r.Y + 116.5f, 7f, 7f);
		}
		DeckText(g, mood, 17f, DeckInk, r.X + 46f, r.Y + 109f);
		Slabs(g, DeckSlab(r, "time", 22, 144, (int)r.Width - 40, 164, "cpu.load", WidgetKind.Text, "{time:HH:mm}", "left"), SlabRect(r, 22, 144, (int)r.Width - 40, 164), t);
		DeckText(g, Resolve("{date:dddd d MMMM}"), 20f, DeckInk, r.X + 24f, r.Y + 318f);
		DeckText(g, "UP  " + Resolve("{uptime}"), 13f, Color.FromArgb(200, DeckInk), r.X + 24f, r.Y + 348f, StringAlignment.Near, "Bahnschrift");
	}

	private void CpuCard(Graphics g, RectangleF r, double t)
	{
		float load = Val("cpu.load");
		string state = load < 10f ? "IDLE" : load < 35f ? "LIGHT" : load < 65f ? "BUSY" : load < 90f ? "HEAVY" : "MAXED";
		// the card heats up with the processor: 0 calm, 1 busy (amber), 2 heavy (orange), 3 maxed (red), blended smoothly
		int level = load < 35f ? 0 : load < 65f ? 1 : load < 90f ? 2 : 3;
		float heat = (float)DeckEase($"{r.X}|heat", level, t);
		Color hc = HeatColor(heat);
		float pulse = (float)(0.5 + 0.5 * Math.Sin(t * (2.0 + heat * 3.2)));
		if (heat > 0.03f)
		{
			GraphicsState s = g.Save();
			g.SetClip(new RectangleF(r.X + 2f, r.Y + 2f, r.Width - 4f, r.Height - 4f));
			float strength = Math.Min(1f, heat);
			HeatGlow(g, new RectangleF(r.X - 60f, r.Y + 10f, 330f, 190f), Color.FromArgb((int)((40 + 60 * pulse) * strength), hc));
			HeatGlow(g, new RectangleF(r.X - 20f, r.Y + 150f, r.Width + 40f, 200f), Color.FromArgb((int)((20 + 40 * pulse) * strength * Math.Min(1f, heat / 2f)), hc));
			g.Restore(s);
		}
		Widget word = DeckSlab(r, "state", 12, 50, 186, 100, "cpu.load", WidgetKind.Text, "state", "left");
		// in quarter steps, so the letters are only rebuilt a few times as the colour shifts
		float stepped = (float)Math.Round(heat * 4f) / 4f;
		word.Color = Imaging.Hex(stepped == 0f ? DeckInk : Imaging.Mix(DeckInk, HeatColor(stepped), Math.Min(1f, stepped) * 0.85));
		Slabs(g, word, SlabRect(r, 12, 50, 186, 100), t, state);
		DeckText(g, Fmt("cpu.load", "0") + "%", 34f, level == 0 ? DeckInk : hc, r.Right - 22f, r.Y + 66f, StringAlignment.Far);
		DeckText(g, "LOAD", 12f, DeckDim, r.Right - 22f, r.Y + 108f, StringAlignment.Far, "Bahnschrift");
		var wave = new RectangleF(r.X + 22f, r.Y + 166f, r.Width - 44f, 158f);
		Waveform(g, wave, "cpu.load", t, heat, hc);
		if (heat > 0.6f)
		{
			Embers(g, wave, heat, hc, t);
		}
		if (heat > 1.4f)
		{
			// a rim that throbs with the heat
			using var rim = new Pen(Color.FromArgb((int)((50 + 110 * pulse) * Math.Min(1f, (heat - 1.4f) / 1.6f)), hc), 2f);
			using GraphicsPath edge = Imaging.Round(new RectangleF(r.X + 1f, r.Y + 1f, r.Width - 2f, r.Height - 2f), 18f);
			g.DrawPath(rim, edge);
		}
		Divider(g, r, 342f);
		float col = (r.Width - 44f) / 3f;
		float clock = Val("cpu.clock");
		Stat(g, "CLOCK", clock > 0f ? (clock / 1000f).ToString("0.0", CultureInfo.InvariantCulture) : "--", "GHz", r.X + 22f, r.Y + 356f);
		Stat(g, "TEMP", Fmt("cpu.temp", "0"), "°", r.X + 22f + col, r.Y + 356f);
		Stat(g, "BUSIEST", Fmt("cpu.busiest", "0"), "%", r.X + 22f + col * 2f, r.Y + 356f);
	}

	private void GpuCard(Graphics g, RectangleF r, double t)
	{
		Slabs(g, DeckSlab(r, "load", 12, 48, (int)r.Width - 24, 126, "gpu.load", WidgetKind.Value, "", "left"), SlabRect(r, 12, 48, (int)r.Width - 24, 126), t);
		string name = _sensors.Text("gpu.name") ?? "Graphics";
		DeckText(g, name.Length > 30 ? name.Substring(0, 30) : name, 13f, DeckDim, r.X + 22f, r.Y + 178f, StringAlignment.Near, "Bahnschrift");
		double f = DeckEase($"{r.X}|gpu", Val("gpu.load") / 100.0, t);
		Ticks(g, new RectangleF(r.X + 22f, r.Y + 206f, r.Width - 44f, 74f), f, Peak($"{r.X}|gpu", f, t));
		float col = (r.Width - 44f) / 2f;
		Stat(g, "CLOCK", Fmt("gpu.clock", "0"), "MHz", r.X + 22f, r.Y + 294f);
		Stat(g, "VRAM", Fmt("gpu.vram", "0.0"), "GB", r.X + 22f + col, r.Y + 294f);
		Divider(g, r, 356f);
		DeckText(g, "POWER", 12f, DeckDim, r.X + 22f, r.Y + 370f, StringAlignment.Near, "Bahnschrift");
		DeckText(g, Fmt("gpu.power", "0") + " W", 32f, DeckInk, r.X + 22f, r.Y + 386f);
		float temp = Val("gpu.temp");
		Pill(g, Fmt("gpu.temp", "0") + "°C", temp >= 83f ? DeckRed : temp >= 70f ? DeckAmber : DeckGreen, r.Right - 22f, r.Y + 396f, StringAlignment.Far);
	}

	private void MemCard(Graphics g, RectangleF r, double t)
	{
		float load = Val("ram.load");
		double f = DeckEase($"{r.X}|mem", load / 100.0, t);
		float cx = r.X + 100f, cy = r.Y + 188f, rad = 74f;
		// rings within rings, the inner ones turning faster the fuller the memory
		using (var track = new Pen(DeckFaint, 10f))
		{
			g.DrawArc(track, cx - rad, cy - rad, rad * 2f, rad * 2f, 135f, 270f);
		}
		if (f > 0.002)
		{
			using var sweep = new LinearGradientBrush(new RectangleF(cx - rad - 6f, cy - rad, rad * 2f + 12f, rad * 2f), DeckBlue, DeckAmber, 0f)
			{
				InterpolationColors = new ColorBlend { Colors = new[] { DeckBlue, Color.FromArgb(170, 210, 255), Color.FromArgb(255, 252, 244), DeckAmber }, Positions = new[] { 0f, 0.4f, 0.7f, 1f } }
			};
			using var arc = new Pen(sweep, 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawArc(arc, cx - rad, cy - rad, rad * 2f, rad * 2f, 135f, (float)(270.0 * f));
		}
		double spin = t * (18.0 + 90.0 * f);
		using (var dash = new Pen(Color.FromArgb(150, DeckBlue), 2f) { DashPattern = new[] { 2f, 5f } })
		{
			g.DrawArc(dash, cx - rad + 16f, cy - rad + 16f, (rad - 16f) * 2f, (rad - 16f) * 2f, (float)(spin % 360.0), 290f);
		}
		using (var inner = new Pen(Color.FromArgb(110, 170, 210, 255), 1.4f))
		{
			g.DrawArc(inner, cx - rad + 26f, cy - rad + 26f, (rad - 26f) * 2f, (rad - 26f) * 2f, (float)(-spin * 1.6 % 360.0), 120f);
			g.DrawArc(inner, cx - rad + 26f, cy - rad + 26f, (rad - 26f) * 2f, (rad - 26f) * 2f, (float)((-spin * 1.6 + 180.0) % 360.0), 120f);
		}
		DeckText(g, Fmt("ram.load", "0") + "%", 30f, DeckInk, cx, cy - 22f, StringAlignment.Center);
		DeckText(g, "IN USE", 11f, DeckDim, cx, cy + 14f, StringAlignment.Center, "Bahnschrift");
		float used = Val("ram.used");
		string total = load > 1f && used > 0f ? (used / load * 100f).ToString("0", CultureInfo.InvariantCulture) : "--";
		float lx = r.X + 194f;
		Legend(g, "USED", Fmt("ram.used", "0.0") + "G", lx, r.Y + 112f, DeckBlue);
		Legend(g, "TOTAL", total + "G", lx, r.Y + 164f, DeckFaint);
		Legend(g, "VRAM", Fmt("gpu.vram", "0.0") + "G", lx, r.Y + 216f, DeckAmber);
		Divider(g, r, 296f);
		DeckText(g, "PRESSURE", 12f, DeckDim, r.X + 22f, r.Y + 310f, StringAlignment.Near, "Bahnschrift");
		Pill(g, load < 70f ? "HEALTHY" : load < 85f ? "ELEVATED" : "CRITICAL", load < 70f ? DeckGreen : load < 85f ? DeckAmber : DeckRed, r.Right - 22f, r.Y + 304f, StringAlignment.Far);
		Line(g, new RectangleF(r.X + 22f, r.Y + 350f, r.Width - 44f, 74f), "ram.load", 0f, 100f, DeckBlue, t, fill: true);
	}

	private void ThermCard(Graphics g, RectangleF r, double t)
	{
		// each part measured against the temperature where it starts to throttle
		var parts = new List<(string Label, string Key, float Limit)>();
		foreach (var p in new[] { ("GPU CORE", "gpu.temp", 83f), ("GPU HOT SPOT", "gpu.hotspot", 100f), ("CPU", "cpu.temp", 90f) })
		{
			// a reading of zero means the sensor is there but not reporting
			if (Val(p.Item2) > 1f)
			{
				parts.Add(p);
			}
		}
		float headroom = float.MaxValue;
		string closest = "";
		foreach (var p in parts)
		{
			float room = p.Limit - Val(p.Key);
			if (room < headroom)
			{
				headroom = room;
				closest = p.Label;
			}
		}
		bool any = parts.Count > 0;
		Color tone = !any ? DeckDim : headroom <= 5f ? DeckRed : headroom <= 15f ? DeckAmber : DeckGreen;
		Widget big = DeckSlab(r, "headroom", 20, 50, 140, 100, "gpu.temp", WidgetKind.Text, "headroom", "left");
		big.Color = Imaging.Hex(any ? Imaging.Mix(DeckInk, tone, 0.35) : DeckInk);
		Slabs(g, big, SlabRect(r, 20, 50, 140, 100), t, any ? Math.Max(0f, headroom).ToString("0", CultureInfo.InvariantCulture) + "°" : "--");
		DeckText(g, "HEADROOM", 14f, DeckInk, r.Right - 22f, r.Y + 64f, StringAlignment.Far);
		DeckText(g, "before throttling", 12f, DeckDim, r.Right - 22f, r.Y + 86f, StringAlignment.Far, "Bahnschrift");
		if (any)
		{
			Pill(g, closest, tone, r.Right - 22f, r.Y + 110f, StringAlignment.Far);
		}
		else
		{
			DeckText(g, "No temperature readings", 14f, DeckDim, r.X + 22f, r.Y + 190f, StringAlignment.Near, "Bahnschrift");
		}
		const float rowH = 62f;
		for (int i = 0; i < parts.Count; i++)
		{
			TempRow(g, new RectangleF(r.X + 22f, r.Y + 170f + i * rowH, r.Width - 44f, rowH), parts[i].Label, parts[i].Key, parts[i].Limit, t);
		}
		// whatever room the gauges leave holds the closest part's last two minutes
		float graphTop = r.Y + 172f + parts.Count * rowH;
		if (any && r.Y + 344f - graphTop >= 56f)
		{
			var lead = parts.First(p => p.Label == closest);
			TempHistory(g, new RectangleF(r.X + 22f, graphTop, r.Width - 44f, r.Y + 344f - graphTop), lead.Key, lead.Limit, t);
		}
		Divider(g, r, 356f);
		// how hard the fans are answering
		float col = (r.Width - 44f) / 2f;
		Stat(g, "GPU FAN", Fmt("gpu.fan", "0"), "%", r.X + 22f, r.Y + 372f);
		Stat(g, "CASE FANS", Fmt("fans.rpm", "0"), "RPM", r.X + 22f + col, r.Y + 372f);
	}

	/// <summary>
	/// A part's last two minutes as a glowing line, scaled to its own range so small swings show, with the high and
	/// low marked, a dashed throttle line when the limit is in view, and a pulsing dot at the reading.
	/// </summary>
	private void TempHistory(Graphics g, RectangleF r, string key, float limit, double t)
	{
		float min = float.MaxValue, max = float.MinValue;
		foreach (float x in _sensors.History(key))
		{
			if (x > 0f)
			{
				min = Math.Min(min, x);
				max = Math.Max(max, x);
			}
		}
		if (max < min)
		{
			return;
		}
		DeckText(g, "LAST 2 MIN", 11f, DeckDim, r.X, r.Y, StringAlignment.Near, "Bahnschrift");
		DeckText(g, "high " + max.ToString("0") + "°  ·  low " + min.ToString("0") + "°", 11f, DeckDim, r.Right, r.Y, StringAlignment.Far, "Bahnschrift");
		var plot = new RectangleF(r.X, r.Y + 22f, r.Width, r.Height - 26f);
		// at least ten degrees tall, so a steady temperature reads as steady rather than as noise
		float mid = (min + max) / 2f, span = Math.Max(10f, max - min + 4f);
		float lo = mid - span / 2f, hi = mid + span / 2f;
		using (var frame = new Pen(DeckFaint, 1f) { DashPattern = new[] { 3f, 4f } })
		{
			g.DrawLine(frame, plot.X, plot.Y, plot.Right, plot.Y);
			g.DrawLine(frame, plot.X, plot.Bottom, plot.Right, plot.Bottom);
		}
		if (limit <= hi)
		{
			float y = plot.Bottom - (limit - lo) / (hi - lo) * plot.Height;
			using var line = new Pen(Color.FromArgb(150, DeckRed), 1f) { DashPattern = new[] { 6f, 4f } };
			g.DrawLine(line, plot.X, y, plot.Right, y);
		}
		float room = limit - Val(key);
		Line(g, plot, key, lo, hi, room <= 5f ? DeckRed : room <= 15f ? DeckAmber : DeckBlue, t, fill: true);
	}

	/// <summary>
	/// One part's temperature on a bar running from cool to its throttle limit and a little past: the zones tinted
	/// green to red, the last two minutes' range shaded, a mark at the limit, and a glowing marker at the reading,
	/// with the half-minute trend beside the label.
	/// </summary>
	private void TempRow(Graphics g, RectangleF r, string label, string key, float limit, double t)
	{
		float v = Val(key), lo = 25f, hi = limit + 12f;
		float Pos(float x) => r.X + Math.Clamp((x - lo) / (hi - lo), 0f, 1f) * r.Width;
		float[] h = _sensors.History(key);
		float before = h.Length > 30 && h[^31] > 0f ? h[^31] : v, trend = v - before;
		Color tone = limit - v <= 5f ? DeckRed : limit - v <= 15f ? DeckAmber : DeckGreen;
		SizeF ls = DeckText(g, label, 12f, DeckDim, r.X, r.Y, StringAlignment.Near, "Bahnschrift");
		(string arrow, Color ac) = trend > 2f ? ("↑ " + trend.ToString("0", CultureInfo.InvariantCulture) + "°", DeckAmber) : trend < -2f ? ("↓ " + (-trend).ToString("0", CultureInfo.InvariantCulture) + "°", DeckBlue) : ("→ steady", DeckDim);
		DeckText(g, arrow, 12f, ac, r.X + ls.Width + 6f, r.Y, StringAlignment.Near, "Bahnschrift SemiBold");
		DeckText(g, v.ToString("0", CultureInfo.InvariantCulture) + "°", 22f, DeckInk, r.Right, r.Y - 7f, StringAlignment.Far);
		var bar = new RectangleF(r.X, r.Y + 24f, r.Width, 8f);
		using (GraphicsPath track = Imaging.Round(bar, 4f))
		{
			using var zones = new LinearGradientBrush(new RectangleF(bar.X - 1f, bar.Y, bar.Width + 2f, bar.Height), DeckGreen, DeckRed, 0f)
			{
				InterpolationColors = new ColorBlend
				{
					Colors = new[] { Color.FromArgb(40, DeckBlue), Color.FromArgb(40, DeckGreen), Color.FromArgb(55, DeckAmber), Color.FromArgb(80, DeckRed) },
					Positions = new[] { 0f, (Pos(limit - 25f) - bar.X) / bar.Width, (Pos(limit - 8f) - bar.X) / bar.Width, 1f }
				}
			};
			g.FillPath(zones, track);
		}
		// where it has been over the last two minutes
		float min = float.MaxValue, max = float.MinValue;
		foreach (float x in h)
		{
			if (x > 0f)
			{
				min = Math.Min(min, x);
				max = Math.Max(max, x);
			}
		}
		if (max >= min)
		{
			using var range = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
			float a = Pos(min), b = Math.Max(Pos(max), a + 3f);
			g.FillRectangle(range, a, bar.Y + 1f, b - a, bar.Height - 2f);
		}
		float at = Pos(v);
		using (var fill = new LinearGradientBrush(new RectangleF(bar.X - 1f, bar.Y, bar.Width + 2f, bar.Height), DeckBlue, DeckRed, 0f)
		{
			InterpolationColors = new ColorBlend { Colors = new[] { DeckBlue, DeckGreen, DeckAmber, DeckRed }, Positions = new[] { 0f, (Pos(limit - 25f) - bar.X) / bar.Width, (Pos(limit - 8f) - bar.X) / bar.Width, 1f } }
		})
		{
			using GraphicsPath done = Imaging.Round(new RectangleF(bar.X, bar.Y + 2f, Math.Max(4f, at - bar.X), bar.Height - 4f), 2f);
			g.FillPath(fill, done);
		}
		using (var tick = new Pen(Color.FromArgb(220, 255, 236, 236), 2f))
		{
			float lx = Pos(limit);
			g.DrawLine(tick, lx, bar.Y - 4f, lx, bar.Bottom + 4f);
		}
		DeckText(g, "limit " + limit.ToString("0") + "°", 10f, DeckDim, r.Right, bar.Bottom + 4f, StringAlignment.Far, "Bahnschrift");
		float glow = 7f + 3f * (float)(0.5 + 0.5 * Math.Sin(t * (2.0 + Math.Max(0f, 20f - (limit - v)) * 0.3)));
		using (var halo = new SolidBrush(Color.FromArgb(80, tone)))
		{
			g.FillEllipse(halo, at - glow, bar.Y + 4f - glow, glow * 2f, glow * 2f);
		}
		using (var dot = new SolidBrush(Color.White))
		{
			g.FillEllipse(dot, at - 4.5f, bar.Y - 0.5f, 9f, 9f);
		}
	}

	private void NetCard(Graphics g, RectangleF r, double t)
	{
		float down = Val("net.down"), up = Val("net.up");
		if (down + up > 0.02f)
		{
			bool on = t * 1.6 % 1.0 < 0.6;
			using var red = new SolidBrush(Color.FromArgb(on ? 255 : 70, DeckRed));
			g.FillEllipse(red, r.Right - 68f, r.Y + 28f, 8f, 8f);
			DeckText(g, "LIVE", 12f, DeckRed, r.Right - 22f, r.Y + 24f, StringAlignment.Far);
		}
		Slabs(g, DeckSlab(r, "down", 12, 48, (int)r.Width - 104, 120, "net.down", WidgetKind.Value, "", "left", "0.0"), SlabRect(r, 12, 48, (int)r.Width - 104, 120), t);
		DeckText(g, "MB/s", 18f, DeckInk, r.Right - 22f, r.Y + 112f, StringAlignment.Far);
		DeckText(g, "DOWNLOAD", 11f, DeckDim, r.Right - 22f, r.Y + 138f, StringAlignment.Far, "Bahnschrift");
		var graph = new RectangleF(r.X + 22f, r.Y + 186f, r.Width - 44f, 140f);
		float[] hd = _sensors.History("net.down"), hu = _sensors.History("net.up");
		float top = Math.Max(1f, Math.Max(hd.Length > 0 ? hd.Max() : 0f, hu.Length > 0 ? hu.Max() : 0f) * 1.15f);
		float scale = (float)DeckEase($"{r.X}|netscale", top, t);
		float mid = graph.Y + graph.Height / 2f;
		using (var axis = new Pen(DeckFaint, 1f))
		{
			g.DrawLine(axis, graph.X, mid, graph.Right, mid);
		}
		Mirror(g, graph, "net.down", scale, DeckBlue, up: true);
		Mirror(g, graph, "net.up", scale, DeckAmber, up: false);
		DeckText(g, "↓", 12f, DeckBlue, graph.X, graph.Y, StringAlignment.Near, "Bahnschrift");
		DeckText(g, "↑", 12f, DeckAmber, graph.X, graph.Bottom - 16f, StringAlignment.Near, "Bahnschrift");
		Divider(g, r, 342f);
		float col = (r.Width - 44f) / 2f;
		Stat(g, "UPLOAD", Fmt("net.up", "0.0"), "MB/s", r.X + 22f, r.Y + 356f, DeckAmber);
		Stat(g, "PEAK", (hd.Length > 0 ? hd.Max() : 0f).ToString("0.0", CultureInfo.InvariantCulture), "MB/s", r.X + 22f + col, r.Y + 356f);
	}

	// ---------- instruments ----------

	/// <summary>Recent history as mirrored bars sliding left, coloured by height and fading with age; the newest burns amber.</summary>
	private void Waveform(Graphics g, RectangleF r, string sensor, double t, float heat = 0f, Color hc = default)
	{
		float[] h = _sensors.History(sensor);
		const int n = 34;
		float step = r.Width / n, bw = step * 0.56f, mid = r.Y + r.Height / 2f;
		float frac = (float)Math.Clamp(_sensors.Age(sensor), 0.0, 1.0);
		using (var axis = new Pen(DeckFaint, 1f))
		{
			g.DrawLine(axis, r.X, mid, r.Right, mid);
		}
		for (int j = 0; j < n; j++)
		{
			float x = r.Right - (j + frac) * step - step / 2f;
			if (x - bw / 2f < r.X)
			{
				break;
			}
			float v = j < h.Length ? h[^(j + 1)] : 0f;
			float half = Math.Max(1.5f, v / 100f * r.Height / 2f * 0.96f);
			if (heat > 1.5f && j < 10)
			{
				// the newest bars flicker like flame when the processor works hard
				half *= 1f + (heat - 1.5f) * 0.09f * (float)Math.Sin(t * 23.0 + j * 1.7);
			}
			Color lead = heat > 0.03f ? Imaging.Mix(DeckAmber, hc, Math.Min(1f, heat)) : DeckAmber;
			if (j == 0)
			{
				// a little live shimmer on the newest bar
				half *= 1f + 0.05f * (float)Math.Sin(t * 9.0);
				using var bloom = new SolidBrush(Color.FromArgb((int)(60 + 30 * Math.Min(1f, heat)), lead));
				g.FillRectangle(bloom, x - bw * 1.4f, mid - half - 4f, bw * 2.8f, half * 2f + 8f);
			}
			float age = j / (float)n;
			Color c = j == 0 ? Imaging.Mix(lead, Color.White, 0.25) : heat > 0.03f ? Imaging.Mix(Spec(v / 100f), hc, Math.Min(1f, heat) * 0.65) : Spec(v / 100f);
			using var brush = new SolidBrush(Color.FromArgb((int)(255 * (1f - age * 0.8f)), c));
			g.FillRectangle(brush, x - bw / 2f, mid - half, bw, half * 2f);
		}
	}

	/// <summary>Blue at rest, then amber, orange and red as heat runs from 0 to 3.</summary>
	private static Color HeatColor(float heat)
	{
		Color[] stops = { DeckBlue, DeckAmber, Color.FromArgb(255, 112, 48), DeckRed };
		heat = Math.Clamp(heat, 0f, 3f);
		int i = Math.Min(2, (int)heat);
		return Imaging.Mix(stops[i], stops[i + 1], heat - i);
	}

	/// <summary>A soft elliptical glow fading to nothing at its edge.</summary>
	private static void HeatGlow(Graphics g, RectangleF r, Color c)
	{
		if (c.A < 2)
		{
			return;
		}
		using var p = new GraphicsPath();
		p.AddEllipse(r);
		using var brush = new PathGradientBrush(p) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } };
		g.FillPath(brush, p);
	}

	/// <summary>Sparks rising out of the waveform, more and faster the hotter it runs.</summary>
	private static void Embers(Graphics g, RectangleF r, float heat, Color hc, double t)
	{
		int count = (int)(heat * 7f);
		for (int k = 0; k < count; k++)
		{
			double speed = 0.35 + 0.25 * Hash01(k * 31 + 7) + heat * 0.12;
			double phase = (t * speed + Hash01(k * 17 + 3)) % 1.0;
			float x = r.X + (float)Hash01(k * 53 + (int)(t * speed + Hash01(k * 17 + 3))) * r.Width;
			x += (float)Math.Sin(t * 3.0 + k) * 4f;
			float y = r.Bottom - (float)phase * (r.Height + 40f);
			float size = 2f + (float)Hash01(k * 11) * 2.5f;
			int a = (int)((1.0 - phase) * Math.Min(1f, heat / 2f) * 230);
			using var b = new SolidBrush(Color.FromArgb(a, Imaging.Mix(hc, Color.White, 0.35)));
			g.FillEllipse(b, x - size / 2f, y - size / 2f, size, size);
		}
	}

	/// <summary>A ramp of ticks lit up to the reading in spectrum colours, with a white tick holding the recent peak.</summary>
	private static void Ticks(Graphics g, RectangleF r, double f, double peak)
	{
		const int n = 26;
		float step = r.Width / n, tw = step * 0.5f;
		int peakAt = (int)Math.Clamp(Math.Ceiling(peak * n) - 1, -1, n - 1);
		for (int i = 0; i < n; i++)
		{
			float p = (i + 0.5f) / n;
			float th = r.Height * (0.3f + 0.7f * p);
			Color c = i == peakAt && peak > 0.02 ? Color.White : p <= f ? Spec(p) : DeckFaint;
			using var brush = new SolidBrush(c);
			g.FillRectangle(brush, r.X + i * step + (step - tw) / 2f, r.Bottom - th, tw, th);
		}
	}

	/// <summary>A history line sliding left with a glow and a pulsing dot at its head; optionally filled beneath.</summary>
	private void Line(Graphics g, RectangleF r, string sensor, float lo, float hi, Color color, double t, bool fill)
	{
		float[] h = _sensors.History(sensor);
		const int n = 60;
		float step = r.Width / (n - 1);
		float frac = (float)Math.Clamp(_sensors.Age(sensor), 0.0, 1.0);
		var pts = new List<PointF>();
		for (int j = 0; j < n && j < h.Length; j++)
		{
			float v = h[^(j + 1)];
			if (v <= 0f)
			{
				break;
			}
			float x = r.Right - (j + frac) * step;
			if (x < r.X)
			{
				break;
			}
			pts.Add(new PointF(x, r.Bottom - Math.Clamp((v - lo) / (hi - lo), 0f, 1f) * r.Height));
		}
		if (pts.Count < 2)
		{
			return;
		}
		pts.Reverse();
		PointF[] line = pts.ToArray();
		if (fill)
		{
			using var area = new GraphicsPath();
			area.AddLines(line);
			area.AddLine(line[^1].X, r.Bottom, line[0].X, r.Bottom);
			using var shade = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Color.FromArgb(90, color), Color.FromArgb(0, color), 90f);
			g.FillPath(shade, area);
		}
		using (var glow = new Pen(Color.FromArgb(50, color), 7f) { LineJoin = LineJoin.Round })
		{
			g.DrawLines(glow, line);
		}
		using (var pen = new Pen(Imaging.Mix(color, Color.White, 0.2), 2f) { LineJoin = LineJoin.Round })
		{
			g.DrawLines(pen, line);
		}
		PointF head = line[^1];
		float pulse = 4f + 3f * (float)(0.5 + 0.5 * Math.Sin(t * 4.0));
		using (var halo = new SolidBrush(Color.FromArgb(70, color)))
		{
			g.FillEllipse(halo, head.X - pulse * 1.6f, head.Y - pulse * 1.6f, pulse * 3.2f, pulse * 3.2f);
		}
		using var dot = new SolidBrush(Color.White);
		g.FillEllipse(dot, head.X - 3f, head.Y - 3f, 6f, 6f);
	}

	/// <summary>History as an area sliding left, above the middle line (up) or hanging below it.</summary>
	private void Mirror(Graphics g, RectangleF r, string sensor, float scale, Color color, bool up)
	{
		float[] h = _sensors.History(sensor);
		const int n = 60;
		float step = r.Width / (n - 1), mid = r.Y + r.Height / 2f, half = r.Height / 2f - 2f;
		float frac = (float)Math.Clamp(_sensors.Age(sensor), 0.0, 1.0);
		var pts = new List<PointF>();
		for (int j = 0; j < n && j < h.Length; j++)
		{
			float x = r.Right - (j + frac) * step;
			if (x < r.X)
			{
				break;
			}
			float d = Math.Clamp(h[^(j + 1)] / scale, 0f, 1f) * half;
			pts.Add(new PointF(x, up ? mid - d : mid + d));
		}
		if (pts.Count < 2)
		{
			return;
		}
		pts.Reverse();
		using var area = new GraphicsPath();
		area.AddLines(pts.ToArray());
		area.AddLine(pts[^1].X, mid, pts[0].X, mid);
		var box = up ? new RectangleF(r.X, r.Y - 1f, r.Width, half + 2f) : new RectangleF(r.X, mid - 1f, r.Width, half + 2f);
		using (var shade = new LinearGradientBrush(box, Color.FromArgb(up ? 150 : 20, color), Color.FromArgb(up ? 20 : 150, color), 90f))
		{
			g.FillPath(shade, area);
		}
		using var pen = new Pen(Imaging.Mix(color, Color.White, 0.3), 1.6f) { LineJoin = LineJoin.Round };
		g.DrawLines(pen, pts.ToArray());
	}

	private void Stat(Graphics g, string label, string value, string unit, float x, float y, Color? mark = null)
	{
		float lx = x;
		if (mark.HasValue)
		{
			using var b = new SolidBrush(mark.Value);
			g.FillEllipse(b, x, y + 4f, 7f, 7f);
			lx += 12f;
		}
		DeckText(g, label, 12f, DeckDim, lx, y, StringAlignment.Near, "Bahnschrift");
		SizeF s = DeckText(g, value, 26f, DeckInk, x - 2f, y + 18f);
		DeckText(g, unit, 13f, DeckDim, x + s.Width - 4f, y + 30f, StringAlignment.Near, "Bahnschrift");
	}

	private void Legend(Graphics g, string label, string value, float x, float y, Color mark)
	{
		using (var b = new SolidBrush(mark))
		{
			g.FillRectangle(b, x, y + 4f, 3f, 40f);
		}
		DeckText(g, label, 11f, DeckDim, x + 10f, y, StringAlignment.Near, "Bahnschrift");
		DeckText(g, value, 22f, DeckInk, x + 9f, y + 16f);
	}

	private void Pill(Graphics g, string text, Color color, float x, float y, StringAlignment align)
	{
		Bitmap label = TextImage(text, 13f, color, "Bahnschrift SemiBold");
		float w = label.Width + 22f, h = 26f;
		float left = align == StringAlignment.Far ? x - w : x;
		using GraphicsPath p = Imaging.Round(new RectangleF(left, y, w, h), h / 2f);
		using (var fill = new SolidBrush(Color.FromArgb(34, color)))
		{
			g.FillPath(fill, p);
		}
		using (var edge = new Pen(Color.FromArgb(170, color), 1.2f))
		{
			g.DrawPath(edge, p);
		}
		g.DrawImageUnscaled(label, (int)Math.Round(left + 11f), (int)Math.Round(y + (h - label.Height) / 2f + 1f));
	}

	private static void Divider(Graphics g, RectangleF r, float y)
	{
		using var pen = new Pen(DeckFaint, 1f);
		g.DrawLine(pen, r.X + 22f, r.Y + y, r.Right - 22f, r.Y + y);
	}

	/// <summary>The prism's spectrum from blue through white to amber and red.</summary>
	private static Color Spec(float p)
	{
		Color[] stops = { DeckBlue, Color.FromArgb(150, 200, 255), Color.FromArgb(255, 253, 244), DeckAmber, DeckRed };
		float[] at = { 0f, 0.35f, 0.58f, 0.8f, 1f };
		p = Math.Clamp(p, 0f, 1f);
		for (int i = 1; i < stops.Length; i++)
		{
			if (p <= at[i])
			{
				return Imaging.Mix(stops[i - 1], stops[i], (p - at[i - 1]) / (at[i] - at[i - 1]));
			}
		}
		return stops[^1];
	}

	// ---------- helpers ----------

	private float Val(string key) => _sensors.Get(key) ?? 0f;

	private string Fmt(string key, string format) => _sensors.Get(key)?.ToString(format, CultureInfo.InvariantCulture) ?? "--";

	private double DeckEase(string id, double target, double t)
	{
		if (!_deckEase.TryGetValue(id, out var st))
		{
			st = (target, t);
		}
		double dt = Math.Clamp(t - st.At, 0.0, 1.0);
		double shown = st.Shown + (target - st.Shown) * (1.0 - Math.Exp(-dt * 4.0));
		_deckEase[id] = (shown, t);
		return shown;
	}

	/// <summary>The recent peak: held for a moment, then sinking back toward the reading.</summary>
	private double Peak(string id, double f, double t)
	{
		if (!_deckPeak.TryGetValue(id, out var st) || f >= st.Peak)
		{
			_deckPeak[id] = (f, t, t);
			return f;
		}
		double peak = t - st.At > 1.5 ? Math.Max(f, st.Peak - Math.Clamp(t - st.Last, 0.0, 1.0) * 0.15) : st.Peak;
		_deckPeak[id] = (peak, st.At, t);
		return peak;
	}

	/// <summary>A slab-letter widget living inside a card, made once.</summary>
	private Widget DeckSlab(RectangleF card, string id, int dx, int dy, int w, int h, string sensor, WidgetKind kind, string text, string align, string format = "0")
	{
		string key = $"{card.X}|{card.Y}|{id}";
		if (!_deckSlabs.TryGetValue(key, out Widget sw))
		{
			_deckSlabs[key] = sw = new Widget
			{
				Kind = kind, Sensor = sensor, Effect = "slabs", Text = text, Format = format, X = (int)card.X + dx, Y = (int)card.Y + dy, W = w, H = h,
				Size = 220f, Font = "Impact", Bold = false, Align = align, Color = "#ece7da"
			};
		}
		return sw;
	}

	private static RectangleF SlabRect(RectangleF card, int dx, int dy, int w, int h) => new RectangleF(card.X + dx, card.Y + dy, w, h);

	/// <summary>Draws text from a cache of pre-rendered images; returns its size.</summary>
	private SizeF DeckText(Graphics g, string text, float size, Color color, float x, float y, StringAlignment align = StringAlignment.Near, string font = "Bahnschrift SemiBold")
	{
		Bitmap bmp = TextImage(text, size, color, font);
		float dx = align == StringAlignment.Center ? -bmp.Width / 2f : align == StringAlignment.Far ? -bmp.Width : 0f;
		g.DrawImageUnscaled(bmp, (int)Math.Round(x + dx), (int)Math.Round(y));
		return new SizeF(bmp.Width, bmp.Height);
	}

	private Bitmap TextImage(string text, float size, Color color, string font)
	{
		string key = $"{font}|{size}|{color.ToArgb()}|{text}";
		if (_deckText.TryGetValue(key, out Bitmap bmp))
		{
			return bmp;
		}
		if (_deckText.Count > 600)
		{
			foreach (Bitmap old in _deckText.Values)
			{
				old.Dispose();
			}
			_deckText.Clear();
		}
		return _deckText[key] = RenderText(text, size, color, font, 0f);
	}

	/// <summary>Text on a transparent image, its letters spread by spacing pixels.</summary>
	private static Bitmap RenderText(string text, float size, Color color, string font, float spacing)
	{
		using var f = new Font(font, size, FontStyle.Regular, GraphicsUnit.Pixel);
		using var probe = new Bitmap(1, 1);
		using Graphics pg = Graphics.FromImage(probe);
		float Width(string s) => s == " " ? size * 0.3f : pg.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
		float total = spacing > 0f ? text.Sum(c => Width(c.ToString()) + spacing) : Width(text);
		var bmp = new Bitmap(Math.Max(1, (int)Math.Ceiling(total) + 4), (int)Math.Ceiling(size * 1.35f) + 2, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.TextRenderingHint = TextRenderingHint.AntiAlias;
		using var brush = new SolidBrush(color);
		if (spacing > 0f)
		{
			float x = 2f;
			foreach (char c in text)
			{
				g.DrawString(c.ToString(), f, brush, x, 0f, StringFormat.GenericTypographic);
				x += Width(c.ToString()) + spacing;
			}
		}
		else
		{
			g.DrawString(text, f, brush, 2f, 0f, StringFormat.GenericTypographic);
		}
		return bmp;
	}

	// ---------- chrome, drawn once per card ----------

	private static Bitmap CardChrome(string kind, int w, int h)
	{
		(string num, string title) = kind switch
		{
			"cpu" => ("01", "PROCESSOR"),
			"gpu" => ("02", "GRAPHICS"),
			"mem" => ("03", "MEMORY"),
			"therm" => ("04", "THERMALS"),
			"net" => ("05", "NETWORK"),
			_ => ("", kind.ToUpperInvariant())
		};
		var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
		using Graphics g = Graphics.FromImage(bmp);
		Setup(g);
		using GraphicsPath body = Imaging.Round(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), 18f);
		using (var fill = new LinearGradientBrush(new RectangleF(0f, -1f, w, h + 2f), Color.FromArgb(244, 19, 21, 29), Color.FromArgb(250, 7, 8, 12), 90f))
		{
			g.FillPath(fill, body);
		}
		using (var edge = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
		{
			g.DrawPath(edge, body);
		}
		// a hairline of split light along the top edge
		using (var hair = new LinearGradientBrush(new RectangleF(24f, 0f, w - 48f, 2f), DeckBlue, DeckAmber, 0f))
		{
			hair.InterpolationColors = new ColorBlend
			{
				Colors = new[] { Color.FromArgb(0, DeckBlue), Color.FromArgb(170, DeckBlue), Color.FromArgb(220, 255, 253, 244), Color.FromArgb(170, DeckAmber), Color.FromArgb(0, DeckAmber) },
				Positions = new[] { 0f, 0.3f, 0.55f, 0.75f, 1f }
			};
			g.FillRectangle(hair, 24f, 0f, w - 48f, 1.5f);
		}
		using (Bitmap n = RenderText(num, 14f, DeckDim, "Bahnschrift", 1f))
		{
			g.DrawImageUnscaled(n, 20, 24);
		}
		using (Bitmap tl = RenderText(title, 14f, DeckInk, "Bahnschrift SemiBold", 2.6f))
		{
			g.DrawImageUnscaled(tl, 48, 24);
		}
		return bmp;
	}

	/// <summary>The hero panel: warm amber glass split by diagonal slats, a rainbow seam across them, and grain.</summary>
	private static Bitmap HeroChrome(int w, int h)
	{
		using var glass = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
		using (Graphics g = Graphics.FromImage(glass))
		{
			Setup(g);
			using (var warm = new LinearGradientBrush(new RectangleF(-1f, -1f, w + 2f, h + 2f), Color.Black, Color.Black, 62f))
			{
				warm.InterpolationColors = new ColorBlend
				{
					Colors = new[] { Color.FromArgb(232, 118, 44), Color.FromArgb(178, 66, 20), Color.FromArgb(70, 22, 8), Color.FromArgb(14, 6, 4) },
					Positions = new[] { 0f, 0.3f, 0.66f, 1f }
				};
				g.FillRectangle(warm, -1f, -1f, w + 2f, h + 2f);
			}
			var rnd = new Random(5);
			for (float u = -h; u < w + h; )
			{
				float sw = 38f + (float)rnd.NextDouble() * 40f;
				int a = rnd.Next(4, 26);
				bool light = rnd.Next(2) == 0;
				float yc = h * 0.6f + (float)(rnd.NextDouble() * 50.0 - 25.0);
				using var slat = new GraphicsPath();
				slat.AddPolygon(new[] { new PointF(u, h + 10f), new PointF(u + h * 0.84f, -10f), new PointF(u + h * 0.84f + sw, -10f), new PointF(u + sw, h + 10f) });
				using (var shade = new SolidBrush(Color.FromArgb(a, light ? Color.White : Color.Black)))
				{
					g.FillPath(shade, slat);
				}
				GraphicsState s = g.Save();
				g.SetClip(slat);
				using (var bow = new LinearGradientBrush(new RectangleF(0f, yc - 40f, w, 80f), Color.Black, Color.Black, 90f))
				{
					bow.InterpolationColors = new ColorBlend
					{
						Colors = new[] { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(90, 80, 140, 255), Color.FromArgb(150, 255, 252, 240), Color.FromArgb(90, 255, 190, 110), Color.FromArgb(0, 0, 0, 0) },
						Positions = new[] { 0f, 0.38f, 0.5f, 0.62f, 1f }
					};
					g.FillRectangle(bow, 0f, yc - 40f, w, 80f);
				}
				g.Restore(s);
				using (var seam = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
				{
					g.DrawLine(seam, u + sw, h + 10f, u + h * 0.84f + sw, -10f);
				}
				u += sw;
			}
		}
		Grain(glass, 10);
		var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
		using (Graphics g = Graphics.FromImage(bmp))
		{
			Setup(g);
			using GraphicsPath body = Imaging.Round(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), 18f);
			using (var tex = new TextureBrush(glass, WrapMode.Clamp))
			{
				g.FillPath(tex, body);
			}
			using (var edge = new Pen(Color.FromArgb(70, 255, 220, 190), 1f))
			{
				g.DrawPath(edge, body);
			}
			using (Bitmap a = RenderText("LUMALI", 34f, Color.White, "Bahnschrift SemiBold", 1f))
			{
				g.DrawImageUnscaled(a, 22, 22);
			}
			using (Bitmap b = RenderText("DASHBOARD", 34f, Color.FromArgb(230, 255, 236, 220), "Bahnschrift Light", 1f))
			{
				g.DrawImageUnscaled(b, 22, 60);
			}
			using (Bitmap c = RenderText("MONITOR EVERYTHING", 13f, Color.FromArgb(220, 255, 240, 228), "Bahnschrift SemiBold", 2.4f))
			{
				g.DrawImageUnscaled(c, 24, h - 62);
			}
			using (Bitmap d = RenderText("WITH LUMALI", 13f, Color.FromArgb(160, 255, 240, 228), "Bahnschrift SemiBold", 2.4f))
			{
				g.DrawImageUnscaled(d, 24, h - 40);
			}
		}
		return bmp;
	}
}
