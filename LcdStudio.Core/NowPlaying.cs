using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>What is playing, as Windows' media controls report it (Spotify, a browser tab, a media player).</summary>
public sealed record MediaInfo(string Title, string Artist, string Album, string App, bool Playing, double Position, double Length, DateTime At)
{
	/// <summary>The position now: moved on since the player last reported it, while it plays.</summary>
	public double PositionNow
	{
		get
		{
			double p = Position;
			if (Playing)
			{
				p += Math.Max(0.0, (DateTime.UtcNow - At).TotalSeconds);
			}
			return Length > 0.0 ? Math.Clamp(p, 0.0, Length) : Math.Max(0.0, p);
		}
	}

	public float Progress => Length > 0.0 ? (float)Math.Clamp(PositionNow / Length, 0.0, 1.0) : 0f;

	public static string Clock(double seconds)
	{
		int s = (int)Math.Max(0.0, seconds);
		return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
	}
}

/// <summary>
/// Reads the media session Windows shows in its volume flyout (GlobalSystemMediaTransportControls) once a second on its
/// own thread: title, artist, album, the app, playing or paused, position and length, and the cover art. It talks to
/// the WinRT interfaces through raw vtable calls (slot numbers and IIDs from C:\Windows\System32\WinMetadata\
/// Windows.Media.winmd), so the exe needs no Windows SDK projection. Read-only: it never controls playback.
/// </summary>
public static class NowPlaying
{
	private static readonly object Gate = new object();

	private static MediaInfo? _current;

	private static SKImage? _art;

	private static SKColor[] _artColors = { new SKColor(255, 75, 43), new SKColor(122, 60, 255) };

	private static int _artVersion;

	private static Thread? _thread;

	private static volatile bool _demo;

	/// <summary>The session playing or paused now, or null when nothing is.</summary>
	public static MediaInfo? Current
	{
		get
		{
			lock (Gate)
			{
				return _current;
			}
		}
	}

	/// <summary>The cover art (at most 512 px), or null. Its version changes with every new picture.</summary>
	public static (SKImage? Art, int Version, SKColor[] Colors) Art
	{
		get
		{
			lock (Gate)
			{
				return (_art, _artVersion, _artColors);
			}
		}
	}

	/// <summary>Starts reading (full app and previews). Safe to call more than once.</summary>
	public static void Start()
	{
		lock (Gate)
		{
			if (_thread != null || _demo)
			{
				return;
			}
			_thread = new Thread(Loop) { IsBackground = true, Name = "LumaLi media" };
			_thread.Start();
		}
	}

