using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SkiaSharp;

namespace LcdStudio.Core;

/// <summary>
/// Fan faces drawn by Skia: Neon Ring and Reactor. Their light is real blurred glow added onto the dark, their arcs
/// follow the reading on a spring instead of snapping to it, and everything that turns or pulses keeps its own phase,
/// so a change of pace never makes it jump.
/// </summary>
public sealed partial class Faces
{
	private sealed class GlowState
	{
		public double At = double.NaN, F, V, Spin0, Spin1, Spin2, Beat, Ripple, Orbit, Travel, Heat, Scan;

		/// <summary>How far the face has turned to its warning colour (0 to 1), and the phase of its slow breath.</summary>
		public double Warn = double.NaN, WarnBreath;

		public bool Warning;
	}

	private readonly Dictionary<(string, string), GlowState> _glowStates = new Dictionary<(string, string), GlowState>();

	private Bitmap? _skFace;

	/// <summary>The surface FaceSkia is drawing on, for work done straight on its pixels.</summary>
	private SKSurface? _faceSurface;

	/// <summary>The face's state for this fan, stepped to now: the reading eased on a spring, and the time since last frame.</summary>
	private (GlowState S, float F, double Dt) GlowStep(FaceContext x, string face)
	{
		if (!_glowStates.TryGetValue((x.Fan.Serial, face), out GlowState? s))
		{
			_glowStates[(x.Fan.Serial, face)] = s = new GlowState();
		}
		double dt = double.IsNaN(s.At) || x.T < s.At ? 0.0 : Math.Min(0.5, x.T - s.At);
		if (double.IsNaN(s.At))
		{
			s.F = x.Fraction;
		}
		s.At = x.T;
		LightSpring(ref s.F, ref s.V, x.Fraction, 3.0, dt);
		return (s, (float)Math.Clamp(s.F, 0.0, 1.0), dt);
	}

