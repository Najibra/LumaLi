using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SkiaSharp;
using SkiaSharp.Resources;
using SkiaSharp.Skottie;

namespace LcdStudio.Core;

/// <summary>
/// Lottie animations (.json, or .lottie, the zipped kind) wherever a picture or GIF can go. One loads as an ordinary
/// bitmap the size of a frame, and Skottie redraws it for the moment being shown, looping, so everything that already
/// plays a GIF plays it without knowing the difference.
/// </summary>
public static class Lottie
{
	private sealed class Playing
	{
		public required Animation Animation;
		public long Frame = -1;
	}

	private static readonly ConditionalWeakTable<Image, Playing> Animations = new ConditionalWeakTable<Image, Playing>();

	/// <summary>
	/// Frames are drawn as large as the screen holds: no wider than it, and no more pixels in all, so one made for the
	/// screen is drawn at its full sharpness and a square one for the fans no larger than it needs.
	/// </summary>
	private const int Longest = 1920, Most = 1920 * 480;

	public static bool IsLottie(string? path)
	{
		string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		return ext == ".json" || ext == ".lottie";
	}

	/// <summary>The animation in the file as a bitmap showing its first frame, or null if it is not one Skottie can play.</summary>
	public static Bitmap? Load(string path)
	{
		try
		{
			string json = path.EndsWith(".lottie", StringComparison.OrdinalIgnoreCase) ? Unzip(path) : File.ReadAllText(path);
			return FromJson(json, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
		}
		catch
		{
			return null;
		}
	}

	private static Bitmap? FromJson(string json, string dir)
	{
		try
		{
			ResourceProvider files = new FileResourceProvider(dir, preDecode: true);
			using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
			Animation? animation = Animation.CreateBuilder().SetResourceProvider(new DataUriResourceProvider(files, preDecode: true)).Build(stream);
			if (animation == null || animation.Size.Width < 1f || animation.Size.Height < 1f)
			{
				animation?.Dispose();
				return null;
			}
			float scale = Math.Min(Longest / Math.Max(animation.Size.Width, animation.Size.Height), MathF.Sqrt(Most / (animation.Size.Width * animation.Size.Height)));
			var bitmap = new Bitmap(Math.Max(1, (int)Math.Round(animation.Size.Width * scale)), Math.Max(1, (int)Math.Round(animation.Size.Height * scale)), PixelFormat.Format32bppPArgb);
			var playing = new Playing { Animation = animation };
			Animations.Add(bitmap, playing);
			Draw(bitmap, playing, 0.0);
			return bitmap;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// The animation inside a .lottie: the first one its manifest names (or the first there is), with the pictures it
	/// keeps beside it in the zip folded into it as data, so nothing is unpacked to disk.
	/// </summary>
	private static string Unzip(string path)
	{
		using ZipArchive zip = ZipFile.OpenRead(path);
		string? Text(ZipArchiveEntry? e)
		{
			if (e == null || e.Length > 64L << 20)
			{
				return null;
			}
			using var r = new StreamReader(e.Open());
			return r.ReadToEnd();
		}
		// some zippers on Windows write their folders with backslashes
		static string Named(ZipArchiveEntry e) => e.FullName.Replace('\\', '/').TrimStart('/');
		ZipArchiveEntry? Entry(string name) => zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => string.Equals(Named(e), name, StringComparison.OrdinalIgnoreCase));
		string? id = null;
		try
		{
			id = JsonNode.Parse(Text(Entry("manifest.json")) ?? "")?["animations"]?[0]?["id"]?.GetValue<string>();
		}
		catch
		{
		}
		ZipArchiveEntry anim = (id != null ? Entry("animations/" + id + ".json") : null)
			?? zip.Entries.FirstOrDefault(e => Named(e).StartsWith("animations/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidDataException("no animation in the .lottie");
		string json = Text(anim) ?? throw new InvalidDataException("animation too large");
		JsonNode? root = JsonNode.Parse(json);
		if (root?["assets"] is not JsonArray assets)
		{
			return json;
		}
		foreach (JsonNode? asset in assets)
		{
			string? p = asset?["p"]?.GetValue<string>();
			if (asset == null || p == null || p.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string dir = asset["u"]?.GetValue<string>() ?? "";
			ZipArchiveEntry? img = Entry((dir.TrimStart('/') + p).Replace('\\', '/')) ?? Entry("images/" + p);
			if (img == null || img.Length > 32L << 20)
			{
				continue;
			}
			using var ms = new MemoryStream();
			using (Stream s = img.Open())
			{
				s.CopyTo(ms);
			}
			string mime = Path.GetExtension(p).ToLowerInvariant() switch
			{
				".jpg" or ".jpeg" => "image/jpeg",
				".webp" => "image/webp",
				".svg" => "image/svg+xml",
				_ => "image/png",
			};
			asset["u"] = "";
			asset["p"] = "data:" + mime + ";base64," + Convert.ToBase64String(ms.ToArray());
			asset["e"] = 1;
		}
		return root.ToJsonString();
	}

	/// <summary>Brings a Lottie bitmap to the moment t, looping. False for any other picture.</summary>
	public static bool Seek(Image img, double t)
	{
		if (!Animations.TryGetValue(img, out Playing? playing))
		{
			return false;
		}
		Draw((Bitmap)img, playing, t);
		return true;
	}

	/// <summary>
	/// The moment of a Lottie bitmap that shows a gauge at the fraction given (0 is its first frame, 1 its last), for
	/// <see cref="Seek"/> to play scrubbed by a reading rather than looping. False for any other picture.
	/// </summary>
	public static bool GaugeTime(Image img, double fraction, out double t)
	{
		t = 0.0;
		if (!Animations.TryGetValue(img, out Playing? playing))
		{
			return false;
		}
		Animation a = playing.Animation;
		double duration = Math.Max(a.Duration.TotalSeconds, 1e-3), fps = a.Fps > 0 ? Math.Min(a.Fps, 60.0) : 30.0;
		// the last whole frame, so a full gauge does not wrap round to the first
		double last = Math.Max(0.0, Math.Ceiling(duration * fps) - 1.0);
		t = (Math.Round(Math.Clamp(double.IsNaN(fraction) ? 0.0 : fraction, 0.0, 1.0) * last) + 0.5) / fps;
		return true;
	}

	private static unsafe void Draw(Bitmap bitmap, Playing playing, double t)
	{
		Animation a = playing.Animation;
		double duration = Math.Max(a.Duration.TotalSeconds, 1e-3), fps = a.Fps > 0 ? Math.Min(a.Fps, 60.0) : 30.0;
		double at = (t % duration + duration) % duration;
		// drawn only when the animation has moved on a frame of its own
		long frame = (long)Math.Floor(at * fps);
		lock (playing)
		{
			if (frame == playing.Frame)
			{
				return;
			}
			playing.Frame = frame;
			BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
			try
			{
				using SKSurface surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul), data.Scan0, data.Stride);
				surface.Canvas.Clear(SKColors.Transparent);
				a.SeekFrameTime(frame / fps);
				a.Render(surface.Canvas, new SKRect(0f, 0f, bitmap.Width, bitmap.Height));
				surface.Canvas.Flush();
			}
			finally
			{
				bitmap.UnlockBits(data);
			}
		}
	}
}
