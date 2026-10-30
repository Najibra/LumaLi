using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace LcdStudio.Core;

/// <summary>
/// The machined-instrument pieces: graphite plates with bevels and screws, an analog clock with a turning
/// bezel, needle gauges, gears that spin with a reading, a grid of square LEDs (one per CPU thread) and an
/// inset readout screen. Graphite greys with a single orange accent.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color MechOrange = Color.FromArgb(255, 122, 26);

	private static readonly Color MechFace = Color.FromArgb(38, 40, 44);

	private readonly Dictionary<(int, int), (double Angle, double Time)> _gears = new Dictionary<(int, int), (double, double)>();

	private bool DrawMech(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "plate" when w.Kind == WidgetKind.Panel:
			Plate(g, w, r);
			return true;
		case "screen" when w.Kind == WidgetKind.Panel:
			InsetScreen(g, w, r);
			return true;
		case "mechclock" when w.Kind == WidgetKind.Text:
			MechClock(g, w, r, t);
			return true;
		case "mech" when w.Kind == WidgetKind.Gauge:
			MechGauge(g, w, r, t);
			return true;
		case "gear" when w.Kind == WidgetKind.Ring:
			Gear(g, w, r, t);
			return true;
		case "leds" when w.Kind == WidgetKind.CoreBars:
			Leds(g, w, r);
			return true;
		case "knob" when w.Kind == WidgetKind.Ring:
			Knob(g, w, r, t);
			return true;
		case "indicators" when w.Kind == WidgetKind.Panel:
			Indicators(g, w, r, t);
			return true;
		case "switches" when w.Kind == WidgetKind.Text:
			Switches(g, w, r);
			return true;
		case "vent" when w.Kind == WidgetKind.Panel:
			Vent(g, w, r);
			return true;
		case "bolt" when w.Kind == WidgetKind.Panel:
			HexBolt(g, r.X + r.Width / 2f, r.Y + r.Height / 2f, Math.Min(r.Width, r.Height) / 2f, w.Radius);
			return true;
		case "rail" when w.Kind == WidgetKind.Divider:
			Rail(g, w, r, t);
			return true;
		default:
			return DrawCarousel(g, w, r, t);
		}
	}

	/// <summary>Brushed graphite with a faint engineering grid.</summary>
	private static void Machined(Graphics g, Color top, Color bottom)
	{
		var full = new RectangleF(0f, 0f, 1920f, 480f);
		using (var wash = new LinearGradientBrush(full, top, bottom, 70f))
		{
			g.FillRectangle(wash, full);
		}
		using var grid = new Pen(Color.FromArgb(14, 255, 255, 255), 1f);
		for (int x = 0; x <= 1920; x += 24)
		{
			g.DrawLine(grid, x, 0, x, 480);
		}
		for (int y = 0; y <= 480; y += 24)
		{
			g.DrawLine(grid, 0, y, 1920, y);
		}
	}

	private static void Screw(Graphics g, float cx, float cy, float size, double angle)
	{
		using (var body = new LinearGradientBrush(new RectangleF(cx - size, cy - size, size * 2f, size * 2f), Color.FromArgb(120, 124, 130), Color.FromArgb(40, 42, 46), 45f))
		{
			g.FillEllipse(body, cx - size, cy - size, size * 2f, size * 2f);
		}
		using var slot = new Pen(Color.FromArgb(25, 26, 28), Math.Max(1.2f, size * 0.3f));
		float dx = (float)Math.Cos(angle) * size * 0.75f, dy = (float)Math.Sin(angle) * size * 0.75f;
		g.DrawLine(slot, cx - dx, cy - dy, cx + dx, cy + dy);
	}

	/// <summary>A raised graphite plate: a light bevel along the top and left, a dark one along the bottom and right, and a screw in each corner.</summary>
	private void Plate(Graphics g, Widget w, RectangleF r)
	{
		using (GraphicsPath shadow = Imaging.Round(new RectangleF(r.X + 4f, r.Y + 6f, r.Width, r.Height), w.Radius))
		using (var sb = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
		{
			g.FillPath(sb, shadow);
		}
		using (GraphicsPath path = Imaging.Round(r, w.Radius))
		{
			using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Imaging.Hex(w.Color, Color.FromArgb(54, 57, 62)), Imaging.Hex(w.Color2, Color.FromArgb(40, 42, 47)), 90f))
			{
				g.FillPath(fill, path);
			}
			using var light = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f);
			using var dark = new Pen(Color.FromArgb(120, 0, 0, 0), 1.4f);
			g.DrawLine(light, r.X + w.Radius, r.Y + 0.7f, r.Right - w.Radius, r.Y + 0.7f);
			g.DrawLine(light, r.X + 0.7f, r.Y + w.Radius, r.X + 0.7f, r.Bottom - w.Radius);
			g.DrawLine(dark, r.X + w.Radius, r.Bottom - 0.7f, r.Right - w.Radius, r.Bottom - 0.7f);
			g.DrawLine(dark, r.Right - 0.7f, r.Y + w.Radius, r.Right - 0.7f, r.Bottom - w.Radius);
		}
		if (w.Thickness > 0f)
		{
			float m = w.Thickness;
			Screw(g, r.X + m, r.Y + m, 5f, 0.6);
			Screw(g, r.Right - m, r.Y + m, 5f, 2.1);
			Screw(g, r.X + m, r.Bottom - m, 5f, 1.2);
			Screw(g, r.Right - m, r.Bottom - m, 5f, 0.2);
		}
		if (w.Text.Length > 0)
		{
			Text(g, w, new RectangleF(r.X + w.Thickness + 18f, r.Y + 10f, r.Width - w.Thickness * 2f - 36f, w.Size * 1.5f), Resolve(w.Text), Imaging.Hex(w.WarnColor, Color.FromArgb(205, 210, 216)), w.Size);
		}
	}

	/// <summary>A dark inset screen with an inner shadow and a thin orange frame, for readout text.</summary>
	private static void InsetScreen(Graphics g, Widget w, RectangleF r)
	{
		using (GraphicsPath path = Imaging.Round(r, w.Radius))
		{
			using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Color.FromArgb(22, 23, 25), Color.FromArgb(30, 32, 35), 90f))
			{
				g.FillPath(fill, path);
			}
			using var frame = new Pen(Color.FromArgb(150, MechOrange), 1.2f);
			g.DrawPath(frame, path);
		}
		using var inner = new Pen(Color.FromArgb(110, 0, 0, 0), 3f);
		g.DrawLine(inner, r.X + w.Radius, r.Y + 2.5f, r.Right - w.Radius, r.Y + 2.5f);
	}

	private static RectangleF Square(RectangleF r, out float cx, out float cy, out float radius)
	{
		float side = Math.Min(r.Width, r.Height);
		cx = r.X + r.Width / 2f;
		cy = r.Y + r.Height / 2f;
		radius = side / 2f;
		return new RectangleF(cx - radius, cy - radius, side, side);
	}

	private static PointF Polar(float cx, float cy, float radius, double degrees)
	{
		double a = degrees * Math.PI / 180.0;
		return new PointF(cx + (float)(Math.Cos(a) * radius), cy + (float)(Math.Sin(a) * radius));
	}

	/// <summary>
	/// The big instrument clock: a dark bezel with a numbered scale that turns slowly against the hands, an
	/// orange ring band with fine ticks, a polar-grid dial with an orange crosshair, chunky orange hour and
	/// minute hands and a smooth white second hand.
	/// </summary>
	private void MechClock(Graphics g, Widget w, RectangleF r, double t)
	{
		Square(r, out float cx, out float cy, out float R);
		RectangleF Circle(float radius) => new RectangleF(cx - radius, cy - radius, radius * 2f, radius * 2f);
		using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
		{
			g.FillEllipse(shadow, Circle(R * 0.99f) with { X = cx - R * 0.99f + 5f, Y = cy - R * 0.99f + 8f });
		}
		using (var bezel = new LinearGradientBrush(Circle(R), Color.FromArgb(70, 73, 78), Color.FromArgb(30, 31, 34), 60f))
		{
			g.FillEllipse(bezel, Circle(R));
		}
		// the turning bezel scale
		float turn = (float)(-t * 1.5 % 360.0);
		Font small = GetFont(w, Math.Max(11f, R * 0.072f));
		using (var tick = new Pen(Color.FromArgb(150, 205, 208, 212), 1f))
		using (var label = new SolidBrush(Color.FromArgb(185, 205, 208, 212)))
		using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
		{
			for (int i = 0; i < 120; i++)
			{
				double a = turn + i * 3.0 - 90.0;
				bool major = i % 10 == 0;
				g.DrawLine(tick, Polar(cx, cy, R * 0.97f, a), Polar(cx, cy, R * (major ? 0.9f : 0.94f), a));
				if (major)
				{
					PointF p = Polar(cx, cy, R * 0.84f, a);
					g.DrawString((i / 2).ToString("00", CultureInfo.InvariantCulture), small, label, p.X, p.Y, format);
				}
			}
		}
		// the orange band with its fine scale
		using (var band = new Pen(MechOrange, R * 0.075f))
		{
			g.DrawEllipse(band, Circle(R * 0.72f));
		}
		using (var fine = new Pen(Color.FromArgb(150, 40, 20, 5), 1f))
		{
			for (int i = 0; i < 60; i++)
			{
				g.DrawLine(fine, Polar(cx, cy, R * 0.745f, i * 6.0), Polar(cx, cy, R * (i % 5 == 0 ? 0.695f : 0.715f), i * 6.0));
			}
		}
		// the dial: polar grid and orange crosshair
		using (var dial = new SolidBrush(Color.FromArgb(34, 36, 40)))
		{
			g.FillEllipse(dial, Circle(R * 0.68f));
		}
		using (var grid = new Pen(Color.FromArgb(40, 220, 225, 230), 1f))
		{
			for (int k = 1; k <= 4; k++)
			{
				g.DrawEllipse(grid, Circle(R * 0.68f * k / 4.5f));
			}
			for (int i = 0; i < 24; i++)
			{
				g.DrawLine(grid, cx, cy, Polar(cx, cy, R * 0.66f, i * 15.0).X, Polar(cx, cy, R * 0.66f, i * 15.0).Y);
			}
		}
		// a dark four-point star across the dial
		using (var star = new SolidBrush(Color.FromArgb(120, 16, 17, 19)))
		{
			var pts = new PointF[8];
			for (int i = 0; i < 8; i++)
			{
				pts[i] = Polar(cx, cy, i % 2 == 0 ? R * 0.6f : R * 0.13f, i * 45.0 - 90.0);
			}
			g.FillPolygon(star, pts);
		}
		// orange numerals inside the dial, and orange marker blocks on the outer edge of the bezel
		Font dialFont = GetFont(w, Math.Max(11f, R * 0.078f));
		using (var orangeText = new SolidBrush(Color.FromArgb(220, MechOrange)))
		using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
		using (var marker = new Pen(MechOrange, R * 0.035f))
		{
			for (int i = 0; i < 12; i++)
			{
				double a = i * 30.0 - 90.0;
				PointF p = Polar(cx, cy, R * 0.55f, a);
				g.DrawString((i == 0 ? 12 : i).ToString(CultureInfo.InvariantCulture), dialFont, orangeText, p.X, p.Y, format);
				if (i % 3 == 0)
				{
					g.DrawLine(marker, Polar(cx, cy, R * 0.985f, a - 1.2), Polar(cx, cy, R * 0.985f, a + 1.2));
				}
			}
		}
		// a small seconds sub-dial low on the face
		float sr = R * 0.12f, sx = cx, sy = cy + R * 0.33f;
		using (var sub = new SolidBrush(Color.FromArgb(26, 27, 30)))
		{
			g.FillEllipse(sub, sx - sr, sy - sr, sr * 2f, sr * 2f);
		}
		using (var subRim = new Pen(Color.FromArgb(150, 215, 218, 222), 1f))
		{
			g.DrawEllipse(subRim, sx - sr, sy - sr, sr * 2f, sr * 2f);
			for (int i = 0; i < 12; i++)
			{
				g.DrawLine(subRim, Polar(sx, sy, sr * 0.95f, i * 30.0), Polar(sx, sy, sr * 0.78f, i * 30.0));
			}
		}
		using (var subHand = new Pen(MechOrange, 2f))
		{
			double subSeconds = DateTime.Now.Second + DateTime.Now.Millisecond / 1000.0;
			g.DrawLine(subHand, sx, sy, Polar(sx, sy, sr * 0.85f, subSeconds * 6.0 - 90.0).X, Polar(sx, sy, sr * 0.85f, subSeconds * 6.0 - 90.0).Y);
		}
		using (var cross = new Pen(Color.FromArgb(200, MechOrange), 1.4f))
		{
			g.DrawLine(cross, cx, cy - R * 0.98f, cx, cy + R * 0.98f);
			g.DrawLine(cross, cx - R * 0.66f, cy, cx - R * 0.5f, cy);
			g.DrawLine(cross, cx + R * 0.5f, cy, cx + R * 0.66f, cy);
		}
		// the hands
		DateTime now = DateTime.Now;
		double seconds = now.Second + now.Millisecond / 1000.0;
		double minutes = now.Minute + seconds / 60.0;
		double hours = now.Hour % 12 + minutes / 60.0;
		using (var handShadow = new Pen(Color.FromArgb(90, 0, 0, 0), R * 0.07f) { StartCap = LineCap.Flat, EndCap = LineCap.Triangle })
		using (var hourHand = new Pen(MechOrange, R * 0.07f) { EndCap = LineCap.Triangle })
		using (var minuteHand = new Pen(MechOrange, R * 0.05f) { EndCap = LineCap.Triangle })
		using (var secondHand = new Pen(Color.FromArgb(240, 242, 244), R * 0.018f))
		{
			PointF h = Polar(cx, cy, R * 0.42f, hours * 30.0 - 90.0), m = Polar(cx, cy, R * 0.6f, minutes * 6.0 - 90.0);
			g.DrawLine(handShadow, cx + 4f, cy + 6f, h.X + 4f, h.Y + 6f);
			g.DrawLine(handShadow, cx + 4f, cy + 6f, m.X + 4f, m.Y + 6f);
			g.DrawLine(hourHand, cx, cy, h.X, h.Y);
			g.DrawLine(minuteHand, cx, cy, m.X, m.Y);
			PointF tail = Polar(cx, cy, R * 0.16f, seconds * 6.0 + 90.0), tip = Polar(cx, cy, R * 0.66f, seconds * 6.0 - 90.0);
			g.DrawLine(secondHand, tail, tip);
		}
		using (var cap = new SolidBrush(MechOrange))
		{
			g.FillEllipse(cap, Circle(R * 0.06f));
		}
		Screw(g, cx, cy, R * 0.025f, t * 0.1);
	}

	/// <summary>
	/// A needle gauge on a 270° scale: bevelled rim, numbered ticks, an orange arc up to the reading (red past
	/// WarnAt), a tapered needle that glides with a soft shadow, the reading in a small window and the label (Print).
	/// </summary>
	private void MechGauge(Graphics g, Widget w, RectangleF r, double t)
	{
		Square(r, out float cx, out float cy, out float R);
		RectangleF Circle(float radius) => new RectangleF(cx - radius, cy - radius, radius * 2f, radius * 2f);
		using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
		{
			g.FillEllipse(shadow, cx - R + 4f, cy - R + 7f, R * 2f, R * 2f);
		}
		using (var rim = new LinearGradientBrush(Circle(R), Color.FromArgb(92, 96, 102), Color.FromArgb(28, 29, 32), 55f))
		{
			g.FillEllipse(rim, Circle(R));
		}
		using (var face = new SolidBrush(MechFace))
		{
			g.FillEllipse(face, Circle(R * 0.9f));
		}
		const float start = 135f, sweep = 270f;
		double f = Eased(w, Fraction(w), t);
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		// four rim screws, a red zone at the top of the scale and a fine inner scale
		for (int k = 0; k < 4; k++)
		{
			PointF s = Polar(cx, cy, R * 0.95f, 45.0 + k * 90.0);
			Screw(g, s.X, s.Y, Math.Max(2.5f, R * 0.03f), k * 0.7);
		}
		using (var zone = new Pen(Color.FromArgb(200, 225, 50, 35), R * 0.025f))
		{
			g.DrawArc(zone, Circle(R * 0.84f), start + sweep * 0.8f, sweep * 0.2f);
		}
		using (var fineScale = new Pen(Color.FromArgb(70, 215, 218, 222), 1f))
		{
			for (int i = 0; i <= 100; i++)
			{
				double a = start + sweep * i / 100.0;
				g.DrawLine(fineScale, Polar(cx, cy, R * 0.5f, a), Polar(cx, cy, R * (i % 10 == 0 ? 0.45f : 0.48f), a));
			}
		}
		using (var track = new Pen(Color.FromArgb(60, 255, 255, 255), R * 0.05f))
		{
			g.DrawArc(track, Circle(R * 0.72f), start, sweep);
		}
		if (f > 0.002)
		{
			using var arc = new Pen(warn ? Color.FromArgb(235, 60, 40) : MechOrange, R * 0.05f);
			g.DrawArc(arc, Circle(R * 0.72f), start, (float)(f * sweep));
		}
		Font numbers = GetFont(w, Math.Max(11f, R * 0.115f));
		using (var tick = new Pen(Color.FromArgb(200, 215, 218, 222), 1.4f))
		using (var minor = new Pen(Color.FromArgb(110, 215, 218, 222), 1f))
		using (var label = new SolidBrush(Color.FromArgb(235, 225, 228, 232)))
		using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
		{
			for (int i = 0; i <= 50; i++)
			{
				double a = start + sweep * i / 50.0;
				bool major = i % 5 == 0;
				g.DrawLine(major ? tick : minor, Polar(cx, cy, R * 0.86f, a), Polar(cx, cy, R * (major ? 0.78f : 0.82f), a));
				if (i % 10 == 0)
				{
					PointF p = Polar(cx, cy, R * 0.6f, a);
					double v = w.Min + (w.Max - w.Min) * i / 50.0;
					g.DrawString(v.ToString("0", CultureInfo.InvariantCulture), numbers, label, p.X, p.Y, format);
				}
			}
		}
		// the reading window and label
		string unit = Sensors.Def(w.Sensor).Unit;
		string text = Formatted(w) + (unit == "%" ? "%" : unit == "°C" ? "°" : unit.Length > 0 ? " " + unit : "");
		var window = new RectangleF(cx - R * 0.29f, cy + R * 0.3f, R * 0.58f, R * 0.27f);
		using (GraphicsPath wp = Imaging.Round(window, 4f))
		using (var wb = new SolidBrush(Color.FromArgb(22, 23, 25)))
		{
			g.FillPath(wb, wp);
		}
		Text(g, w, window, text, warn ? Color.FromArgb(255, 90, 60) : MechOrange, R * 0.2f);
		if (w.Print.Length > 0)
		{
			Text(g, w, new RectangleF(cx - R * 0.6f, window.Bottom + R * 0.03f, R * 1.2f, R * 0.2f), w.Print, Color.FromArgb(235, 225, 228, 232), R * 0.12f);
		}
		// the needle
		double na = (start + f * sweep) * Math.PI / 180.0;
		float ux = (float)Math.Cos(na), uy = (float)Math.Sin(na), px = -uy, py = ux;
		float len = R * 0.8f, tail = R * 0.16f, wid = R * 0.035f;
		PointF[] needle =
		{
			new PointF(cx + ux * len, cy + uy * len),
			new PointF(cx - ux * tail + px * wid, cy - uy * tail + py * wid),
			new PointF(cx - ux * tail - px * wid, cy - uy * tail - py * wid)
		};
		using (var ns = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
		{
			PointF[] sh = Array.ConvertAll(needle, (PointF p) => new PointF(p.X + 3f, p.Y + 5f));
			g.FillPolygon(ns, sh);
		}
		using (var nb = new SolidBrush(Color.FromArgb(240, 242, 244)))
		{
			g.FillPolygon(nb, needle);
		}
		using (var cap = new SolidBrush(MechOrange))
		{
			g.FillEllipse(cap, Circle(R * 0.09f));
		}
		Screw(g, cx, cy, R * 0.035f, 0.8);
		// a soft glare across the glass
		using (var glare = new Pen(Color.FromArgb(28, 255, 255, 255), R * 0.12f))
		{
			g.DrawArc(glare, Circle(R * 0.78f), 200f, 70f);
		}
	}

	/// <summary>
	/// An orange gear with Thickness teeth that turns at a speed set by its reading (faster when busier), the
	/// second gear of a pair (Print "reverse") turning the other way so they look meshed.
	/// </summary>
	private void Gear(Graphics g, Widget w, RectangleF r, double t)
	{
		Square(r, out float cx, out float cy, out float R);
		int teeth = Math.Max(6, (int)w.Thickness);
		var key = (w.X, w.Y);
		if (!_gears.TryGetValue(key, out var st))
		{
			st = (0.0, t);
		}
		double dt = Math.Clamp(t - st.Time, 0.0, 0.5);
		double speed = (12.0 + 140.0 * Fraction(w)) * (24.0 / teeth) * (w.Print == "reverse" ? -1.0 : 1.0);
		double angle = (st.Angle + speed * dt) % 360.0;
		_gears[key] = (angle, t);
		var pts = new List<PointF>();
		float outer = R, root = R * 0.84f;
		for (int i = 0; i < teeth; i++)
		{
			double a0 = angle + i * 360.0 / teeth, step = 360.0 / teeth;
			pts.Add(Polar(cx, cy, root, a0));
			pts.Add(Polar(cx, cy, outer, a0 + step * 0.12));
			pts.Add(Polar(cx, cy, outer, a0 + step * 0.42));
			pts.Add(Polar(cx, cy, root, a0 + step * 0.54));
		}
		using var gear = new GraphicsPath(FillMode.Alternate);
		gear.AddPolygon(pts.ToArray());
		gear.AddEllipse(cx - R * 0.22f, cy - R * 0.22f, R * 0.44f, R * 0.44f);
		for (int k = 0; k < 5; k++)
		{
			PointF h = Polar(cx, cy, R * 0.55f, angle + k * 72.0);
			gear.AddEllipse(h.X - R * 0.11f, h.Y - R * 0.11f, R * 0.22f, R * 0.22f);
		}
		using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
		{
			GraphicsState s = g.Save();
			g.TranslateTransform(4f, 6f);
			g.FillPath(shadow, gear);
			g.Restore(s);
		}
		using (var fill = new LinearGradientBrush(new RectangleF(cx - R, cy - R, R * 2f, R * 2f), Imaging.Mix(MechOrange, Color.White, 0.2), Imaging.Mix(MechOrange, Color.Black, 0.25), 60f))
		{
			g.FillPath(fill, gear);
		}
		using var edge = new Pen(Color.FromArgb(90, 60, 25, 0), 1f);
		g.DrawPath(edge, gear);
	}

	// ---------- small hardware ----------

	/// <summary>A hex bolt head with a lit top-left edge and a dark socket.</summary>
	private static void HexBolt(Graphics g, float cx, float cy, float size, float turn)
	{
		var pts = new PointF[6];
		for (int i = 0; i < 6; i++)
		{
			pts[i] = Polar(cx, cy, size, turn + i * 60.0);
		}
		using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
		{
			g.FillPolygon(shadow, Array.ConvertAll(pts, (PointF p) => new PointF(p.X + 1.5f, p.Y + 2.5f)));
		}
		using (var body = new LinearGradientBrush(new RectangleF(cx - size, cy - size, size * 2f, size * 2f), Color.FromArgb(130, 134, 140), Color.FromArgb(44, 46, 50), 50f))
		{
			g.FillPolygon(body, pts);
		}
		using var socket = new SolidBrush(Color.FromArgb(30, 31, 34));
		g.FillEllipse(socket, cx - size * 0.4f, cy - size * 0.4f, size * 0.8f, size * 0.8f);
	}

	/// <summary>
	/// A knurled orange rotary knob: ridges round the edge, a darker cap and a white index notch whose angle
	/// follows the reading over a 270° turn, with a small scale of dots around it.
	/// </summary>
	private void Knob(Graphics g, Widget w, RectangleF r, double t)
	{
		Square(r, out float cx, out float cy, out float R);
		double f = Eased(w, Fraction(w), t);
		double turn = 135.0 + f * 270.0;
		using (var scale = new SolidBrush(Color.FromArgb(150, 210, 214, 220)))
		{
			for (int i = 0; i <= 10; i++)
			{
				PointF p = Polar(cx, cy, R * 0.97f, 135.0 + i * 27.0);
				g.FillEllipse(scale, p.X - 1.5f, p.Y - 1.5f, 3f, 3f);
			}
		}
		float body = R * 0.82f;
		using (var shadow = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
		{
			g.FillEllipse(shadow, cx - body + 3f, cy - body + 5f, body * 2f, body * 2f);
		}
		using (var ring = new LinearGradientBrush(new RectangleF(cx - body, cy - body, body * 2f, body * 2f), Imaging.Mix(MechOrange, Color.White, 0.25), Imaging.Mix(MechOrange, Color.Black, 0.35), 55f))
		{
			g.FillEllipse(ring, cx - body, cy - body, body * 2f, body * 2f);
		}
		using (var ridge = new Pen(Color.FromArgb(110, 70, 30, 0), 1.2f))
		{
			for (int i = 0; i < 36; i++)
			{
				g.DrawLine(ridge, Polar(cx, cy, body * 0.98f, turn + i * 10.0), Polar(cx, cy, body * 0.82f, turn + i * 10.0));
			}
		}
		float cap = body * 0.66f;
		using (var top = new LinearGradientBrush(new RectangleF(cx - cap, cy - cap, cap * 2f, cap * 2f), Imaging.Mix(MechOrange, Color.White, 0.1), Imaging.Mix(MechOrange, Color.Black, 0.2), 230f))
		{
			g.FillEllipse(top, cx - cap, cy - cap, cap * 2f, cap * 2f);
		}
		using var notch = new Pen(Color.FromArgb(245, 248, 250), Math.Max(2f, R * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
		g.DrawLine(notch, Polar(cx, cy, cap * 0.25f, turn), Polar(cx, cy, cap * 0.9f, turn));
	}

	/// <summary>
	/// Columns of round indicator lamps (Thickness px apart). A lamp lights when its reading's level reaches its
	/// row; Text lists one sensor per column, and a running light sweeps any column with nothing to show.
	/// </summary>
	private void Indicators(Graphics g, Widget w, RectangleF r, double t)
	{
		float pitch = Math.Max(8f, w.Thickness);
		int cols = Math.Max(1, (int)(r.Width / pitch)), rows = Math.Max(1, (int)(r.Height / pitch));
		string[] sensors = w.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
		float d = pitch * 0.62f;
		using var off = new SolidBrush(Color.FromArgb(28, 29, 32));
		using var rim = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
		using var on = new SolidBrush(MechOrange);
		using var glow = new SolidBrush(Color.FromArgb(70, MechOrange));
		for (int c = 0; c < cols; c++)
		{
			int lit;
			if (c < sensors.Length)
			{
				SensorDef def = Sensors.Def(sensors[c].Trim());
				float v = _sensors.Get(def.Key) ?? def.Min;
				lit = (int)Math.Round(Math.Clamp((v - def.Min) / Math.Max(0.001, def.Max - def.Min), 0.0, 1.0) * rows);
			}
			else
			{
				lit = -1;
			}
			int runner = (int)(t * 4.0 + c * 3) % (rows + 4);
			for (int k = 0; k < rows; k++)
			{
				float x = r.X + c * pitch + (pitch - d) / 2f, y = r.Bottom - (k + 1) * pitch + (pitch - d) / 2f;
				bool isOn = lit >= 0 ? k < lit : k == runner;
				if (isOn)
				{
					g.FillEllipse(glow, x - 2f, y - 2f, d + 4f, d + 4f);
				}
				g.FillEllipse(isOn ? on : off, x, y, d, d);
				g.DrawEllipse(rim, x, y, d, d);
			}
		}
	}

	/// <summary>
	/// A row of toggle switches, each flipped up when its reading passes a threshold. Text holds entries of the
	/// form "LABEL:sensor:threshold" separated by '|'; the lever is orange when on.
	/// </summary>
	private void Switches(Graphics g, Widget w, RectangleF r)
	{
		string[] entries = w.Text.Split('|', StringSplitOptions.RemoveEmptyEntries);
		if (entries.Length == 0)
		{
			return;
		}
		float slot = r.Width / entries.Length;
		Font font = GetFont(w, w.Size);
		using var format = new StringFormat { Alignment = StringAlignment.Center };
		for (int i = 0; i < entries.Length; i++)
		{
			string[] parts = entries[i].Split(':');
			float threshold = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 50f;
			bool on = parts.Length > 1 && (_sensors.Get(parts[1]) ?? 0f) >= threshold;
			float cx = r.X + slot * (i + 0.5f), plateW = Math.Min(slot * 0.5f, 26f), plateH = r.Height - w.Size * 1.6f;
			var plate = new RectangleF(cx - plateW / 2f, r.Y, plateW, plateH);
			using (GraphicsPath pp = Imaging.Round(plate, 4f))
			using (var pb = new LinearGradientBrush(new RectangleF(plate.X, plate.Y - 1f, plate.Width, plate.Height + 2f), Color.FromArgb(70, 73, 78), Color.FromArgb(32, 34, 37), 90f))
			{
				g.FillPath(pb, pp);
			}
			float lever = plateH * 0.42f;
			float baseY = plate.Y + plateH / 2f;
			using (var lp = new Pen(on ? MechOrange : Color.FromArgb(150, 154, 160), plateW * 0.36f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
			{
				g.DrawLine(lp, cx, baseY, cx, on ? baseY - lever : baseY + lever);
			}
			using (var nut = new SolidBrush(Color.FromArgb(110, 114, 120)))
			{
				g.FillEllipse(nut, cx - plateW * 0.3f, baseY - plateW * 0.3f, plateW * 0.6f, plateW * 0.6f);
			}
			using var label = new SolidBrush(on ? MechOrange : Color.FromArgb(205, 210, 216));
			g.DrawString(parts[0], font, label, new RectangleF(cx - slot / 2f, r.Bottom - w.Size * 1.4f, slot, w.Size * 1.5f), format);
		}
	}

	/// <summary>Recessed vent slots across the widget, Thickness px tall each.</summary>
	private static void Vent(Graphics g, Widget w, RectangleF r)
	{
		float h = Math.Max(4f, w.Thickness);
		int n = Math.Max(1, (int)(r.Height / (h * 2f)));
		for (int i = 0; i < n; i++)
		{
			var slot = new RectangleF(r.X, r.Y + i * h * 2f, r.Width, h);
			using (GraphicsPath p = Imaging.Round(slot, h / 2f))
			using (var b = new SolidBrush(Color.FromArgb(20, 21, 23)))
			{
				g.FillPath(b, p);
			}
			using var lip = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);
			g.DrawLine(lip, slot.X + h / 2f, slot.Bottom + 0.5f, slot.Right - h / 2f, slot.Bottom + 0.5f);
		}
	}

	/// <summary>An orange signal rail along the widget's long side with round nodes at both ends and a pulse travelling along it.</summary>
	private static void Rail(Graphics g, Widget w, RectangleF r, double t)
	{
		bool vertical = r.Height > r.Width;
		float x1 = vertical ? r.X + r.Width / 2f : r.X, y1 = vertical ? r.Y : r.Y + r.Height / 2f;
		float x2 = vertical ? x1 : r.Right, y2 = vertical ? r.Bottom : y1;
		using (var line = new Pen(Color.FromArgb(210, MechOrange), Math.Max(1.5f, w.Thickness)))
		{
			g.DrawLine(line, x1, y1, x2, y2);
		}
		double p = t * 0.35 % 1.0;
		float px = x1 + (float)((x2 - x1) * p), py = y1 + (float)((y2 - y1) * p);
		using (var pulse = new SolidBrush(Color.FromArgb(120, 255, 210, 160)))
		{
			g.FillEllipse(pulse, px - 5f, py - 5f, 10f, 10f);
		}
		using var node = new SolidBrush(MechOrange);
		g.FillEllipse(node, x1 - 6f, y1 - 6f, 12f, 12f);
		g.FillEllipse(node, x2 - 6f, y2 - 6f, 12f, 12f);
		using var hole = new SolidBrush(Color.FromArgb(40, 42, 46));
		g.FillEllipse(hole, x1 - 2.5f, y1 - 2.5f, 5f, 5f);
		g.FillEllipse(hole, x2 - 2.5f, y2 - 2.5f, 5f, 5f);
	}

	/// <summary>A grid of square LEDs, one per CPU thread, each glowing orange with its thread's load.</summary>
	private void Leds(Graphics g, Widget w, RectangleF r)
	{
		int n = Math.Max(1, _sensors.CoreCount);
		int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n * r.Width / Math.Max(1f, r.Height))));
		int rows = (n + cols - 1) / cols;
		float cell = Math.Min(r.Width / cols, r.Height / rows);
		float size = cell * 0.72f;
		for (int i = 0; i < n; i++)
		{
			float v = _sensors.Get($"cpu.core{i + 1}") ?? 0f;
			float x = r.X + i % cols * cell, y = r.Y + i / cols * cell;
			var led = new RectangleF(x + (cell - size) / 2f, y + (cell - size) / 2f, size, size);
			using (var socket = new SolidBrush(Color.FromArgb(24, 25, 27)))
			{
				g.FillRectangle(socket, RectangleF.Inflate(led, 2f, 2f));
			}
			double f = Math.Clamp(v / 100.0, 0.0, 1.0);
			Color c = Imaging.Mix(Color.FromArgb(70, 40, 20), MechOrange, Math.Sqrt(f));
			if (f > 0.5)
			{
				using var glow = new SolidBrush(Color.FromArgb((int)(80 * (f - 0.5) * 2), MechOrange));
				g.FillRectangle(glow, RectangleF.Inflate(led, 4f, 4f));
			}
			using var b = new SolidBrush(c);
			g.FillRectangle(b, led);
		}
	}
}