	/// <summary>
	/// Draws the whole face with Skia into a frame of its own and lays it onto the face: in place of what is there, or
	/// over it when the drawing leaves the frame clear around itself.
	/// </summary>
	private unsafe void FaceSkia(Graphics g, Action<SKCanvas> draw, bool over = false)
	{
		_skFace ??= new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
		BitmapData data = _skFace.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
		try
		{
			using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul), data.Scan0, data.Stride);
			_faceSurface = surface;
			draw(surface.Canvas);
			surface.Canvas.Flush();
		}
		finally
		{
			_faceSurface = null;
			_skFace.UnlockBits(data);
		}
		CompositingMode mode = g.CompositingMode;
		g.CompositingMode = (over || _faceOver) ? CompositingMode.SourceOver : CompositingMode.SourceCopy;
		g.DrawImageUnscaled(_skFace, 0, 0);
		g.CompositingMode = mode;
	}

	/// <summary>
	/// A stroke of neon: a wide soft bloom, a tighter glow and the tube, then a fine bright core. With a tint shader
	/// every layer takes its colours from it, so the light keeps the tube's colour along its length; the core is only
	/// lightened, never white, so the colour never burns out.
	/// </summary>
	private static void Neon(SKCanvas c, SKPath path, float width, Color color, SKShader? tint)
	{
		using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, BlendMode = SKBlendMode.Plus, Shader = tint };
		void Layer(float w, float alpha, float blur)
		{
			p.StrokeWidth = w;
			p.Color = tint == null ? SkiaKit.Sk(color, alpha) : new SKColor(255, 255, 255, (byte)(255 * alpha));
			using SKMaskFilter? soft = blur > 0f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur) : null;
			p.MaskFilter = soft;
			c.DrawPath(path, p);
			p.MaskFilter = null;
		}
		Layer(width * 1.6f, 0.4f, width * 0.9f);
		Layer(width, 0.35f, width * 0.25f);
		Layer(width * 0.7f, 0.75f, 0f);
		p.Shader = null;
		p.StrokeWidth = Math.Max(1.5f, width * 0.2f);
		p.Color = SkiaKit.Sk(Imaging.Mix(color, Color.White, 0.45), 0.55f);
		c.DrawPath(path, p);
	}

	/// <summary>The reading as a white outline with a soft glow of the given colour behind it.</summary>
	private void GlowNumber(SKCanvas c, string value, string font, FontStyle style, RectangleF box, Color glow)
	{
		using GraphicsPath gp = FitText(value, font, style, box);
		using SKPath path = SkiaKit.FromGdi(gp);
		using var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
		p.Color = SkiaKit.Sk(glow, 0.55f);
		p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f);
		c.DrawPath(path, p);
		p.Color = SkiaKit.Sk(glow, 0.5f);
		p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f);
		c.DrawPath(path, p);
		p.MaskFilter = null;
		p.BlendMode = SKBlendMode.SrcOver;
		p.Color = SKColors.White;
		c.DrawPath(path, p);
	}

	/// <summary>
	/// Neon Ring: a neon tube round the face filling with the reading, shading from C1 to C2 along its length with a
	/// hot spot at its head, and a comet of C2 orbiting just outside it; the reading glows at the centre.
	/// </summary>
	private void NeonRing(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Neon Ring");
		x = Warned(x, s, dt);
		s.Orbit += dt * 1.6;
		// the tube breathes slowly rather than flickering, so it never jumps from one frame to the next
		float hum = 0.92f + 0.08f * (float)Math.Sin(x.T * 1.3);
		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(6, 6, 14)));
			SkiaKit.Glow(c, 200f, 200f, 240f, 240f, SkiaKit.Sk(x.C1, 0.1f + 0.08f * f), SKBlendMode.Plus);
			var oval = new SKRect(48f, 48f, 352f, 352f);
			using (var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 16f, Color = new SKColor(255, 255, 255, 22) })
			{
				c.DrawOval(oval, track);
			}
			float sweep = Math.Max(2f, f * 360f);
			// the colour runs from C1 at the start to C2 at the head; it starts a little before the tube does and comes
			// back to C1 behind it, so the glow round the start cap has no seam where the colour wheel wraps
			float end = Math.Min(0.995f, (sweep + 10f) / 360f);
			using (var arc = new SKPath())
			using (SKShader tube = SKShader.CreateSweepGradient(new SKPoint(200f, 200f), new[] { SkiaKit.Sk(x.C1, hum), SkiaKit.Sk(x.C1, hum), SkiaKit.Sk(x.C2, hum), SkiaKit.Sk(x.C1, hum) }, new[] { 0f, 10f / 360f, Math.Max(11f / 360f, end), 1f }, SKMatrix.CreateRotationDegrees(-100f, 200f, 200f)))
			{
				arc.AddArc(oval, -90f, sweep);
				Neon(c, arc, 14f, Imaging.Mix(x.C1, x.C2, 0.3), tube);
			}
			// the head of the tube burns brightest
			double head = (-90.0 + sweep) * Math.PI / 180.0;
			float hx = 200f + 152f * (float)Math.Cos(head), hy = 200f + 152f * (float)Math.Sin(head);
			SkiaKit.Glow(c, hx, hy, 34f, 34f, SkiaKit.Sk(x.C2, 0.45f * hum), SKBlendMode.Plus);
			SkiaKit.Glow(c, hx, hy, 10f, 10f, new SKColor(255, 255, 255, (byte)(200 * hum)), SKBlendMode.Plus);
			// the comet: a fading tail along its orbit, a soft halo and a bright spark
			float a = (float)(s.Orbit * 180.0 / Math.PI % 360.0);
			var orbit = new SKRect(28f, 28f, 372f, 372f);
			using (var tail = new SKPath())
			using (SKShader fade = SKShader.CreateSweepGradient(new SKPoint(200f, 200f), new[] { SkiaKit.Sk(x.C2, 0f), SkiaKit.Sk(x.C2, 0.8f) }, new[] { 0f, 40f / 360f }, SKMatrix.CreateRotationDegrees(a - 40f, 200f, 200f)))
			using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round, Shader = fade, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.2f) })
			{
				tail.AddArc(orbit, a - 40f, 40f);
				c.DrawPath(tail, p);
			}
			float ox = 200f + 172f * (float)Math.Cos(s.Orbit), oy = 200f + 172f * (float)Math.Sin(s.Orbit);
			SkiaKit.Glow(c, ox, oy, 14f, 14f, SkiaKit.Sk(x.C2, 0.7f), SKBlendMode.Plus);
			using (var spark = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 230), BlendMode = SKBlendMode.Plus })
			{
				c.DrawCircle(ox, oy, 2.6f, spark);
			}
			GlowNumber(c, x.Value, "Segoe UI Semibold", FontStyle.Regular, new RectangleF(110f, 150f, 180f, 92f), x.C1);
		});
		Small(x.G, x.Label, Imaging.WithAlpha(x.C1, 220), 128f, 12f, bold: true, 1f);
		Small(x.G, x.Time, Color.FromArgb(170, 255, 255, 255), 268f, 13f, bold: false);
	}

	/// <summary>
	/// Reactor: three segmented rings of C2 turning against each other round a pulsing core of C1, faster and brighter
	/// the higher the reading, with rings of energy rippling out from the core; a gauge of C1 fills over three quarters
	/// of the way round, and the reading glows at the centre.
	/// </summary>
	private void Reactor(FaceContext x)
	{
		var (s, f, dt) = GlowStep(x, "Reactor");
		x = Warned(x, s, dt);
		// every pace follows the reading, through phases of their own, so a change of pace never jumps
		double pace = 0.6 + 0.8 * f;
		s.Spin0 += dt * 48.0 * pace;
		s.Spin1 -= dt * 72.0 * pace;
		s.Spin2 += dt * 48.0 * pace;
		s.Beat += dt * 3.0 * (0.7 + 0.6 * f);
		s.Ripple += dt * (0.25 + 0.6 * f);
		FaceSkia(x.G, c =>
		{
			c.Clear(Back(x, new SKColor(2, 6, 14)));
			SkiaKit.Glow(c, 200f, 200f, 210f, 210f, SkiaKit.Sk(x.C2, 0.12f + 0.08f * f), SKBlendMode.Plus);
			// the rings: each a set of segments, glowing softly behind and sharp in front
			double[] spins = { s.Spin0, s.Spin1, s.Spin2 };
			for (int i = 0; i < 3; i++)
			{
				float r = 175 - i * 30;
				int n = 10 + i * 4;
				var oval = new SKRect(200f - r, 200f - r, 200f + r, 200f + r);
				using var ring = new SKPath();
				for (int j = 0; j < n; j++)
				{
					ring.AddArc(oval, (float)(spins[i] + j * 360.0 / n), 360f / n * 0.6f);
				}
				float level = (120 + i * 40) / 255f * (0.75f + 0.25f * f);
				using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.Plus, StrokeWidth = 9 - i * 2 + 6, Color = SkiaKit.Sk(x.C2, level * 0.45f), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) };
				c.DrawPath(ring, p);
				p.MaskFilter = null;
				p.StrokeWidth = 9 - i * 2;
				p.Color = SkiaKit.Sk(x.C2, level);
				c.DrawPath(ring, p);
			}
			// the core, breathing, and rings of energy rippling out of it, each fading as it widens
			float pulse = 0.6f + 0.4f * (float)Math.Sin(s.Beat);
			SkiaKit.Glow(c, 200f, 200f, 95f, 95f, SkiaKit.Sk(x.C1, 0.55f * pulse), SKBlendMode.Plus);
			using (var wave = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.4f) })
			{
				for (int k = 0; k < 3; k++)
				{
					float life = (float)((s.Ripple + k / 3.0) % 1.0);
					wave.Color = SkiaKit.Sk(x.C1, 0.7f * (float)Math.Sin(Math.PI * life));
					c.DrawCircle(200f, 200f, 40f + 80f * life, wave);
				}
			}
			// the gauge
			using (var gauge = new SKPath())
			{
				float sweep = 270f * Math.Max(0.01f, f);
				gauge.AddArc(new SKRect(70f, 70f, 330f, 330f), 135f, sweep);
				Neon(c, gauge, 8f, x.C1, null);
				double head = (135.0 + sweep) * Math.PI / 180.0;
				SkiaKit.Glow(c, 200f + 130f * (float)Math.Cos(head), 200f + 130f * (float)Math.Sin(head), 16f, 16f, new SKColor(255, 255, 255, 170), SKBlendMode.Plus);
			}
			GlowNumber(c, x.Value, "Consolas", FontStyle.Bold, new RectangleF(120f, 165f, 160f, 70f), x.C1);
		});
		Small(x.G, ShortLabelOr(x), Imaging.WithAlpha(x.C1, 230), 148f, 12f, bold: true, 1f);
		Small(x.G, x.Time, Imaging.WithAlpha(x.C1, 190), 252f, 12f, bold: false);
	}
}
