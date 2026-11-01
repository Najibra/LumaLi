using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace LcdStudio.Core;

/// <summary>
/// Standby: a quiet face for when the PC is idle, locked, or about to sleep or shut down. A logo on the left (the
/// image on the panel widget, or the LumaLi mark when there is none), a large clock whose colon breathes once a
/// second, the date, and the minute's seconds as a fine line. Everything drifts a few pixels over the minutes so a
/// screen left on it for hours does not wear in.
/// </summary>
public sealed partial class ScreenRenderer
{
	private static readonly Color StandbyInk = Color.FromArgb(236, 238, 246), StandbySoft = Color.FromArgb(132, 138, 158);

	private bool DrawStandby(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "standby" || w.Kind != WidgetKind.Panel)
		{
			return DrawMission(g, w, r, t);
		}
		Standby(g, w, t);
		return true;
	}

	private void Standby(Graphics g, Widget w, double t)
	{
		DateTime now = DateTime.Now;
		// slow drift against burn-in: a few pixels either way over a couple of minutes
		float dx = 12f * (float)Math.Sin(t / 97.0), dy = 6f * (float)Math.Sin(t / 131.0);
		g.Clear(Color.FromArgb(3, 4, 7));
		using (var glow = new GraphicsPath())
		{
			glow.AddEllipse(560f + dx, -140f + dy, 1300f, 760f);
			using var b = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(18, 22, 40), SurroundColors = new[] { Color.FromArgb(3, 4, 7) } };
			g.FillPath(b, glow);
		}
		GraphicsState state = g.Save();
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		g.TranslateTransform(dx, dy);

		// the logo: the user's own image when one is set, otherwise the LumaLi mark
		Image? logo = GetImage(w.Image);
		var box = new RectangleF(120f, 80f, 320f, 320f);
		if (logo != null)
		{
			float s = Math.Min(box.Width / logo.Width, box.Height / logo.Height);
			float lw = logo.Width * s, lh = logo.Height * s;
			g.DrawImage(logo, box.X + (box.Width - lw) / 2f, box.Y + (box.Height - lh) / 2f, lw, lh);
		}
		else
		{
			float breathe = 0.5f + 0.5f * (float)Math.Sin(t * 0.8);
			using (var ring = new Pen(Color.FromArgb(70, StandbyInk), 2f))
			{
				g.DrawEllipse(ring, 200f, 110f, 160f, 160f);
			}
			using (var arc = new Pen(Color.FromArgb((int)(140 + 80 * breathe), 160, 180, 255), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
			{
				g.DrawArc(arc, 200f, 110f, 160f, 160f, (float)(t * 24.0 % 360.0), 110f);
			}
			DeckText(g, "LumaLi", 40f, StandbyInk, 280f, 300f, StringAlignment.Center, "Segoe UI Light");
		}
		using (var rule = new Pen(Color.FromArgb(40, StandbyInk), 1f))
		{
			g.DrawLine(rule, 560f, 90f, 560f, 390f);
		}

		// the time, its colon breathing once a second
		const float size = 250f;
		string hh = now.ToString("HH", CultureInfo.CurrentCulture), mm = now.ToString("mm", CultureInfo.CurrentCulture);
		Bitmap hours = TextImage(hh, size, StandbyInk, "Segoe UI Light"), colon = TextImage(":", size, StandbyInk, "Segoe UI Light");
		// centred in the space right of the logo
		const float mid = 1240f;
		float total = hours.Width - 20f + colon.Width - 30f + TextImage(mm, size, StandbyInk, "Segoe UI Light").Width;
		float x = mid - total / 2f, top = 30f;
		g.DrawImageUnscaled(hours, (int)x, (int)top);
		x += hours.Width - 20f;
		float pulse = 0.35f + 0.65f * (float)(0.5 + 0.5 * Math.Cos((now.Millisecond / 1000.0) * Math.PI * 2.0));
		using (var ia = new System.Drawing.Imaging.ImageAttributes())
		{
			ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = pulse });
			g.DrawImage(colon, new Rectangle((int)x, (int)top - 14, colon.Width, colon.Height), 0, 0, colon.Width, colon.Height, GraphicsUnit.Pixel, ia);
		}
		x += colon.Width - 30f;
		DeckText(g, mm, size, StandbyInk, x, top, StringAlignment.Near, "Segoe UI Light");

		// the date, and the minute's seconds as a fine line
		DeckText(g, now.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture), 42f, StandbySoft, mid, 330f, StringAlignment.Center, "Segoe UI Light");
		float seconds = (now.Second + now.Millisecond / 1000f) / 60f;
		using (var track = new Pen(Color.FromArgb(34, StandbyInk), 2f))
		{
			g.DrawLine(track, mid - 520f, 410f, mid + 520f, 410f);
		}
		using (var fill = new Pen(Color.FromArgb(170, 160, 180, 255), 2f))
		{
			g.DrawLine(fill, mid - 520f, 410f, mid - 520f + 1040f * seconds, 410f);
		}
		g.Restore(state);
	}
}
