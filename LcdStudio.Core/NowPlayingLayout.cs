using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Now Playing: what Windows says is playing, across the whole screen.
/// - Behind: the cover, hugely blurred and slowly drifting (crossfading when the song changes), soft bokeh lights in
///   the cover's two most vivid colours, darkened where the type sits.
/// - Left: the cover itself on a coloured glow, floating gently; a note tile when there is no cover.
/// - Middle: NOW PLAYING and the app, the title (scrolling when long), artist, album; a 48-bar equaliser that dances
///   while it plays and settles when paused; the progress bar with a glowing head, time gone and time left.
/// - Right: the clock and date, the weather (animated icon, temperature, sky, the day's range) when a city is set,
///   and five rings: CPU / GPU load, CPU / GPU temperature, memory, with white figures.
/// The colours follow the cover and ease over when it changes.
/// </summary>
public sealed partial class ScreenRenderer
{
	private const string NpThin = "Montserrat ExtraLight", NpLight = "Montserrat Light", NpCap = "Bahnschrift SemiBold";

	private static readonly SKRect NpCoverBox = new SKRect(70f, 70f, 410f, 410f);

	private double _npAt = double.NaN, _npDt, _npDrift, _npBeat, _npScroll, _npFloat;

	private float _npEnergy;

	private string _npTitle = "";

	private int _npArtVersion = -1;

	private SKImage? _npAmbient, _npAmbientPrev, _npCover, _npCoverPrev, _npShadow;

	private double _npChangedAt = -10.0;

	private SKColor _npA = new SKColor(255, 75, 43), _npB = new SKColor(122, 60, 255);

