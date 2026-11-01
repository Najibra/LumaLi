using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Drawing with Skia onto the frame being rendered: smooth, sub-pixel, additive light where GDI+ snaps to whole
/// pixels and can only paint over.
/// </summary>
public sealed partial class ScreenRenderer
{
	private Bitmap? _skLayer;

	/// <summary>The surface SkiaDraw is drawing on, for work done straight on its pixels (it covers the whole frame there).</summary>
	private SKSurface? _skDrawing;

	/// <summary>
	/// Runs the drawing on a Skia canvas over the given part of the frame, in frame coordinates. While a frame is being
	/// rendered it draws straight into the frame's pixels (opaque by then, so light can add onto what is there);
	/// anywhere else it draws on a clear layer laid over the target.
	/// </summary>
	private unsafe void SkiaDraw(Graphics g, Rectangle area, Action<SKCanvas> draw)
	{
		area = Rectangle.Intersect(area, new Rectangle(0, 0, _w, _h));
		if (area.Width <= 0 || area.Height <= 0)
		{
			return;
		}
		bool direct = _canvas != null && _canvasGraphics == g;
		Bitmap target;
		Rectangle locked;
		if (direct)
		{
			g.Flush(FlushIntention.Sync);
			target = _canvas!;
			locked = area;
		}
		else
		{
			if (_skLayer == null || _skLayer.Width != area.Width || _skLayer.Height != area.Height)
			{
				_skLayer?.Dispose();
				_skLayer = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
			}
			target = _skLayer;
			locked = new Rectangle(0, 0, area.Width, area.Height);
		}
		BitmapData data = target.LockBits(locked, ImageLockMode.ReadWrite, direct ? PixelFormat.Format32bppArgb : PixelFormat.Format32bppPArgb);
		try
		{
			using SKSurface surface = SKSurface.Create(new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul), data.Scan0, data.Stride);
			SKCanvas c = surface.Canvas;
			if (!direct)
			{
				c.Clear(SKColors.Transparent);
			}
			c.Translate(-area.X, -area.Y);
			_skDrawing = surface;
			draw(c);
			c.Flush();
		}
		finally
		{
			_skDrawing = null;
			target.UnlockBits(data);
		}
		if (!direct)
		{
			// placed at its exact pixel size: DrawImageUnscaled scales by the bitmap's DPI, which in the app window is
			// the display's (125 % and up), so previews came out enlarged and shifted
			g.DrawImage(_skLayer!, new Rectangle(area.X, area.Y, area.Width, area.Height), new Rectangle(0, 0, area.Width, area.Height), GraphicsUnit.Pixel);
		}
	}
}

/// <summary>Small Skia drawing pieces shared by the screen layouts and the fan faces.</summary>
internal static class SkiaKit
{
	/// <summary>Bilinear sampling, for soft layers drawn scaled (Skia 3 takes it per draw, not from the paint).</summary>
	public static readonly SKSamplingOptions Linear = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

	[ThreadStatic]
	private static uint[]? _across;

