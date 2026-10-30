using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace LcdStudio.UI;

/// <summary>
/// The app icon: a deep blue tile holding the 8.8" screen (a wide white panel with three stepped pixel bars,
/// like live readings) above a lime light strip. Every size is drawn natively, so 16 px stays crisp.
/// </summary>
internal static class AppIcon
{
	public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

	public static Icon Create()
	{
		// the icon built into the exe carries every size; drawing is the fallback when running from a build folder
		try
		{
			using Icon embedded = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
			if (embedded != null) return new Icon(embedded, 32, 32);
		}
		catch { }
		using Bitmap bitmap = Draw(64);
		return Icon.FromHandle(bitmap.GetHicon());
	}

	public static Bitmap Draw(int s)
	{
		var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		g.Clear(Color.Transparent);
		bool tiny = s <= 24;
		float m = s <= 20 ? 0 : s * 0.03f;
		var tile = new RectangleF(m, m, s - 2 * m, s - 2 * m);
		using (GraphicsPath path = Round(tile, s * 0.22f))
		{
			using (var fill = new LinearGradientBrush(tile, Color.FromArgb(47, 107, 255), Color.FromArgb(14, 22, 92), 60f))
				g.FillPath(fill, path);
			if (!tiny)
			{
				// a soft sheen across the top half
				var top = new RectangleF(tile.X, tile.Y, tile.Width, tile.Height * 0.5f);
				using var sheen = new LinearGradientBrush(RectangleF.Inflate(top, 0, 1), Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
				g.SetClip(path);
				g.FillRectangle(sheen, top);
				g.ResetClip();
			}
		}
		// the screen: a wide white panel
		var screen = new RectangleF(s * 0.15f, s * 0.27f, s * 0.70f, s * 0.36f);
		using (GraphicsPath sp = Round(screen, Math.Max(1, s * 0.05f)))
		using (var white = new SolidBrush(Color.White))
			g.FillPath(white, sp);
		// three stepped pixel bars inside it, snapped to whole pixels so they stay sharp
		g.SmoothingMode = SmoothingMode.None;
		float inner = screen.Height * 0.62f, barW = screen.Width * 0.17f, gap = screen.Width * 0.09f;
		float x0 = screen.X + (screen.Width - (3 * barW + 2 * gap)) / 2, baseY = screen.Bottom - screen.Height * 0.19f;
		float[] heights = { 0.45f, 0.75f, 1f };
		using (var blue = new SolidBrush(Color.FromArgb(0, 71, 245)))
			for (int i = 0; i < 3; i++)
			{
				float h = Math.Max(2, inner * heights[i]);
				g.FillRectangle(blue, (float)Math.Round(x0 + i * (barW + gap)), (float)Math.Round(baseY - h), (float)Math.Max(1, Math.Round(barW)), (float)Math.Round(h));
			}
		// the light strip under the screen
		g.SmoothingMode = SmoothingMode.AntiAlias;
		var strip = new RectangleF(s * 0.27f, s * 0.71f, s * 0.46f, Math.Max(1.5f, s * 0.06f));
		using (GraphicsPath lp = Round(strip, strip.Height / 2))
		using (var lime = new SolidBrush(Color.FromArgb(215, 255, 90)))
		{
			if (!tiny)
				using (var glow = new SolidBrush(Color.FromArgb(70, 215, 255, 90)))
				using (GraphicsPath gp = Round(RectangleF.Inflate(strip, s * 0.03f, s * 0.025f), strip.Height))
					g.FillPath(glow, gp);
			g.FillPath(lime, lp);
		}
		return bmp;
	}

	/// <summary>Writes a Windows .ico holding every size as PNG.</summary>
	public static void Save(string path)
	{
		var images = new byte[Sizes.Length][];
		for (int i = 0; i < Sizes.Length; i++)
		{
			using Bitmap b = Draw(Sizes[i]);
			using var ms = new MemoryStream();
			b.Save(ms, ImageFormat.Png);
			images[i] = ms.ToArray();
		}
		using var w = new BinaryWriter(File.Create(path));
		w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);
		int offset = 6 + 16 * Sizes.Length;
		for (int i = 0; i < Sizes.Length; i++)
		{
			w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i])); w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
			w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
			w.Write(images[i].Length); w.Write(offset);
			offset += images[i].Length;
		}
		foreach (byte[] img in images) w.Write(img);
	}

	static GraphicsPath Round(RectangleF r, float radius)
	{
		var p = new GraphicsPath();
		float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
		p.AddArc(r.X, r.Y, d, d, 180, 90);
		p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
		p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
		p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
		p.CloseFigure();
		return p;
	}
}
