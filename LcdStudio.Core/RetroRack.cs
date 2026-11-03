using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Retro Rack: a wall of matte orange devices, after a rendered "game cores" wall (no consoles, logos or brand text).
/// - Lumali Pro (left): a cream technical-drawing screen with the time, the date and five readings drawn as dimension
///   lines; three orange knobs (CPU, GPU, memory) whose marks turn to the reading on a spring; buttons 1–3 that light
///   (3 power, 2 when the processor passes 50 %, 1 flashing red on an alert); red/blue lamps; black switch tabs.
/// - Porthole (second): the real moon phase on a slowly turning moon behind glass, a ring of ticks with the weekdays
///   (today marked), a seconds marker running round the ring; vent pads in the corners.
/// - Brushed silver: two black knobs with white levers for the CPU and GPU temperature on printed scales, four lamps
///   lighting with the processor load, and amber light rising behind metal fins with the memory in use.
/// - Speaker: a mesh grille with an embossed emblem; light ripples out across it, faster with the graphics load.
/// - Fan: a VU meter for the system's watts (CPU + GPU) with a sprung needle, and a fan turning with the case fans'
///   speed behind orange grille bars; a slider shows the graphics fan.
/// Everything that does not move is drawn once into a cached picture.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly SKColor RrOrange = new(246, 108, 46), RrInk = new(34, 30, 28), RrCream = new(236, 232, 220);

	private SKImage? _rrBase, _rrMoon;

	private double _rrAt = double.NaN, _rrFan, _rrSpk, _rrBeat;

	private bool DrawRetroRack(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "retrorack" || w.Kind != WidgetKind.Panel)
		{
			return DrawMachined(g, w, r, t);
		}
		RetroRack(g, t);
		return true;
	}

	private void RetroRack(Graphics g, double t)
	{
		double dt = double.IsNaN(_rrAt) ? 0.0 : Math.Clamp(t - _rrAt, 0.0, 0.25);
		_rrAt = t;
		float cpu = Math.Clamp(McEased("rr.cpu", Val("cpu.load") / 100f, dt, 2.0), 0f, 1f);
		float gpu = Math.Clamp(McEased("rr.gpu", Val("gpu.load") / 100f, dt, 2.0), 0f, 1f);
		float ram = Math.Clamp(McEased("rr.ram", Val("ram.load") / 100f, dt, 2.0), 0f, 1f);
		bool cpuT = McOnline("cpu.temp"), gpuT = McOnline("gpu.temp");
		float ct = cpuT ? McEased("rr.ct", Val("cpu.temp"), dt, 2.0) : 0f;
		float gt = gpuT ? McEased("rr.gt", Val("gpu.temp"), dt, 2.0) : 0f;
		float watts = (McOnline("cpu.power") ? Val("cpu.power") : 0f) + (McOnline("gpu.power") ? Val("gpu.power") : 0f);
		float wEase = RrSpring(Math.Clamp(watts / 600f, 0f, 1f), dt);
		bool rpmOn = McOnline("fans.rpm");
		float rpm = rpmOn ? Val("fans.rpm") : 0f;
		float gfan = McOnline("gpu.fan") ? Math.Clamp(McEased("rr.gf", Val("gpu.fan") / 100f, dt, 2.0), 0f, 1f) : 0f;
		float net = (McOnline("net.down") ? Val("net.down") : 0f) + (McOnline("net.up") ? Val("net.up") : 0f);
		bool alert = ct >= 85f || gt >= 85f || cpu >= 0.95f || gpu >= 0.95f;
		// the fan is kept under ~1.1 turns a second so 30 frames a second never strobe it backwards
		double turns = rpmOn ? Math.Min(1.1, rpm / 1500.0) : 0.15 + 0.9 * gpu;
		_rrFan += dt * turns * 360.0;
		_rrSpk += dt * (0.25 + 0.9 * gpu);
		_rrBeat += dt * (1.0 + 2.0 * gpu);
		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			_rrBase ??= RrMakeBase();
			c.DrawImage(_rrBase, 0f, 0f);

			// ----- Lumali Pro -----
			RrScreen(c, now, cpu, gpu, ram, cpuT, ct, gpuT, gt);
			RrKnobMark(c, 100f, 335f, 40f, cpu);
			RrKnobMark(c, 222f, 335f, 40f, gpu);
			RrKnobMark(c, 344f, 335f, 40f, ram);
			var wh = new SKColor(255, 240, 228, 235);
			McText(c, RrPct(cpu), 100f, 386f, 15f, wh, align: 0, track: 1f, font: "Bahnschrift SemiCondensed", stroke: 0.4f);
			McText(c, RrPct(gpu), 222f, 386f, 15f, wh, align: 0, track: 1f, font: "Bahnschrift SemiCondensed", stroke: 0.4f);
			McText(c, RrPct(ram), 344f, 386f, 15f, wh, align: 0, track: 1f, font: "Bahnschrift SemiCondensed", stroke: 0.4f);
			RrButton(c, 70f, 432f, 15f, new SKColor(230, 40, 28), alert && t * 1.6 % 1.0 < 0.55);
			RrButton(c, 128f, 432f, 15f, new SKColor(240, 100, 40), cpu >= 0.5f);
			RrButton(c, 186f, 432f, 15f, new SKColor(250, 150, 80), true);
			RrLamp(c, 300f, 432f, 4.5f, new SKColor(255, 40, 30), net > 0.05f ? t * 6.0 % 1.0 < 0.5 : t % 2.0 < 1.0);
			RrLamp(c, 318f, 432f, 4.5f, new SKColor(40, 140, 255), true);

			// ----- porthole -----
			RrMoonDraw(c, now, t);
			float sa = (now.Second + now.Millisecond / 1000f) / 60f * 360f;
			RrRingMark(c, 704f, 240f, 150f, sa, RrCream, 9f);
			int day = ((int)now.DayOfWeek + 6) % 7;
			RrRingMark(c, 704f, 240f, 196f, -90f + day * (360f / 7f) + 90f, new SKColor(255, 236, 210), 8f, outward: true);

			// ----- brushed silver -----
			RrLever(c, 1076f, 110f, 46f, cpuT ? Math.Clamp((ct - 20f) / 80f, 0f, 1f) : 0f);
			RrLever(c, 1076f, 370f, 46f, gpuT ? Math.Clamp((gt - 20f) / 80f, 0f, 1f) : 0f);
			McText(c, cpuT ? Math.Round(ct).ToString("0", CultureInfo.InvariantCulture) + "°" : "--", 1182f, 150f, 20f, RrInk, align: 1, font: "Bahnschrift SemiBold");
			McText(c, gpuT ? Math.Round(gt).ToString("0", CultureInfo.InvariantCulture) + "°" : "--", 1182f, 410f, 20f, RrInk, align: 1, font: "Bahnschrift SemiBold");
			for (int i = 0; i < 4; i++)
			{
				float th = new[] { 0.1f, 0.4f, 0.7f, 0.9f }[i];
				var col = new[] { new SKColor(255, 160, 80), new SKColor(255, 120, 50), new SKColor(240, 70, 35), new SKColor(220, 30, 25) }[i];
				RrButton(c, 996f, 288f - i * 32f, 11f, col, cpu >= th);
			}
			RrFins(c, new SKRect(1036f, 176f, 1170f, 304f), ram);

			// ----- speaker -----
			RrRipples(c, gpu);

			// ----- fan -----
			RrMeter(c, wEase, watts);
			RrFan(c, 1730f, 345f, (float)(_rrFan % 360.0), (float)turns);
			RrSlider(c, 1880f, 290f, 440f, gfan);
			McText(c, rpmOn ? "FAN " + Math.Round(rpm).ToString("#,0", CultureInfo.InvariantCulture) + " RPM" : "FAN --", 1586f, 206f, 14f, wh, track: 2f, font: "Bahnschrift SemiCondensed", stroke: 0.4f);
		});
	}

	private static string RrPct(float v) => Math.Round(v * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

	private float _rrNeedle, _rrNeedleV;

	/// <summary>The meter needle on a spring that overshoots a little.</summary>
	private float RrSpring(float target, double dt)
	{
		float f = (float)dt;
		for (int i = 0; i < 4; i++)
		{
			float h = f / 4f;
			_rrNeedleV += ((target - _rrNeedle) * 60f - _rrNeedleV * 9f) * h;
			_rrNeedle += _rrNeedleV * h;
		}
		return _rrNeedle;
	}

	// ---------- the static wall ----------

	private SKImage RrMakeBase()
	{
		using var surface = SKSurface.Create(new SKImageInfo(1920, 480, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		c.Clear(new SKColor(30, 16, 10));
		var rnd = new Random(11);

		// ----- Lumali Pro -----
		RrPlate(c, new SKRect(12f, 12f, 452f, 468f), 10f, RrOrange);
		McText(c, "Lumali Pro", 30f, 22f, 24f, new SKColor(255, 238, 226), font: "Segoe UI|B");
		McText(c, "MK-II", 434f, 30f, 12f, new SKColor(255, 220, 200, 200), align: 1, track: 2f, font: "Bahnschrift SemiCondensed");
		// screen bezel and cream screen with a technical drawing
		RrInset(c, new SKRect(30f, 58f, 434f, 252f), 6f);
		var scr = new SKRect(38f, 66f, 426f, 244f);
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, scr.Top), new SKPoint(0f, scr.Bottom), new[] { new SKColor(244, 241, 232), new SKColor(226, 222, 210) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(scr, p);
		}
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0, 0, 0, 22) })
		{
			for (float x = scr.Left + 12f; x < scr.Right; x += 24f)
			{
				c.DrawLine(x, scr.Top, x, scr.Bottom, p);
			}
			for (float y = scr.Top + 12f; y < scr.Bottom; y += 24f)
			{
				c.DrawLine(scr.Left, y, scr.Right, y, p);
			}
			p.Color = new SKColor(20, 20, 20, 120);
			c.DrawLine(226f, scr.Top + 8f, 226f, scr.Bottom - 8f, p);
			c.DrawLine(48f, 172f, 214f, 172f, p);
			c.DrawLine(48f, 168f, 48f, 176f, p);
			c.DrawLine(214f, 168f, 214f, 176f, p);
			c.DrawRect(150f, 196f, 56f, 30f, p);
			c.DrawLine(150f, 196f, 206f, 226f, p);
			p.Color = new SKColor(20, 20, 20, 200);
			p.StrokeWidth = 3f;
			c.DrawLine(48f, 236f, 96f, 236f, p);
		}
		// knob faces, labels, switch tabs
		var label = new SKColor(255, 238, 226, 235);
		foreach ((float x, string name) in new[] { (100f, "CPU"), (222f, "GPU"), (344f, "MEMORY") })
		{
			McText(c, name, x, 276f, 15f, label, align: 0, track: 3f, font: "Bahnschrift SemiCondensed", stroke: 0.4f);
			RrKnob(c, x, 335f, 40f);
		}
		for (int i = 0; i < 3; i++)
		{
			McText(c, (i + 1).ToString(CultureInfo.InvariantCulture), 48f + i * 58f, 426f, 12f, label, align: 0, font: "Bahnschrift SemiCondensed");
		}
		using (var p = new SKPaint { IsAntialias = true })
		{
			for (int i = 0; i < 3; i++)
			{
				var tab = new SKRect(352f + i * 30f, 420f, 374f + i * 30f, 446f);
				p.Color = new SKColor(0, 0, 0, 90);
				c.DrawRoundRect(SKRect.Inflate(tab, 1f, 1f), 3f, 3f, p);
				p.Color = new SKColor(26, 24, 24);
				c.DrawRoundRect(tab, 3f, 3f, p);
				p.Color = new SKColor(255, 255, 255, 60);
				c.DrawRect(tab.Left + 4f, tab.Top + 6f, tab.Width - 8f, 1.5f, p);
				c.DrawRect(tab.Left + 4f, tab.Top + 12f, tab.Width - 8f, 1.5f, p);
			}
		}

		// ----- porthole -----
		RrPlate(c, new SKRect(464f, 12f, 944f, 468f), 10f, RrOrange);
		foreach (SKRect pad in new[] { new SKRect(478f, 26f, 578f, 90f), new SKRect(830f, 26f, 930f, 90f), new SKRect(478f, 390f, 578f, 454f), new SKRect(830f, 390f, 930f, 454f) })
		{
			RrVent(c, pad);
		}
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(24, 22, 22) })
		{
			c.DrawRoundRect(new SKRect(458f, 190f, 470f, 290f), 3f, 3f, p);
			c.DrawRoundRect(new SKRect(938f, 190f, 950f, 290f), 3f, 3f, p);
		}
		// the ring: ticks, weekday names, four triangles
		using (var p = new SKPaint { IsAntialias = true, StrokeWidth = 1.4f, Color = new SKColor(255, 228, 200, 200) })
		{
			for (int i = 0; i < 120; i++)
			{
				float a = i * 3f * MathF.PI / 180f;
				float r0 = i % 10 == 0 ? 158f : i % 2 == 0 ? 162f : 165f;
				c.DrawLine(704f + MathF.Cos(a) * r0, 240f + MathF.Sin(a) * r0, 704f + MathF.Cos(a) * 170f, 240f + MathF.Sin(a) * 170f, p);
			}
			p.Style = SKPaintStyle.Stroke;
			c.DrawCircle(704f, 240f, 172f, p);
			p.Style = SKPaintStyle.Fill;
			for (int k = 0; k < 4; k++)
			{
				float a = (k * 90f - 90f) * MathF.PI / 180f;
				float cx = 704f + MathF.Cos(a) * 181f, cy = 240f + MathF.Sin(a) * 181f;
				using var tri = new SKPath();
				tri.MoveTo(cx + MathF.Cos(a) * 6f, cy + MathF.Sin(a) * 6f);
				tri.LineTo(cx - MathF.Sin(a) * 5f - MathF.Cos(a) * 4f, cy + MathF.Cos(a) * 5f - MathF.Sin(a) * 4f);
				tri.LineTo(cx + MathF.Sin(a) * 5f - MathF.Cos(a) * 4f, cy - MathF.Cos(a) * 5f - MathF.Sin(a) * 4f);
				tri.Close();
				c.DrawPath(tri, p);
			}
		}
		string[] days = { "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };
		for (int i = 0; i < 7; i++)
		{
			float deg = i * (360f / 7f);
			c.Save();
			c.Translate(704f, 240f);
			c.RotateDegrees(deg);
			McText(c, days[i], 0f, -206f, 10f, new SKColor(255, 228, 200, 210), align: 0, track: 2f, font: "Bahnschrift SemiCondensed");
			c.Restore();
		}
		// the porthole rim: dark metal with bolts
		using (var sh = SKShader.CreateSweepGradient(new SKPoint(704f, 240f), new[] { new SKColor(60, 58, 58), new SKColor(20, 20, 22), new SKColor(70, 68, 66), new SKColor(18, 18, 20), new SKColor(60, 58, 58) }, null))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh, Style = SKPaintStyle.Stroke, StrokeWidth = 12f })
		{
			c.DrawCircle(704f, 240f, 146f, p);
		}
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) })
		{
			c.DrawCircle(706f, 243f, 153f, p);
		}

		// ----- brushed silver -----
		var al = new SKRect(956f, 12f, 1196f, 468f);
		using (var p = new SKPaint { Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) })
		{
			c.DrawRoundRect(new SKRect(al.Left + 3f, al.Top + 6f, al.Right + 3f, al.Bottom + 6f), 8f, 8f, p);
		}
		c.Save();
		c.ClipRoundRect(new SKRoundRect(al, 8f, 8f), SKClipOperation.Intersect, true);
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(al.Left, al.Top), new SKPoint(al.Right, al.Bottom), new[] { new SKColor(214, 214, 212), new SKColor(176, 176, 176), new SKColor(204, 204, 202) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(al, p);
		}
		using (var p = new SKPaint { StrokeWidth = 1f })
		{
			for (float y = al.Top; y < al.Bottom; y += 1f)
			{
				byte v = (byte)rnd.Next(0, 255);
				p.Color = v > 128 ? new SKColor(255, 255, 255, (byte)rnd.Next(4, 26)) : new SKColor(0, 0, 0, (byte)rnd.Next(4, 22));
				float x0 = al.Left + (float)rnd.NextDouble() * 60f;
				c.DrawLine(x0, y + 0.5f, al.Right - (float)rnd.NextDouble() * 60f, y + 0.5f, p);
			}
		}
		c.Restore();
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(255, 255, 255, 140) })
		{
			c.DrawRoundRect(SKRect.Inflate(al, -1f, -1f), 8f, 8f, p);
		}
		RrDial(c, 1076f, 110f, 46f, "CPU °C");
		RrDial(c, 1076f, 370f, 46f, "GPU °C");
		McText(c, "LOAD", 996f, 172f, 10f, RrInk, align: 0, track: 2f, font: "Bahnschrift SemiCondensed");
		RrInset(c, new SKRect(1032f, 172f, 1174f, 308f), 4f);

		// ----- speaker -----
		RrPlate(c, new SKRect(1208f, 12f, 1552f, 468f), 10f, RrOrange);
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(40, 18, 8, 200) })
		{
			for (int col = 0; col < 3; col++)
			{
				for (float y = 60f; y < 420f; y += 9f)
				{
					if ((int)((y - 60f) / 9f) % 12 == 11)
					{
						continue;
					}
					c.DrawCircle(1222f + col * 8f, y, 2.2f, p);
				}
			}
			p.Color = new SKColor(24, 22, 22);
			c.DrawRoundRect(new SKRect(1202f, 30f, 1214f, 90f), 3f, 3f, p);
			c.DrawRoundRect(new SKRect(1202f, 390f, 1214f, 450f), 3f, 3f, p);
		}
		var mesh = new SKRect(1254f, 34f, 1536f, 446f);
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(34, 30, 26) })
		{
			c.DrawRoundRect(SKRect.Inflate(mesh, 6f, 6f), 16f, 16f, p);
		}
		c.Save();
		c.ClipRoundRect(new SKRoundRect(mesh, 12f, 12f), SKClipOperation.Intersect, true);
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(1395f, 220f), 300f, new[] { new SKColor(150, 118, 58), new SKColor(110, 84, 40) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(mesh, p);
		}
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(26, 20, 12, 230) })
		{
			int row = 0;
			for (float y = mesh.Top + 3f; y < mesh.Bottom; y += 5.2f, row++)
			{
				for (float x = mesh.Left + 3f + (row % 2) * 3f; x < mesh.Right; x += 6f)
				{
					c.DrawCircle(x, y, 1.9f, p);
				}
			}
		}
		// the embossed emblem: rough concentric rings and a swirl
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = new SKColor(20, 14, 8, 200) })
		{
			foreach ((float rad, float wid, float dash) in new[] { (128f, 9f, 60f), (96f, 6f, 40f), (62f, 10f, 26f), (30f, 7f, 14f) })
			{
				p.StrokeWidth = wid;
				p.PathEffect = SKPathEffect.CreateDash(new[] { dash, dash * 0.35f }, rad);
				c.DrawCircle(1395f, 240f, rad, p);
			}
			p.PathEffect = null;
		}
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(mesh.Left, mesh.Top), new SKPoint(mesh.Right, mesh.Bottom), new[] { new SKColor(255, 230, 170, 40), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 60) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(mesh, p);
		}
		c.Restore();

		// ----- fan -----
		RrPlate(c, new SKRect(1564f, 12f, 1908f, 468f), 10f, RrOrange);
		// VU meter face
		var face = new SKRect(1584f, 30f, 1800f, 190f);
		RrInset(c, SKRect.Inflate(face, 6f, 6f), 10f);
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, face.Top), new SKPoint(0f, face.Bottom), new[] { new SKColor(240, 232, 208), new SKColor(220, 208, 178) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawRoundRect(face, 8f, 8f, p);
		}
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = RrInk })
		{
			const float px = 1692f, py = 250f, rr = 190f;
			using var arc = new SKPath();
			arc.AddArc(new SKRect(px - rr, py - rr, px + rr, py + rr), -90f - 38f, 76f);
			c.DrawPath(arc, p);
			p.Color = new SKColor(210, 40, 28);
			p.StrokeWidth = 5f;
			using var red = new SKPath();
			red.AddArc(new SKRect(px - rr + 4f, py - rr + 4f, px + rr - 4f, py + rr - 4f), -90f + 38f * 0.5f, 38f * 0.5f);
			c.DrawPath(red, p);
			p.Color = RrInk;
			for (int i = 0; i <= 24; i++)
			{
				float a = (-90f - 38f + i * 76f / 24f) * MathF.PI / 180f;
				float len = i % 4 == 0 ? 12f : 6f;
				p.StrokeWidth = i % 4 == 0 ? 1.6f : 1f;
				c.DrawLine(px + MathF.Cos(a) * rr, py + MathF.Sin(a) * rr, px + MathF.Cos(a) * (rr + len), py + MathF.Sin(a) * (rr + len), p);
				if (i % 4 == 0)
				{
					McText(c, (i / 4 * 100).ToString(CultureInfo.InvariantCulture), px + MathF.Cos(a) * (rr + 22f), py + MathF.Sin(a) * (rr + 22f) - 5f, 10f, RrInk, align: 0, font: "Bahnschrift SemiCondensed");
				}
			}
		}
		McText(c, "WATTS", 1692f, 150f, 13f, RrInk, align: 0, track: 4f, font: "Bahnschrift SemiCondensed");
		// fan opening
		using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 140), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f) })
		{
			c.DrawCircle(1732f, 349f, 118f, p);
		}
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(1730f, 345f), 116f, new[] { new SKColor(20, 18, 18), new SKColor(36, 32, 30) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { IsAntialias = true, Shader = sh })
		{
			c.DrawCircle(1730f, 345f, 114f, p);
		}
		// buttons and slider slot on the right, bolts
		RrButton(c, 1880f, 60f, 10f, new SKColor(236, 222, 196), false);
		RrButton(c, 1880f, 100f, 10f, new SKColor(236, 222, 196), false);
		RrInset(c, new SKRect(1872f, 282f, 1888f, 448f), 4f);
		foreach (var (bx, by) in new[] { (1580f, 452f), (1890f, 452f), (1890f, 26f), (1226f, 26f), (1226f, 454f), (26f, 454f), (438f, 454f), (478f, 240f) })
		{
			RrBolt(c, bx, by);
		}

		// a faint warm wear over all the orange plates
		using (var p = new SKPaint { IsAntialias = true })
		{
			for (int i = 0; i < 2600; i++)
			{
				p.Color = rnd.Next(2) == 0 ? new SKColor(255, 220, 190, (byte)rnd.Next(8, 22)) : new SKColor(90, 30, 10, (byte)rnd.Next(8, 22));
				c.DrawCircle((float)rnd.NextDouble() * 1920f, (float)rnd.NextDouble() * 480f, 0.6f + (float)rnd.NextDouble() * 1.4f, p);
			}
		}
		return surface.Snapshot();
	}

	/// <summary>A matte orange plate with a drop shadow, a soft light from the top left and bevelled edges.</summary>
	private static void RrPlate(SKCanvas c, SKRect r, float rad, SKColor col)
	{
		using var p = new SKPaint { IsAntialias = true };
		p.Color = new SKColor(0, 0, 0, 150);
		p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7f);
		c.DrawRoundRect(new SKRect(r.Left + 4f, r.Top + 7f, r.Right + 4f, r.Bottom + 7f), rad, rad, p);
		p.MaskFilter = null;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom),
			new[] { new SKColor((byte)Math.Min(255, col.Red + 22), (byte)Math.Min(255, col.Green + 22), (byte)Math.Min(255, col.Blue + 14)), col, new SKColor((byte)(col.Red * 0.9f), (byte)(col.Green * 0.86f), (byte)(col.Blue * 0.86f)) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawRoundRect(r, rad, rad, p);
			p.Shader = null;
		}
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 2f;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), new[] { new SKColor(255, 190, 150, 150), new SKColor(255, 190, 150, 0), new SKColor(60, 20, 8, 0), new SKColor(60, 20, 8, 160) }, new[] { 0f, 0.35f, 0.65f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawRoundRect(SKRect.Inflate(r, -1f, -1f), rad, rad, p);
		}
	}

	/// <summary>A recess: dark inside edge at the top left, a lit lip at the bottom right.</summary>
	private static void RrInset(SKCanvas c, SKRect r, float rad)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(60, 22, 8, 200) };
		c.DrawRoundRect(r, rad, rad, p);
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 2f;
		using var sh = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), new[] { new SKColor(30, 10, 4, 200), new SKColor(255, 190, 150, 140) }, null, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = sh;
		c.DrawRoundRect(r, rad, rad, p);
	}

	/// <summary>A raised vent pad with a recessed field of perforations.</summary>
	private static void RrVent(SKCanvas c, SKRect r)
	{
		RrPlate(c, r, 8f, new SKColor(232, 100, 50));
		var inner = SKRect.Inflate(r, -14f, -14f);
		RrInset(c, inner, 6f);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(26, 12, 6, 230) };
		for (float y = inner.Top + 5f; y < inner.Bottom - 2f; y += 5f)
		{
			for (float x = inner.Left + 5f; x < inner.Right - 2f; x += 5f)
			{
				c.DrawCircle(x, y, 1.5f, p);
			}
		}
	}

	private static void RrBolt(SKCanvas c, float x, float y)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120) };
		c.DrawCircle(x + 1f, y + 2f, 6.5f, p);
		using var sh = SKShader.CreateLinearGradient(new SKPoint(x - 6f, y - 6f), new SKPoint(x + 6f, y + 6f), new[] { new SKColor(200, 196, 190), new SKColor(90, 88, 86) }, null, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = sh;
		c.DrawCircle(x, y, 6f, p);
		p.Shader = null;
		p.Color = new SKColor(50, 48, 46);
		using var hex = new SKPath();
		for (int i = 0; i < 6; i++)
		{
			float a = i * MathF.PI / 3f;
			if (i == 0)
			{
				hex.MoveTo(x + MathF.Cos(a) * 3.2f, y + MathF.Sin(a) * 3.2f);
			}
			else
			{
				hex.LineTo(x + MathF.Cos(a) * 3.2f, y + MathF.Sin(a) * 3.2f);
			}
		}
		hex.Close();
		c.DrawPath(hex, p);
	}

	/// <summary>An orange knob: shadow, a turned rim, a slightly dished face.</summary>
	private static void RrKnob(SKCanvas c, float x, float y, float r)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 130), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) };
		c.DrawCircle(x + 4f, y + 7f, r + 2f, p);
		p.MaskFilter = null;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(x - r, y - r), new SKPoint(x + r, y + r), new[] { new SKColor(250, 140, 90), new SKColor(196, 70, 30) }, null, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawCircle(x, y, r, p);
		}
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(x - r * 0.25f, y - r * 0.3f), r * 0.9f, new[] { new SKColor(255, 178, 130), new SKColor(236, 104, 56), new SKColor(214, 84, 40) }, new[] { 0f, 0.6f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawCircle(x, y, r * 0.8f, p);
		}
		p.Shader = null;
		p.Style = SKPaintStyle.Stroke;
		p.StrokeWidth = 1.2f;
		p.Color = new SKColor(255, 210, 180, 120);
		c.DrawCircle(x, y, r * 0.8f, p);
	}

	private static void RrKnobMark(SKCanvas c, float x, float y, float r, float v)
	{
		float a = (-135f + 270f * v - 90f) * MathF.PI / 180f;
		using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = 4f, Color = new SKColor(110, 34, 10) };
		c.DrawLine(x + MathF.Cos(a) * r * 0.3f, y + MathF.Sin(a) * r * 0.3f, x + MathF.Cos(a) * r * 0.74f, y + MathF.Sin(a) * r * 0.74f, p);
		p.StrokeWidth = 1.2f;
		p.Color = new SKColor(255, 200, 170, 160);
		c.DrawLine(x + MathF.Cos(a) * r * 0.3f - 1.5f, y + MathF.Sin(a) * r * 0.3f - 1.5f, x + MathF.Cos(a) * r * 0.74f - 1.5f, y + MathF.Sin(a) * r * 0.74f - 1.5f, p);
	}

	/// <summary>A round glossy button; lit, it glows.</summary>
	private static void RrButton(SKCanvas c, float x, float y, float r, SKColor col, bool lit)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 130), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) };
		c.DrawCircle(x + 2f, y + 3f, r + 1f, p);
		p.MaskFilter = null;
		if (lit)
		{
			SkiaKit.Glow(c, x, y, r * 2.6f, r * 2.6f, col.WithAlpha(110), SKBlendMode.Plus);
		}
		float k = lit ? 1f : 0.62f;
		var body = new SKColor((byte)(col.Red * k), (byte)(col.Green * k), (byte)(col.Blue * k));
		var hi = new SKColor((byte)Math.Min(255, body.Red + 70), (byte)Math.Min(255, body.Green + 70), (byte)Math.Min(255, body.Blue + 60));
		using var sh = SKShader.CreateRadialGradient(new SKPoint(x - r * 0.35f, y - r * 0.4f), r * 1.3f, new[] { hi, body, new SKColor((byte)(body.Red * 0.7f), (byte)(body.Green * 0.6f), (byte)(body.Blue * 0.6f)) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = sh;
		c.DrawCircle(x, y, r, p);
		p.Shader = null;
		p.Color = new SKColor(255, 255, 255, lit ? (byte)150 : (byte)80);
		c.DrawOval(new SKRect(x - r * 0.5f, y - r * 0.72f, x + r * 0.15f, y - r * 0.3f), p);
	}

	private static void RrLamp(SKCanvas c, float x, float y, float r, SKColor col, bool on)
	{
		using var p = new SKPaint { IsAntialias = true, Color = on ? col : new SKColor((byte)(col.Red / 4), (byte)(col.Green / 4), (byte)(col.Blue / 4)) };
		if (on)
		{
			SkiaKit.Glow(c, x, y, r * 3.4f, r * 3.4f, col.WithAlpha(120), SKBlendMode.Plus);
		}
		c.DrawCircle(x, y, r, p);
		p.Color = new SKColor(255, 255, 255, 120);
		c.DrawCircle(x - r * 0.3f, y - r * 0.35f, r * 0.3f, p);
	}

	/// <summary>A printed temperature scale (20–100 °C over 270°) round a black knurled knob.</summary>
	private static void RrDial(SKCanvas c, float x, float y, float r, string name)
	{
		using var p = new SKPaint { IsAntialias = true, Color = RrInk, StrokeWidth = 1.2f };
		for (int i = 0; i <= 40; i++)
		{
			float a = (-135f + i * 270f / 40f - 90f) * MathF.PI / 180f;
			float len = i % 10 == 0 ? 10f : i % 5 == 0 ? 7f : 4f;
			p.StrokeWidth = i % 10 == 0 ? 1.8f : 1f;
			p.Color = i >= 33 ? new SKColor(200, 40, 28) : RrInk;
			c.DrawLine(x + MathF.Cos(a) * (r + 8f), y + MathF.Sin(a) * (r + 8f), x + MathF.Cos(a) * (r + 8f + len), y + MathF.Sin(a) * (r + 8f + len), p);
			if (i % 10 == 0)
			{
				McTextStatic(c, (20 + i * 2).ToString(CultureInfo.InvariantCulture), x + MathF.Cos(a) * (r + 30f), y + MathF.Sin(a) * (r + 30f));
			}
		}
		p.Color = new SKColor(0, 0, 0, 140);
		p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f);
		c.DrawCircle(x + 3f, y + 6f, r, p);
		p.MaskFilter = null;
		using (var sh = SKShader.CreateSweepGradient(new SKPoint(x, y), new[] { new SKColor(40, 40, 42), new SKColor(16, 16, 18), new SKColor(60, 60, 62), new SKColor(14, 14, 16), new SKColor(40, 40, 42) }, null))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawCircle(x, y, r, p);
			p.Shader = null;
		}
		p.Color = new SKColor(255, 255, 255, 40);
		p.StrokeWidth = 1f;
		for (int i = 0; i < 72; i++)
		{
			float a = i * 5f * MathF.PI / 180f;
			c.DrawLine(x + MathF.Cos(a) * (r - 6f), y + MathF.Sin(a) * (r - 6f), x + MathF.Cos(a) * r, y + MathF.Sin(a) * r, p);
		}
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(x - r * 0.3f, y - r * 0.3f), r, new[] { new SKColor(70, 70, 72), new SKColor(22, 22, 24) }, null, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawCircle(x, y, r * 0.78f, p);
		}
	}

	/// <summary>Small dial numbers, drawn with a plain font so the static picture needs no glyph cache.</summary>
	private static void McTextStatic(SKCanvas c, string text, float x, float y)
	{
		using var font = new SKFont(SKTypeface.FromFamilyName("Bahnschrift", SKFontStyleWeight.SemiBold, SKFontStyleWidth.SemiCondensed, SKFontStyleSlant.Upright), 11f);
		using var p = new SKPaint { IsAntialias = true, Color = RrInk };
		float w = font.MeasureText(text);
		c.DrawText(text, x - w / 2f, y + 4f, SKTextAlign.Left, font, p);
	}

	/// <summary>The white lever on a black knob, with a red dot near its tip.</summary>
	private static void RrLever(SKCanvas c, float x, float y, float r, float v)
	{
		float deg = -135f + 270f * v;
		c.Save();
		c.Translate(x, y);
		c.RotateDegrees(deg);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) };
		c.DrawRoundRect(new SKRect(-7f + 2f, -r + 2f + 3f, 7f + 2f, 10f + 3f), 6f, 6f, p);
		p.MaskFilter = null;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(-7f, 0f), new SKPoint(7f, 0f), new[] { new SKColor(255, 255, 255), new SKColor(214, 212, 208) }, null, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawRoundRect(new SKRect(-7f, -r + 2f, 7f, 10f), 6f, 6f, p);
			p.Shader = null;
		}
		p.Color = new SKColor(214, 40, 30);
		c.DrawCircle(0f, -r + 12f, 3f, p);
		c.Restore();
	}

	/// <summary>Metal fins over a window; amber light rises behind them with the reading.</summary>
	private static void RrFins(SKCanvas c, SKRect r, float v)
	{
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(20, 14, 10) };
		c.DrawRect(r, p);
		float top = r.Bottom - r.Height * v;
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(0f, top), new SKPoint(0f, r.Bottom), new[] { new SKColor(255, 190, 90), new SKColor(255, 110, 30) }, null, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawRect(r.Left, top, r.Width, r.Bottom - top, p);
			p.Shader = null;
		}
		SkiaKit.Glow(c, r.MidX, top, r.Width * 0.6f, 16f, new SKColor(255, 200, 120, 120), SKBlendMode.Plus);
		for (float y = r.Top + 2f; y < r.Bottom - 2f; y += 9f)
		{
			using var sh = SKShader.CreateLinearGradient(new SKPoint(0f, y), new SKPoint(0f, y + 6f), new[] { new SKColor(236, 236, 234), new SKColor(150, 150, 150) }, null, SKShaderTileMode.Clamp);
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawRect(r.Left + 3f, y, r.Width - 6f, 6f, p);
			p.Shader = null;
		}
	}

	// ---------- the moon ----------

	/// <summary>A grey moon of maria and craters, made once.</summary>
	internal static SKImage RrMakeMoon()
	{
		const int s = 280;
		using var surface = SKSurface.Create(new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		c.Clear(SKColors.Transparent);
		const float cx = s / 2f, r = s / 2f;
		c.ClipPath(MoonClip(cx, r), SKClipOperation.Intersect, true);
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(cx - 30f, cx - 30f), r * 1.3f, new[] { new SKColor(214, 214, 214), new SKColor(160, 160, 164) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(0f, 0f, s, s, p);
		}
		var rnd = new Random(5);
		using var paint = new SKPaint { IsAntialias = true };
		paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f);
		for (int i = 0; i < 9; i++)
		{
			paint.Color = new SKColor(70, 72, 80, (byte)rnd.Next(70, 130));
			float x = (float)rnd.NextDouble() * s, y = (float)rnd.NextDouble() * s;
			c.DrawOval(new SKRect(x - 20f - rnd.Next(30), y - 16f - rnd.Next(24), x + 20f + rnd.Next(30), y + 16f + rnd.Next(24)), paint);
		}
		paint.MaskFilter = null;
		for (int i = 0; i < 170; i++)
		{
			float x = (float)rnd.NextDouble() * s, y = (float)rnd.NextDouble() * s;
			float cr = 1.5f + MathF.Pow((float)rnd.NextDouble(), 2.4f) * 22f;
			paint.Style = SKPaintStyle.Fill;
			paint.Color = new SKColor(40, 40, 46, 70);
			c.DrawCircle(x + cr * 0.12f, y + cr * 0.12f, cr, paint);
			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = Math.Max(1f, cr * 0.18f);
			paint.Color = new SKColor(255, 255, 255, 90);
			using var arc = new SKPath();
			arc.AddArc(new SKRect(x - cr, y - cr, x + cr, y + cr), 20f, 160f);
			c.DrawPath(arc, paint);
			paint.Color = new SKColor(20, 20, 26, 90);
			using var dark = new SKPath();
			dark.AddArc(new SKRect(x - cr, y - cr, x + cr, y + cr), 200f, 160f);
			c.DrawPath(dark, paint);
		}
		paint.Style = SKPaintStyle.Fill;
		for (int i = 0; i < 900; i++)
		{
			paint.Color = rnd.Next(2) == 0 ? new SKColor(255, 255, 255, (byte)rnd.Next(20, 70)) : new SKColor(0, 0, 0, (byte)rnd.Next(20, 60));
			c.DrawCircle((float)rnd.NextDouble() * s, (float)rnd.NextDouble() * s, 0.7f + (float)rnd.NextDouble(), paint);
		}
		using (var sh = SKShader.CreateRadialGradient(new SKPoint(cx, cx), r, new[] { SKColors.Transparent, SKColors.Transparent, new SKColor(0, 0, 0, 110) }, new[] { 0f, 0.72f, 1f }, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(0f, 0f, s, s, p);
		}
		return surface.Snapshot();
	}

	private static SKPath MoonClip(float cx, float r)
	{
		var p = new SKPath();
		p.AddCircle(cx, cx, r);
		return p;
	}

	/// <summary>The moon in the porthole: turning slowly, shadowed to tonight's phase, under glass.</summary>
	private void RrMoonDraw(SKCanvas c, DateTime now, double t)
	{
		_rrMoon ??= RrMakeMoon();
		const float cx = 704f, cy = 240f, r = 138f;
		c.Save();
		using (var clip = new SKPath())
		{
			clip.AddCircle(cx, cy, r);
			c.ClipPath(clip, SKClipOperation.Intersect, true);
		}
		c.Clear(new SKColor(8, 8, 12));
		c.Save();
		c.Translate(cx, cy);
		c.RotateDegrees((float)(t * 1.5 % 360.0));
		c.DrawImage(_rrMoon, new SKRect(-r, -r, r, r), SkiaKit.Linear);
		c.Restore();
		using (SKPath shade = MoonShade(cx, cy, r, now))
		{
			using var p = new SKPaint { IsAntialias = true, Color = new SKColor(6, 6, 10, 215), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f) };
			c.DrawPath(shade, p);
		}
		// glass: a soft reflection across the top left and a crisp arc
		using (var sh = SKShader.CreateLinearGradient(new SKPoint(cx - r, cy - r), new SKPoint(cx, cy), new[] { new SKColor(255, 255, 255, 60), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp))
		using (var p = new SKPaint { Shader = sh })
		{
			c.DrawRect(cx - r, cy - r, 2f * r, 2f * r, p);
		}
		c.Restore();
		using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round, Color = new SKColor(255, 255, 255, 110) })
		{
			using var arc = new SKPath();
			arc.AddArc(new SKRect(cx - r + 14f, cy - r + 14f, cx + r - 14f, cy + r - 14f), 200f, 50f);
			c.DrawPath(arc, p);
		}
	}

	/// <summary>Tonight's dark side of the moon: the shape between the limb and the terminator (from the 2000-01-06 new moon).</summary>
	internal static SKPath MoonShade(float cx, float cy, float r, DateTime now)
	{
		double age = ((now.ToUniversalTime() - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays % 29.530588853 + 29.530588853) % 29.530588853;
		float f = (float)(age / 29.530588853);
		float a = r * MathF.Cos(f * MathF.PI * 2f);
		bool waxing = f < 0.5f;
		var shade = new SKPath();
		{
			const int n = 48;
			for (int i = 0; i <= n; i++)
			{
				float y = -r + 2f * r * i / n;
				float edge = MathF.Sqrt(Math.Max(0f, 1f - y * y / (r * r)));
				float x = waxing ? -r * edge : r * edge;
				if (i == 0)
				{
					shade.MoveTo(cx + x, cy + y);
				}
				else
				{
					shade.LineTo(cx + x, cy + y);
				}
			}
			for (int i = n; i >= 0; i--)
			{
				float y = -r + 2f * r * i / n;
				float edge = MathF.Sqrt(Math.Max(0f, 1f - y * y / (r * r)));
				shade.LineTo(cx + (waxing ? a : -a) * edge, cy + y);
			}
			shade.Close();
		}
		return shade;
	}

	/// <summary>A small triangle on a ring at the given angle (0 = top), pointing in (or out).</summary>
	private static void RrRingMark(SKCanvas c, float cx, float cy, float rad, float deg, SKColor col, float size, bool outward = false)
	{
		float a = (deg - 90f) * MathF.PI / 180f;
		float ux = MathF.Cos(a), uy = MathF.Sin(a);
		float px = cx + ux * rad, py = cy + uy * rad;
		float dir = outward ? 1f : -1f;
		using var tri = new SKPath();
		tri.MoveTo(px + ux * size * dir, py + uy * size * dir);
		tri.LineTo(px - uy * size * 0.7f, py + ux * size * 0.7f);
		tri.LineTo(px + uy * size * 0.7f, py - ux * size * 0.7f);
		tri.Close();
		using var p = new SKPaint { IsAntialias = true, Color = col };
		c.DrawPath(tri, p);
	}

	// ---------- the screen ----------

	private void RrScreen(SKCanvas c, DateTime now, float cpu, float gpu, float ram, bool cpuT, float ct, bool gpuT, float gt)
	{
		var ink = new SKColor(26, 26, 28);
		McText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), 48f, 80f, 66f, ink, track: 2f, font: "Bahnschrift Light");
		McText(c, now.ToString("ss", CultureInfo.InvariantCulture), 214f, 80f, 18f, ink, align: 1, font: "Bahnschrift Light");
		McText(c, now.ToString("ddd dd.MM.yyyy", CultureInfo.InvariantCulture).ToUpperInvariant(), 48f, 182f, 12f, ink, track: 1f, font: "Consolas");
		McText(c, "7294.019  ///  //", 104f, 230f, 10f, ink, font: "Consolas");
		string Deg(bool on, float v) => on ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + " °C" : "--";
		var rows = new (string Name, string Value, float Frac)[]
		{
			("CPU LOAD", RrPct(cpu), cpu),
			("GPU LOAD", RrPct(gpu), gpu),
			("CPU TEMP", Deg(cpuT, ct), cpuT ? Math.Clamp(ct / 100f, 0f, 1f) : 0f),
			("GPU TEMP", Deg(gpuT, gt), gpuT ? Math.Clamp(gt / 100f, 0f, 1f) : 0f),
			("MEMORY", RrPct(ram), ram)
		};
		using var p = new SKPaint { IsAntialias = true, StrokeWidth = 1.2f, Color = ink };
		for (int i = 0; i < rows.Length; i++)
		{
			float y = 72f + i * 34f;
			McText(c, rows[i].Name, 238f, y + 2f, 11f, ink, track: 1f, font: "Consolas");
			McText(c, rows[i].Value, 416f, y, 19f, ink, align: 1, font: "Bahnschrift SemiBold");
			// a dimension line: the length is the reading, with end ticks and arrows
			float ly = y + 25f, x0 = 238f, x1 = 238f + 178f * Math.Clamp(rows[i].Frac, 0f, 1f);
			p.Color = new SKColor(26, 26, 28, 60);
			c.DrawLine(x0, ly, 416f, ly, p);
			p.Color = ink;
			c.DrawLine(x0, ly - 4f, x0, ly + 4f, p);
			if (x1 - x0 > 6f)
			{
				c.DrawLine(x0, ly, x1, ly, p);
				c.DrawLine(x1, ly - 4f, x1, ly + 4f, p);
				c.DrawLine(x1, ly, x1 - 5f, ly - 2.5f, p);
				c.DrawLine(x1, ly, x1 - 5f, ly + 2.5f, p);
			}
		}
		// the screen's glass
		using var sh = SKShader.CreateLinearGradient(new SKPoint(38f, 66f), new SKPoint(260f, 244f), new[] { new SKColor(255, 255, 255, 50), new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 0) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
		using var gp = new SKPaint { Shader = sh };
		c.DrawRect(38f, 66f, 388f, 178f, gp);
	}

	// ---------- speaker, meter, fan, slider ----------

	private void RrRipples(SKCanvas c, float gpu)
	{
		var mesh = new SKRect(1254f, 34f, 1536f, 446f);
		c.Save();
		c.ClipRoundRect(new SKRoundRect(mesh, 12f, 12f), SKClipOperation.Intersect, true);
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus };
		for (int k = 0; k < 4; k++)
		{
			// a soft ring: a radial gradient that is clear inside and outside a band
			float f = (float)((_rrSpk + k * 0.25) % 1.0);
			float rad = 20f + f * 250f, band = 34f * (1f - f) + 10f;
			var col = new SKColor(255, 170, 80, (byte)(95f * (1f - f) * (0.4f + 0.6f * gpu)));
			float outer = rad + band;
			float i0 = Math.Max(0f, (rad - band) / outer), i1 = rad / outer;
			using var sh = SKShader.CreateRadialGradient(new SKPoint(1395f, 240f), outer, new[] { SKColors.Transparent, SKColors.Transparent, col, SKColors.Transparent }, new[] { 0f, i0, i1, 1f }, SKShaderTileMode.Clamp);
			p.Color = SKColors.White;
			p.Shader = sh;
			p.StrokeWidth = band * 2f;
			c.DrawCircle(1395f, 240f, rad, p);
			p.Shader = null;
		}
		// the cone's thump
		float beat = (float)(_rrBeat % 1.0);
		float thump = MathF.Exp(-beat * 6f) * (0.3f + 0.7f * gpu);
		SkiaKit.Glow(c, 1395f, 240f, 150f, 150f, new SKColor(255, 150, 60, (byte)(60f * thump)), SKBlendMode.Plus);
		c.Restore();
	}

	private void RrMeter(SKCanvas c, float v, float watts)
	{
		var face = new SKRect(1584f, 30f, 1800f, 190f);
		const float px = 1692f, py = 250f;
		float a = (-90f - 38f + 76f * Math.Clamp(v, -0.02f, 1.03f)) * MathF.PI / 180f;
		c.Save();
		c.ClipRoundRect(new SKRoundRect(face, 8f, 8f), SKClipOperation.Intersect, true);
		using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = 2f, Color = new SKColor(0, 0, 0, 60) };
		c.DrawLine(px + 3f, py + 4f, px + MathF.Cos(a) * 200f + 3f, py + MathF.Sin(a) * 200f + 4f, p);
		p.Color = new SKColor(200, 36, 24);
		c.DrawLine(px, py, px + MathF.Cos(a) * 200f, py + MathF.Sin(a) * 200f, p);
		McText(c, Math.Round(watts).ToString("0", CultureInfo.InvariantCulture) + " W", 1692f, 166f, 13f, RrInk, align: 0, font: "Bahnschrift SemiBold");
		using var sh = SKShader.CreateLinearGradient(new SKPoint(face.Left, face.Top), new SKPoint(face.MidX, face.Bottom), new[] { new SKColor(255, 255, 255, 70), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp);
		using var gp = new SKPaint { Shader = sh };
		c.DrawRect(face, gp);
		c.Restore();
	}

	private static void RrFan(SKCanvas c, float cx, float cy, float deg, float turns)
	{
		c.Save();
		using (var clip = new SKPath())
		{
			clip.AddCircle(cx, cy, 112f);
			c.ClipPath(clip, SKClipOperation.Intersect, true);
		}
		using var p = new SKPaint { IsAntialias = true };
		void Blades(float rot, byte alpha)
		{
			for (int i = 0; i < 15; i++)
			{
				float a0 = (rot + i * 24f) * MathF.PI / 180f;
				using var blade = new SKPath();
				const float r0 = 34f, r1 = 108f;
				blade.MoveTo(cx + MathF.Cos(a0) * r0, cy + MathF.Sin(a0) * r0);
				blade.QuadTo(cx + MathF.Cos(a0 + 0.12f) * 70f, cy + MathF.Sin(a0 + 0.12f) * 70f, cx + MathF.Cos(a0 + 0.36f) * r1, cy + MathF.Sin(a0 + 0.36f) * r1);
				blade.LineTo(cx + MathF.Cos(a0 + 0.6f) * r1, cy + MathF.Sin(a0 + 0.6f) * r1);
				blade.QuadTo(cx + MathF.Cos(a0 + 0.38f) * 70f, cy + MathF.Sin(a0 + 0.38f) * 70f, cx + MathF.Cos(a0 + 0.3f) * r0, cy + MathF.Sin(a0 + 0.3f) * r0);
				blade.Close();
				p.Color = new SKColor(64, 60, 58, alpha);
				c.DrawPath(blade, p);
				p.Style = SKPaintStyle.Stroke;
				p.StrokeWidth = 1.2f;
				p.Color = new SKColor(150, 144, 138, (byte)(alpha * 0.6f));
				c.DrawPath(blade, p);
				p.Style = SKPaintStyle.Fill;
			}
		}
		if (turns > 0.6f)
		{
			Blades(deg - 6f, 90);
		}
		Blades(deg, 255);
		c.Restore();
		// the hub: rings
		foreach ((float rad, SKColor col) in new[] { (34f, new SKColor(40, 38, 36)), (26f, new SKColor(70, 66, 62)), (16f, new SKColor(30, 28, 28)), (7f, new SKColor(110, 104, 98)) })
		{
			p.Color = col;
			c.DrawCircle(cx, cy, rad, p);
		}
		// orange grille bars over the fan
		for (int i = 0; i < 7; i++)
		{
			float x = 1640f + i * 30f;
			var bar = new SKRect(x, 226f, x + 9f, 462f);
			p.Color = new SKColor(0, 0, 0, 110);
			c.DrawRoundRect(new SKRect(bar.Left + 3f, bar.Top + 4f, bar.Right + 3f, bar.Bottom + 4f), 4f, 4f, p);
			using var sh = SKShader.CreateLinearGradient(new SKPoint(bar.Left, 0f), new SKPoint(bar.Right, 0f), new[] { new SKColor(250, 130, 80), new SKColor(220, 86, 40), new SKColor(170, 60, 24) }, null, SKShaderTileMode.Clamp);
			p.Color = SKColors.White;
			p.Shader = sh;
			c.DrawRoundRect(bar, 4f, 4f, p);
			p.Shader = null;
		}
		p.Color = new SKColor(200, 76, 34);
		c.DrawRoundRect(new SKRect(1632f, 222f, 1838f, 232f), 4f, 4f, p);
	}

	private static void RrSlider(SKCanvas c, float x, float top, float bottom, float v)
	{
		float y = bottom - 12f - (bottom - top - 24f) * v;
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120) };
		c.DrawRoundRect(new SKRect(x - 13f + 2f, y - 10f + 3f, x + 13f + 2f, y + 10f + 3f), 3f, 3f, p);
		using var sh = SKShader.CreateLinearGradient(new SKPoint(0f, y - 10f), new SKPoint(0f, y + 10f), new[] { new SKColor(244, 236, 220), new SKColor(196, 186, 168) }, null, SKShaderTileMode.Clamp);
		p.Color = SKColors.White;
		p.Shader = sh;
		c.DrawRoundRect(new SKRect(x - 13f, y - 10f, x + 13f, y + 10f), 3f, 3f, p);
		p.Shader = null;
		p.Color = new SKColor(120, 110, 100);
		c.DrawRect(x - 9f, y - 1f, 18f, 2f, p);
	}
}