	private bool DrawNowPlaying(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "nowplaying" || w.Kind != WidgetKind.Panel)
		{
			return false;
		}
		NowPlayingScene(g, t);
		return true;
	}

	private void NowPlayingScene(Graphics g, double t)
	{
		double dt = double.IsNaN(_npAt) ? 0.0 : Math.Clamp(t - _npAt, 0.0, 0.25);
		_npAt = t;
		_npDt = dt;
		DateTime now = Faces.PreviewClock.HasValue ? Faces.PreviewClock.Value.AddSeconds(t) : DateTime.Now;
		MediaInfo? m = NowPlaying.Current;
		var (art, version, colors) = NowPlaying.Art;
		bool playing = m != null && m.Playing;

		// a new cover: keep the last one to fade from, and make the blurred backdrop and the sharp tile once
		int key = art == null ? -1 - (m == null ? 0 : 1) : version;
		if (key != _npArtVersion)
		{
			_npArtVersion = key;
			_npAmbientPrev = _npAmbient;
			_npCoverPrev = _npCover;
			_npAmbient = NpMakeAmbient(art, colors);
			_npCover = art == null ? null : NpMakeCover(art);
			_npChangedAt = t;
		}
		float fade = (float)Math.Clamp((t - _npChangedAt) / 1.4, 0.0, 1.0);
		fade = fade * fade * (3f - 2f * fade);
		if (fade >= 1f)
		{
			_npAmbientPrev = null;
			_npCoverPrev = null;
		}

		// colours ease toward the cover's (or LumaLi's own when there is none)
		SKColor wantA = art != null ? colors[0] : new SKColor(255, 75, 43), wantB = art != null ? colors[1] : new SKColor(122, 60, 255);
		float k = (float)(1.0 - Math.Exp(-dt * 1.6));
		_npA = NpMix(_npA, wantA, k);
		_npB = NpMix(_npB, wantB, k);

		// motion: the backdrop drifts, the equaliser's phase runs while playing and its energy eases in and out
		_npDrift += dt * 0.05;
		_npFloat += dt * 0.55;
		_npEnergy += (float)((((playing ? 1.0 : 0.07) - _npEnergy) * (1.0 - Math.Exp(-dt * 2.4))));
		_npBeat += dt * (0.6 + 2.4 * _npEnergy);
		string title = m == null ? "" : m.Title;
		if (title != _npTitle)
		{
			_npTitle = title;
			_npScroll = 0.0;
		}
		_npScroll += dt;

		SkiaDraw(g, new Rectangle(0, 0, _w, _h), c =>
		{
			c.Clear(new SKColor(10, 10, 14));
			NpBackdrop(c, fade, t);
			NpCoverTile(c, m, fade, t);
			NpMiddle(c, m, t);
			NpRight(c, now, t);
		});
	}

	private static SKColor NpMix(SKColor a, SKColor b, float k) => new SKColor(
		(byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k));

	/// <summary>The backdrop: the cover (or a two-colour field) blurred into a 2080 × 560 wash, made once per cover.</summary>
	private static SKImage NpMakeAmbient(SKImage? art, SKColor[] colors)
	{
		var smallInfo = new SKImageInfo(520, 140, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var small = SKSurface.Create(smallInfo);
		SKCanvas s = small.Canvas;
		s.Clear(new SKColor(12, 12, 18));
		if (art != null)
		{
			// cover-fit: the middle band of the cover across the width
			float scale = 520f / art.Width;
			float h = art.Height * scale;
			s.DrawImage(art, new SKRect(0f, (140f - h) / 2f, 520f, (140f + h) / 2f), SkiaKit.Linear);
		}
		else
		{
			SKColor a = colors.Length > 0 ? colors[0] : new SKColor(255, 75, 43), b = colors.Length > 1 ? colors[1] : new SKColor(122, 60, 255);
			SkiaKit.Glow(s, 130f, 90f, 200f, 110f, a.WithAlpha(150), SKBlendMode.Plus);
			SkiaKit.Glow(s, 400f, 50f, 220f, 120f, b.WithAlpha(150), SKBlendMode.Plus);
		}
		using SKImage raw = small.Snapshot();
		using var blurred = SKSurface.Create(smallInfo);
		using (var p = new SKPaint())
		{
			p.ImageFilter = SKImageFilter.CreateBlur(16f, 16f, SKShaderTileMode.Clamp);
			// darker and a little more saturated, so the type stands out and the colour stays rich
			float sat = 1.35f, dim = 0.5f;
			float lr = 0.2126f * (1f - sat), lg = 0.7152f * (1f - sat), lb = 0.0722f * (1f - sat);
			p.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
			{
				(lr + sat) * dim, lg * dim, lb * dim, 0, 0,
				lr * dim, (lg + sat) * dim, lb * dim, 0, 0,
				lr * dim, lg * dim, (lb + sat) * dim, 0, 0,
				0, 0, 0, 1, 0
			});
			blurred.Canvas.DrawImage(raw, 0f, 0f, p);
		}
		using SKImage soft = blurred.Snapshot();
		using var big = SKSurface.Create(new SKImageInfo(2080, 560, SKColorType.Bgra8888, SKAlphaType.Premul));
		big.Canvas.DrawImage(soft, new SKRect(0f, 0f, 2080f, 560f), SkiaKit.Linear);
		return big.Snapshot();
	}

	/// <summary>The cover at its tile size with rounded corners, made once per cover.</summary>
	private static SKImage NpMakeCover(SKImage art)
	{
		int size = (int)NpCoverBox.Width;
		using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
		SKCanvas c = surface.Canvas;
		c.Clear(SKColors.Transparent);
		using var clip = new SKRoundRect(new SKRect(0f, 0f, size, size), 22f);
		c.ClipRoundRect(clip, SKClipOperation.Intersect, true);
		float side = Math.Min(art.Width, art.Height);
		var src = new SKRect((art.Width - side) / 2f, (art.Height - side) / 2f, (art.Width + side) / 2f, (art.Height + side) / 2f);
		c.DrawImage(art, src, new SKRect(0f, 0f, size, size), new SKSamplingOptions(SKCubicResampler.Mitchell));
		return surface.Snapshot();
	}

	private void NpBackdrop(SKCanvas c, float fade, double t)
	{
		float ox = -80f + 80f * MathF.Sin((float)_npDrift), oy = -40f + 40f * MathF.Sin((float)(_npDrift * 0.7 + 1.3));
		using var p = new SKPaint();
		if (_npAmbientPrev != null && fade < 1f)
		{
			c.DrawImage(_npAmbientPrev, ox, oy);
			p.Color = SKColors.White.WithAlpha((byte)(255 * fade));
			if (_npAmbient != null)
			{
				c.DrawImage(_npAmbient, ox, oy, p);
			}
		}
		else if (_npAmbient != null)
		{
			c.DrawImage(_npAmbient, ox, oy);
		}

		// out-of-focus lights drifting up through the wash, in the two colours
		for (int i = 0; i < 12; i++)
		{
			float hx = NpHash(i, 3), hy = NpHash(i, 7), hs = NpHash(i, 11);
			float u = (float)((t * (0.012 + 0.012 * hs) + hy) % 1.0);
			float x = 80f + hx * 1760f + 30f * MathF.Sin((float)(t * 0.2) + i);
			float y = 520f - u * 580f;
			float a = MathF.Sin(u * MathF.PI);
			float rad = 26f + 40f * hs;
			SkiaKit.Glow(c, x, y, rad, rad, (i % 2 == 0 ? _npA : _npB).WithAlpha((byte)(46 * a)), SKBlendMode.Plus);
		}

		// darker behind the type, and at the top and bottom edges
		using (var shade = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(1920f, 0f),
			new[] { new SKColor(0, 0, 0, 70), new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 150), new SKColor(0, 0, 0, 170) },
			new[] { 0f, 0.3f, 0.65f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Color = SKColors.White;
			p.Shader = shade;
			c.DrawRect(0f, 0f, 1920f, 480f, p);
		}
		using (var edge = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, 480f),
			new[] { new SKColor(0, 0, 0, 90), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 110) },
			new[] { 0f, 0.2f, 0.75f, 1f }, SKShaderTileMode.Clamp))
		{
			p.Shader = edge;
			c.DrawRect(0f, 0f, 1920f, 480f, p);
		}
		p.Shader = null;
		// a hairline between the song and the clock
		p.Color = new SKColor(255, 255, 255, 26);
		c.DrawRect(1316f, 70f, 1f, 340f, p);
	}

	private static float NpHash(int i, int salt)
	{
		uint h = (uint)(i * 374761393 + salt * 668265263);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
	}

	private void NpCoverTile(SKCanvas c, MediaInfo? m, float fade, double t)
	{
		float lift = 4f * MathF.Sin((float)_npFloat);
		SKRect box = NpCoverBox;
		box.Offset(0f, lift);
		// coloured glow behind, breathing slowly while it plays
		float breathe = 0.75f + 0.25f * MathF.Sin((float)(t * 1.1)) * _npEnergy;
		SkiaKit.Glow(c, box.MidX - 40f, box.MidY + 10f, 300f, 250f, _npA.WithAlpha((byte)(80 * breathe)), SKBlendMode.Plus);
		SkiaKit.Glow(c, box.MidX + 60f, box.MidY - 20f, 260f, 230f, _npB.WithAlpha((byte)(70 * breathe)), SKBlendMode.Plus);
		// a soft shadow, made once
		_npShadow ??= NpMakeShadow();
		c.DrawImage(_npShadow, box.Left - 40f, box.Top - 26f + 0.5f * lift);

		void Tile(SKImage? img, byte alpha)
		{
			using var p = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha) };
			if (img != null)
			{
				c.DrawImage(img, box.Left, box.Top, SkiaKit.Linear, p);
				return;
			}
			// no cover: a tile in the two colours with a note
			using var shader = SKShader.CreateLinearGradient(new SKPoint(box.Left, box.Top), new SKPoint(box.Right, box.Bottom),
				new[] { NpMix(_npA, new SKColor(20, 16, 30), 0.35f).WithAlpha(alpha), NpMix(_npB, new SKColor(14, 12, 26), 0.45f).WithAlpha(alpha) }, null, SKShaderTileMode.Clamp);
			p.Shader = shader;
			c.DrawRoundRect(box, 22f, 22f, p);
			p.Shader = null;
			WeatherArt.Note(c, box.MidX, box.MidY, 130f, new SKColor(255, 255, 255, (byte)(alpha * 0.85f)));
		}
		if (_npCoverPrev != null && fade < 1f)
		{
			Tile(_npCoverPrev, 255);
			Tile(_npCover, (byte)(255 * fade));
		}
		else
		{
			Tile(_npCover, 255);
		}
		// a fine light edge and, while paused, two bars in a dark disc
		using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(255, 255, 255, 40) })
		{
			c.DrawRoundRect(box, 22f, 22f, edge);
		}
		float pause = 1f - Math.Clamp(_npEnergy * 1.6f, 0f, 1f);
		if (m != null && pause > 0.02f)
		{
			using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, (byte)(120 * pause)) };
			c.DrawCircle(box.MidX, box.MidY, 44f, p);
			p.Color = new SKColor(255, 255, 255, (byte)(230 * pause));
			c.DrawRoundRect(box.MidX - 15f, box.MidY - 18f, 9f, 36f, 3f, 3f, p);
			c.DrawRoundRect(box.MidX + 6f, box.MidY - 18f, 9f, 36f, 3f, 3f, p);
		}
	}

	private static SKImage NpMakeShadow()
	{
		using var surface = SKSurface.Create(new SKImageInfo(420, 420, SKColorType.Bgra8888, SKAlphaType.Premul));
		surface.Canvas.Clear(SKColors.Transparent);
		using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18f) };
		surface.Canvas.DrawRoundRect(new SKRect(44f, 46f, 376f, 386f), 24f, 24f, p);
		return surface.Snapshot();
	}

	private void NpMiddle(SKCanvas c, MediaInfo? m, double t)
	{
		const float left = 470f, right = 1270f;
		using var p = new SKPaint { IsAntialias = true };
		// the label: a dot that pulses while playing, NOW PLAYING or PAUSED, and the app
		bool playing = m != null && m.Playing;
		float pulse = playing ? 0.55f + 0.45f * MathF.Sin((float)(t * 3.0)) : 0.4f;
		p.Color = _npA.WithAlpha((byte)(255 * pulse));
		c.DrawCircle(left + 6f, 87f, 6f, p);
		SKRect lab = McText(c, m == null ? "NOTHING PLAYING" : playing ? "NOW PLAYING" : "PAUSED", left + 22f, 79f, 18f, new SKColor(255, 255, 255, 170), track: 3f, font: NpCap);
		if (m != null && m.App.Length > 0)
		{
			McText(c, "·  " + m.App.ToUpperInvariant(), lab.Right + 14f, 79f, 18f, NpMix(_npA, SKColors.White, 0.35f), track: 3f, font: NpCap);
		}

		if (m == null)
		{
			McText(c, "Nothing playing", left, 112f, 60f, new SKColor(255, 255, 255, 200), font: NpLight);
			McText(c, "Play music in Spotify, a browser or any player", left, 200f, 26f, new SKColor(255, 255, 255, 120), font: NpLight);
		}
		else
		{
			NpTitle(c, m.Title.Length > 0 ? m.Title : "Untitled", left, right, 108f);
			if (m.Artist.Length > 0)
			{
				McText(c, NpFit(m.Artist, NpLight, 36f, right - left), left, 194f, 36f, new SKColor(255, 255, 255, 215), font: NpLight);
			}
			if (m.Album.Length > 0 && m.Album != m.Title)
			{
				McText(c, NpFit(m.Album.ToUpperInvariant(), NpCap, 18f, right - left, 2f), left, 246f, 18f, new SKColor(255, 255, 255, 120), track: 2f, font: NpCap);
			}
		}

		// the equaliser: 48 bars that dance on layered waves while it plays, low and still when it doesn't
		const int bars = 48;
		float step = (right - left) / bars, bw = step * 0.55f, foot = 352f, tall = 74f;
		for (int i = 0; i < bars; i++)
		{
			float hi = NpHash(i, 41), hj = NpHash(i, 43);
			float bass = 1f - i / (float)bars * 0.45f;
			float wave = 0.5f + 0.5f * MathF.Sin((float)(_npBeat * (2.1 + 1.7 * hi)) + i * 0.55f)
				* (0.6f + 0.4f * MathF.Sin((float)(_npBeat * (0.9 + hj)) + i * 1.9f));
			float hgt = tall * Math.Clamp(0.08f + _npEnergy * bass * (0.22f + 0.78f * MathF.Pow(Math.Clamp(wave, 0f, 1f), 1.25f)), 0.06f, 1f);
			float x = left + i * step;
			SKColor col = NpMix(_npA, _npB, i / (float)(bars - 1));
			using var shader = SKShader.CreateLinearGradient(new SKPoint(0f, foot - hgt), new SKPoint(0f, foot),
				new[] { NpMix(col, SKColors.White, 0.25f), col.WithAlpha(200) }, null, SKShaderTileMode.Clamp);
			p.Color = SKColors.White;
			p.Shader = shader;
			c.DrawRoundRect(x, foot - hgt, bw, hgt, bw / 2f, bw / 2f, p);
			p.Shader = null;
			p.Color = col.WithAlpha(46);
			c.DrawRoundRect(x, foot + 4f, bw, hgt * 0.3f, bw / 2f, bw / 2f, p);
		}

		// the progress bar
		const float y = 400f;
		p.Color = new SKColor(255, 255, 255, 40);
		c.DrawRoundRect(left, y - 2f, right - left, 4f, 2f, 2f, p);
		if (m != null && m.Length > 0.0)
		{
			float head = left + (right - left) * m.Progress;
			using (var shader = SKShader.CreateLinearGradient(new SKPoint(left, 0f), new SKPoint(right, 0f), new[] { _npA, _npB }, null, SKShaderTileMode.Clamp))
			{
				p.Color = SKColors.White;
				p.Shader = shader;
				c.DrawRoundRect(left, y - 2.5f, Math.Max(5f, head - left), 5f, 2.5f, 2.5f, p);
				p.Shader = null;
			}
			SKColor at = NpMix(_npA, _npB, m.Progress);
			SkiaKit.Glow(c, head, y, 34f, 18f, at.WithAlpha(150), SKBlendMode.Plus);
			p.Color = SKColors.White;
			c.DrawCircle(head, y, 7f, p);
			McText(c, MediaInfo.Clock(m.PositionNow), left, 420f, 20f, new SKColor(255, 255, 255, 200), font: NpCap);
			McText(c, "-" + MediaInfo.Clock(m.Length - m.PositionNow), right, 420f, 20f, new SKColor(255, 255, 255, 140), align: 1, font: NpCap);
		}
		else if (m != null)
		{
			// a live stream: a light gliding along the bar
			float u = (float)(t * 0.25 % 1.0);
			SkiaKit.Glow(c, left + (right - left) * u, y, 60f, 14f, _npA.WithAlpha(150), SKBlendMode.Plus);
			McText(c, "LIVE", left, 420f, 20f, NpMix(_npA, SKColors.White, 0.3f), track: 2f, font: NpCap);
		}
	}

	/// <summary>The title in white; a long one holds for three seconds, then scrolls round with faded ends.</summary>
	private void NpTitle(SKCanvas c, string title, float left, float right, float top)
	{
		const float size = 64f, gap = 90f;
		SKRect b = McPath(title, NpLight, size, 0f).TightBounds;
		var white = new SKColor(255, 255, 255);
		if (b.Width <= right - left)
		{
			McText(c, title, left, top, size, white, font: NpLight);
			return;
		}
		float lap = b.Width + gap;
		double moving = Math.Max(0.0, _npScroll - 3.0);
		double period = lap / 55.0 + 3.0;
		double inLap = moving % period;
		float off = inLap < lap / 55.0 ? (float)(inLap * 55.0) : 0f;
		var clip = new SKRect(left - 4f, top - 20f, right, top + size + 24f);
		c.SaveLayer(clip, null);
		McText(c, title, left - off, top, size, white, font: NpLight);
		McText(c, title, left - off + lap, top, size, white, font: NpLight);
		using (var p = new SKPaint { BlendMode = SKBlendMode.DstOut })
		{
			using var fadeR = SKShader.CreateLinearGradient(new SKPoint(right - 60f, 0f), new SKPoint(right, 0f), new[] { SKColors.Transparent, SKColors.Black }, null, SKShaderTileMode.Clamp);
			p.Shader = fadeR;
			c.DrawRect(right - 60f, clip.Top, 60f, clip.Height, p);
			if (off > 0f)
			{
				using var fadeL = SKShader.CreateLinearGradient(new SKPoint(left - 4f, 0f), new SKPoint(left + 36f, 0f), new[] { SKColors.Black, SKColors.Transparent }, null, SKShaderTileMode.Clamp);
				p.Shader = fadeL;
				c.DrawRect(left - 4f, clip.Top, 40f, clip.Height, p);
			}
		}
		c.Restore();
	}

	/// <summary>The text, cut with an ellipsis to fit the width.</summary>
	private string NpFit(string text, string font, float size, float width, float track = 0f)
	{
		if (McPath(text, font, size, track).TightBounds.Width <= width)
		{
			return text;
		}
		for (int n = text.Length - 1; n > 1; n--)
		{
			string cut = text[..n].TrimEnd() + "…";
			if (McPath(cut, font, size, track).TightBounds.Width <= width)
			{
				return cut;
			}
		}
		return text;
	}

	private void NpRight(SKCanvas c, DateTime now, double t)
	{
		const float x0 = 1350f;
		var white = new SKColor(255, 255, 255);
		SKRect clock = McText(c, now.ToString("HH:mm", CultureInfo.InvariantCulture), x0, 64f, 112f, white, font: NpThin);
		McText(c, now.ToString("dddd d MMMM", CultureInfo.CurrentCulture), x0 + 4f, 196f, 24f, new SKColor(255, 255, 255, 180), font: NpLight);

		WeatherNow? w = Weather.Current;
		if (w != null)
		{
			float flash = WeatherArt.Draw(c, w.Kind, w.IsDay, x0 + 34f, 262f, 70f, t, new SKColor(255, 178, 58));
			if (flash > 0f)
			{
				SkiaKit.Glow(c, x0 + 34f, 262f, 120f, 80f, new SKColor(200, 210, 255, (byte)(40 * flash)), SKBlendMode.Plus);
			}
			SKRect deg = McText(c, Weather.Degrees(w.Temp), x0 + 84f, 240f, 44f, white, font: NpLight);
			string place = w.Place.Split(',')[0];
			McText(c, NpFit(w.Text + (place.Length > 0 ? "  ·  " + place : ""), NpLight, 21f, 1880f - deg.Right - 22f), deg.Right + 22f, 242f, 21f, new SKColor(255, 255, 255, 200), font: NpLight);
			McText(c, "H " + Weather.Degrees(w.High) + "   L " + Weather.Degrees(w.Low), deg.Right + 22f, 272f, 17f, new SKColor(255, 255, 255, 130), track: 1f, font: NpCap);
		}

		// five rings with white figures
		(string Key, string Label, float Lo, float Hi, string Unit)[] rings =
		{
			("cpu.load", "CPU", 0f, 100f, "%"),
			("gpu.load", "GPU", 0f, 100f, "%"),
			("cpu.temp", "CPU °C", 20f, 95f, "°"),
			("gpu.temp", "GPU °C", 20f, 95f, "°"),
			("ram.load", "RAM", 0f, 100f, "%")
		};
		double dt = _npDt;
		using var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f, Color = new SKColor(255, 255, 255, 34) };
		using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f, StrokeCap = SKStrokeCap.Round };
		for (int i = 0; i < rings.Length; i++)
		{
			var ring = rings[i];
			float cx = 1388f + i * 111f, cy = 370f, r = 36f;
			bool on = _sensors.Get(ring.Key).HasValue;
			float v = on ? Val(ring.Key) : 0f;
			float f = Math.Clamp(McEased("np." + ring.Key, (v - ring.Lo) / (ring.Hi - ring.Lo), dt, 2.6), 0f, 1f);
			c.DrawCircle(cx, cy, r, track);
			if (on && f > 0.004f)
			{
				SKColor ca = NpMix(_npA, _npB, i / 4f), cb = NpMix(ca, SKColors.White, 0.25f);
				using var sweep = SKShader.CreateSweepGradient(new SKPoint(cx, cy), new[] { ca, cb, ca }, new[] { 0f, 0.5f, 1f });
				arc.Shader = sweep;
				arc.Color = SKColors.White;
				c.Save();
				c.RotateDegrees(-90f, cx, cy);
				c.DrawArc(new SKRect(cx - r, cy - r, cx + r, cy + r), 0f, 360f * f, false, arc);
				c.Restore();
				float a = -MathF.PI / 2f + MathF.PI * 2f * f;
				SkiaKit.Glow(c, cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r, 16f, 16f, ca.WithAlpha(150), SKBlendMode.Plus);
			}
			string text = on ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + ring.Unit : "--";
			McText(c, text, cx, cy - 11f, 22f, white, align: 0, font: NpCap);
			McText(c, ring.Label, cx, 420f, 14f, new SKColor(255, 255, 255, 150), align: 0, track: 1.5f, font: NpCap);
		}
	}
}
