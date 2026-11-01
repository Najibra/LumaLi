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
		float m = s <= 20 ? 0 : s * 0.04f;
		var tile = new RectangleF(m, m, s - 2 * m, s - 2 * m);
		using (GraphicsPath path = Round(tile, s * 0.23f))
		{
			using (var fill = new LinearGradientBrush(tile, Color.FromArgb(30, 30, 35), Color.FromArgb(10, 10, 12), 90f))
				g.FillPath(fill, path);
			if (!tiny)
			{
				using var edge = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1f, s / 128f));
				g.DrawPath(edge, path);
			}
		}
		// the mark: ring, bar and the red dot
		float k = tiny ? 0.78f : 0.62f;
		float ms = s * k;
		Brand.Mark(g, new RectangleF((s - ms) / 2f, (s - ms) / 2f, ms, ms), Color.FromArgb(242, 242, 243), 1f, small: tiny);
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
