using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LcdStudio.UI;

/// <summary>
/// The LumaLi mark: a white ring (a fan), a hand from its centre (a gauge needle, a watch hand) and one red-orange dot on the ring
/// (the light), the accent the watch faces use. The wordmark is "lumali" in lower case.
/// </summary>
internal static class Brand
{
	public static readonly Color Red = Color.FromArgb(255, 75, 43);

	/// <summary>
	/// Draws the mark in the square r. draw (0 to 1) sweeps the ring in from twelve o'clock, then the bar and the dot
	/// arrive, so the startup screen can build it up; 1 draws it whole.
	/// </summary>
	public static void Mark(Graphics g, RectangleF r, Color ink, float draw = 1f, bool small = false)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		float s = Math.Min(r.Width, r.Height);
		float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
		float stroke = Math.Max(1.5f, s * (small ? 0.13f : 0.105f));
		float rr = s / 2f - stroke / 2f - s * 0.06f;
		float ring = Math.Clamp(draw / 0.6f, 0f, 1f);
		ring = 1f - (1f - ring) * (1f - ring) * (1f - ring);
		if (ring > 0f)
		{
			using var pen = new Pen(ink, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			if (ring >= 0.999f)
			{
				g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2f, rr * 2f);
			}
			else
			{
				g.DrawArc(pen, cx - rr, cy - rr, rr * 2f, rr * 2f, -90f, 360f * ring);
			}
		}
		// the hand: from the centre toward the dot, like a gauge needle or a watch hand; it swings up into place
		float bar = Math.Clamp((draw - 0.45f) / 0.35f, 0f, 1f);
		bar = 1f - (1f - bar) * (1f - bar) * (1f - bar);
		if (bar > 0f)
		{
			float ha = (-45f - 90f * (1f - bar)) * MathF.PI / 180f;
			float len = rr * 0.56f;
			using var pen = new Pen(ink, Math.Max(1.5f, stroke * 0.85f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
			g.DrawLine(pen, cx, cy, cx + MathF.Cos(ha) * len, cy + MathF.Sin(ha) * len);
		}
		// the dot, at half past one on the ring, popping in last
		float dot = Math.Clamp((draw - 0.7f) / 0.3f, 0f, 1f);
		if (dot > 0f)
		{
			float pop = dot < 0.7f ? dot / 0.7f * 1.18f : 1.18f - 0.18f * (dot - 0.7f) / 0.3f;
			float a = -45f * MathF.PI / 180f;
			float dr = stroke * (small ? 0.95f : 0.88f) * pop;
			float dx = cx + MathF.Cos(a) * rr, dy = cy + MathF.Sin(a) * rr;
			using var b = new SolidBrush(Red);
			g.FillEllipse(b, dx - dr, dy - dr, dr * 2f, dr * 2f);
		}
	}

	/// <summary>The wordmark at the given height in pixels, left edge at x, vertically centred on cy. Returns its width.</summary>
	public static float Word(Graphics g, string text, float x, float cy, float height, Color ink, float alpha = 1f)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		using var family = new FontFamily(Theme.DisplayFamily);
		using var path = new GraphicsPath();
		path.AddString(text, family, (int)FontStyle.Regular, height, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF b = path.GetBounds();
		using var m = new Matrix();
		m.Translate(x - b.X, cy - b.Y - b.Height / 2f);
		path.Transform(m);
		using var brush = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp(alpha, 0f, 1f)), ink));
		g.FillPath(brush, path);
		return b.Width;
	}
}
