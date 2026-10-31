using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LcdStudio.Core;

/// <summary>
/// Video files (.mp4 and friends) wherever a picture or GIF can go, the way <see cref="Lottie"/> does it: one loads as
/// an ordinary bitmap that is redrawn for the moment being shown, looping. The video is decoded once with Windows'
/// own Media Foundation into JPEG frames, kept beside it as "name.mp4.frames", so later loads are instant and nothing
/// extra needs installing. Every fan and preview showing the same clip shares one copy of the frames.
/// </summary>
public static class VideoClip
{
	public const double MaxSeconds = 30.0, MaxFps = 30.0;

	/// <summary>Frames are kept no larger than 1280×720 (or the same number of pixels), enough for the screen and fans.</summary>
	private const int Longest = 1280, Most = 1280 * 720;

	private static readonly string[] Extensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };

	public static string DialogPattern => "*" + string.Join(";*", Extensions);

	private sealed class Frames
	{
		public required List<byte[]> Jpegs;
		public required double Fps;
		public required int Width, Height;
	}

	private sealed class Playing
	{
		public required Frames Frames;
		public int Shown = -1;
	}

	private static readonly Dictionary<string, Frames> Shared = new Dictionary<string, Frames>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConditionalWeakTable<Image, Playing> Clips = new ConditionalWeakTable<Image, Playing>();

	public static bool IsVideo(string? path)
	{
		return Array.IndexOf(Extensions, Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;
	}

	public static string CachePath(string path) => path + ".frames";

	/// <summary>The video as a bitmap showing its first frame, or null if Windows can't decode it.</summary>
	public static Bitmap? Load(string path)
	{
		try
		{
			Frames? frames = Get(path);
			if (frames == null || frames.Jpegs.Count == 0)
			{
				return null;
			}
			var bitmap = new Bitmap(frames.Width, frames.Height, PixelFormat.Format24bppRgb);
			var playing = new Playing { Frames = frames };
			Clips.Add(bitmap, playing);
			Show(bitmap, playing, 0);
			return bitmap;
		}
		catch (Exception ex)
		{
			Log.Write("video unreadable " + Path.GetFileName(path) + ": " + ex.Message);
			return null;
		}
	}

	/// <summary>Decodes the video into its frame cache now (on upload), so the first showing doesn't wait. The frame count.</summary>
	public static int Prepare(string path)
	{
		return Get(path)?.Jpegs.Count ?? 0;
	}

	/// <summary>Drops the shared frames and the cache file, for a video being deleted.</summary>
	public static void Forget(string path)
	{
		lock (Shared)
		{
			Shared.Remove(path);
		}
		try
		{
			File.Delete(CachePath(path));
		}
		catch
		{
		}
	}

	/// <summary>Brings a video bitmap to the moment t, looping. False for any other picture.</summary>
	public static bool Seek(Image img, double t)
	{
		if (!Clips.TryGetValue(img, out Playing? playing))
		{
			return false;
		}
		Frames f = playing.Frames;
		long n = (long)Math.Floor(Math.Max(0.0, t) * f.Fps);
		Show((Bitmap)img, playing, (int)(n % f.Jpegs.Count));
		return true;
	}

	private static void Show(Bitmap bitmap, Playing playing, int index)
	{
		lock (playing)
		{
			if (index == playing.Shown)
			{
				return;
			}
			playing.Shown = index;
			using var ms = new MemoryStream(playing.Frames.Jpegs[index]);
			using var frame = new Bitmap(ms);
			using Graphics g = Graphics.FromImage(bitmap);
			g.CompositingMode = CompositingMode.SourceCopy;
			g.DrawImageUnscaled(frame, 0, 0);
		}
	}

	private static Frames? Get(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		lock (Shared)
		{
			if (Shared.TryGetValue(path, out Frames? have))
			{
				return have;
			}
		}
		Frames? frames = ReadCache(path) ?? Decode(path);
		if (frames == null)
		{
			return null;
		}
		lock (Shared)
		{
			Shared[path] = frames;
		}
		return frames;
	}

	// ---- the frame cache: "LLVF", fps, width, height, count, then each JPEG with its length ----

	private static Frames? ReadCache(string path)
	{
		string cache = CachePath(path);
		try
		{
			if (!File.Exists(cache) || File.GetLastWriteTimeUtc(cache) < File.GetLastWriteTimeUtc(path))
			{
				return null;
			}
			using var r = new BinaryReader(File.OpenRead(cache));
			if (r.ReadInt32() != 0x46564C4C)
			{
				return null;
			}
			double fps = r.ReadDouble();
			int w = r.ReadInt32(), h = r.ReadInt32(), count = r.ReadInt32();
			var list = new List<byte[]>(count);
			for (int i = 0; i < count; i++)
			{
				list.Add(r.ReadBytes(r.ReadInt32()));
			}
			return new Frames { Jpegs = list, Fps = fps, Width = w, Height = h };
		}
		catch
		{
			return null;
		}
	}

	private static void WriteCache(string path, Frames f)
	{
		try
		{
			string tmp = CachePath(path) + ".tmp";
			using (var w = new BinaryWriter(File.Create(tmp)))
			{
				w.Write(0x46564C4C);
				w.Write(f.Fps);
				w.Write(f.Width);
				w.Write(f.Height);
				w.Write(f.Jpegs.Count);
				foreach (byte[] j in f.Jpegs)
				{
					w.Write(j.Length);
					w.Write(j);
				}
			}
			File.Move(tmp, CachePath(path), overwrite: true);
		}
		catch (Exception ex)
		{
			Log.Write("video cache not written: " + ex.Message);
		}
	}

	// ---- decoding with Media Foundation's source reader, which converts any codec Windows has to RGB32 ----

	private static readonly Guid EnableVideoProcessing = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
	private static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
	private static readonly Guid SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
	private static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
	private static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");
	private static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
	private static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
	private static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
	private static readonly Guid DisplayAperture = new Guid("d7388766-18fe-48c6-a177-ee894867c8c4");

	private const uint FirstVideo = 0xFFFFFFFC, AllStreams = 0xFFFFFFFE, EndOfStream = 2;

	[DllImport("mfplat.dll")]
	private static extern int MFStartup(uint version, uint flags);

	[DllImport("mfplat.dll")]
	private static extern int MFShutdown();

	[DllImport("mfplat.dll")]
	private static extern int MFCreateAttributes(out IntPtr attributes, uint size);

	[DllImport("mfplat.dll")]
	private static extern int MFCreateMediaType(out IntPtr type);

	[DllImport("mfreadwrite.dll")]
	private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IntPtr reader);

	private static void Check(int hr, string what)
	{
		if (hr < 0)
		{
			throw new IOException($"{what} failed (0x{hr:x8})");
		}
	}

	private static unsafe IntPtr Slot(IntPtr com, int slot) => (*(IntPtr**)com)[slot];

	private static unsafe Frames? Decode(string path)
	{
		Check(MFStartup(0x00020070, 0), "MFStartup");
		IntPtr attrs = IntPtr.Zero, reader = IntPtr.Zero, want = IntPtr.Zero, have = IntPtr.Zero;
		try
		{
			Check(MFCreateAttributes(out attrs, 1), "MFCreateAttributes");
			Guid key = EnableVideoProcessing;
			Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Slot(attrs, 21))(attrs, &key, 1), "SetUINT32");
			Check(MFCreateSourceReaderFromURL(Path.GetFullPath(path), attrs, out reader), "Opening the video");
			var select = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Slot(reader, 4);
			select(reader, AllStreams, 0);
			Check(select(reader, FirstVideo, 1), "Finding a video stream");
			Check(MFCreateMediaType(out want), "MFCreateMediaType");
			var setGuid = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(want, 24);
			Guid k1 = MajorType, v1 = Video, k2 = SubType, v2 = Rgb32;
			setGuid(want, &k1, &v1);
			setGuid(want, &k2, &v2);
			Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, int>)Slot(reader, 7))(reader, FirstVideo, null, want), "Asking for RGB frames");
			Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(reader, 6))(reader, FirstVideo, &have), "Reading the frame format");
			var getU64 = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Slot(have, 8);
			var getU32 = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Slot(have, 7);
			ulong size = 0, rate = 0;
			Guid fs = FrameSize, fr = FrameRate, ds = DefaultStride, da = DisplayAperture;
			Check(getU64(have, &fs, &size), "Reading the frame size");
			int width = (int)(size >> 32), height = (int)(size & 0xFFFFFFFF);
			int stride = width * 4;
			uint s32;
			if (getU32(have, &ds, &s32) >= 0)
			{
				stride = (int)s32;
			}
			// the picture inside the coded frame (a 1080p video is often coded 1088 tall)
			int cropX = 0, cropY = 0, cropW = width, cropH = height;
			byte* area = stackalloc byte[16];
			uint got;
			if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, byte*, uint, uint*, int>)Slot(have, 15))(have, &da, area, 16, &got) >= 0 && got >= 16)
			{
				cropX = *(short*)(area + 2);
				cropY = *(short*)(area + 6);
				cropW = *(int*)(area + 8);
				cropH = *(int*)(area + 12);
				if (cropW <= 0 || cropH <= 0 || cropX + cropW > width || cropY + cropH > height)
				{
					(cropX, cropY, cropW, cropH) = (0, 0, width, height);
				}
			}
			double srcFps = getU64(have, &fr, &rate) >= 0 && (rate & 0xFFFFFFFF) != 0 ? (double)(rate >> 32) / (rate & 0xFFFFFFFF) : 30.0;
			double fps = Math.Clamp(srcFps, 1.0, MaxFps);
			float scale = Math.Min(1f, Math.Min((float)Longest / Math.Max(cropW, cropH), MathF.Sqrt((float)Most / (cropW * (float)cropH))));
			int outW = Math.Max(2, (int)Math.Round(cropW * scale)), outH = Math.Max(2, (int)Math.Round(cropH * scale));
			var jpegs = new List<byte[]>();
			var read = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Slot(reader, 9);
			double next = 0.0;
			using var full = new Bitmap(width, height, PixelFormat.Format32bppRgb);
			using var small = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
			using (Graphics g = Graphics.FromImage(small))
			{
				g.InterpolationMode = InterpolationMode.HighQualityBilinear;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				while (true)
				{
					uint stream, flags;
					long time;
					IntPtr sample = IntPtr.Zero;
					Check(read(reader, FirstVideo, 0, &stream, &flags, &time, &sample), "Reading a frame");
					if (sample == IntPtr.Zero)
					{
						if ((flags & EndOfStream) != 0)
						{
							break;
						}
						continue;
					}
					try
					{
						double at = time / 1e7;
						if (at >= MaxSeconds)
						{
							break;
						}
						// keeps one frame per step of the chosen rate
						if (at + 0.5 / fps < next)
						{
							continue;
						}
						next += 1.0 / fps;
						IntPtr buffer;
						Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(sample, 41))(sample, &buffer), "Reading a frame's pixels");
						try
						{
							byte* data;
							uint max, len;
							Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &max, &len), "Locking a frame");
							try
							{
								BitmapData bd = full.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
								try
								{
									int rowBytes = width * 4, abs = Math.Abs(stride);
									for (int y = 0; y < height; y++)
									{
										// a negative stride means the rows are stored bottom-up
										byte* src = stride >= 0 ? data + (long)y * abs : data + (long)(height - 1 - y) * abs;
										if ((long)(src - data) + rowBytes > len)
										{
											break;
										}
										Buffer.MemoryCopy(src, (byte*)bd.Scan0 + (long)y * bd.Stride, rowBytes, rowBytes);
									}
								}
								finally
								{
									full.UnlockBits(bd);
								}
							}
							finally
							{
								((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, 4))(buffer);
							}
						}
						finally
						{
							Marshal.Release(buffer);
						}
						g.DrawImage(full, new Rectangle(0, 0, outW, outH), cropX, cropY, cropW, cropH, GraphicsUnit.Pixel);
						jpegs.Add(Imaging.Jpeg(small, 85));
					}
					finally
					{
						Marshal.Release(sample);
					}
				}
			}
			if (jpegs.Count == 0)
			{
				return null;
			}
			var frames = new Frames { Jpegs = jpegs, Fps = fps, Width = outW, Height = outH };
			WriteCache(path, frames);
			Log.Write($"video {Path.GetFileName(path)}: {jpegs.Count} frames {outW}x{outH} at {fps:0.#} fps");
			return frames;
		}
		finally
		{
			foreach (IntPtr p in new[] { have, want, reader, attrs })
			{
				if (p != IntPtr.Zero)
				{
					Marshal.Release(p);
				}
			}
			MFShutdown();
		}
	}
}
