using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace LcdStudio.Core;

public sealed class VideoFrames : IDisposable
{
	private string? _folder;

	private List<byte[]> _frames = new List<byte[]>();

	private int _shownIndex = -1;

	private Bitmap? _shown;

	public int Count => _frames.Count;

	public static bool IsFrameFolder(string? path)
	{
		if (!string.IsNullOrEmpty(path))
		{
			return Directory.Exists(path);
		}
		return false;
	}

	private void Load(string? folder)
	{
		if (folder == _folder)
		{
			return;
		}
		_folder = folder;
		_frames = new List<byte[]>();
		_shown?.Dispose();
		_shown = null;
		_shownIndex = -1;
		if (!IsFrameFolder(folder))
		{
			return;
		}
		foreach (string item in Directory.GetFiles(folder, "*.jpg").OrderBy<string, string>((string f) => f, StringComparer.OrdinalIgnoreCase))
		{
			_frames.Add(File.ReadAllBytes(item));
		}
	}

	public void Draw(Graphics g, string? folder, int fps, double t, RectangleF dest, float opacity)
	{
		Load(folder);
		if (_frames.Count == 0)
		{
			return;
		}
		int num = (int)(t * (double)Math.Clamp(fps, 1, 60)) % _frames.Count;
		if (num != _shownIndex)
		{
			_shown?.Dispose();
			using MemoryStream stream = new MemoryStream(_frames[num]);
			_shown = new Bitmap(stream);
			_shownIndex = num;
		}
		if (opacity >= 0.999f && _shown.Width == (int)dest.Width && _shown.Height == (int)dest.Height)
		{
			g.DrawImageUnscaled(_shown, (int)dest.X, (int)dest.Y);
		}
		else
		{
			Imaging.DrawCover(g, _shown, dest, t, opacity);
		}
	}

	public static int MakeFrames(string source, string dest, bool sideways, bool stretch = false, int quality = 88)
	{
		Directory.CreateDirectory(dest);
		int result = 0;
		foreach (string item in Directory.GetFiles(source, "*.jpg").OrderBy<string, string>((string f) => f, StringComparer.OrdinalIgnoreCase))
		{
			using Bitmap bitmap = new Bitmap(item);
			if (sideways)
			{
				bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
			}
			using Bitmap bitmap2 = new Bitmap(1920, 480, PixelFormat.Format24bppRgb);
			using (Graphics graphics = Graphics.FromImage(bitmap2))
			{
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.Clear(Color.Black);
				if (stretch)
				{
					graphics.DrawImage(bitmap, new Rectangle(0, 0, 1920, 480));
				}
				else
				{
					Imaging.DrawCover(graphics, bitmap, new RectangleF(0f, 0f, 1920f, 480f), 0.0);
				}
			}
			File.WriteAllBytes(Path.Combine(dest, $"{result++:0000}.jpg"), Imaging.Jpeg(bitmap2, quality));
		}
		return result;
	}

	public void Dispose()
	{
		_shown?.Dispose();
	}
}
