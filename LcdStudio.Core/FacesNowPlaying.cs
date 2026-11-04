using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Two fan faces that don't follow a reading:
/// - Now Playing: the song's cover in a circle over a blurred wash of it, a progress ring round the edge in the
///   cover's colours with a glowing head, the title (scrolling when long), artist and time. A note on the fan's two
///   colours when nothing plays.
/// - Weather: the sky for the weather and time of day, an animated icon (sun, moon and stars, clouds, rain, snow, a
///   storm with lightning that lights the sky, fog), the temperature in white, the sky in words, the place and the
///   day's range.
/// </summary>
public sealed partial class Faces
{
	private const string NfLight = "Montserrat Light", NfCap = "Bahnschrift SemiBold";

	private int _nfArtVersion = -2;

	private SKImage? _nfAmbient, _nfCover;

	private readonly Dictionary<string, (string Title, double Since, float Energy)> _nfState = new();

	private void NowPlayingFace(FaceContext x)
	{
		var (s, _, dt) = GlowStep(x, "Now Playing");
		MediaInfo? m = NowPlaying.Current;
		var (art, version, colors) = NowPlaying.Art;
		SKColor a = art != null ? colors[0] : SkiaKit.Sk(x.C1), b = art != null ? colors[1] : SkiaKit.Sk(x.C2);
		int key = art == null ? -1 : version;
		if (key != _nfArtVersion)
		{
			_nfArtVersion = key;
			_nfAmbient = art == null ? null : NfAmbient(art);
			_nfCover = art == null ? null : NfCover(art);
		}
		string fan = x.Fan.Serial ?? "";
		if (!_nfState.TryGetValue(fan, out var st))
		{
			st = ("", x.T, 0f);
		}
		string title = m?.Title ?? "";
		if (title != st.Title)
		{
			st = (title, x.T, st.Energy);
		}
		bool playing = m != null && m.Playing;
		st.Energy += (float)(((playing ? 1.0 : 0.0) - st.Energy) * (1.0 - Math.Exp(-dt * 2.4)));
		_nfState[fan] = st;
		s.Orbit += dt * (0.2 + 0.8 * st.Energy);

		FaceSkia(x.G, c =>
		{
			c.Clear(new SKColor(10, 10, 16));
			if (_nfAmbient != null)
			{
				c.DrawImage(_nfAmbient, 0f, 0f);
			}
			else
			{
				SkiaKit.Glow(c, 120f + 30f * MathF.Sin((float)(s.Orbit * 0.4)), 140f, 240f, 220f, a.WithAlpha(90), SKBlendMode.Plus);
				SkiaKit.Glow(c, 290f, 260f + 20f * MathF.Sin((float)(s.Orbit * 0.3)), 240f, 220f, b.WithAlpha(90), SKBlendMode.Plus);
			}
			using var p = new SKPaint { IsAntialias = true };
			// darker toward the type at the foot
			using (var shade = SKShader.CreateLinearGradient(new SKPoint(0f, 180f), new SKPoint(0f, 400f), new[] { new SKColor(0, 0, 0, 30), new SKColor(0, 0, 0, 150) }, null, SKShaderTileMode.Clamp))
			{
				p.Color = SKColors.White;
				p.Shader = shade;
				c.DrawRect(0f, 0f, 400f, 400f, p);
				p.Shader = null;
			}

			// the progress ring round the edge
			var ring = new SKRect(16f, 16f, 384f, 384f);
			using (var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, Color = new SKColor(255, 255, 255, 34) })
			{
				c.DrawOval(ring, track);
			}
			if (m != null && m.Length > 0.0)
			{
				float f = m.Progress;
				using var sweep = SKShader.CreateSweepGradient(new SKPoint(200f, 200f), new[] { a, b, a }, new[] { 0f, 0.5f, 1f }, SKMatrix.CreateRotationDegrees(-90f, 200f, 200f));
				using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, StrokeCap = SKStrokeCap.Round, Color = SKColors.White, Shader = sweep };
				c.DrawArc(ring, -90f, Math.Max(1f, 360f * f), false, arc);
				float ang = (-90f + 360f * f) * MathF.PI / 180f;
				float hx = 200f + 184f * MathF.Cos(ang), hy = 200f + 184f * MathF.Sin(ang);
				SkiaKit.Glow(c, hx, hy, 26f, 26f, NfMix(a, b, f).WithAlpha(170), SKBlendMode.Plus);
				p.Color = SKColors.White;
				c.DrawCircle(hx, hy, 5.5f, p);
			}

			// the cover, floating a little, on a glow that breathes while it plays
			float lift = 3f * MathF.Sin((float)(x.T * 0.55));
			float breathe = 0.7f + 0.3f * st.Energy * MathF.Sin((float)(x.T * 1.2));
			SkiaKit.Glow(c, 186f, 150f + lift, 150f, 140f, a.WithAlpha((byte)(90 * breathe)), SKBlendMode.Plus);
			SkiaKit.Glow(c, 216f, 140f + lift, 140f, 130f, b.WithAlpha((byte)(80 * breathe)), SKBlendMode.Plus);
			if (_nfCover != null)
			{
				c.DrawImage(_nfCover, 200f - _nfCover.Width / 2f, 150f + lift - _nfCover.Height / 2f, SkiaKit.Linear, null);
			}
			else
			{
				using var tile = SKShader.CreateLinearGradient(new SKPoint(100f, 50f), new SKPoint(300f, 250f), new[] { NfMix(a, new SKColor(20, 16, 30), 0.3f), NfMix(b, new SKColor(14, 12, 26), 0.4f) }, null, SKShaderTileMode.Clamp);
				p.Shader = tile;
				p.Color = SKColors.White;
				c.DrawCircle(200f, 150f + lift, 100f, p);
				p.Shader = null;
				WeatherArt.Note(c, 200f, 150f + lift, 86f, new SKColor(255, 255, 255, 220));
			}
			using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(255, 255, 255, 50) })
			{
				c.DrawCircle(200f, 150f + lift, 100f, edge);
			}
			float pause = m == null ? 0f : 1f - Math.Clamp(st.Energy * 1.6f, 0f, 1f);
			if (pause > 0.02f)
			{
				p.Color = new SKColor(0, 0, 0, (byte)(120 * pause));
				c.DrawCircle(200f, 150f + lift, 34f, p);
				p.Color = new SKColor(255, 255, 255, (byte)(230 * pause));
				c.DrawRoundRect(188f, 136f + lift, 8f, 28f, 3f, 3f, p);
				c.DrawRoundRect(204f, 136f + lift, 8f, 28f, 3f, 3f, p);
			}

			var white = new SKColor(255, 255, 255);
			if (m == null)
			{
				AfText(c, "Nothing playing", 200f, 274f, 28f, new SKColor(255, 255, 255, 215), align: 0, font: NfLight);
				AfText(c, WatchNow(x).ToString("HH:mm", CultureInfo.InvariantCulture), 200f, 318f, 22f, new SKColor(255, 255, 255, 150), align: 0, track: 1f, font: NfCap);
				return;
			}
			NfTitle(c, m.Title.Length > 0 ? m.Title : "Untitled", x.T - st.Since);
			if (m.Artist.Length > 0)
			{
				AfText(c, NfFit(m.Artist, NfLight, 21f, 270f), 200f, 312f, 21f, new SKColor(255, 255, 255, 200), align: 0, font: NfLight);
			}
			string time = m.Length > 0.0 ? MediaInfo.Clock(m.PositionNow) + "  /  " + MediaInfo.Clock(m.Length) : m.App.ToUpperInvariant();
			AfText(c, time, 200f, 344f, 15f, new SKColor(255, 255, 255, 140), align: 0, track: 1f, font: NfCap);
		});
	}

	private static SKColor NfMix(SKColor a, SKColor b, float k) => new SKColor(
		(byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k));

	/// <summary>The title in white, centred; a long one waits three seconds, then scrolls round with faded ends.</summary>
	private void NfTitle(SKCanvas c, string title, double since)
	{
		const float size = 30f, top = 266f, left = 60f, right = 340f, gap = 60f;
		SKRect bounds = AfPath(title, NfLight, size).TightBounds;
		var white = new SKColor(255, 255, 255);
		if (bounds.Width <= right - left)
		{
			AfText(c, title, 200f, top, size, white, align: 0, font: NfLight);
			return;
		}
		float lap = bounds.Width + gap;
		double moving = Math.Max(0.0, since - 3.0);
		double period = lap / 40.0 + 3.0;
		double inLap = moving % period;
		float off = inLap < lap / 40.0 ? (float)(inLap * 40.0) : 0f;
		var clip = new SKRect(left, top - 12f, right, top + size + 16f);
		c.SaveLayer(clip, null);
		AfText(c, title, left - off, top, size, white, font: NfLight);
		AfText(c, title, left - off + lap, top, size, white, font: NfLight);
		using (var p = new SKPaint { BlendMode = SKBlendMode.DstOut })
		{
			using var fadeR = SKShader.CreateLinearGradient(new SKPoint(right - 40f, 0f), new SKPoint(right, 0f), new[] { SKColors.Transparent, SKColors.Black }, null, SKShaderTileMode.Clamp);
			p.Shader = fadeR;
			c.DrawRect(right - 40f, clip.Top, 40f, clip.Height, p);
			using var fadeL = SKShader.CreateLinearGradient(new SKPoint(left, 0f), new SKPoint(left + 40f, 0f), new[] { SKColors.Black, SKColors.Transparent }, null, SKShaderTileMode.Clamp);
			p.Shader = fadeL;
			c.DrawRect(left, clip.Top, 40f, clip.Height, p);
		}
		c.Restore();
	}

	private string NfFit(string text, string font, float size, float width)
	{
		if (AfPath(text, font, size).TightBounds.Width <= width)
		{
			return text;
		}
		for (int n = text.Length - 1; n > 1; n--)
		{
			string cut = text[..n].TrimEnd() + "…";
			if (AfPath(cut, font, size).TightBounds.Width <= width)
			{
				return cut;
			}
		}
		return text;
	}

	private static SKImage NfAmbient(SKImage art)
	{
		var smallInfo = new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var small = SKSurface.Create(smallInfo);
		float side = Math.Min(art.Width, art.Height);
		small.Canvas.DrawImage(art, new SKRect((art.Width - side) / 2f, (art.Height - side) / 2f, (art.Width + side) / 2f, (art.Height + side) / 2f), new SKRect(0f, 0f, 100f, 100f), SkiaKit.Linear);
		using SKImage raw = small.Snapshot();
		using var soft = SKSurface.Create(smallInfo);
		using (var p = new SKPaint())
		{
			p.ImageFilter = SKImageFilter.CreateBlur(7f, 7f, SKShaderTileMode.Clamp);
			float sat = 1.35f, dim = 0.55f;
			float lr = 0.2126f * (1f - sat), lg = 0.7152f * (1f - sat), lb = 0.0722f * (1f - sat);
			p.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
			{
				(lr + sat) * dim, lg * dim, lb * dim, 0, 0,
				lr * dim, (lg + sat) * dim, lb * dim, 0, 0,
				lr * dim, lg * dim, (lb + sat) * dim, 0, 0,
				0, 0, 0, 1, 0
			});
			soft.Canvas.DrawImage(raw, 0f, 0f, p);
		}
		using SKImage blurred = soft.Snapshot();
		using var big = SKSurface.Create(new SKImageInfo(400, 400, SKColorType.Bgra8888, SKAlphaType.Premul));
		big.Canvas.DrawImage(blurred, new SKRect(0f, 0f, 400f, 400f), SkiaKit.Linear);
		return big.Snapshot();
	}

	private static SKImage NfCover(SKImage art)
	{
		using var surface = SKSurface.Create(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		c.Clear(SKColors.Transparent);
		using var circle = new SKPath();
		circle.AddCircle(100f, 100f, 100f);
		c.ClipPath(circle, SKClipOperation.Intersect, true);
		float side = Math.Min(art.Width, art.Height);
		c.DrawImage(art, new SKRect((art.Width - side) / 2f, (art.Height - side) / 2f, (art.Width + side) / 2f, (art.Height + side) / 2f), new SKRect(0f, 0f, 200f, 200f), new SKSamplingOptions(SKCubicResampler.Mitchell));
		return surface.Snapshot();
	}

	// ---------- Weather ----------

	private void WeatherFace(FaceContext x)
	{
		var (_, _, _) = GlowStep(x, "Weather");
		WeatherNow? w = Weather.Current;
		SKColor sky = SkiaKit.Sk(x.C1), sun = SkiaKit.Sk(x.C2);
		string kind = w?.Kind ?? "Cloudy";
		bool day = w?.IsDay ?? true;
		FaceSkia(x.G, c =>
		{
			// the sky: the fan's colour by day, deep blue at night, slate under rain and storms
			SKColor top, foot;
			if (kind is "Rain" or "Storm" or "Fog")
			{
				top = day ? new SKColor(52, 66, 92) : new SKColor(18, 22, 36);
				foot = day ? new SKColor(104, 120, 146) : new SKColor(40, 46, 66);
			}
			else if (day)
			{
				top = NfMix(sky, SKColors.Black, 0.35f);
				foot = NfMix(NfMix(sky, SKColors.White, 0.25f), sun, 0.18f);
			}
			else
			{
				top = new SKColor(6, 10, 30);
				foot = new SKColor(36, 32, 86);
			}
			if (!x.Over)
			{
				using var shader = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 400f), new[] { top, foot }, null, SKShaderTileMode.Clamp);
				using var p = new SKPaint { Shader = shader };
				c.DrawRect(0f, 0f, 400f, 400f, p);
			}
			else
			{
				c.Clear(SKColors.Transparent);
			}
			if (!day && kind is "Clear" or "Partly")
			{
				WeatherArt.Stars(c, 200f, 100f, 290f, x.T, tall: 0.62f, count: 16);
			}
			float flash = WeatherArt.Draw(c, kind, day, 200f, 124f, 172f, x.T, sun);
			if (flash > 0f)
			{
				using var lit = new SKPaint { Color = new SKColor(200, 210, 255, (byte)(60 * flash)), BlendMode = SKBlendMode.Plus };
				c.DrawRect(0f, 0f, 400f, 400f, lit);
			}
			// a soft dark band behind the type so it reads over any sky
			SkiaKit.Glow(c, 200f, 290f, 210f, 90f, new SKColor(0, 0, 0, 70), SKBlendMode.SrcOver);
			var white = new SKColor(255, 255, 255);
			if (w == null)
			{
				AfText(c, "No weather yet", 200f, 254f, 28f, white, align: 0, font: NfLight);
				AfText(c, "Set your city in", 200f, 300f, 18f, new SKColor(255, 255, 255, 190), align: 0, font: NfLight);
				AfText(c, "SETTINGS  ›  WEATHER", 200f, 328f, 15f, new SKColor(255, 255, 255, 170), align: 0, track: 1.5f, font: NfCap);
				return;
			}
			AfText(c, Weather.Degrees(w.Temp), 214f, 214f, 84f, white, align: 0, font: NfLight);
			AfText(c, NfFit(w.Text, NfLight, 24f, 300f), 200f, 312f, 24f, new SKColor(255, 255, 255, 225), align: 0, font: NfLight);
			string place = w.Place.Split(',')[0];
			string line = (place.Length > 0 ? place.ToUpperInvariant() + "   " : "") + "H " + Weather.Degrees(w.High) + "  L " + Weather.Degrees(w.Low);
			AfText(c, NfFit(line, NfCap, 15f, 260f), 200f, 346f, 15f, new SKColor(255, 255, 255, 165), align: 0, track: 1f, font: NfCap);
		});
	}
}
