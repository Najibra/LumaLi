using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LcdStudio;

public static class Imaging
{
	private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().First((ImageCodecInfo c) => c.FormatID == ImageFormat.Jpeg.Guid);

	private static readonly Dictionary<Image, (int[] Delays, int Total)> GifTimes = new Dictionary<Image, (int[], int)>();

	public static byte[] Jpeg(Bitmap bmp, int quality)
	{
		using MemoryStream memoryStream = new MemoryStream(262144);
		using EncoderParameters encoderParameters = new EncoderParameters(1);
		encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 30, 100));
		bmp.Save(memoryStream, JpegCodec, encoderParameters);
		return memoryStream.ToArray();
	}

	public static byte[] Png(Bitmap bmp)
	{
		using MemoryStream memoryStream = new MemoryStream();
		bmp.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	/// <summary>Mixes src over dst in place: amount 1 shows only src, 0 leaves dst. Both the same size; ~2 ms at 1920 × 480.</summary>
	public static unsafe void Blend(Bitmap dst, Bitmap src, float amount)
	{
		if (dst.Width != src.Width || dst.Height != src.Height || amount <= 0f)
		{
			return;
		}
		int a = (int)(Math.Clamp(amount, 0f, 1f) * 256f), b = 256 - a;
		Rectangle rect = new Rectangle(0, 0, dst.Width, dst.Height);
		BitmapData d = dst.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
		BitmapData s = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			for (int y = 0; y < rect.Height; y++)
			{
				byte* pd = (byte*)d.Scan0 + (long)y * d.Stride;
				byte* ps = (byte*)s.Scan0 + (long)y * s.Stride;
				int n = rect.Width * 4;
				for (int i = 0; i < n; i++)
				{
					pd[i] = (byte)((pd[i] * b + ps[i] * a) >> 8);
				}
			}
		}
		finally
		{
			src.UnlockBits(s);
			dst.UnlockBits(d);
		}
	}

	public static Bitmap Rotated(Bitmap src, int degrees)
	{
		degrees = (degrees % 360 + 360) % 360;
		bool flag = ((degrees == 90 || degrees == 270) ? true : false);
		bool flag2 = flag;
		Bitmap bitmap = new Bitmap(flag2 ? src.Height : src.Width, flag2 ? src.Width : src.Height, PixelFormat.Format24bppRgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
		graphics.PixelOffsetMode = PixelOffsetMode.Half;
		graphics.TranslateTransform((float)bitmap.Width / 2f, (float)bitmap.Height / 2f);
		graphics.RotateTransform(degrees);
		graphics.TranslateTransform((float)(-src.Width) / 2f, (float)(-src.Height) / 2f);
		graphics.DrawImageUnscaled(src, 0, 0);
		return bitmap;
	}

	public static Color Hex(string? s, Color fallback)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return fallback;
		}
		s = s.Trim().TrimStart('#');
		if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, null, out var result))
		{
			return Color.FromArgb(255, Color.FromArgb(result));
		}
		if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, null, out var result2))
		{
			return Color.FromArgb((int)result2);
		}
		return fallback;
	}

	public static string Hex(Color c)
	{
		if (c.A == byte.MaxValue)
		{
			return $"#{c.R:x2}{c.G:x2}{c.B:x2}";
		}
		return $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";
	}

	public static Color WithAlpha(Color c, int a)
	{
		return Color.FromArgb(Math.Clamp(a, 0, 255), c.R, c.G, c.B);
	}

	public static Color Mix(Color a, Color b, double t)
	{
		t = Math.Clamp(t, 0.0, 1.0);
		return Color.FromArgb((int)((double)(int)a.A + (double)(b.A - a.A) * t), (int)((double)(int)a.R + (double)(b.R - a.R) * t), (int)((double)(int)a.G + (double)(b.G - a.G) * t), (int)((double)(int)a.B + (double)(b.B - a.B) * t));
	}

	public static Color Hue(double h, double sat = 1.0, double val = 1.0)
	{
		h = (h % 1.0 + 1.0) % 1.0 * 6.0;
		int num = (int)h;
		double num2 = h - (double)num;
		double num3 = val * (1.0 - sat);
		double num4 = val * (1.0 - sat * num2);
		double num5 = val * (1.0 - sat * (1.0 - num2));
		var (num6, num7, num8) = num switch
		{
			0 => (val, num5, num3), 
			1 => (num4, val, num3), 
			2 => (num3, val, num5), 
			3 => (num3, num4, val), 
			4 => (num5, num3, val), 
			_ => (val, num3, num4), 
		};
		return Color.FromArgb((int)(num6 * 255.0), (int)(num7 * 255.0), (int)(num8 * 255.0));
	}

	public static GraphicsPath Round(RectangleF r, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
		if (num <= 1f)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	public static Image? Load(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		if (LcdStudio.Core.Lottie.IsLottie(path))
		{
			return LcdStudio.Core.Lottie.Load(path);
		}
		if (LcdStudio.Core.VideoClip.IsVideo(path))
		{
			return LcdStudio.Core.VideoClip.Load(path);
		}
		try
		{
			return Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Whether the picture at the path moves: a GIF, a Lottie animation or a video.</summary>
	public static bool IsMoving(string? path)
	{
		return (path?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ?? false) || LcdStudio.Core.Lottie.IsLottie(path) || LcdStudio.Core.VideoClip.IsVideo(path);
	}

	public static void DrawCover(Graphics g, Image img, RectangleF r, double t, float opacity = 1f)
	{
		SelectGifFrame(img, t);
		float num = Math.Max(r.Width / (float)img.Width, r.Height / (float)img.Height);
		float num2 = (float)img.Width * num;
		float num3 = (float)img.Height * num;
		RectangleF rectangleF = new RectangleF(r.X + (r.Width - num2) / 2f, r.Y + (r.Height - num3) / 2f, num2, num3);
		GraphicsState gstate = g.Save();
		g.SetClip(r, CombineMode.Intersect);
		if (opacity >= 0.999f)
		{
			g.DrawImage(img, rectangleF);
		}
		else
		{
			using ImageAttributes imageAttributes = new ImageAttributes();
			imageAttributes.SetColorMatrix(new ColorMatrix
			{
				Matrix33 = opacity
			});
			g.DrawImage(img, Rectangle.Round(rectangleF), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, imageAttributes);
		}
		g.Restore(gstate);
	}

	public static void SelectGifFrame(Image img, double t)
	{
		if (LcdStudio.Core.Lottie.Seek(img, t) || LcdStudio.Core.VideoClip.Seek(img, t) || !img.FrameDimensionsList.Contains(FrameDimension.Time.Guid))
		{
			return;
		}
		int frameCount = img.GetFrameCount(FrameDimension.Time);
		if (frameCount <= 1)
		{
			return;
		}
		(int[], int) value;
		lock (GifTimes)
		{
			if (!GifTimes.TryGetValue(img, out value))
			{
				int[] array = new int[frameCount];
				try
				{
					byte[] value2 = img.GetPropertyItem(20736).Value;
					for (int i = 0; i < frameCount; i++)
					{
						array[i] = Math.Max(2, BitConverter.ToInt32(value2, i * 4)) * 10;
					}
				}
				catch
				{
					Array.Fill(array, 100);
				}
				value = (array, array.Sum());
				GifTimes[img] = value;
			}
		}
		int num = (int)(t * 1000.0 % (double)value.Item2);
		int num2 = 0;
		int j;
		for (j = 0; j < frameCount - 1; j++)
		{
			num2 += value.Item1[j];
			if (num < num2)
			{
				break;
			}
		}
		img.SelectActiveFrame(FrameDimension.Time, j);
	}
}
