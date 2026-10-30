using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LcdStudio.Core;

/// <summary>The Deer face for the round fan LCDs, matching the Deer screen layout's dotted LED look.</summary>
public sealed partial class Faces
{
	/// <summary>
	/// A dark card face lit warmly from the top left: a ring of radial ticks filled to the reading (the last
	/// few in the pulsing accent, with one bright tick circling), the label in mono caps, the reading in LED
	/// dots, and the time in small dots with a breathing accent dot.
	/// </summary>
	private void Deer(FaceContext x)
	{
		Graphics g = x.G;
		var full = new RectangleF(0f, 0f, 400f, 400f);
		using (var bg = new PathGradientBrush(new[] { new PointF(0f, 0f), new PointF(400f, 0f), new PointF(400f, 400f), new PointF(0f, 400f) })
		{
			CenterPoint = new PointF(200f, 190f),
			CenterColor = Color.FromArgb(32, 32, 33),
			SurroundColors = new[] { Color.FromArgb(14, 14, 15) }
		})
		{
			g.FillRectangle(bg, full);
		}
		using (var warm = new GraphicsPath())
		{
			warm.AddEllipse(-180f, -200f, 420f, 380f);
			using var glow = new PathGradientBrush(warm) { CenterColor = Color.FromArgb(46, 255, 170, 110), SurroundColors = new[] { Color.FromArgb(0, 255, 170, 110) } };
			g.FillPath(glow, warm);
		}

		// the tick ring
		Color ink = x.C1, accent = x.C2;
		const int ticks = 60;
		int lit = (int)Math.Round(x.Fraction * ticks);
		int sweep = (int)(x.T * 12.0) % ticks;
		int pulse = (int)(185 + 70 * Math.Sin(x.T * 2.5));
		using var dim = new Pen(Color.FromArgb(52, 52, 54), 3f);
		using var on = new Pen(Color.FromArgb(225, ink), 3f);
		using var hot = new Pen(Color.FromArgb(pulse, accent), 3.4f);
		using var spark = new Pen(Color.FromArgb(255, 255, 255), 3.4f);
		for (int i = 0; i < ticks; i++)
		{
			double a = -Math.PI / 2 + i * Math.PI * 2 / ticks;
			float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
			Pen pen = i >= lit ? (i == sweep ? on : dim) : (i >= lit - 3 ? hot : (i == sweep ? spark : on));
			g.DrawLine(pen, 200f + c * 162f, 200f + s * 162f, 200f + c * 184f, 200f + s * 184f);
		}

		// the label, the reading in LED dots, and the time
		using (var labelFont = new Font("Consolas", 19f, GraphicsUnit.Pixel))
		using (var labelBrush = new SolidBrush(Color.FromArgb(200, 210, 210, 210)))
		using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
		{
			g.DrawString(x.Label, labelFont, labelBrush, new RectangleF(60f, 102f, 280f, 30f), center);
		}
		var (cells, cols) = ScreenRenderer.PixoCells(x.Value);
		float cell = Math.Min(13f, 232f / Math.Max(1, cols));
		DotGlyphs(g, cells, 200f - cols * cell / 2f, 200f - 3.5f * cell + 6f, cell, ink);
		var (timeCells, timeCols) = ScreenRenderer.PixoCells(x.Time);
		const float timeCell = 3.6f;
		float tx = 200f - timeCols * timeCell / 2f - 8f;
		DotGlyphs(g, timeCells, tx, 278f, timeCell, Color.FromArgb(150, 150, 150));
		using var dot = new SolidBrush(Color.FromArgb(pulse, accent));
		g.FillEllipse(dot, tx + timeCols * timeCell + 10f, 278f + 3.5f * timeCell - 4f, 8f, 8f);
	}

	private static void DotGlyphs(Graphics g, System.Collections.Generic.List<(int Col, int Row, char Ch)> cells, float x0, float y0, float cell, Color color)
	{
		using var b = new SolidBrush(color);
		float inset = cell * 0.14f;
		foreach (var c in cells)
		{
			g.FillEllipse(b, x0 + c.Col * cell + inset, y0 + c.Row * cell + inset, cell - inset * 2f, cell - inset * 2f);
		}
	}
}