	/// <summary>A made-up song with generated cover art, for previews.</summary>
	public static void Demo(bool playing = true)
	{
		_demo = true;
		using var surface = SKSurface.Create(new SKImageInfo(400, 400, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		using (var p = new SKPaint { IsAntialias = true })
		{
			p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(400, 400), new[] { new SKColor(255, 94, 58), new SKColor(214, 38, 140), new SKColor(76, 40, 190) }, null, SKShaderTileMode.Clamp);
			c.DrawRect(0, 0, 400, 400, p);
			p.Shader = null;
			p.Color = new SKColor(255, 214, 120, 230);
			c.DrawCircle(250, 170, 92, p);
			p.Color = new SKColor(20, 10, 40, 150);
			for (int i = 0; i < 9; i++)
			{
				c.DrawRect(0, 250 + i * 17, 400, 7, p);
			}
		}
		SKImage art = surface.Snapshot();
		lock (Gate)
		{
			_current = new MediaInfo("Midnight Drive", "Neon Avenue", "City Lights", "Spotify", playing, 83.0, 214.0, DateTime.UtcNow);
			SetArt(art);
		}
	}

	private static void SetArt(SKImage? art)
	{
		// the old cover is left to the garbage collector: a frame being drawn may still hold it
		_art = art;
		_artVersion++;
		if (art != null)
		{
			_artColors = Colors(art);
		}
	}

	/// <summary>The cover's two most vivid colours (by hue, a quarter of the wheel apart if it can), for glows and rings.</summary>
	private static SKColor[] Colors(SKImage art)
	{
		using var small = new SKBitmap(24, 24, SKColorType.Bgra8888, SKAlphaType.Premul);
		using (var px = small.PeekPixels())
		{
			art.ScalePixels(px, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
		}
		float[] bins = new float[24];
		float[] r = new float[24], g = new float[24], b = new float[24];
		for (int y = 0; y < 24; y++)
		{
			for (int x = 0; x < 24; x++)
			{
				SKColor c = small.GetPixel(x, y);
				// SkiaSharp gives saturation and value as 0–100
				c.ToHsv(out float h, out float s, out float v);
				s /= 100f;
				v /= 100f;
				float weight = s * v * s;
				if (weight < 0.04f)
				{
					continue;
				}
				int k = (int)(h / 15f) % 24;
				bins[k] += weight;
				r[k] += c.Red * weight;
				g[k] += c.Green * weight;
				b[k] += c.Blue * weight;
			}
		}
		int best = -1, second = -1;
		for (int k = 0; k < 24; k++)
		{
			if (best < 0 || bins[k] > bins[best])
			{
				best = k;
			}
		}
		for (int k = 0; k < 24; k++)
		{
			int gap = Math.Min(Math.Abs(k - best), 24 - Math.Abs(k - best));
			if (gap >= 4 && (second < 0 || bins[k] > bins[second]))
			{
				second = k;
			}
		}
		SKColor Vivid(int k, SKColor fallback)
		{
			if (k < 0 || bins[k] <= 0.001f)
			{
				return fallback;
			}
			var avg = new SKColor((byte)(r[k] / bins[k]), (byte)(g[k] / bins[k]), (byte)(b[k] / bins[k]));
			avg.ToHsv(out float h, out float s, out float v);
			// rich and bright, never washed out or burnt to white (0–100 scale)
			return SKColor.FromHsv(h, Math.Clamp(s * 1.15f, 62f, 92f), Math.Clamp(v * 1.2f, 78f, 98f));
		}
		SKColor a = Vivid(best, new SKColor(255, 75, 43));
		a.ToHsv(out float ha, out _, out _);
		SKColor bb = Vivid(second, SKColor.FromHsv((ha + 60f) % 360f, 80f, 95f));
		return new[] { a, bb };
	}

	// ---------- the reader thread ----------

	private static void Loop()
	{
		RoInitialize(1);
		IntPtr manager = IntPtr.Zero;
		string lastTrack = "";
		int artTries = 0;
		while (true)
		{
			try
			{
				if (manager == IntPtr.Zero)
				{
					manager = RequestManager();
				}
				MediaInfo? info = null;
				IntPtr props = IntPtr.Zero;
				if (manager != IntPtr.Zero)
				{
					info = Read(manager, out props);
				}
				try
				{
					string track = info == null ? "" : info.Title + "\u0001" + info.Artist + "\u0001" + info.App;
					if (track != lastTrack)
					{
						lastTrack = track;
						artTries = 0;
						lock (Gate)
						{
							SetArt(null);
						}
					}
					// some players hand over the cover a moment after the title, so it is asked for again a few times
					if (info != null && props != IntPtr.Zero && Art.Art == null && artTries < 6)
					{
						artTries++;
						SKImage? art = Thumbnail(props);
						if (art != null)
						{
							lock (Gate)
							{
								SetArt(art);
							}
						}
					}
				}
				finally
				{
					if (props != IntPtr.Zero)
					{
						Marshal.Release(props);
					}
				}
				lock (Gate)
				{
					_current = info;
				}
			}
			catch (Exception ex)
			{
				Log.Write("now playing: " + ex.Message);
				if (manager != IntPtr.Zero)
				{
					Marshal.Release(manager);
					manager = IntPtr.Zero;
				}
				Thread.Sleep(10000);
			}
			Thread.Sleep(1000);
		}
	}

	private static readonly Guid IidManagerStatics = new Guid("2050c4ee-11a0-57de-aed7-c97c70338245");

	private static readonly Guid IidAsyncInfo = new Guid("00000036-0000-0000-c000-000000000046");

	private static readonly Guid IidStream = new Guid("0000000c-0000-0000-c000-000000000046");

	private static IntPtr RequestManager()
	{
		const string cls = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
		Check(WindowsCreateString(cls, cls.Length, out IntPtr name));
		IntPtr statics;
		try
		{
			Guid iid = IidManagerStatics;
			Check(RoGetActivationFactory(name, ref iid, out statics));
		}
		finally
		{
			WindowsDeleteString(name);
		}
		try
		{
			Check(CallPtr(statics, 6, out IntPtr op));
			return Await(op);
		}
		finally
		{
			Marshal.Release(statics);
		}
	}

	/// <summary>The current session's details; props is its media properties (released by the caller), for the art.</summary>
	private static MediaInfo? Read(IntPtr manager, out IntPtr props)
	{
		props = IntPtr.Zero;
		Check(CallPtr(manager, 6, out IntPtr session));
		if (session == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			string app = Friendly(CallString(session, 6));
			int status = 0;
			Check(CallPtr(session, 9, out IntPtr playback));
			if (playback != IntPtr.Zero)
			{
				Check(CallInt(playback, 7, out status));
				Marshal.Release(playback);
			}
			// 0 closed, 1 opened, 2 changing, 3 stopped, 4 playing, 5 paused
			if (status <= 1 || status == 3)
			{
				return null;
			}
			double position = 0.0, length = 0.0;
			DateTime at = DateTime.UtcNow;
			Check(CallPtr(session, 8, out IntPtr timeline));
			if (timeline != IntPtr.Zero)
			{
				Check(CallLong(timeline, 6, out long start));
				Check(CallLong(timeline, 7, out long end));
				Check(CallLong(timeline, 10, out long pos));
				Check(CallLong(timeline, 11, out long updated));
				Marshal.Release(timeline);
				length = Math.Max(0.0, (end - start) / 1e7);
				position = Math.Max(0.0, (pos - start) / 1e7);
				if (updated > 0)
				{
					DateTime u = DateTime.FromFileTimeUtc(updated);
					if (u.Year >= 2020 && u <= DateTime.UtcNow.AddSeconds(5))
					{
						at = u;
					}
				}
			}
			Check(CallPtr(session, 7, out IntPtr op));
			props = Await(op);
			if (props == IntPtr.Zero)
			{
				return null;
			}
			string title = CallString(props, 6), artist = CallString(props, 9), album = CallString(props, 10);
			if (title.Length == 0 && artist.Length == 0)
			{
				return null;
			}
			return new MediaInfo(title, artist, album, app, status == 4, position, length, at);
		}
		finally
		{
			Marshal.Release(session);
		}
	}

	/// <summary>"SpotifyAB.SpotifyMusic_…!Spotify" → Spotify, "chrome.exe" → Chrome.</summary>
	private static string Friendly(string id)
	{
		string s = id;
		int bang = s.LastIndexOf('!');
		if (bang >= 0)
		{
			s = s[(bang + 1)..];
		}
		s = Path.GetFileNameWithoutExtension(s.Replace('\\', '/').Split('/')[^1]);
		string low = s.ToLowerInvariant();
		if (low.Contains("spotify"))
		{
			return "Spotify";
		}
		if (low.Contains("chrome"))
		{
			return "Chrome";
		}
		if (low.Contains("msedge") || low == "edge")
		{
			return "Edge";
		}
		if (low.Contains("firefox"))
		{
			return "Firefox";
		}
		if (low.Contains("zunemusic") || low.Contains("media"))
		{
			return "Media Player";
		}
		if (low == "app" || s.Length == 0)
		{
			return "Music";
		}
		return char.ToUpperInvariant(s[0]) + s[1..];
	}

	private static SKImage? Thumbnail(IntPtr props)
	{
		Check(CallPtr(props, 15, out IntPtr reference));
		Trace("thumbnail reference " + reference);
		if (reference == IntPtr.Zero)
		{
			return null;
		}
		IntPtr ras = IntPtr.Zero, stream = IntPtr.Zero;
		try
		{
			Check(CallPtr(reference, 6, out IntPtr op));
			ras = Await(op);
			Trace("thumbnail stream " + ras);
			if (ras == IntPtr.Zero)
			{
				return null;
			}
			Guid iid = IidStream;
			Check(CreateStreamOverRandomAccessStream(ras, ref iid, out stream));
			Trace("istream " + stream);
			using var ms = new MemoryStream();
			byte[] buf = new byte[65536];
			unsafe
			{
				fixed (byte* b = buf)
				{
					var read = (delegate* unmanaged[Stdcall]<IntPtr, byte*, uint, uint*, int>)(*(void***)stream)[3];
					while (ms.Length < 16 * 1024 * 1024)
					{
						uint got = 0;
						int hr = read(stream, b, (uint)buf.Length, &got);
						if (hr < 0 || got == 0)
						{
							break;
						}
						ms.Write(buf, 0, (int)got);
					}
				}
			}
			Trace("thumbnail bytes " + ms.Length);
			if (ms.Length == 0)
			{
				return null;
			}
			using SKBitmap? bmp = SKBitmap.Decode(ms.ToArray());
			if (bmp == null)
			{
				return null;
			}
			// at most 512 px on a side; square-ish covers stay as they are
			float k = Math.Min(1f, 512f / Math.Max(bmp.Width, bmp.Height));
			var info = new SKImageInfo(Math.Max(1, (int)(bmp.Width * k)), Math.Max(1, (int)(bmp.Height * k)), SKColorType.Bgra8888, SKAlphaType.Premul);
			using var scaled = new SKBitmap(info);
			bmp.ScalePixels(scaled, new SKSamplingOptions(SKCubicResampler.Mitchell));
			return SKImage.FromBitmap(scaled);
		}
		finally
		{
			if (stream != IntPtr.Zero)
			{
				Marshal.Release(stream);
			}
			if (ras != IntPtr.Zero)
			{
				Marshal.Release(ras);
			}
			Marshal.Release(reference);
		}
	}

	/// <summary>Waits (up to 3 s) for an IAsyncOperation and returns its result, releasing the operation.</summary>
	private static IntPtr Await(IntPtr op)
	{
		if (op == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		try
		{
			Guid iid = IidAsyncInfo;
			Check(Marshal.QueryInterface(op, ref iid, out IntPtr async));
			try
			{
				int status = 0;
				for (int i = 0; i < 300; i++)
				{
					Check(CallInt(async, 7, out status));
					if (status != 0)
					{
						break;
					}
					Thread.Sleep(10);
				}
				if (status != 1)
				{
					Trace("async status " + status);
					if (status == 0)
					{
						CallVoid(async, 9);
					}
					return IntPtr.Zero;
				}
			}
			finally
			{
				Marshal.Release(async);
			}
			Check(CallPtr(op, 8, out IntPtr result));
			return result;
		}
		finally
		{
			Marshal.Release(op);
		}
	}

	private static void Trace(string s)
	{
		if (Environment.GetEnvironmentVariable("LUMALI_MEDIA_TRACE") == "1")
		{
			Log.Write("now playing: " + s);
		}
	}

	// ---------- raw calls ----------

	private static void Check(int hr)
	{
		if (hr < 0)
		{
			Marshal.ThrowExceptionForHR(hr);
		}
	}

	private static unsafe int CallPtr(IntPtr p, int slot, out IntPtr result)
	{
		IntPtr r = IntPtr.Zero;
		int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)(*(void***)p)[slot])(p, &r);
		result = r;
		return hr;
	}

	private static unsafe int CallInt(IntPtr p, int slot, out int result)
	{
		int r = 0;
		int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)(*(void***)p)[slot])(p, &r);
		result = r;
		return hr;
	}

	private static unsafe int CallLong(IntPtr p, int slot, out long result)
	{
		long r = 0;
		int hr = ((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)(*(void***)p)[slot])(p, &r);
		result = r;
		return hr;
	}

	private static unsafe void CallVoid(IntPtr p, int slot)
	{
		((delegate* unmanaged[Stdcall]<IntPtr, int>)(*(void***)p)[slot])(p);
	}

	private static string CallString(IntPtr p, int slot)
	{
		if (CallPtr(p, slot, out IntPtr h) < 0 || h == IntPtr.Zero)
		{
			return "";
		}
		try
		{
			IntPtr raw = WindowsGetStringRawBuffer(h, out uint len);
			return raw == IntPtr.Zero ? "" : (Marshal.PtrToStringUni(raw, (int)len) ?? "").Trim();
		}
		finally
		{
			WindowsDeleteString(h);
		}
	}

	[DllImport("combase.dll")]
	private static extern int RoInitialize(int type);

	[DllImport("combase.dll", CharSet = CharSet.Unicode)]
	private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

	[DllImport("combase.dll")]
	private static extern int WindowsDeleteString(IntPtr hstring);

	[DllImport("combase.dll")]
	private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

	[DllImport("combase.dll")]
	private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

	[DllImport("shcore.dll")]
	private static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, ref Guid riid, out IntPtr ppv);
}
