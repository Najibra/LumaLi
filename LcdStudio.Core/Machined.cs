using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Machined: a soft industrial device face, after a reference of matte orange metal under raised light-grey plates
/// with 45° chamfers and soft shadows, engraved hairlines and text, a recessed slot with two black discs, a strip of
/// blue lights, blue light leaking from under the plates, a blue gear and a dome, and a ruler with a marker.
/// - Left plate: the time in big grey chamfered figures that roll when they change, a small caption and notes.
/// - Orange body: the readings as engraved lines of text (processor, graphics, heat, memory); the two black discs
///   slide along their slot with the CPU and GPU load, each with a mark that turns with it; the blue strip lights with
///   the memory in use; a ruler with markers for the CPU (left) and GPU (right) temperature; small squares blink with
///   network traffic; the blue dome glows and pulses with the graphics load.
/// - Right plate: the day of the month in the same figures, the month and uptime, a vent whose slits fill with network
///   traffic, and the blue gear (turning with the case fans) peeking from under its edge.
/// - Blue light breathes out from under the left plate's edge, brighter when the processor is busy.
/// The static parts are drawn once (the right plate on its own layer, so the gear can turn beneath it).
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly SKColor MnDeep = new(112, 34, 10, 215), MnLight = new(255, 178, 134, 80), MnGreyInk = new(122, 122, 120);

	private static readonly SKPoint[] MnPlateA = { new(0f, 0f), new(400f, 0f), new(400f, 236f), new(470f, 306f), new(470f, 480f), new(0f, 480f) };

	private static readonly SKPoint[] MnPlateB = { new(1560f, 0f), new(1920f, 0f), new(1920f, 480f), new(1500f, 480f), new(1500f, 250f), new(1560f, 190f) };

	private SKImage? _mnBack, _mnFront;

	private double _mnAt = double.NaN, _mnGear, _mnDome, _mnFlowA, _mnFlowB;

	// the edges of the plates that blue light leaks from, top to bottom
	private static readonly SKPoint[] MnEdgeA = { new(404f, -10f), new(404f, 234f), new(474f, 304f), new(474f, 490f) };

	private static readonly SKPoint[] MnEdgeB = { new(1556f, -10f), new(1556f, 188f), new(1496f, 248f), new(1496f, 490f) };

	private readonly SKImage?[] _mnEdge = new SKImage?[2], _mnRim = new SKImage?[2];

	private readonly SKPoint[] _mnEdgeAt = new SKPoint[2];

	private float _mnDisc1 = float.NaN, _mnDisc2 = float.NaN;

	private bool DrawMachined(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "machined" || w.Kind != WidgetKind.Panel)
		{
			return false;
		}
		Machined(g, t);
		return true;
	}

	private void Machined(Graphics g, double t)
	{
		double dt = double.IsNaN(_mnAt) ? 0.0 : Math.Clamp(t - _mnAt, 0.0, 0.25);
		_mnAt = t;
		float cpu = Math.Clamp(McEased("mn.cpu", Val("cpu.load") / 100f, dt, 1.6), 0f, 1f);
		float gpu = Math.Clamp(McEased("mn.gpu", Val("gpu.load") / 100f, dt, 1.6), 0f, 1f);
		float ram = Math.Clamp(McEased("mn.ram", Val("ram.load") / 100f, dt, 1.6), 0f, 1f);
		bool cpuT = McOnline("cpu.temp"), gpuT = McOnline("gpu.temp");
		float ct = cpuT ? McEased("mn.ct", Val("cpu.temp"), dt, 1.6) : 0f;
		float gt = gpuT ? McEased("mn.gt", Val("gpu.temp"), dt, 1.6) : 0f;
		float net = (McOnline("net.down") ? Val("net.down") : 0f) + (McOnline("net.up") ? Val("net.up") : 0f);
		float netF = Math.Clamp(McEased("mn.net", MathF.Log10(1f + net * 10f) / 2f, dt, 3.0), 0f, 1f);
		bool rpmOn = McOnline("fans.rpm");
		double turns = rpmOn ? Math.Min(0.5, Val("fans.rpm") / 3000.0) : 0.1 + 0.3 * gpu;
		_mnGear += dt * turns * 360.0;
		_mnDome += dt * (0.4 + 1.4 * gpu);
		_mnFlowA += dt * (0.07 + 0.33 * cpu);
		_mnFlowB += dt * (0.07 + 0.33 * gpu);
		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;
		// the discs glide to their places with a little give
		float d1 = 1030f + 170f * cpu, d2 = 1250f + 130f * gpu;
		float e = 1f - MathF.Exp(-(float)dt * 4f);
		_mnDisc1 = float.IsNaN(_mnDisc1) ? d1 : _mnDisc1 + (d1 - _mnDisc1) * e;
		_mnDisc2 = float.IsNaN(_mnDisc2) ? d2 : _mnDisc2 + (d2 - _mnDisc2) * e;

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			if (_mnBack == null)
			{
				(_mnBack, _mnFront) = MnMakeBase();
			}
			c.DrawImage(_mnBack, 0f, 0f);

			// blue light from under the left plate's edge: brighter and flowing faster with the processor
			MnEdgeGlow(c, 0, MnPlateA, MnEdgeA, cpu, _mnFlowA, t);
			MnEdgeRim(c, 0, cpu, t);

			// left plate: caption, the time, notes
			MnPlateText(c, "LOCAL TIME", 40f, 186f, 11f);
			MnPlateText(c, "AND WHAT IT IS NOW", 40f, 200f, 11f);
			ClrNumber(c, "mn.time", now.ToString("HH:mm", CultureInfo.InvariantCulture), 40f, 222f, 104f, t, MnGreyInk);
			MnPlateText(c, now.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant() + ".", 40f, 352f, 10f);
			MnPlateText(c, "THE SYSTEM HAS BEEN RUNNING FOR " + MnUptime() + ".", 40f, 366f, 10f);
			MnPlateText(c, "EVERY READING ON THIS PANEL IS LIVE.", 40f, 380f, 10f);

			// orange body: the readings, engraved
			string Deg(bool on, float v) => on ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "°" : "--";
			string[] lines =
			{
				"Processor " + RrPct(cpu),
				"AND",
				"Graphics " + RrPct(gpu),
				"Heat " + Deg(cpuT, ct) + " / " + Deg(gpuT, gt),
				"Memory " + RrPct(ram),
				"MADE BY LUMALI"
			};
			for (int i = 0; i < lines.Length; i++)
			{
				MnEngrave(c, lines[i], 546f, 96f + i * 29f, 23f, "Segoe UI");
			}

			// the discs in their slot
			MnDisc(c, _mnDisc1, 305f, 44f, cpu);
			MnDisc(c, _mnDisc2, 305f, 44f, gpu);
			MnEngrave(c, "CPU  ▸", 952f, 386f, 12f, "Bahnschrift SemiCondensed", track: 3f);
			MnEngrave(c, "◂  GPU", 1396f, 386f, 12f, "Bahnschrift SemiCondensed", track: 3f, align: 1);
			string fans = McOnline("fans.rpm") ? Val("fans.rpm").ToString("#,0", CultureInfo.InvariantCulture) + " rpm" : "--";
			MnEngrave(c, "Fans " + fans, 1030f, 60f, 21f, "Segoe UI");
			MnEngrave(c, "Network " + (net >= 1f ? net.ToString("0.0", CultureInfo.InvariantCulture) + " MB/s" : (net * 1024f).ToString("0", CultureInfo.InvariantCulture) + " KB/s"), 1030f, 90f, 21f, "Segoe UI");
			MnEngrave(c, "Up " + MnUptime().ToLowerInvariant(), 1030f, 120f, 21f, "Segoe UI");

			// the blue strip: memory
			const int dots = 14;
			for (int i = 0; i < dots; i++)
			{
				float x = 540f + i * 25f;
				float k = (i + 0.5f) / dots;
				bool lit = k <= ram;
				float edge = Math.Clamp((ram - (float)i / dots) * dots, 0f, 1f);
				MnBlueDot(c, x, 446f, lit ? 1f : edge * 0.6f);
			}
			MnEngrave(c, "MEMORY " + RrPct(ram), 540f, 410f, 12f, "Bahnschrift SemiCondensed", track: 3f);

			// the ruler: CPU temperature on the left, GPU on the right
			MnMarker(c, 1440f, cpuT ? ct : 0f, left: true);
			MnMarker(c, 1440f, gpuT ? gt : 0f, left: false);

			// squares that blink with network traffic, the dome that pulses with the graphics load
			bool blink = net > 0.02f && t * (2.0 + 6.0 * netF) % 1.0 < 0.5;
			using (var p = new SKPaint { IsAntialias = true, Color = MnDeep })
			{
				if (blink)
				{
					c.DrawRect(926f, 198f, 11f, 11f, p);
				}
			}
			float pulse = 0.5f + 0.5f * MathF.Sin((float)(_mnDome * Math.PI * 2.0));
			MnDome(c, 1250f, 482f, 84f, 0.35f + 0.45f * gpu + 0.2f * pulse * gpu);

			// the gear beneath the right plate, then the plate, then what is on it
			MnGear(c, 1548f, 70f, 52f, (float)(_mnGear % 360.0));
			MnEdgeGlow(c, 1, MnPlateB, MnEdgeB, gpu, _mnFlowB, t);
			c.DrawImage(_mnFront!, 0f, 0f);
			MnEdgeRim(c, 1, gpu, t);
			MnPlateText(c, "DAY", 1604f, 186f, 11f);
			MnPlateText(c, "OF THE MONTH", 1604f, 200f, 11f);
			ClrNumber(c, "mn.day", now.ToString("dd", CultureInfo.InvariantCulture), 1604f, 222f, 104f, t, MnGreyInk);
			MnPlateText(c, now.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant() + ". WEEK " + ISOWeek.GetWeekOfYear(now).ToString(CultureInfo.InvariantCulture) + ".", 1604f, 352f, 10f);
			MnPlateText(c, "NETWORK " + (net >= 1f ? net.ToString("0.0", CultureInfo.InvariantCulture) + " MB/S" : (net * 1024f).ToString("0", CultureInfo.InvariantCulture) + " KB/S") + ".", 1604f, 366f, 10f);
			// the vent's slits fill with network traffic
			using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(96, 96, 94) })
			{
				int lit = (int)MathF.Round(netF * 12f);
				for (int i = 0; i < 12; i++)
				{
					p.Color = i < lit ? new SKColor(70, 130, 220) : new SKColor(150, 150, 148);
					c.DrawRect(1668f + i * 8f, 34f, 3f, 48f, p);
				}
			}
		});
	}

	private string MnUptime()
	{
		if (!McOnline("uptime.h"))
		{
			return "A WHILE";
		}
		float h = Val("uptime.h");
		int hh = (int)h, mm = (int)((h - hh) * 60f);
		return hh >= 24 ? $"{hh / 24} D {hh % 24} H" : $"{hh} H {mm:00} M";
	}

	/// <summary>
	/// A glow bar: blue light leaking from under a plate edge onto the metal (a cached soft spill and a pale core line,
	/// breathing, brighter with the reading) with three pulses of light gliding down the edge, faster with the reading.
	/// </summary>
	private void MnEdgeGlow(SKCanvas c, int i, SKPoint[] plate, SKPoint[] edge, float level, double flow, double t)
	{
		using var path = new SKPath();
		path.AddPoly(edge, false);
		if (_mnEdge[i] == null)
		{
			SKRect b = path.Bounds;
			b.Inflate(80f, 0f);
			b = new SKRect(b.Left, 0f, b.Right, 480f);
			var info = new SKImageInfo((int)MathF.Ceiling(b.Width), 480, SKColorType.Bgra8888, SKAlphaType.Premul);
			using var s = SKSurface.Create(info);
			SKCanvas e = s.Canvas;
			e.Clear(SKColors.Transparent);
			e.Translate(-b.Left, 0f);
			using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
			p.StrokeWidth = 22f;
			p.Color = new SKColor(25, 95, 255, 165);
			p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14f);
			e.DrawPath(path, p);
			p.StrokeWidth = 8f;
			p.Color = new SKColor(55, 145, 255, 240);
			p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f);
			e.DrawPath(path, p);
			p.StrokeWidth = 4.5f;
			p.Color = new SKColor(110, 195, 255, 255);
			p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.8f);
			e.DrawPath(path, p);
			p.StrokeWidth = 1.6f;
			p.Color = new SKColor(205, 238, 255, 255);
			p.MaskFilter = null;
			e.DrawPath(path, p);
			_mnEdge[i] = s.Snapshot();
			_mnEdgeAt[i] = new SKPoint(b.Left, 0f);
			// the same light catching the plate's own rim, kept to the plate
			e.Clear(SKColors.Transparent);
			e.Save();
			using (SKPath inside = MnPoly(plate))
			{
				e.ClipPath(inside, SKClipOperation.Intersect, true);
			}
			p.StrokeWidth = 16f;
			p.Color = new SKColor(50, 140, 255, 120);
			p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f);
			e.DrawPath(path, p);
			p.StrokeWidth = 3f;
			p.Color = new SKColor(90, 175, 255, 170);
			p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f);
			e.DrawPath(path, p);
			e.Restore();
			_mnRim[i] = s.Snapshot();
		}
		c.Save();
		using (SKPath cut = MnPoly(plate))
		{
			c.ClipPath(cut, SKClipOperation.Difference, true);
		}
		float breathe = 0.88f + 0.12f * MathF.Sin((float)t * 1.1f + i * 1.7f);
		using (var p = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Clamp(255f * (0.78f + 0.22f * level) * breathe, 0f, 255f)) })
		{
			c.DrawImage(_mnEdge[i], _mnEdgeAt[i].X, _mnEdgeAt[i].Y, p);
		}
		using var pm = new SKPathMeasure(path, false);
		float len = pm.Length;
		for (int k = 0; k < 3; k++)
		{
			float u = (float)((flow + k / 3.0) % 1.0);
			if (!pm.GetPosition(u * len, out SKPoint at))
			{
				continue;
			}
			float fade = MathF.Sin(u * MathF.PI) * (0.45f + 0.55f * level);
			// a comet tail of light along the edge behind the pulse
			float head = u * len, tail = Math.Max(0f, head - (90f + 90f * level));
			if (head - tail > 4f && pm.GetPosition(tail, out SKPoint from))
			{
				using var seg = new SKPath();
				pm.GetSegment(tail, head, seg, true);
				using var tp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = SKColors.White };
				using (var sh = SKShader.CreateLinearGradient(from, at, new[] { new SKColor(40, 130, 255, 0), new SKColor(40, 130, 255, (byte)(200 * fade)) }, null, SKShaderTileMode.Clamp))
				{
					tp.Shader = sh;
					tp.StrokeWidth = 14f;
					tp.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f);
					c.DrawPath(seg, tp);
				}
				using (var sh = SKShader.CreateLinearGradient(from, at, new[] { new SKColor(170, 225, 255, 0), new SKColor(200, 238, 255, (byte)(240 * fade)) }, null, SKShaderTileMode.Clamp))
				{
					tp.Shader = sh;
					tp.StrokeWidth = 3f;
					tp.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.8f);
					c.DrawPath(seg, tp);
				}
			}
			SkiaKit.Glow(c, at.X, at.Y, 34f, 44f, new SKColor(35, 125, 255, (byte)(180 * fade)), SKBlendMode.SrcOver);
			SkiaKit.Glow(c, at.X, at.Y, 7f, 16f, new SKColor(200, 236, 255, (byte)(230 * fade)), SKBlendMode.SrcOver);
		}
		c.Restore();
	}

	/// <summary>The edge light caught on the plate's own rim (drawn over the plate).</summary>
	private void MnEdgeRim(SKCanvas c, int i, float level, double t)
	{
		if (_mnRim[i] == null)
		{
			return;
		}
		float breathe = 0.88f + 0.12f * MathF.Sin((float)t * 1.1f + i * 1.7f);
		using var p = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Clamp(255f * (0.6f + 0.4f * level) * breathe, 0f, 255f)) };
		c.DrawImage(_mnRim[i], _mnEdgeAt[i].X, _mnEdgeAt[i].Y, p);
	}

	/// <summary>Text cut into the orange metal: a light lip under a dark groove.</summary>
	private void MnEngrave(SKCanvas c, string text, float x, float y, float size, string font, float track = 0f, int align = -1)
	{
		McText(c, text, x, y + 1.2f, size, MnLight, align: align, track: track, font: font);
		McText(c, text, x, y, size, MnDeep, align: align, track: track, font: font);
	}

	/// <summary>Small print on the grey plates.</summary>
	private void MnPlateText(SKCanvas c, string text, float x, float y, float size)
	{
		McText(c, text, x, y, size, new SKColor(108, 108, 106), track: 0.6f, font: "Bahnschrift SemiCondensed");
	}

	private static SKPath MnPoly(SKPoint[] pts)
	{
		var p = new SKPath();
		p.AddPoly(pts, true);
		return p;
	}

	// ---------- the static parts ----------

	private (SKImage Back, SKImage Front) MnMakeBase()
	{
		var info = new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var back = SKSurface.Create(info);
		SKCanvas c = back.Canvas;
		// the orange metal: soft light from the top left, a few broad sheens
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(1920f, 480f), new[] { new SKColor(238, 112, 60), new SKColor(220, 88, 42), new SKColor(196, 70, 32) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(0f, 0f, 1920f, 480f, p);
		}
		SkiaKit.Glow(c, 760f, 120f, 520f, 220f, new SKColor(255, 160, 110, 40), SKBlendMode.Plus);
		SkiaKit.Glow(c, 1300f, 300f, 360f, 200f, new SKColor(255, 140, 90, 26), SKBlendMode.Plus);
		var rnd = new Random(9);
		using (var p = new SKPaint { IsAntialias = true })
		{
			for (int i = 0; i < 1800; i++)
			{
				p.Color = rnd.Next(2) == 0 ? new SKColor(255, 210, 180, (byte)rnd.Next(6, 16)) : new SKColor(90, 30, 10, (byte)rnd.Next(6, 16));
				c.DrawCircle((float)rnd.NextDouble() * 1920f, (float)rnd.NextDouble() * 480f, 0.6f + (float)rnd.NextDouble(), p);
			}
		}

		// engraved hairlines: a frame round the readings and lines running off to the slot, the strip and the ruler
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, StrokeJoin = SKStrokeJoin.Miter })
		{
			void Groove(params float[] v)
			{
				using var path = new SKPath();
				path.MoveTo(v[0], v[1]);
				for (int i = 2; i < v.Length; i += 2)
				{
					path.LineTo(v[i], v[i + 1]);
				}
				p.Color = MnLight;
				c.Save();
				c.Translate(0f, 1.3f);
				c.DrawPath(path, p);
				c.Restore();
				p.Color = MnDeep;
				c.DrawPath(path, p);
			}
			Groove(510f, 26f, 900f, 26f, 900f, 200f, 860f, 240f, 860f, 288f, 830f, 318f, 510f, 318f);
			Groove(900f, 140f, 940f, 140f, 940f, 250f, 960f, 250f);
			Groove(830f, 318f, 830f, 418f, 912f, 418f);
			Groove(1470f, 360f, 1470f, 300f, 1488f, 282f, 1488f, 250f);
			Groove(980f, 26f, 1380f, 26f, 1410f, 56f);
			Groove(1180f, 420f, 1460f, 420f, 1480f, 440f, 1480f, 480f);
		}
		// screw rings and little squares
		foreach (var (x, y) in new[] { (980f, 120f), (1340f, 120f), (1440f, 400f) })
		{
			MnRing(c, x, y);
		}
		using (var p = new SKPaint { IsAntialias = true, Color = MnDeep })
		{
			c.DrawRect(510f, 340f, 9f, 9f, p);
			c.DrawRect(926f, 222f, 11f, 11f, p);
			p.Style = SKPaintStyle.Stroke;
			p.StrokeWidth = 1.4f;
			c.DrawRect(526f, 340f, 9f, 9f, p);
			c.DrawRect(926f, 198f, 11f, 11f, p);
		}
		// the logo box and the engraved bars
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = MnDeep })
		{
			c.DrawRect(546f, 44f, 70f, 40f, p);
			p.Style = SKPaintStyle.Fill;
			for (int i = 0; i < 10; i++)
			{
				c.DrawRect(546f + i * 9f, 270f, 3f, 36f, p);
			}
		}
		McText(c, "LM", 581f, 52f, 24f, MnDeep, align: 0, track: 2f, font: "Bahnschrift SemiBold");
		// the slot: a groove round a deep stadium recess
		var slot = new SKRect(970f, 252f, 1430f, 358f);
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = MnDeep })
		{
			c.DrawRoundRect(SKRect.Inflate(slot, 18f, 18f), 71f, 71f, p);
		}
		using (var p = new SKPaint { IsAntialias = true })
		{
			using var sh = SKShader.CreateLinearGradient(new SKPoint(0f, slot.Top), new SKPoint(0f, slot.Bottom), new[] { new SKColor(150, 48, 16), new SKColor(206, 80, 38), new SKColor(232, 104, 56) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
			p.Shader = sh;
			c.DrawRoundRect(slot, 53f, 53f, p);
			p.Shader = null;
			c.Save();
			c.ClipRoundRect(new SKRoundRect(slot, 53f, 53f), SKClipOperation.Intersect, true);
			p.Style = SKPaintStyle.Stroke;
			p.StrokeWidth = 14f;
			p.Color = new SKColor(70, 18, 4, 110);
			p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f);
			c.DrawRoundRect(new SKRect(slot.Left, slot.Top - 4f, slot.Right, slot.Bottom), 53f, 53f, p);
			p.MaskFilter = null;
			c.Restore();
			p.Style = SKPaintStyle.Fill;
			p.Color = MnDeep;
			c.DrawCircle(1400f, 305f, 4f, p);
		}
		// the blue strip's dark glass
		var strip = new SKRect(522f, 432f, 900f, 460f);
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) })
		{
			c.DrawRoundRect(new SKRect(strip.Left + 2f, strip.Top + 4f, strip.Right + 2f, strip.Bottom + 4f), 14f, 14f, p);
		}
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, strip.Top), new SKPoint(0f, strip.Bottom), new[] { new SKColor(60, 66, 80), new SKColor(18, 20, 28) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawRoundRect(strip, 14f, 14f, p);
		}
		// the ruler, hatch and a small vertical caption
		using (var p = new SKPaint { IsAntialias = true, StrokeWidth = 1.4f, Color = MnDeep })
		{
			c.DrawLine(1440f, 34f, 1440f, 234f, p);
			for (int i = 0; i <= 40; i++)
			{
				float y = 34f + i * 5f;
				float len = i % 10 == 0 ? 14f : i % 5 == 0 ? 9f : 5f;
				c.DrawLine(1440f - len / 2f, y, 1440f + len / 2f, y, p);
			}
			p.StrokeWidth = 2f;
			c.Save();
			c.ClipRect(new SKRect(1470f, 34f, 1500f, 120f));
			for (float y = 20f; y < 140f; y += 7f)
			{
				c.DrawLine(1470f, y + 18f, 1500f, y, p);
			}
			c.Restore();
		}
		McText(c, "100", 1426f, 30f, 9f, MnDeep, align: 1, font: "Bahnschrift SemiCondensed");
		McText(c, "20", 1426f, 230f, 9f, MnDeep, align: 1, font: "Bahnschrift SemiCondensed");
		c.Save();
		c.Translate(1486f, 230f);
		c.RotateDegrees(-90f);
		McText(c, "°C  CPU  ◂  ▸  GPU", 0f, 0f, 10f, MnDeep, track: 2f, font: "Bahnschrift SemiCondensed");
		c.Restore();

		// the left plate, raised, with its little marks
		MnRaise(c, MnPlateA);
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(24, 24, 24) })
		{
			c.DrawRect(40f, 40f, 30f, 8f, p);
			c.DrawRect(40f, 56f, 30f, 8f, p);
			c.DrawRect(40f, 440f, 10f, 10f, p);
			p.Style = SKPaintStyle.Stroke;
			p.StrokeWidth = 1.5f;
			c.DrawRect(58f, 440f, 10f, 10f, p);
		}

		// the right plate on its own layer, so the gear can turn underneath
		using var front = SKSurface.Create(info);
		SKCanvas f = front.Canvas;
		f.Clear(SKColors.Transparent);
		MnRaise(f, MnPlateB);
		// a chamfered vent box, the bottom recess with two dark discs, small squares
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(140, 140, 138) })
		{
			using var box = new SKPath();
			box.AddPoly(new[] { new SKPoint(1656f, 24f), new SKPoint(1780f, 24f), new SKPoint(1780f, 80f), new SKPoint(1766f, 94f), new SKPoint(1656f, 94f) }, true);
			f.DrawPath(box, p);
		}
		var rec = new SKRect(1560f, 420f, 1900f, 540f);
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, rec.Top), new SKPoint(0f, 480f), new[] { new SKColor(150, 150, 148), new SKColor(196, 196, 194) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh })
		{
			f.DrawRoundRect(rec, 60f, 60f, p);
		}
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(26, 26, 30) })
		{
			f.DrawCircle(1622f, 482f, 38f, p);
			f.DrawCircle(1838f, 482f, 38f, p);
			p.Color = new SKColor(255, 255, 255, 40);
			f.DrawCircle(1612f, 466f, 10f, p);
			f.DrawCircle(1828f, 466f, 10f, p);
			p.Color = new SKColor(24, 24, 24);
			f.DrawRect(1880f, 120f, 10f, 10f, p);
			p.Style = SKPaintStyle.Stroke;
			p.StrokeWidth = 1.5f;
			f.DrawRect(1862f, 120f, 10f, 10f, p);
		}
		return (back.Snapshot(), front.Snapshot());
	}

	/// <summary>A raised light-grey plate: a soft shadow on the metal, a cool grey face, a lit top edge.</summary>
	private static void MnRaise(SKCanvas c, SKPoint[] pts)
	{
		using SKPath path = MnPoly(pts);
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(60, 16, 4, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14f) })
		{
			c.Save();
			c.Translate(10f, 14f);
			c.DrawPath(path, p);
			c.Restore();
		}
		SKRect b = path.Bounds;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(b.Left, b.Top), new SKPoint(b.Right, b.Bottom), new[] { new SKColor(236, 236, 234), new SKColor(214, 214, 212), new SKColor(196, 196, 194) }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawPath(path, p);
		}
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = new SKColor(255, 255, 255, 170) })
		{
			c.Save();
			c.ClipPath(path, SKClipOperation.Intersect, true);
			c.DrawPath(path, p);
			c.Restore();
		}
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(120, 120, 118, 120) })
		{
			c.DrawPath(path, p);
		}
	}

	private static void MnRing(SKCanvas c, float x, float y)
	{
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, Color = new SKColor(150, 48, 16, 150) };
		c.DrawCircle(x, y + 1f, 18f, p);
		p.Color = new SKColor(255, 170, 120, 90);
		p.StrokeWidth = 2f;
		c.DrawCircle(x, y + 2.5f, 21f, p);
		p.Color = MnDeep;
		p.StrokeWidth = 1.4f;
		c.DrawCircle(x, y, 22f, p);
	}

	/// <summary>A black disc in the slot, with a small orange mark that turns with its reading.</summary>
	private static void MnDisc(SKCanvas c, float x, float y, float r, float v)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(40, 10, 2, 140), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) };
		c.DrawCircle(x + 4f, y + 7f, r, p);
		p.MaskFilter = null;
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(x - r * 0.3f, y - r * 0.4f), r * 1.3f, new[] { new SKColor(58, 58, 64), new SKColor(22, 22, 26), new SKColor(10, 10, 12) }, new[] { 0f, 0.6f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawCircle(x, y, r, p);
			p.Shader = null;
		}
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 1.5f;
		p.Color = new SKColor(255, 255, 255, 40);
		c.DrawCircle(x, y, r - 1f, p);
		p.Style = SKPaintStyle.Fill;
		float a = (-135f + 270f * v - 90f) * MathF.PI / 180f;
		p.Color = new SKColor(240, 110, 60);
		c.DrawCircle(x + MathF.Cos(a) * r * 0.72f, y + MathF.Sin(a) * r * 0.72f, 2.6f, p);
	}

	private static void MnBlueDot(SKCanvas c, float x, float y, float k)
	{
		using var p = new SKPaint { IsAntialias = true };
		if (k > 0.02f)
		{
			SkiaKit.Glow(c, x, y, 16f, 12f, new SKColor(80, 170, 255, (byte)(150 * k)), SKBlendMode.Plus);
		}
		p.Color = new SKColor((byte)(40 + 130 * k), (byte)(50 + 170 * k), (byte)(70 + 185 * k));
		c.DrawCircle(x, y, 4f, p);
		if (k > 0.02f)
		{
			p.Color = new SKColor(235, 248, 255, (byte)(230 * k));
			c.DrawCircle(x, y, 2f, p);
		}
	}

	/// <summary>A triangle on the ruler at a temperature (20–100 °C, 100 at the top), on its left or right.</summary>
	private void MnMarker(SKCanvas c, float x, float temp, bool left)
	{
		float y = 234f - Math.Clamp((temp - 20f) / 80f, 0f, 1f) * 200f;
		float dir = left ? -1f : 1f;
		using var tri = new SKPath();
		tri.MoveTo(x + dir * 10f, y);
		tri.LineTo(x + dir * 20f, y - 6f);
		tri.LineTo(x + dir * 20f, y + 6f);
		tri.Close();
		using var p = new SKPaint { IsAntialias = true, Color = left ? MnDeep : new SKColor(30, 70, 150, 230) };
		c.DrawPath(tri, p);
		if (temp > 0f)
		{
			McText(c, Math.Round(temp).ToString("0", CultureInfo.InvariantCulture) + "°", x + dir * 24f, y - 6f, 12f, p.Color, align: left ? 1 : -1, font: "Bahnschrift SemiBold");
		}
	}

	/// <summary>The blue dome rising from the bottom edge, its glow set by k.</summary>
	private static void MnDome(SKCanvas c, float x, float y, float r, float k)
	{
		SkiaKit.Glow(c, x, y - r * 0.6f, r * 2.2f, r * 1.4f, new SKColor(60, 150, 255, (byte)Math.Clamp(90f * k, 0f, 140f)), SKBlendMode.Plus);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(40, 10, 2, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) };
		c.DrawCircle(x + 3f, y + 4f, r + 2f, p);
		p.MaskFilter = null;
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(x - r * 0.25f, y - r * 0.7f), r * 1.4f, new[] { new SKColor((byte)(90 + 80 * k), (byte)(150 + 70 * k), 255), new SKColor(40, 100, 210), new SKColor(16, 50, 130) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawCircle(x, y, r, p);
			p.Shader = null;
		}
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 2f;
		p.Color = new SKColor(200, 230, 255, (byte)(100 + 120 * Math.Clamp(k, 0f, 1f)));
		using var arc = new SKPath();
		arc.AddArc(new SKRect(x - r + 3f, y - r + 3f, x + r - 3f, y + r - 3f), 200f, 140f);
		c.DrawPath(arc, p);
	}

	/// <summary>A blue gear with rounded teeth, turned to deg.</summary>
	private static void MnGear(SKCanvas c, float x, float y, float r, float deg)
	{
		using var gear = new SKPath();
		const int teeth = 22;
		for (int i = 0; i < teeth * 4; i++)
		{
			float a = (i / (float)(teeth * 4)) * MathF.PI * 2f;
			float rr = (i % 4) < 2 ? r : r - 6f;
			float px = x + MathF.Cos(a) * rr, py = y + MathF.Sin(a) * rr;
			if (i == 0)
			{
				gear.MoveTo(px, py);
			}
			else
			{
				gear.LineTo(px, py);
			}
		}
		gear.Close();
		c.Save();
		c.RotateDegrees(deg, x, y);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(40, 10, 2, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f) };
		c.Save();
		c.Translate(3f, 5f);
		c.DrawPath(gear, p);
		c.Restore();
		p.MaskFilter = null;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(x - r, y - r), new SKPoint(x + r, y + r), new[] { new SKColor(70, 120, 200), new SKColor(28, 60, 130) }, null, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawPath(gear, p);
			p.Shader = null;
		}
		p.Color = new SKColor(20, 40, 90);
		c.DrawCircle(x, y, r * 0.3f, p);
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 1.5f;
		p.Color = new SKColor(150, 190, 255, 90);
		c.DrawCircle(x, y, r * 0.62f, p);
		c.Restore();
	}
}
