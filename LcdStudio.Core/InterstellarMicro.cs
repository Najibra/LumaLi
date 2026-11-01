using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Interstellar Micro: the Interstellar scene with its type set as a pack of technical labels (after the
/// "micrographics" look): hairline boxes on smoked glass, inverted tags, REF / LOT fields, barcodes that encode the
/// readings, a field of code cells that flip as the load moves, crosshair and hatch marks, and one red accent.
/// Text decodes: a changed number scrambles through random digits before it settles, the time re-decodes each minute,
/// a red laser line sweeps the barcodes, and the caption types itself out behind a block cursor.
/// Numbers stay white.
/// </summary>
public sealed partial class ScreenRenderer
{
	private const string MiCond = "Bahnschrift SemiCondensed", MiLight = "Bahnschrift Light SemiCondensed", MiBold = "Bahnschrift SemiBold SemiCondensed";

	private static readonly SKColor MiInk = new SKColor(232, 226, 216), MiDim = new SKColor(196, 184, 170), MiRed = new SKColor(240, 74, 62);

	private bool _isMicro;

	private readonly Dictionary<string, (string Value, double At)> _miChanged = new Dictionary<string, (string, double)>();

	private static readonly string[] MiCodes = { "CPU-01", "GPU-02", "TMP-03", "TMP-04", "MEM-05" };

	/// <summary>
	/// The text part way through decoding: letters settle left to right over the given seconds; the unsettled ones show
	/// random characters that change twenty times a second. Spaces and punctuation never scramble.
	/// </summary>
	private static string MiDecode(string target, double since, double seconds, double t)
	{
		if (since >= seconds)
		{
			return target;
		}
		var chars = target.ToCharArray();
		int tick = (int)Math.Floor(t * 20.0);
		for (int i = 0; i < chars.Length; i++)
		{
			char ch = chars[i];
			double settle = seconds * (0.35 + 0.65 * (i + 1) / chars.Length);
			if (since >= settle || !char.IsLetterOrDigit(ch))
			{
				continue;
			}
			double h = Hash01(tick * 31 + i * 7 + 3);
			chars[i] = char.IsDigit(ch) ? (char)('0' + (int)(h * 10.0)) : (char)('A' + (int)(h * 26.0));
		}
		return new string(chars);
	}

	/// <summary>A hairline label box on smoked glass, so the type reads over the scene.</summary>
	private static void MiBox(SKCanvas c, SKRect r, byte glass = 150)
	{
		using var fill = new SKPaint { Color = new SKColor(10, 6, 5, glass) };
		c.DrawRect(r, fill);
		using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = MiInk.WithAlpha(120) };
		c.DrawRect(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), line);
	}

	/// <summary>An inverted tag: a filled block of ink with the text knocked out in the dark.</summary>
	private SKRect MiTag(SKCanvas c, string text, float x, float y, float size, SKColor fill, string font = MiBold, float padX = 7f, float padY = 4f, SKColor? textColor = null)
	{
		SKRect b = McPath(text, font, size, 1.2f).TightBounds;
		var r = new SKRect(x, y, x + b.Width + padX * 2f, y + b.Height + padY * 2f);
		using (var p = new SKPaint { Color = fill })
		{
			c.DrawRect(r, p);
		}
		McText(c, text, x + padX, y + padY, size, textColor ?? new SKColor(14, 9, 8), track: 1.2f, font: font);
		return r;
	}

	/// <summary>A barcode whose bars come from the text, so it changes when the reading does.</summary>
	private static void MiBarcode(SKCanvas c, string seed, float x, float y, float w, float h, SKColor ink)
	{
		int hash = 17;
		foreach (char ch in seed)
		{
			hash = hash * 31 + ch;
		}
		using var p = new SKPaint { Color = ink };
		float bx = x;
		int i = 0;
		while (bx < x + w)
		{
			double r1 = Hash01(hash + i * 13), r2 = Hash01(hash + i * 29 + 7);
			float bw = 1f + (float)Math.Floor(r1 * 3.2);
			float gap = 1f + (float)Math.Floor(r2 * 2.6);
			c.DrawRect(bx, y, Math.Min(bw, x + w - bx), h, p);
			bx += bw + gap;
			i++;
		}
	}

	/// <summary>A field of code cells (like a 2D code) where a few cells flip as time and the reading move.</summary>
	private static void MiCells(SKCanvas c, float x, float y, int cols, int rows, float cell, double t, float load, SKColor ink)
	{
		using var p = new SKPaint { Color = ink };
		int tick = (int)Math.Floor(t * (1.5 + 6.0 * load));
		for (int r = 0; r < rows; r++)
		{
			for (int k = 0; k < cols; k++)
			{
				int id = r * 97 + k;
				bool on = Hash01(id * 5 + 1) > 0.5;
				if (Hash01(id * 11 + tick * 3) > 0.9)
				{
					on = !on;
				}
				if (on)
				{
					c.DrawRect(x + k * cell, y + r * cell, cell - 0.6f, cell - 0.6f, p);
				}
			}
		}
	}

	/// <summary>A crosshair-in-circle mark, turning slowly.</summary>
	private static void MiCross(SKCanvas c, float x, float y, float r, double t, SKColor ink)
	{
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = ink };
		c.DrawCircle(x, y, r, p);
		c.Save();
		c.RotateDegrees((float)(t * 12.0 % 360.0), x, y);
		c.DrawLine(x - r * 1.25f, y, x + r * 1.25f, y, p);
		c.DrawLine(x, y - r * 1.25f, x, y + r * 1.25f, p);
		c.Restore();
	}

	/// <summary>A box of diagonal hatching.</summary>
	private static void MiHatch(SKCanvas c, SKRect r, SKColor ink)
	{
		c.Save();
		c.ClipRect(r);
		using var p = new SKPaint { IsAntialias = true, StrokeWidth = 2.4f, Color = ink };
		for (float k = -r.Height; k < r.Width; k += 7f)
		{
			c.DrawLine(r.Left + k, r.Bottom, r.Left + k + r.Height, r.Top, p);
		}
		c.Restore();
	}

	/// <summary>A red laser line sweeping across a barcode now and then.</summary>
	private static void MiLaser(SKCanvas c, float x, float y, float w, float h, double t, double offset)
	{
		double ph = (t * 0.45 + offset) % 1.0;
		if (ph > 0.45)
		{
			return;
		}
		float u = (float)(ph / 0.45);
		float lx = x + w * u;
		using var p = new SKPaint { IsAntialias = true, StrokeWidth = 1.6f, Color = MiRed.WithAlpha(230) };
		c.DrawLine(lx, y - 3f, lx, y + h + 3f, p);
		SkiaKit.Glow(c, lx, y + h / 2f, 10f, h * 0.7f, MiRed.WithAlpha(110), SKBlendMode.Plus);
	}

	/// <summary>The label pack: the time card on the left, five reading labels on the right.</summary>
	private void IsMicroText(SKCanvas c, double dt, double t)
	{
		DateTime now = DateTime.Now;
		string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
		if (time != _isShownTime)
		{
			_isShownTime = time;
			_isTimeAt = t;
		}
		float cpu = Math.Clamp(Val("cpu.load") / 100f, 0f, 1f);
		TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
		double beta = 0.2 + 0.75 * cpu;
		double gamma = 1.0 / Math.Sqrt(1.0 - beta * beta);
		int week = ISOWeek.GetWeekOfYear(now);

		// ---- the time card ----
		var card = new SKRect(28f, 24f, 616f, 300f);
		MiBox(c, card);
		// header: a barcode, the label's code line and the year
		MiBarcode(c, "LUMALI" + now.ToString("yyyyMMdd"), 40f, 36f, 250f, 22f, MiInk.WithAlpha(220));
		MiLaser(c, 40f, 36f, 250f, 22f, t, 0.0);
		McText(c, "WTS  LUMALI  PRSSNLT-" + now.ToString("MMdd", CultureInfo.InvariantCulture), 306f, 38f, 11f, MiDim, track: 1.6f, font: MiCond);
		McText(c, now.ToString("yyyy", CultureInfo.InvariantCulture), 604f, 38f, 15f, MiRed, align: 1, track: 1f, font: MiCond);
		McText(c, "LABEL  ASSET  PACK  01", 306f, 54f, 8.5f, MiDim.WithAlpha(160), track: 1.6f, font: MiCond);
		using (var rule = new SKPaint { StrokeWidth = 1f, Color = MiInk.WithAlpha(110) })
		{
			c.DrawLine(card.Left, 70f, card.Right, 70f, rule);
			c.DrawLine(card.Left, 206f, card.Right, 206f, rule);
			c.DrawLine(430f, 70f, 430f, 206f, rule);
		}
		// the time decodes when the minute turns
		string shownTime = MiDecode(time, t - _isTimeAt, 0.9, t);
		SKRect tr = McText(c, shownTime, 38f, 82f, 116f, SKColors.White, font: MiLight);
		McText(c, now.ToString("ss", CultureInfo.InvariantCulture), tr.Right + 10f, tr.Bottom, 26f, MiDim, bottom: true, font: MiLight);
		// the date block
		McText(c, now.ToString("MMM dd", CultureInfo.InvariantCulture).ToUpperInvariant(), 444f, 84f, 34f, SKColors.White, font: MiCond);
		McText(c, $"W. {week:00}", 444f, 132f, 20f, MiInk, font: MiLight);
		McText(c, now.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant(), 444f, 164f, 11f, MiDim, track: 2f, font: MiCond);
		MiCross(c, 586f, 176f, 13f, t, MiInk.WithAlpha(200));
		// the tag line: an inverted tag, then REF and LOT
		bool caution = false;
		foreach (IsRow m in IsRows)
		{
			if (McOnline(m.Key) && Val(m.Key) >= m.Hi * 0.9f && m.Unit == "°C")
			{
				caution = true;
			}
		}
		SKRect tag = MiTag(c, caution ? "CAUTION" : "EVENT HORIZON", 40f, 218f, 15f, caution ? MiRed : MiInk);
		McText(c, "REF", tag.Right + 18f, 222f, 10f, MiDim, track: 1.5f, font: MiCond);
		McText(c, $"{up.Days:00}-{up.Hours:00}{up.Minutes:00}", tag.Right + 46f, 220f, 15f, SKColors.White, font: MiCond);
		McText(c, "LOT", tag.Right + 140f, 222f, 10f, MiDim, track: 1.5f, font: MiCond);
		McText(c, "×" + gamma.ToString("0.00", CultureInfo.InvariantCulture), tag.Right + 166f, 220f, 15f, SKColors.White, font: MiCond);
		MiHatch(c, new SKRect(560f, 216f, 604f, 244f), MiInk.WithAlpha(170));
		// the caption types itself out behind a block cursor, then holds, and the next one begins
		string[] captions =
		{
			$"TIME DILATION ×{gamma.ToString("0.00", CultureInfo.InvariantCulture)}  (PROCESSOR {Val("cpu.load"):0}%)",
			$"MISSION TIME {up.Days}D {up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}  [IN PROGRESS]",
			"ADVERSE EVENTS NA  /  STATUS " + (caution ? "CAUTION" : "NOMINAL"),
			$"GRAVITY WELL {Val("gpu.load"):0}%  /  ORBIT LAP 80 S"
		};
		const double capLen = 6.0;
		int which = (int)Math.Floor(t / capLen);
		double cp = t - which * capLen;
		string cap = captions[((which % captions.Length) + captions.Length) % captions.Length];
		int shown = Math.Clamp((int)(cp * 34.0), 0, cap.Length);
		SKRect cr = McText(c, cap.Substring(0, Math.Max(1, shown)), 40f, 260f, 12f, MiInk, track: 1.4f, font: MiCond);
		if (shown < cap.Length || (int)(t * 2.0) % 2 == 0)
		{
			using var cur = new SKPaint { Color = MiRed };
			c.DrawRect(cr.Right + 4f, 258f, 7f, 15f, cur);
		}
		MiCells(c, 520f, 254f, 12, 3, 7f, t, cpu, MiInk.WithAlpha(180));

		// a small INFO strip under the card
		var info = new SKRect(28f, 312f, 300f, 344f);
		MiBox(c, info, 120);
		MiTag(c, "INFO", 34f, 318f, 12f, MiInk);
		MiBarcode(c, "INFO" + now.Minute, 100f, 320f, 120f, 16f, MiInk.WithAlpha(190));
		McText(c, "DIGIFORM", 228f, 322f, 10f, MiDim, track: 1.2f, font: MiCond);

		// ---- the reading labels ----
		const float x0 = 1494f, x1 = 1894f, top = 22f, rowH = 89f, h = 82f;
		using var track = new SKPaint { StrokeWidth = 1f, Color = MiInk.WithAlpha(80) };
		using var fillBar = new SKPaint { Color = MiInk };
		for (int i = 0; i < IsRows.Length; i++)
		{
			IsRow m = IsRows[i];
			float y = top + i * rowH;
			var r = new SKRect(x0, y, x1, y + h);
			MiBox(c, r);
			bool online = McOnline(m.Key);
			float v = online ? McEased("is." + m.Key, Val(m.Key), dt, 3.0) : 0f;
			string value = online ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) : "--";
			if (!_miChanged.TryGetValue(m.Key, out var ch))
			{
				ch = (value, -100.0);
			}
			else if (ch.Value != value)
			{
				ch = (value, t);
			}
			_miChanged[m.Key] = ch;
			bool warn = online && m.Unit == "°C" && v >= m.Hi * 0.9f;

			SKRect code = MiTag(c, MiCodes[i], x0 + 8f, y + 8f, 10f, warn ? MiRed : MiInk, padX: 5f, padY: 3f);
			McText(c, m.Label, code.Right + 10f, y + 10f, 11f, MiInk, track: 1.6f, font: MiCond);
			// the number decodes when it changes
			string shownValue = MiDecode(value, t - ch.At, 0.45, t);
			SKRect vr = McText(c, shownValue, x0 + 10f, y + 32f, 40f, SKColors.White, font: MiLight);
			McText(c, m.Unit, vr.Right + 5f, vr.Bottom, 15f, MiDim, bottom: true, font: MiCond);
			// a barcode that encodes the reading, swept by the laser
			MiBarcode(c, m.Key + value, x1 - 182f, y + 32f, 170f, 26f, MiInk.WithAlpha(215));
			MiLaser(c, x1 - 182f, y + 32f, 170f, 26f, t, 0.17 * (i + 1));
			McText(c, $"REF 11-{5110 + i * 7}", x1 - 182f, y + 64f, 8.5f, MiDim, track: 1.2f, font: MiCond);
			McText(c, $"LOT {96024 + i * 13}", x1 - 12f, y + 64f, 8.5f, MiDim, align: 1, track: 1.2f, font: MiCond);
			// the bar: a hairline track with a solid block filled to the reading
			float f = Math.Clamp(v / m.Hi, 0f, 1f);
			float by = y + h - 9f;
			c.DrawLine(x0 + 10f, by, x0 + 190f, by, track);
			fillBar.Color = warn ? MiRed : MiInk;
			c.DrawRect(x0 + 10f, by - 2f, 180f * f, 4f, fillBar);
		}
	}
}
