using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace LcdStudio.Core;

/// <summary>
/// The pieces of the Pixotype look: a monoline 5×7 pixel typeface ("pixo"), fine dashed rules, staircase
/// history graphs and a slowly twisting wireframe ribbon behind everything.
/// </summary>
public sealed partial class ScreenRenderer
{
	// ---------- the pixel typeface ----------

	/// <summary>5×7 glyphs, one string per row, '#' for an ink cell. Square shapes with notched corners.</summary>
	internal static readonly Dictionary<char, string[]> Pixo = new Dictionary<char, string[]>
	{
		['0'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
		['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
		['2'] = new[] { "#### ", "    #", "    #", " ### ", "#    ", "#    ", "#####" },
		['3'] = new[] { "#### ", "    #", "    #", " ### ", "    #", "    #", "#### " },
		['4'] = new[] { "#   #", "#   #", "#   #", "#####", "    #", "    #", "    #" },
		['5'] = new[] { "#####", "#    ", "#    ", "#### ", "    #", "    #", "#### " },
		['6'] = new[] { " ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### " },
		['7'] = new[] { "#####", "    #", "    #", "   # ", "  #  ", "  #  ", "  #  " },
		['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
		['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },
		['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
		['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
		['C'] = new[] { " ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####" },
		['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
		['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
		['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
		['G'] = new[] { " ####", "#    ", "#    ", "#  ##", "#   #", "#   #", " ####" },
		['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
		['I'] = new[] { "###", " # ", " # ", " # ", " # ", " # ", "###" },
		['J'] = new[] { "#####", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " },
		['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
		['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
		['M'] = new[] { "#   #", "## ##", "# # #", "#   #", "#   #", "#   #", "#   #" },
		['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
		['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
		['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
		['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
		['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
		['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
		['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
		['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
		['V'] = new[] { "#   #", "#   #", "#   #", "#   #", " # # ", " # # ", "  #  " },
		['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #" },
		['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
		['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
		['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
		['%'] = new[] { "##   ", "##  #", "   # ", "  #  ", " #   ", "#  ##", "   ##" },
		['°'] = new[] { " ## ", "#  #", "#  #", " ## ", "    ", "    ", "    " },
		[':'] = new[] { " ", "#", "#", " ", "#", "#", " " },
		['.'] = new[] { " ", " ", " ", " ", " ", " ", "#" },
		[','] = new[] { "  ", "  ", "  ", "  ", "  ", " #", "# " },
		['-'] = new[] { "    ", "    ", "    ", "####", "    ", "    ", "    " },
		['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
		['/'] = new[] { "    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    " },
		['('] = new[] { " #", "# ", "# ", "# ", "# ", "# ", " #" },
		[')'] = new[] { "# ", " #", " #", " #", " #", " #", "# " },
		['↓'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "# # #", " ### ", "  #  " },
		['↑'] = new[] { "  #  ", " ### ", "# # #", "  #  ", "  #  ", "  #  ", "  #  " },
	};

	private readonly Dictionary<(int, int, string), (string Old, string Now, double Since)> _pixoState = new Dictionary<(int, int, string), (string, string, double)>();

	/// <summary>Cells of one string: digits keep a fixed width so numbers don't jitter; spaces are 3 cells.</summary>
	internal static (List<(int Col, int Row, char Ch)> Cells, int Cols) PixoCells(string text)
	{
		var cells = new List<(int, int, char)>();
		int x = 0;
		foreach (char raw in text)
		{
			char c = char.ToUpperInvariant(raw);
			if (c == ' ' || !Pixo.TryGetValue(c, out string[] rows))
			{
				x += 3;
				continue;
			}
			int width = rows[0].Length;
			for (int r = 0; r < 7; r++)
			{
				for (int col = 0; col < width; col++)
				{
					if (rows[r][col] == '#')
					{
						cells.Add((x + col, r, c));
					}
				}
			}
			x += width + 1;
		}
		return (cells, Math.Max(0, x - 1));
	}

	/// <summary>
	/// Pixel lettering, Size px per cell. A value is its number with the unit set at half size beside its top.
	/// When the text changes a scan line sweeps left to right, putting the new text down behind it; a clock's
	/// colons breathe.
	/// </summary>
	private void PixoText(Graphics g, Widget w, RectangleF r, double t, string? shownText = null)
	{
		string text = shownText ?? (w.Kind == WidgetKind.Value ? Formatted(w) : Resolve(w.Text));
		// "dots" sets the same letters as round LED dots instead of square cells
		bool round = w.Effect is "dots" or "status";
		void Cell(Brush b, float x, float y, float size)
		{
			if (round)
			{
				float inset = size * 0.14f;
				g.FillEllipse(b, x + inset, y + inset, size - inset * 2f, size - inset * 2f);
			}
			else
			{
				g.FillRectangle(b, x, y, size, size);
			}
		}
		string unit = w.Kind == WidgetKind.Value && w.Sensor != "time" ? (w.Text.Length > 0 ? w.Text : Sensors.Def(w.Sensor).Unit) : "";
		if (unit == "°C")
		{
			unit = "°";
		}
		float cell = Math.Max(1f, w.Size);
		float unitCell = Math.Max(1f, (float)Math.Round(cell * 0.45f));
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_pixoState.TryGetValue(key, out var st))
		{
			// the first sight of a widget scans it in, so a freshly shown layout writes itself onto the screen
			st = ("", text, t + (w.X + w.Y) * 0.0004);
		}
		if (st.Now != text)
		{
			st = (st.Now, text, t);
		}
		_pixoState[key] = st;
		Color color = w.Kind == WidgetKind.Value ? ValueColor(w) : Imaging.Hex(w.Color, Color.White);

		var (cells, cols) = PixoCells(text);
		var (unitCells, unitCols) = PixoCells(unit);
		float width = cols * cell + (unitCols > 0 ? cell * 1.4f + unitCols * unitCell : 0f);
		float x0 = Align(w.Align) switch
		{
			StringAlignment.Center => r.X + (r.Width - width) / 2f,
			StringAlignment.Far => r.Right - width,
			_ => r.X,
		};
		x0 = (float)Math.Round(x0);
		float y0 = (float)Math.Round(r.Y + (r.Height - 7 * cell) / 2f);

		double p = Math.Clamp((t - st.Since) / (0.25 + cols * 0.012), 0.0, 1.0);
		float front = (float)(p * (cols + 2)) - 1f;
		bool clock = w.Kind == WidgetKind.Text && w.Text.Contains("{time");
		int breathe = clock ? (int)(150 + 105 * Math.Cos(t * Math.PI)) : 255;
		using var ink = new SolidBrush(color);
		using var dim = new SolidBrush(Color.FromArgb(breathe, color));
		using var lead = new SolidBrush(Color.FromArgb(110, color));
		if (p < 1.0 && st.Old.Length > 0)
		{
			// the old text stays ahead of the scan line
			foreach (var c in PixoCells(st.Old).Cells)
			{
				if (c.Col > front + 1)
				{
					Cell(ink, x0 + c.Col * cell, y0 + c.Row * cell, cell);
				}
			}
		}
		foreach (var c in cells)
		{
			if (c.Col > front + 1)
			{
				continue;
			}
			Brush b = c.Col > front - 1 ? lead : c.Ch == ':' ? dim : ink;
			Cell(b, x0 + c.Col * cell, y0 + c.Row * cell, cell);
		}
		if (p < 1.0)
		{
			// the scan line itself, a one-cell column of faint light
			using var scan = new SolidBrush(Color.FromArgb(60, color));
			g.FillRectangle(scan, x0 + (front + 1) * cell, y0 - cell, cell, cell * 9f);
		}
		if (unitCols > 0 && p > 0.6)
		{
			float ux = x0 + cols * cell + cell * 1.4f;
			foreach (var c in unitCells)
			{
				Cell(ink, ux + c.Col * unitCell, y0 + c.Row * unitCell, unitCell);
			}
		}
	}

	// ---------- rules and graphs ----------

	/// <summary>A fine dashed rule along the widget's long side.</summary>
	private static void DashedRule(Graphics g, Widget w, RectangleF r)
	{
		using var pen = new Pen(Imaging.Hex(w.Color, Color.FromArgb(90, 255, 255, 255)), Math.Max(1f, w.Thickness)) { DashPattern = new[] { 4f, 3f } };
		SmoothingMode mode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.None;
		if (r.Height > r.Width)
		{
			g.DrawLine(pen, (float)Math.Round(r.X + r.Width / 2f), r.Y, (float)Math.Round(r.X + r.Width / 2f), r.Bottom);
		}
		else
		{
			g.DrawLine(pen, r.X, (float)Math.Round(r.Y + r.Height / 2f), r.Right, (float)Math.Round(r.Y + r.Height / 2f));
		}
		g.SmoothingMode = mode;
	}

	/// <summary>
	/// History as a crisp staircase over a dotted baseline and a dotted halfway line, with a square marker on
	/// the newest reading that pulses gently.
	/// </summary>
	private void StepGraph(Graphics g, Widget w, RectangleF r, double t)
	{
		Color color = Imaging.Hex(w.Color, Color.White);
		SmoothingMode mode = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.None;
		using (var dots = new SolidBrush(Color.FromArgb(70, color)))
		{
			for (float x = r.X; x <= r.Right; x += 8f)
			{
				g.FillRectangle(dots, (float)Math.Round(x), r.Bottom, 2f, 2f);
				if (((int)((x - r.X) / 8f)) % 2 == 0)
				{
					g.FillRectangle(dots, (float)Math.Round(x), (float)Math.Round(r.Y + r.Height / 2f), 1f, 1f);
				}
			}
		}
		float[] history = _sensors.History(w.Sensor);
		float[] data;
		lock (history)
		{
			data = (float[])history.Clone();
		}
		if (data.Length < 2)
		{
			g.SmoothingMode = mode;
			return;
		}
		float step = Math.Max(4f, w.Thickness > 0f ? w.Thickness : 8f);
		int n = Math.Min(data.Length, (int)(r.Width / step) + 1);
		var pts = new List<PointF>(n * 2);
		float lastY = 0f;
		for (int i = 0; i < n; i++)
		{
			float v = data[data.Length - n + i];
			float f = (float)Math.Clamp((v - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0);
			float x = (float)Math.Round(r.Right - (n - 1 - i) * step);
			float y = (float)Math.Round(r.Bottom - f * r.Height);
			if (i > 0)
			{
				pts.Add(new PointF(x, lastY));
			}
			pts.Add(new PointF(x, y));
			lastY = y;
		}
		using (var pen = new Pen(Color.FromArgb(230, color), 2f))
		{
			g.DrawLines(pen, pts.ToArray());
		}
		PointF end = pts[pts.Count - 1];
		int glow = (int)(60 + 50 * Math.Sin(t * 2.4));
		using (var halo = new SolidBrush(Color.FromArgb(glow, color)))
		{
			g.FillRectangle(halo, end.X - 6f, end.Y - 6f, 12f, 12f);
		}
		using (var marker = new SolidBrush(color))
		{
			g.FillRectangle(marker, end.X - 3f, end.Y - 3f, 6f, 6f);
		}
		g.SmoothingMode = mode;
	}

	/// <summary>
	/// The newer widget styles: Pixotype's pixel type, dashed rules and step graphs here, then the neon ones.
	/// False when the widget is drawn the ordinary way.
	/// </summary>
	private bool DrawPixotype(Graphics g, Widget w, RectangleF r, double t)
	{
		switch (w.Effect)
		{
		case "pixo" or "dots" when w.Kind == WidgetKind.Text || w.Kind == WidgetKind.Value:
			PixoText(g, w, r, t);
			return true;
		case "dashed" when w.Kind == WidgetKind.Divider:
			DashedRule(g, w, r);
			return true;
		case "steps" when w.Kind == WidgetKind.Graph:
			StepGraph(g, w, r, t);
			return true;
		default:
			return DrawNeon(g, w, r, t);
		}
	}

	// ---------- the wireframe ribbon ----------

	/// <summary>
	/// A dark page with a faint wireframe ribbon of fine lines drifting across it and slowly twisting, half the
	/// strands drawn as lines and half as dotted traces, like the mesh behind the Pixotype specimen.
	/// </summary>
	private void Mesh(Graphics g, Color page, Color strand, double t)
	{
		g.Clear(page);
		const int strands = 34;
		const int samples = 97;
		var pts = new PointF[samples];
		using var line = new Pen(Color.FromArgb(26, strand), 1f);
		using var dot = new SolidBrush(Color.FromArgb(40, strand));
		for (int k = 0; k < strands; k++)
		{
			double offset = (k / (double)(strands - 1) - 0.5) * 230.0;
			for (int i = 0; i < samples; i++)
			{
				double x = i * (1920.0 + 80.0) / (samples - 1) - 40.0;
				double centre = 240.0 + 120.0 * Math.Sin(x * 0.0024 + t * 0.11) + 40.0 * Math.Sin(x * 0.0061 - t * 0.07);
				double twist = Math.Cos(x * 0.0032 - t * 0.16 + 0.6);
				pts[i] = new PointF((float)x, (float)(centre + offset * twist));
			}
			if (k % 2 == 0)
			{
				g.DrawLines(line, pts);
			}
			else
			{
				for (int i = 0; i < samples; i += 2)
				{
					g.FillRectangle(dot, pts[i].X, pts[i].Y, 1.6f, 1.6f);
				}
			}
		}
	}
}
