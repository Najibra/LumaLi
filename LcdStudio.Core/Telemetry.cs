using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Telemetry: a mission-dashboard sheet after a reference in sage green, signal orange and charcoal (bold grotesk
/// figures, bar chart on a grid, a phase timeline, hexagon outlines, hazard dashes, crosshair marks).
/// - Left, the sage card: eight bars on a grid (CPU/GPU load, RAM, VRAM, CPU/GPU temperature, case fans, GPU fan) that
///   rise on springs; the highest turns orange; a slowly falling peak tick on each; hexagons drift up the dark strip.
/// - Middle, charcoal: the time in big rolling figures with orange seconds, uptime and week, the busiest app and the
///   memory in use; a 60-second history that scrolls smoothly: CPU as orange blocks, GPU as a sage line with a wash,
///   tags at the newest values, and two phase bars filled to the current loads with average and peak.
/// - Right: an orange CPU card and a sage GPU card (temperature in big figures, hazard dashes that run with the load,
///   a barcode of the reading, hexagons lighting in turn; clock, power and a third reading below), and a row of stats
///   with line icons: download and upload (with 60 s sparklines), case fan speed (turning fan) and the game frame rate
///   (or the memory in use when no game runs).
/// Every changed figure rolls up in its cell.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly SKColor TmMint = new(205, 223, 205), TmMintDim = new(150, 168, 152), TmOrange = new(242, 104, 60),
		TmInk = new(32, 35, 34), TmDark = new(43, 46, 45), TmPaper = new(228, 238, 228);

	private const string TmBold = "Segoe UI Variable Display|B", TmCap = "Bahnschrift SemiBold", TmCond = "Bahnschrift SemiCondensed";

	private sealed class TmRollState
	{
		public readonly char[] Ch = new char[12], Was = new char[12];

		public readonly double[] Since = new double[12];
	}

	private readonly Dictionary<string, TmRollState> _tmRoll = new();

	private SKImage? _tmBase;

	private double _tmDt;

	private double _tmAt = double.NaN, _tmSampleAt = double.NaN, _tmHex, _tmDashA, _tmDashB, _tmFan;

	private readonly float[] _tmCpuH = new float[61], _tmGpuH = new float[61], _tmDownH = new float[61], _tmUpH = new float[61];

	private readonly float[] _tmPeak = new float[8], _tmHot = new float[8];

	private bool DrawTelemetry(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "telemetry" || w.Kind != WidgetKind.Panel)
		{
			return DrawNowPlaying(g, w, r, t);
		}
		Telemetry(g, t);
		return true;
	}

	private void Telemetry(Graphics g, double t)
	{
		double dt = double.IsNaN(_tmAt) ? 0.0 : Math.Clamp(t - _tmAt, 0.0, 0.25);
		_tmAt = t;
		_tmDt = dt;
		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;
		float Pct(string key) => McOnline(key) ? Math.Clamp(Val(key) / 100f, 0f, 1f) : 0f;
		float cpu = Math.Clamp(McEased("tm.cpu", Pct("cpu.load"), dt, 2.2), 0f, 1f);
		float gpu = Math.Clamp(McEased("tm.gpu", Pct("gpu.load"), dt, 2.2), 0f, 1f);
		float down = McOnline("net.down") ? Val("net.down") : 0f, up = McOnline("net.up") ? Val("net.up") : 0f;

		// one sample a second for the history and the sparklines
		if (double.IsNaN(_tmSampleAt) || t - _tmSampleAt > 5.0 || t < _tmSampleAt)
		{
			// start from the sensors' own recent history, so the graphs aren't flat at first
			void Seed(float[] a, string key, float scale, float fallback)
			{
				float[] h = _sensors.History(key);
				for (int i = 0; i < a.Length; i++)
				{
					int j = h.Length - a.Length + i;
					a[i] = j >= 0 && h.Length >= a.Length ? (scale > 0f ? Math.Clamp(h[j] / scale, 0f, 1f) : h[j]) : fallback;
				}
				a[^1] = fallback;
			}
			Seed(_tmCpuH, "cpu.load", 100f, cpu);
			Seed(_tmGpuH, "gpu.load", 100f, gpu);
			Seed(_tmDownH, "net.down", 0f, down);
			Seed(_tmUpH, "net.up", 0f, up);
			_tmSampleAt = t;
		}
		while (t - _tmSampleAt >= 1.0)
		{
			Push(_tmCpuH, cpu);
			Push(_tmGpuH, gpu);
			Push(_tmDownH, down);
			Push(_tmUpH, up);
			_tmSampleAt += 1.0;
		}
		static void Push(float[] a, float v)
		{
			Array.Copy(a, 1, a, 0, a.Length - 1);
			a[^1] = v;
		}
		float frac = (float)Math.Clamp(t - _tmSampleAt, 0.0, 1.0);

		_tmHex += dt * 9.0;
		_tmDashA += dt * (8.0 + 60.0 * cpu);
		_tmDashB += dt * (8.0 + 60.0 * gpu);
		double turns = McOnline("fans.rpm") ? Math.Min(1.1, Val("fans.rpm") / 1800.0) : 0.15;
		_tmFan += dt * turns * 360.0;

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_tmBase ??= TmMakeBase();
			c.DrawImage(_tmBase, 0f, 0f);
			TmBars(c, dt, t);
			TmMiddle(c, now, cpu, gpu, frac, t);
			TmCard(c, true, t);
			TmCard(c, false, t);
			TmStats(c, down, up, frac, t);
		});
	}

	// ---------- left: the bar chart ----------

	private static readonly SKRect TmPlot = new(150f, 58f, 584f, 370f);

	private void TmBars(SKCanvas c, double dt, double t)
	{
		// hexagons drifting up the dark strip
		c.Save();
		c.ClipRect(new SKRect(16f, 16f, 72f, 464f));
		using (var hp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = new SKColor(205, 223, 205, 70) })
		{
			const float r = 30f, pitch = r * 1.732f;
			float off = (float)(_tmHex % pitch);
			for (int i = -1; i < 10; i++)
			{
				float y = 480f - i * pitch - off;
				TmHexPath(c, 44f + (i % 2 == 0 ? -14f : 14f), y, r, 30f, hp);
			}
		}
		c.Restore();

		var v = new float[8];
		var on = new bool[8];
		string[] text = new string[8];
		void Set(int i, string key, float value, float scale, string shown)
		{
			on[i] = McOnline(key);
			v[i] = on[i] ? Math.Clamp(McEased("tm.b" + i, value / scale, dt, 2.0), 0f, 1f) : 0f;
			text[i] = on[i] ? shown : "--";
		}
		string P0(string key) => Math.Round(Val(key)).ToString("0", CultureInfo.InvariantCulture);
		Set(0, "cpu.load", Val("cpu.load"), 100f, P0("cpu.load"));
		Set(1, "gpu.load", Val("gpu.load"), 100f, P0("gpu.load"));
		Set(2, "ram.load", Val("ram.load"), 100f, P0("ram.load"));
		Set(3, "gpu.vram", Val("gpu.vram"), 32f, Val("gpu.vram").ToString("0.0", CultureInfo.InvariantCulture));
		Set(4, "cpu.temp", Val("cpu.temp"), 100f, P0("cpu.temp") + "°");
		Set(5, "gpu.temp", Val("gpu.temp"), 100f, P0("gpu.temp") + "°");
		Set(6, "fans.duty", Val("fans.duty"), 100f, P0("fans.duty"));
		Set(7, "gpu.fan", Val("gpu.fan"), 100f, P0("gpu.fan"));
		int top = 0;
		for (int i = 1; i < 8; i++)
		{
			if (v[i] > v[top])
			{
				top = i;
			}
		}
		float slot = TmPlot.Width / 8f;
		using var p = new SKPaint { IsAntialias = true };
		// a light sweep across the chart every 8 s
		float sweep = (float)(t % 8.0 / 1.6);
		for (int i = 0; i < 8; i++)
		{
			float e = 1f - MathF.Exp(-(float)dt * 5f);
			_tmHot[i] += ((i == top && v[i] > 0.01f ? 1f : 0f) - _tmHot[i]) * e;
			_tmPeak[i] = Math.Max(v[i], _tmPeak[i] - (float)dt * 0.025f);
			float cx = TmPlot.Left + slot * (i + 0.5f);
			const float bw = 32f;
			float h = TmPlot.Height * v[i];
			float y = TmPlot.Bottom - h;
			p.Color = TmLerp(TmInk, TmOrange, _tmHot[i]);
			if (h > 0.5f)
			{
				c.DrawRect(cx - bw / 2f, y, bw, h, p);
				// a fine light line at the top
				p.Color = new SKColor(255, 255, 255, (byte)(50 + 60 * _tmHot[i]));
				c.DrawRect(cx - bw / 2f, y, bw, 2f, p);
			}
			// the sweep brightens a bar as it passes
			float sx = TmPlot.Left + sweep * TmPlot.Width;
			float near = sweep <= 1f ? Math.Clamp(1f - MathF.Abs(sx - cx) / 40f, 0f, 1f) : 0f;
			if (near > 0f && h > 0.5f)
			{
				p.Color = new SKColor(255, 255, 255, (byte)(70 * near));
				c.DrawRect(cx - bw / 2f, y, bw, h, p);
			}
			// the peak of the last minute or so, falling slowly
			float py = TmPlot.Bottom - TmPlot.Height * _tmPeak[i];
			if (_tmPeak[i] - v[i] > 0.012f)
			{
				p.Color = TmLerp(TmInk, TmOrange, _tmHot[i]);
				c.DrawRect(cx - bw / 2f - 4f, py - 1.5f, bw + 8f, 3f, p);
			}
			// the reading above the bar
			float ty = Math.Max(TmPlot.Top - 30f, Math.Min(py, y) - 30f);
			TmNumber(c, "tm.bv" + i, text[i], cx, ty, 21f, TmInk, t, align: 0, font: TmBold, track: 0f);
		}
		if (sweep <= 1f)
		{
			float sx = TmPlot.Left + sweep * TmPlot.Width;
			float a = MathF.Sin(sweep * MathF.PI);
			p.Color = new SKColor(TmOrange.Red, TmOrange.Green, TmOrange.Blue, (byte)(200 * a));
			c.DrawRect(sx - 1f, TmPlot.Top - 6f, 2f, TmPlot.Height + 6f, p);
		}
	}

	// ---------- middle: the time, notes and the 60-second history ----------

	private static readonly SKRect TmChart = new(692f, 266f, 1140f, 434f);

	private void TmMiddle(SKCanvas c, DateTime now, float cpu, float gpu, float frac, double t)
	{
		using var p = new SKPaint { IsAntialias = true };
		// the live dot in the header
		float blink = 0.5f + 0.5f * MathF.Cos((float)(t * Math.PI * 2.0));
		p.Color = new SKColor(TmOrange.Red, TmOrange.Green, TmOrange.Blue, (byte)(110 + 145 * blink));
		c.DrawCircle(688f, 31f, 5f, p);
		McText(c, now.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant(), 1222f, 24f, 15f, TmMint, align: 1, track: 1f, font: TmCap);

		// the time, with the seconds in orange
		float end = TmNumber(c, "tm.time", now.ToString("HH:mm", CultureInfo.InvariantCulture), 680f, 70f, 108f, TmPaper, t);
		TmNumber(c, "tm.sec", now.ToString("ss", CultureInfo.InvariantCulture), end + 12f, 72f, 30f, TmOrange, t);
		string upt = McOnline("uptime.h") ? MnUptime() : "--";
		McText(c, "UPTIME  " + upt + "    WEEK " + ISOWeek.GetWeekOfYear(now).ToString(CultureInfo.InvariantCulture), 682f, 166f, 16f, TmMintDim, track: 1.2f, font: TmCap);

		// the busiest app and the memory
		const float rx = 1072f;
		McText(c, "BUSIEST APP", rx, 66f, 14f, TmMintDim, track: 1.2f, font: TmCap);
		string app = _sensors.Text("top.app") ?? "--";
		if (app.Length > 13)
		{
			app = app[..12] + "…";
		}
		McText(c, app, rx, 86f, 24f, TmPaper, font: TmBold);
		McText(c, McOnline("top.cpu") ? Math.Round(Val("top.cpu")).ToString("0", CultureInfo.InvariantCulture) + "% OF THE CPU" : "", rx, 118f, 14f, TmMintDim, track: 0.8f, font: TmCap);
		McText(c, "MEMORY", rx, 140f, 14f, TmMintDim, track: 1.2f, font: TmCap);
		string mem = McOnline("ram.used") ? Val("ram.used").ToString("0.0", CultureInfo.InvariantCulture) + (McOnline("ram.total") ? " / " + Math.Round(Val("ram.total")).ToString("0", CultureInfo.InvariantCulture) : "") + " GB" : "--";
		McText(c, mem, rx, 160f, 21f, TmPaper, font: TmBold);

		// phase bars: filled to the current load, with the minute's average and peak
		static (float Avg, float Max) Stats(float[] h)
		{
			float s = 0f, m = 0f;
			foreach (float x in h)
			{
				s += x;
				m = Math.Max(m, x);
			}
			return (s / h.Length, m);
		}
		var (ca, cm) = Stats(_tmCpuH);
		var (ga, gm) = Stats(_tmGpuH);
		McText(c, "CPU LOAD", 680f, 198f, 16f, TmOrange, track: 1.2f, font: TmCap);
		McText(c, "AVG " + RrPct(ca) + "   PEAK " + RrPct(cm), 944f, 199f, 14f, TmOrange, align: 1, track: 0.6f, font: TmCap);
		McText(c, "GPU LOAD", 960f, 198f, 16f, TmMint, track: 1.2f, font: TmCap);
		McText(c, "AVG " + RrPct(ga) + "   PEAK " + RrPct(gm), 1222f, 199f, 14f, TmMint, align: 1, track: 0.6f, font: TmCap);
		p.Color = new SKColor(TmOrange.Red, TmOrange.Green, TmOrange.Blue, 70);
		c.DrawRect(680f, 222f, 264f, 7f, p);
		p.Color = TmOrange;
		c.DrawRect(680f, 222f, 264f * cpu, 7f, p);
		using (var o = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = TmMint })
		{
			c.DrawRect(960.7f, 222.7f, 261f, 5.6f, o);
		}
		p.Color = TmMint;
		c.DrawRect(960f, 222f, 262f * gpu, 7f, p);

		// the history: CPU as orange blocks, GPU as a sage line with a wash, scrolling smoothly
		SKRect P = TmChart;
		float step = P.Width / 60f;
		// the scale ranges to the minute's peak in quarters, eased so the graph never jumps
		float want = Math.Clamp(MathF.Ceiling(Math.Max(Math.Max(cm, gm), Math.Max(cpu, gpu)) * 1.12f * 4f) / 4f, 0.25f, 1f);
		float range = Math.Clamp(McEased("tm.range", want, _tmDt, 1.2), 0.2f, 1.05f);
		float Y(float x) => P.Bottom - Math.Clamp(x / range, 0f, 1f) * P.Height;
		McText(c, "LAST 60 SECONDS", 680f, P.Bottom + 13f, 14f, TmMintDim, track: 0.8f, font: TmCap);
		McText(c, "SCALE 0–" + RrPct(want), 1222f, P.Bottom + 13f, 14f, TmMintDim, align: 1, track: 0.8f, font: TmCap);
		c.Save();
		c.ClipRect(P);
		for (int i = 0; i < 61; i++)
		{
			// block i covers the second after sample i was taken; the last one grows with the live reading
			float x0 = P.Right - (60 - i + frac) * step;
			float x1 = i < 60 ? x0 + step : P.Right;
			float val = i < 60 ? _tmCpuH[i + 1] : cpu;
			if (x1 - x0 < 2.5f)
			{
				continue;
			}
			float yy = Y(val);
			p.Color = new SKColor(TmOrange.Red, TmOrange.Green, TmOrange.Blue, (byte)(150 + 105 * i / 60));
			c.DrawRect(new SKRect(x0 + 1f, yy, x1 - 1f, P.Bottom), p);
		}
		using (var line = new SKPath())
		{
			for (int i = 0; i < 61; i++)
			{
				float x = P.Right - (60 - i + frac) * step;
				float y = Y(_tmGpuH[i]);
				if (i == 0)
				{
					line.MoveTo(x, y);
				}
				else
				{
					line.LineTo(x, y);
				}
			}
			line.LineTo(P.Right, Y(gpu));
			using var wash = new SKPath(line);
			wash.LineTo(P.Right, P.Bottom);
			wash.LineTo(P.Left - step * 2f, P.Bottom);
			wash.Close();
			using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, P.Top), new SKPoint(0f, P.Bottom), new[] { new SKColor(205, 223, 205, 90), new SKColor(205, 223, 205, 0) }, null, SKShaderTileMode.Clamp))
			{
				p.Color = SKColors.White;
				p.Shader = sh;
				c.DrawPath(wash, p);
				p.Shader = null;
			}
			using var lp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(26, 28, 27, 160) };
			c.DrawPath(line, lp);
			lp.StrokeWidth = 2.6f;
			lp.Color = TmPaper;
			c.DrawPath(line, lp);
		}
		c.Restore();
		// the grid over it: dashed load lines
		using (var gp = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(205, 223, 205, 45), PathEffect = SKPathEffect.CreateDash(new[] { 3f, 5f }, 0f) })
		{
			for (int k = 1; k < 4; k++)
			{
				float y = P.Bottom - P.Height * k / 4f;
				c.DrawLine(P.Left, y, P.Right, y, gp);
			}
		}
		// the newest points and their tags
		float yc = Y(cpu), yg = Y(gpu);
		p.Color = TmPaper;
		c.DrawCircle(P.Right, yg, 5f, p);
		p.Color = TmDark;
		c.DrawCircle(P.Right, yg, 2.2f, p);
		float tc = yc, tg = yg;
		if (MathF.Abs(tc - tg) < 30f)
		{
			float mid = (tc + tg) / 2f;
			float s = tc <= tg ? -1f : 1f;
			tc = mid + s * 15f;
			tg = mid - s * 15f;
		}
		tc = Math.Clamp(tc, P.Top + 2f, P.Bottom + 6f);
		tg = Math.Clamp(tg, P.Top + 2f, P.Bottom + 6f);
		TmTag(c, RrPct(cpu), P.Right + 14f, tc, TmOrange, TmInk, P.Right, yc, t, "tm.tc");
		TmTag(c, RrPct(gpu), P.Right + 14f, tg, TmMint, TmInk, P.Right, yg, t, "tm.tg");
	}

	/// <summary>A small filled tag with a rolling reading, joined by a dotted leader to its point.</summary>
	private void TmTag(SKCanvas c, string text, float x, float yMid, SKColor fill, SKColor ink, float px, float py, double t, string id)
	{
		using var p = new SKPaint { IsAntialias = true, Color = fill, StrokeWidth = 1.4f, PathEffect = SKPathEffect.CreateDash(new[] { 2f, 3f }, 0f) };
		c.DrawLine(px + 6f, py, x, yMid, p);
		p.PathEffect = null;
		var box = new SKRect(x, yMid - 13f, x + 66f, yMid + 13f);
		c.DrawRect(box, p);
		TmNumber(c, id, text, box.MidX, yMid - 8f, 17f, ink, t, align: 0, font: TmBold, track: 0f);
	}

	// ---------- right: the two cards ----------

	private void TmCard(SKCanvas c, bool cpuCard, double t)
	{
		float x = cpuCard ? 1260f : 1590f, y = 16f, w = 314f;
		SKColor ink = TmInk;
		string pre = cpuCard ? "cpu" : "gpu";
		bool tOn = McOnline(pre + ".temp");
		float temp = tOn ? Val(pre + ".temp") : 0f;
		float load = Pct(pre + ".load");
		float Pct(string key) => McOnline(key) ? Math.Clamp(Val(key) / 100f, 0f, 1f) : 0f;
		bool hot = tOn && temp >= (cpuCard ? 85f : 80f);
		using var p = new SKPaint { IsAntialias = true, Color = ink };

		// caption, a caution tag when hot
		string cap = cpuCard ? "PROCESSOR" : "GRAPHICS";
		if (hot && t % 1.0 < 0.6)
		{
			SKRect b = McText(c, cap, x + 20f, y + 18f, 17f, ink, track: 1.4f, font: TmCap);
			var tag = new SKRect(b.Right + 10f, y + 14f, b.Right + 94f, y + 38f);
			c.DrawRect(tag, p);
			McText(c, "CAUTION", tag.MidX, tag.Top + 6f, 13f, cpuCard ? TmOrange : TmMint, align: 0, track: 1.2f, font: TmCap);
		}
		else
		{
			McText(c, cap, x + 20f, y + 18f, 17f, ink, track: 1.4f, font: TmCap);
		}

		// hazard dashes running with the load
		var hz = new SKRect(x + 20f, y + 50f, x + 150f, y + 62f);
		c.Save();
		c.ClipRect(hz);
		using (var path = new SKPath())
		{
			float off = (float)((cpuCard ? _tmDashA : _tmDashB) % 12.0);
			for (float k = hz.Left - 24f + off; k < hz.Right + 12f; k += 12f)
			{
				path.MoveTo(k, hz.Bottom);
				path.LineTo(k + 6f, hz.Bottom);
				path.LineTo(k + 12f, hz.Top);
				path.LineTo(k + 6f, hz.Top);
				path.Close();
			}
			c.DrawPath(path, p);
		}
		c.Restore();
		// a barcode of the reading
		TmBarcode(c, x + 166f, y + 50f, 120f, 12f, (int)MathF.Round(temp * 10f) + (cpuCard ? 17 : 91), ink);

		// hexagons lighting in turn, faster with the load
		c.Save();
		c.ClipRect(new SKRect(x, y, x + w, y + 296f));
		using (var hp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(ink.Red, ink.Green, ink.Blue, 80) })
		using (var fp = new SKPaint { IsAntialias = true, Color = new SKColor(ink.Red, ink.Green, ink.Blue, 0) })
		{
			const float r = 30f;
			(float X, float Y)[] cells = { (x + 236f, y + 104f), (x + 288f, y + 134f), (x + 236f, y + 164f), (x + 288f, y + 74f) };
			double phase = t * (0.6 + 1.6 * load) + (cpuCard ? 0.0 : 0.5);
			for (int i = 0; i < cells.Length; i++)
			{
				float lit = MathF.Max(0f, MathF.Cos((float)((phase - i) / cells.Length * Math.PI * 2.0)));
				lit = MathF.Pow(lit, 4f);
				fp.Color = new SKColor(ink.Red, ink.Green, ink.Blue, (byte)(60 * lit));
				TmHexPath(c, cells[i].X, cells[i].Y, r, 0f, fp);
				TmHexPath(c, cells[i].X, cells[i].Y, r, 0f, hp);
			}
		}
		c.Restore();

		// the temperature in big figures
		float end = TmNumber(c, "tm.t" + pre, tOn ? Math.Round(temp).ToString("0", CultureInfo.InvariantCulture) : "--", x + 18f, y + 86f, 100f, ink, t);
		McText(c, "°C", end + 8f, y + 90f, 30f, ink, font: TmBold);
		McText(c, cpuCard ? "CPU TEMPERATURE" : "GPU TEMPERATURE", x + 20f, y + 178f, 15f, ink, track: 1.2f, font: TmCap);

		// three small readings under a hairline
		c.DrawRect(x + 20f, y + 204f, w - 40f, 1.5f, p);
		string clock = cpuCard
			? (McOnline("cpu.clock") ? (Val("cpu.clock") / 1000f).ToString("0.00", CultureInfo.InvariantCulture) : "--")
			: (McOnline("gpu.clock") ? Math.Round(Val("gpu.clock")).ToString("0", CultureInfo.InvariantCulture) : "--");
		string power = McOnline(pre + ".power") ? Math.Round(Val(pre + ".power")).ToString("0", CultureInfo.InvariantCulture) : "--";
		string third = cpuCard
			? (McOnline("cpu.busiest") ? Math.Round(Val("cpu.busiest")).ToString("0", CultureInfo.InvariantCulture) : "--")
			: (McOnline("gpu.hotspot") ? Math.Round(Val("gpu.hotspot")).ToString("0", CultureInfo.InvariantCulture) + "°" : "--");
		string[] vals = { clock, power, third };
		string[] labels = cpuCard ? new[] { "CLOCK GHZ", "POWER W", "MAX CORE %" } : new[] { "CLOCK MHZ", "POWER W", "HOT SPOT °C" };
		for (int i = 0; i < 3; i++)
		{
			float cx = x + 20f + i * 98f;
			TmNumber(c, "tm.s" + pre + i, vals[i], cx, y + 218f, 30f, ink, t, font: TmBold, track: 0f);
			McText(c, labels[i], cx, y + 256f, 14f, ink, track: 0.6f, font: TmCap);
		}
	}

	// ---------- right, bottom: stats with icons ----------

	private void TmStats(SKCanvas c, float down, float up, float frac, double t)
	{
		static string Rate(float v) => v >= 10f ? v.ToString("0", CultureInfo.InvariantCulture) : v >= 1f ? v.ToString("0.0", CultureInfo.InvariantCulture) : v.ToString("0.00", CultureInfo.InvariantCulture);
		bool fps = McOnline("game.fps");
		bool rpm = McOnline("fans.rpm");
		(string Label, string Value, string Unit)[] items =
		{
			("DOWNLOAD", McOnline("net.down") ? Rate(down) : "--", "MB/S"),
			("UPLOAD", McOnline("net.up") ? Rate(up) : "--", "MB/S"),
			("CASE FANS", rpm ? Val("fans.rpm").ToString("#,0", CultureInfo.InvariantCulture) : "--", "RPM"),
			fps ? ("GAME", Math.Round(Val("game.fps")).ToString("0", CultureInfo.InvariantCulture), "FPS")
				: ("GPU MEMORY", McOnline("gpu.vram") ? Val("gpu.vram").ToString("0.0", CultureInfo.InvariantCulture) : "--", "GB")
		};
		for (int i = 0; i < 4; i++)
		{
			float x = 1262f + i * 162f;
			TmIcon(c, i, x + 11f, 346f, i == 3 && !fps, down, up, t);
			McText(c, items[i].Label, x + 32f, 339f, 14f, TmMintDim, track: 1.2f, font: TmCap);
			float end = TmNumber(c, "tm.st" + i, items[i].Value, x, 368f, 44f, TmPaper, t, font: TmBold, track: -0.5f);
			McText(c, items[i].Unit, end + 6f, 396f, 14f, TmMintDim, track: 0.8f, font: TmCap);
			// a sparkline for the network, a meter for the others
			var s = new SKRect(x, 434f, x + 136f, 452f);
			if (i < 2)
			{
				TmSpark(c, i == 0 ? _tmDownH : _tmUpH, i == 0 ? down : up, s, frac, i == 0 ? TmMint : TmOrange);
			}
			else
			{
				float k = i == 2 ? (rpm ? Val("fans.rpm") / 3000f : 0f) : fps ? Val("game.fps") / 240f : Val("gpu.vram") / 32f;
				k = Math.Clamp(McEased("tm.m" + i, Math.Clamp(k, 0f, 1f), _tmDt, 2.0), 0f, 1f);
				TmMeter(c, s, k, i == 2 ? TmMint : TmOrange);
			}
		}
	}

	private void TmSpark(SKCanvas c, float[] h, float now, SKRect s, float frac, SKColor color)
	{
		float max = 0.05f;
		foreach (float v in h)
		{
			max = Math.Max(max, v);
		}
		max = Math.Max(max, now) * 1.1f;
		float step = s.Width / 60f;
		using var line = new SKPath();
		for (int i = 0; i < 61; i++)
		{
			float x = s.Right - (60 - i + frac) * step;
			float y = s.Bottom - h[i] / max * s.Height;
			if (i == 0)
			{
				line.MoveTo(Math.Max(x, s.Left), y);
			}
			else
			{
				line.LineTo(Math.Max(x, s.Left), y);
			}
		}
		float ye = s.Bottom - now / max * s.Height;
		line.LineTo(s.Right, ye);
		using var p = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(205, 223, 205, 60) };
		c.DrawLine(s.Left, s.Bottom + 0.5f, s.Right, s.Bottom + 0.5f, p);
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 2f;
		p.StrokeJoin = SKStrokeJoin.Round;
		p.Color = color;
		c.DrawPath(line, p);
		p.Style = SKPaintStyle.Fill;
		c.DrawCircle(s.Right, ye, 3f, p);
	}

	private static void TmMeter(SKCanvas c, SKRect s, float k, SKColor color)
	{
		using var p = new SKPaint { IsAntialias = true };
		const int n = 17;
		float gap = 2f, bw = (s.Width - gap * (n - 1)) / n;
		for (int i = 0; i < n; i++)
		{
			float lit = Math.Clamp(k * n - i, 0f, 1f);
			p.Color = lit > 0f ? new SKColor(color.Red, color.Green, color.Blue, (byte)(70 + 185 * lit)) : new SKColor(205, 223, 205, 40);
			c.DrawRect(s.Left + i * (bw + gap), s.Top + 6f, bw, s.Height - 6f, p);
		}
	}

	/// <summary>Line icons for the stats: download, upload, a fan that turns, a signal that pulses.</summary>
	private void TmIcon(SKCanvas c, int kind, float x, float y, bool memory, float down, float up, double t)
	{
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = TmMint };
		switch (kind)
		{
			case 0:
			case 1:
			{
				c.DrawCircle(x, y, 10f, p);
				float flow = kind == 0 ? down : up;
				float a = flow > 0.02f && t * (1.5 + Math.Min(4.0, flow)) % 1.0 < 0.5 ? 1f : 0.55f;
				p.Color = new SKColor(TmMint.Red, TmMint.Green, TmMint.Blue, (byte)(255 * a));
				float d = kind == 0 ? 1f : -1f;
				c.DrawLine(x, y - 5f * d, x, y + 5f * d, p);
				c.DrawLine(x - 4f, y + 1f * d, x, y + 5f * d, p);
				c.DrawLine(x + 4f, y + 1f * d, x, y + 5f * d, p);
				break;
			}
			case 2:
			{
				c.DrawCircle(x, y, 10f, p);
				c.Save();
				c.RotateDegrees((float)(_tmFan % 360.0), x, y);
				using (var f = new SKPaint { IsAntialias = true, Color = TmMint })
				{
					for (int i = 0; i < 3; i++)
					{
						c.Save();
						c.RotateDegrees(i * 120f, x, y);
						c.DrawOval(new SKRect(x - 2.6f, y - 8f, x + 2.6f, y - 1f), f);
						c.Restore();
					}
				}
				c.Restore();
				break;
			}
			default:
			{
				if (memory)
				{
					c.DrawRect(new SKRect(x - 9f, y - 6f, x + 9f, y + 6f), p);
					for (int i = -1; i <= 1; i++)
					{
						c.DrawLine(x + i * 5f, y + 6f, x + i * 5f, y + 10f, p);
						c.DrawLine(x + i * 5f, y - 6f, x + i * 5f, y - 10f, p);
					}
					break;
				}
				// signal arcs that light outward in turn
				for (int i = 0; i < 3; i++)
				{
					float lit = (float)((t * 1.5 - i * 0.25) % 1.0);
					p.Color = new SKColor(TmMint.Red, TmMint.Green, TmMint.Blue, (byte)(120 + 135 * MathF.Max(0f, 1f - lit * 2f)));
					float rr = 4f + i * 4.5f;
					using var arc = new SKPath();
					arc.AddArc(new SKRect(x - rr, y + 6f - rr, x + rr, y + 6f + rr), 225f, 90f);
					c.DrawPath(arc, p);
				}
				using (var f = new SKPaint { IsAntialias = true, Color = TmMint })
				{
					c.DrawCircle(x, y + 6f, 2f, f);
				}
				break;
			}
		}
	}

	// ---------- shared pieces ----------

	private static SKColor TmLerp(SKColor a, SKColor b, float k)
	{
		k = Math.Clamp(k, 0f, 1f);
		return new SKColor((byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k), (byte)(a.Alpha + (b.Alpha - a.Alpha) * k));
	}

	private static void TmHexPath(SKCanvas c, float x, float y, float r, float rot, SKPaint p)
	{
		using var path = new SKPath();
		for (int i = 0; i < 6; i++)
		{
			float a = (rot + i * 60f) * MathF.PI / 180f;
			float px = x + MathF.Cos(a) * r, py = y + MathF.Sin(a) * r;
			if (i == 0)
			{
				path.MoveTo(px, py);
			}
			else
			{
				path.LineTo(px, py);
			}
		}
		path.Close();
		c.DrawPath(path, p);
	}

	private static void TmBarcode(SKCanvas c, float x, float y, float w, float h, int seed, SKColor ink)
	{
		var rnd = new Random(seed);
		using var p = new SKPaint { IsAntialias = false, Color = ink };
		float xx = x;
		while (xx < x + w)
		{
			float bw = rnd.Next(1, 4);
			if (xx + bw > x + w)
			{
				break;
			}
			c.DrawRect(xx, y, bw, h, p);
			xx += bw + rnd.Next(1, 4);
		}
	}

	/// <summary>
	/// A line of bold figures in fixed cells, x at its left, centre or right (align -1, 0, 1) and top at the top of the
	/// figures. A changed character rolls up in its cell. Returns the right edge of the line.
	/// </summary>
	private float TmNumber(SKCanvas c, string id, string text, float x, float top, float size, SKColor color, double t, int align = -1, string font = TmBold, float track = -1f)
	{
		SKRect zb = McPath("0", font, size, 0f).TightBounds;
		float h = zb.Height, cw0 = zb.Width;
		float gap = size * 0.07f + track;
		int n = Math.Min(12, text.Length);
		var widths = new float[n];
		float total = 0f;
		for (int i = 0; i < n; i++)
		{
			char ch = text[i];
			widths[i] = char.IsDigit(ch) ? cw0 : ch == ' ' ? size * 0.25f : Math.Max(size * 0.12f, McPath(ch.ToString(), font, size, 0f).TightBounds.Width);
			total += widths[i] + (i < n - 1 ? gap : 0f);
		}
		float x0 = align < 0 ? x : align == 0 ? x - total / 2f : x - total;
		bool fresh = !_tmRoll.TryGetValue(id, out TmRollState? st);
		if (st == null)
		{
			st = _tmRoll[id] = new TmRollState();
		}
		for (int k = n; k < 12; k++)
		{
			st.Ch[k] = '\0';
		}
		using var paint = new SKPaint { IsAntialias = true, Color = color };
		float cx = x0;
		for (int i = 0; i < n; i++)
		{
			int k = n - 1 - i;
			char ch = text[i];
			if (st.Ch[k] != ch)
			{
				st.Was[k] = st.Ch[k];
				st.Since[k] = fresh ? -100.0 : t;
				st.Ch[k] = ch;
			}
			float s = (float)Math.Clamp((t - st.Since[k]) / 0.42, 0.0, 1.0);
			s = 1f - MathF.Pow(1f - s, 3f);
			float cw = widths[i];
			if (s < 1f)
			{
				c.Save();
				c.ClipRect(new SKRect(cx - size * 0.1f, top - size * 0.12f, cx + cw + size * 0.1f, top + h + size * 0.2f));
				TmGlyph(c, st.Was[k], cx, cw, top - zb.Top - s * h * 1.3f, font, size, paint);
				TmGlyph(c, ch, cx, cw, top - zb.Top + (1f - s) * h * 1.3f, font, size, paint);
				c.Restore();
			}
			else
			{
				TmGlyph(c, ch, cx, cw, top - zb.Top, font, size, paint);
			}
			cx += cw + gap;
		}
		return x0 + total;
	}

	private void TmGlyph(SKCanvas c, char ch, float cellX, float cellW, float dy, string font, float size, SKPaint paint)
	{
		if (ch == '\0' || ch == ' ')
		{
			return;
		}
		SKPath path = McPath(ch.ToString(), font, size, 0f);
		SKRect b = path.TightBounds;
		float dx = char.IsDigit(ch) ? cellX + (cellW - b.Width) / 2f - b.Left : cellX - b.Left;
		c.Save();
		c.Translate(dx, dy);
		c.DrawPath(path, paint);
		c.Restore();
	}

	// ---------- the static parts ----------

	private SKImage TmMakeBase()
	{
		var info = new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var s = SKSurface.Create(info);
		SKCanvas c = s.Canvas;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 480f), new[] { new SKColor(48, 51, 50), new SKColor(38, 41, 40) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, p);
		}
		using var paint = new SKPaint { IsAntialias = true };

		// the sage card with its dark strip
		paint.Color = TmMint;
		c.DrawRect(16f, 16f, 628f, 448f, paint);
		paint.Color = TmDark;
		c.DrawRect(16f, 16f, 56f, 448f, paint);
		// a crosshair mark and the vertical title
		using (var o = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, Color = TmInk })
		{
			c.DrawCircle(108f, 38f, 8f, o);
			c.DrawLine(108f, 26f, 108f, 50f, o);
			c.DrawLine(96f, 38f, 120f, 38f, o);
		}
		c.Save();
		c.Translate(96f, 446f);
		c.RotateDegrees(-90f);
		McText(c, "System load", 0f, 0f, 26f, TmInk, font: TmBold);
		c.Restore();
		using (var o = new SKPaint { IsAntialias = true, StrokeWidth = 2f, Color = TmInk })
		{
			c.DrawLine(134f, 30f, 134f, 446f, o);
		}
		// the grid
		SKRect P = TmPlot;
		using (var gp = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(32, 35, 34, 40) })
		{
			for (int i = 0; i <= 8; i++)
			{
				float x = P.Left + P.Width * i / 8f;
				c.DrawLine(x, P.Top - 34f, x, P.Bottom, gp);
			}
			for (int i = 0; i <= 8; i++)
			{
				float y = P.Bottom - P.Height * i / 8f;
				c.DrawLine(P.Left, y, P.Right, y, gp);
			}
			gp.Color = TmInk;
			gp.StrokeWidth = 1.5f;
			c.DrawLine(P.Left, P.Bottom, P.Right, P.Bottom, gp);
			c.DrawLine(P.Right + 10f, P.Top, P.Right + 10f, P.Bottom, gp);
			for (int i = 0; i <= 4; i++)
			{
				float y = P.Bottom - P.Height * i / 4f;
				c.DrawLine(P.Right + 10f, y, P.Right + 16f, y, gp);
				McText(c, (i * 25).ToString(CultureInfo.InvariantCulture), P.Right + 22f, y - 6f, 14f, TmInk, font: TmCap);
			}
		}
		// the bar names
		(string A, string B)[] names = { ("CPU", "LOAD %"), ("GPU", "LOAD %"), ("RAM", "USED %"), ("VRAM", "GB"), ("CPU", "TEMP °C"), ("GPU", "TEMP °C"), ("FANS", "DUTY %"), ("GPU", "FAN %") };
		float slot = P.Width / 8f;
		for (int i = 0; i < 8; i++)
		{
			float cx = P.Left + slot * (i + 0.5f);
			McText(c, names[i].A, cx, P.Bottom + 12f, 18f, TmInk, align: 0, font: TmBold);
			McText(c, names[i].B, cx, P.Bottom + 36f, 14f, new SKColor(32, 35, 34, 200), align: 0, track: 0.2f, font: TmCond);
		}
		// a legend at the foot
		paint.Color = TmOrange;
		c.DrawRect(150f, 436f, 12f, 12f, paint);
		McText(c, "HIGHEST", 170f, 436f, 14f, TmInk, track: 1f, font: TmCap);
		paint.Color = TmInk;
		c.DrawRect(262f, 441f, 18f, 3f, paint);
		McText(c, "RECENT PEAK", 288f, 436f, 14f, TmInk, track: 1f, font: TmCap);
		McText(c, "SCALE 0–100", 584f, 436f, 14f, TmInk, align: 1, track: 1f, font: TmCap);

		// the middle: header rule, the chart's frame and time marks
		McText(c, "SYSTEM TELEMETRY DASHBOARD", 702f, 24f, 15f, TmMint, track: 1.4f, font: TmCap);
		paint.Color = TmMint;
		c.DrawRect(680f, 48f, 542f, 2f, paint);
		SKRect C = TmChart;
		paint.Color = new SKColor(205, 223, 205, 120);
		c.DrawRect(680f, C.Bottom + 4f, 542f, 1.5f, paint);
		using (var gp = new SKPaint { IsAntialias = true, StrokeWidth = 1f, Color = new SKColor(205, 223, 205, 50), PathEffect = SKPathEffect.CreateDash(new[] { 1.5f, 4f }, 0f) })
		{
			string[] marks = { "−60 S", "−45", "−30", "−15", "NOW" };
			for (int i = 0; i <= 4; i++)
			{
				float x = C.Left + C.Width * i / 4f;
				c.DrawLine(x, C.Top - 4f, x, C.Bottom, gp);
				McText(c, marks[i], x, C.Top - 22f, 14f, TmMintDim, align: i == 0 ? -1 : i == 4 ? 1 : 0, track: 0.8f, font: TmCap);
			}
		}

		// the right: the two cards, the stats' rule
		paint.Color = TmOrange;
		c.DrawRect(1260f, 16f, 314f, 296f, paint);
		paint.Color = TmMint;
		c.DrawRect(1590f, 16f, 314f, 296f, paint);
		foreach (float x in new[] { 1260f, 1590f })
		{
			using var o = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = TmInk };
			// a crosshair box in the corner
			float bx = x + 280f, by = 32f;
			c.DrawRect(new SKRect(bx - 8f, by - 8f, bx + 8f, by + 8f), o);
			c.DrawLine(bx - 4f, by, bx + 4f, by, o);
			c.DrawLine(bx, by - 4f, bx, by + 4f, o);
		}
		paint.Color = new SKColor(205, 223, 205, 120);
		c.DrawRect(1260f, 322f, 644f, 1.5f, paint);
		// thin dividers between the stats
		for (int i = 1; i < 4; i++)
		{
			c.DrawRect(1262f + i * 162f - 14f, 338f, 1f, 116f, paint);
		}
		return s.Snapshot();
	}
}
