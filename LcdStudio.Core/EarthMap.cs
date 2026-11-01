using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LcdStudio.Core;

/// <summary>
/// The Earth for Mission Control (screen and fans): NASA's Blue Marble (day) and Black Marble (night city lights),
/// both public domain and bundled in the exe, blended along the real day/night line for the current time.
/// </summary>
public static class EarthMap
{
	public const int W = 960, H = 480;

	private static readonly object Gate = new object();

	private static byte[]? _day, _night, _now;

	private static DateTime _at = DateTime.MinValue;

	/// <summary>One of the bundled maps as 960 × 480 BGRA pixels.</summary>
	private static byte[] Load(string name)
	{
		var px = new byte[W * H * 4];
		using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
		if (s == null)
		{
			return px;
		}
		using var src = new Bitmap(s);
		using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
		using (Graphics g = Graphics.FromImage(bmp))
		{
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.DrawImage(src, new Rectangle(0, 0, W, H));
		}
		BitmapData d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			for (int y = 0; y < H; y++)
			{
				Marshal.Copy(d.Scan0 + y * d.Stride, px, y * W * 4, W * 4);
			}
		}
		finally
		{
			bmp.UnlockBits(d);
		}
		return px;
	}

	/// <summary>Where the sun is overhead: its latitude (declination) and longitude, in degrees.</summary>
	public static (double Lat, double Lon) Sun(DateTime utc)
	{
		double day = utc.DayOfYear + utc.TimeOfDay.TotalHours / 24.0;
		double decl = 23.44 * Math.Sin(2.0 * Math.PI * (284.0 + day) / 365.0);
		double lon = (12.0 - utc.TimeOfDay.TotalHours) * 15.0;
		lon = (lon + 540.0) % 360.0 - 180.0;
		return (decl, lon);
	}

	/// <summary>
	/// The map for now, 960 × 480 BGRA (opaque): the muted day side and the night side's city lights, blended across a
	/// soft twilight band along the terminator. Made again once a minute; the array is never changed after it is
	/// handed out, so callers may keep it.
	/// </summary>
	public static byte[] Now(DateTime utc)
	{
		lock (Gate)
		{
			if (_now != null && utc >= _at && (utc - _at).TotalSeconds < 60.0)
			{
				return _now;
			}
			_day ??= Load("earth-day.jpg");
			_night ??= Load("earth-night.jpg");
			var (sLat, sLon) = Sun(utc);
			double sd = Math.Sin(sLat * Math.PI / 180.0), cd = Math.Cos(sLat * Math.PI / 180.0);
			var cosCol = new double[W];
			for (int x = 0; x < W; x++)
			{
				double lon = (x + 0.5) / W * 360.0 - 180.0;
				cosCol[x] = Math.Cos((lon - sLon) * Math.PI / 180.0);
			}
			var px = new byte[W * H * 4];
			for (int y = 0; y < H; y++)
			{
				double lat = 90.0 - (y + 0.5) / H * 180.0;
				double sp = Math.Sin(lat * Math.PI / 180.0) * sd, cp = Math.Cos(lat * Math.PI / 180.0) * cd;
				int row = y * W * 4;
				for (int x = 0; x < W; x++)
				{
					float cz = (float)(sp + cp * cosCol[x]);
					float u = Math.Clamp((cz + 0.1f) / 0.18f, 0f, 1f);
					float w = u * u * (3f - 2f * u);
					int i = row + x * 4;
					for (int k = 0; k < 3; k++)
					{
						// the day side muted and cooled so overlays read over it; the night side's lights lifted
						float day = _day[i + k] * (k == 0 ? 0.62f : 0.5f);
						float night = Math.Min(255f, _night[i + k] * 1.35f);
						px[i + k] = (byte)(day * w + night * (1f - w));
					}
					px[i + 3] = 255;
				}
			}
			_now = px;
			_at = utc;
			return px;
		}
	}
}