	/// <summary>
	/// Stretches a soft layer two or four times over onto the target and adds it as light adds, sampled as Skia's
	/// bilinear would. Skia 3 stretches bilinearly on the CPU at a crawl (five milliseconds to fill the screen), so it
	/// is done here: across at the layer's own height first, then each row of the target blended from its two nearest
	/// by repeated halving, a few vector operations a row.
	/// </summary>
	public static unsafe void UpscaleAdd(SKImage small, SKPixmap target)
	{
		using SKPixmap src = small.PeekPixels();
		int sw = src.Width, sh = src.Height, w = target.Width, h = target.Height, scale = w / sw;
		if (scale * sw != w || scale * sh != h || (scale != 2 && scale != 4) || src.ColorType != SKColorType.Bgra8888 || target.ColorType != SKColorType.Bgra8888)
		{
			throw new ArgumentException("UpscaleAdd stretches 32-bit layers two or four times over");
		}
		// a pixel's centre x + 0.5 falls on the layer at (x + 0.5) / scale; the two layer pixels either side of it, and
		// the share of the right one in 256ths, clamped at the edges
		static void Taps(int i, int scale, int n, out int i0, out int i1, out int f)
		{
			float u = (i + 0.5f) / scale - 0.5f;
			int a = (int)Math.Floor(u);
			f = (int)((u - a) * 256f + 0.5f);
			i0 = Math.Clamp(a, 0, n - 1);
			i1 = Math.Clamp(a + 1, 0, n - 1);
		}
		uint[] across = _across is { } buf && buf.Length >= w * sh ? buf : (_across = new uint[w * sh]);
		fixed (uint* acr = across)
		{
			byte* sp = (byte*)src.GetPixels();
			for (int y = 0; y < sh; y++)
			{
				uint* row = (uint*)(sp + y * src.RowBytes), o = acr + y * w;
				for (int x = 0; x < w; x++)
				{
					Taps(x, scale, sw, out int i0, out int i1, out int f);
					uint a = row[i0], b = row[i1], fa = (uint)(256 - f), fb = (uint)f;
					o[x] = ((((a & 0x00FF00FFu) * fa + (b & 0x00FF00FFu) * fb) >> 8) & 0x00FF00FFu)
						| ((((a >> 8) & 0x00FF00FFu) * fa + ((b >> 8) & 0x00FF00FFu) * fb) & 0xFF00FF00u);
				}
			}
			byte* dp = (byte*)target.GetPixels();
			int bytes = w * 4;
			for (int y = 0; y < h; y++)
			{
				// the share of the lower row lands on eighths (1, 3, 5, 7 four times over; 2, 6 twice over; 0 at the edges)
				Taps(y, scale, sh, out int j0, out int j1, out int f);
				int n = (f + 16) / 32;
				byte* a = (byte*)(acr + j0 * w), b = (byte*)(acr + j1 * w), d = dp + y * target.RowBytes;
				int x = 0;
				for (; x + 16 <= bytes; x += 16)
				{
					Vector128<byte> va = Sse2.LoadVector128(a + x), vb = Sse2.LoadVector128(b + x);
					Vector128<byte> m = Sse2.Average(va, vb), v = n switch
					{
						0 => va,
						1 => Sse2.Average(va, Sse2.Average(va, m)),
						2 => Sse2.Average(va, m),
						3 => Sse2.Average(m, Sse2.Average(va, m)),
						4 => m,
						5 => Sse2.Average(m, Sse2.Average(m, vb)),
						6 => Sse2.Average(m, vb),
						7 => Sse2.Average(vb, Sse2.Average(m, vb)),
						_ => vb,
					};
					Sse2.Store(d + x, Sse2.AddSaturate(Sse2.LoadVector128(d + x), v));
				}
				for (; x < bytes; x++)
				{
					d[x] = (byte)Math.Min(255, d[x] + (a[x] * (8 - n) + b[x] * n + 4) / 8);
				}
			}
		}
	}

	public static SKColor Sk(Color c, float alpha = 1f) => new SKColor(c.R, c.G, c.B, (byte)Math.Clamp((int)(c.A * alpha), 0, 255));

	/// <summary>A smooth curve through the points: each span a cubic whose ends follow the neighbouring points.</summary>
	public static SKPath Curve(PointF[] p)
	{
		var path = new SKPath();
		path.MoveTo(p[0].X, p[0].Y);
		for (int i = 0; i < p.Length - 1; i++)
		{
			PointF a = p[Math.Max(0, i - 1)], b = p[i], c = p[i + 1], d = p[Math.Min(p.Length - 1, i + 2)];
			path.CubicTo(b.X + (c.X - a.X) / 6f, b.Y + (c.Y - a.Y) / 6f, c.X - (d.X - b.X) / 6f, c.Y - (d.Y - b.Y) / 6f, c.X, c.Y);
		}
		return path;
	}

	/// <summary>A GDI+ path (text outlines, say) as a Skia path, keeping its fill rule.</summary>
	public static SKPath FromGdi(System.Drawing.Drawing2D.GraphicsPath gp)
	{
		var p = new SKPath { FillType = gp.FillMode == System.Drawing.Drawing2D.FillMode.Alternate ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
		if (gp.PointCount == 0)
		{
			return p;
		}
		PointF[] pts = gp.PathPoints;
		byte[] types = gp.PathTypes;
		for (int i = 0; i < pts.Length; i++)
		{
			switch (types[i] & 0x07)
			{
			case 0:
				p.MoveTo(pts[i].X, pts[i].Y);
				break;
			case 1:
				p.LineTo(pts[i].X, pts[i].Y);
				break;
			case 3:
				p.CubicTo(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, pts[i + 2].X, pts[i + 2].Y);
				i += 2;
				break;
			}
			if ((types[i] & 0x80) != 0)
			{
				p.Close();
			}
		}
		return p;
	}

	/// <summary>A soft elliptical glow, brightest at its centre and fading to nothing at its rim.</summary>
	public static void Glow(SKCanvas c, float x, float y, float rx, float ry, SKColor color, SKBlendMode mode)
	{
		c.Save();
		c.Translate(x, y);
		c.Scale(rx, ry);
		using var shader = SKShader.CreateRadialGradient(new SKPoint(0f, 0f), 1f, new[] { color, color.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
		using var paint = new SKPaint { Shader = shader, IsAntialias = true, BlendMode = mode };
		c.DrawCircle(0f, 0f, 1f, paint);
		c.Restore();
	}
}
