using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LcdStudio.Core;

/// <summary>
/// Relief lettering: text pressed out of whatever is drawn beneath it. The letters keep the background's own
/// picture, lifted toward a paper tone, with a lit top edge, a shaded lower rim and a soft cast shadow, so the
/// readings look like raised paper in the scene rather than a label on top of it.
/// </summary>
public sealed partial class ScreenRenderer
{
	private Bitmap? _canvas;

	private Graphics? _canvasGraphics;

	private sealed class ReliefMap
	{
		public string Look = "";
		public int X, Y, W, H;
		public float[] Mul = Array.Empty<float>();
		public float[] Add = Array.Empty<float>();
	}

	private readonly Dictionary<(int, int, string), ReliefMap> _reliefs = new Dictionary<(int, int, string), ReliefMap>();

	private bool DrawRelief(Graphics g, Widget w, RectangleF r, double t)
	{
		if (w.Effect != "relief" || (w.Kind != WidgetKind.Text && w.Kind != WidgetKind.Value))
		{
			return DrawHeadline(g, w, r, t);
		}
		// only possible while rendering a frame into a bitmap; elsewhere (an on-screen preview) fall back to paper type
		if (_canvas == null || _canvasGraphics != g)
		{
			PaperStack(g, w, r, t);
			return true;
		}
		string text = w.Kind == WidgetKind.Value ? Formatted(w) + (Sensors.Def(w.Sensor).Unit == "%" ? "%" : Sensors.Def(w.Sensor).Unit == "°C" ? "°" : "") : Resolve(w.Text);
		bool warn = w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt;
		string look = $"{text}|{warn}|{w.W}|{w.H}|{w.Size}|{w.Font}|{w.Align}|{w.Color}";
		var key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_reliefs.TryGetValue(key, out ReliefMap map) || map.Look != look)
		{
			_reliefs[key] = map = BuildRelief(w, r, text, warn);
			map.Look = look;
		}
		ApplyRelief(g, map);
		return true;
	}

	/// <summary>
	/// Works out, once per change of text, how each pixel around the letters is to be reworked: inside the
	/// letters the background is mixed toward the paper colour (Color) and lit along the top-left rims and
	/// shaded along the bottom-right ones; outside, a soft shadow falls down and to the right.
	/// </summary>
	private ReliefMap BuildRelief(Widget w, RectangleF r, string text, bool warn)
	{
		int pad = (int)Math.Ceiling(w.Size * 0.18f) + 4;
		var map = new ReliefMap
		{
			X = (int)Math.Floor(r.X) - pad,
			Y = (int)Math.Floor(r.Y) - pad,
			W = (int)Math.Ceiling(r.Width) + pad * 2,
			H = (int)Math.Ceiling(r.Height) + pad * 2
		};
		int n = map.W * map.H;
		var mask = new float[n];
		using (var bmp = new Bitmap(map.W, map.H, PixelFormat.Format32bppArgb))
		{
			using (Graphics mg = Graphics.FromImage(bmp))
			{
				mg.SmoothingMode = SmoothingMode.AntiAlias;
				using var path = new GraphicsPath(FillMode.Winding);
				FontFamily family;
				try
				{
					family = new FontFamily(w.Font);
				}
				catch
				{
					family = new FontFamily("Segoe UI");
				}
				using (family)
				{
					FontStyle style = w.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
					path.AddString(text, family, (int)style, w.Size, PointF.Empty, StringFormat.GenericTypographic);
				}
				RectangleF b = path.GetBounds();
				float x = Align(w.Align) switch { StringAlignment.Center => (r.Width - b.Width) / 2f, StringAlignment.Far => r.Width - b.Width, _ => 0f } - b.X + pad;
				float y = (r.Height - b.Height) / 2f - b.Y + pad;
				mg.TranslateTransform(x, y);
				mg.FillPath(Brushes.White, path);
			}
			BitmapData data = bmp.LockBits(new Rectangle(0, 0, map.W, map.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			unsafe
			{
				for (int yy = 0; yy < map.H; yy++)
				{
					byte* row = (byte*)data.Scan0 + yy * data.Stride;
					for (int xx = 0; xx < map.W; xx++)
					{
						mask[yy * map.W + xx] = row[xx * 4 + 3] / 255f;
					}
				}
			}
			bmp.UnlockBits(data);
		}
		float At(int xx, int yy) => xx < 0 || yy < 0 || xx >= map.W || yy >= map.H ? 0f : mask[yy * map.W + xx];
		// the cast shadow: the mask moved down-right and softened
		int sdx = Math.Max(2, (int)(w.Size * 0.05f)), sdy = Math.Max(3, (int)(w.Size * 0.07f)), blur = Math.Max(1, (int)(w.Size * 0.05f));
		var shadow = new float[n];
		for (int yy = 0; yy < map.H; yy++)
		{
			for (int xx = 0; xx < map.W; xx++)
			{
				shadow[yy * map.W + xx] = At(xx - sdx, yy - sdy);
			}
		}
		shadow = BoxBlur(shadow, map.W, map.H, blur);
		shadow = BoxBlur(shadow, map.W, map.H, blur);
		int rim = Math.Max(1, (int)(w.Size * 0.025f));
		Color paper = warn ? Imaging.Hex(w.WarnColor, Color.FromArgb(255, 170, 90)) : Imaging.Hex(w.Color, Color.FromArgb(246, 232, 212));
		float strength = w.Thickness > 0f ? w.Thickness / 100f : 0.5f;
		map.Mul = new float[n];
		map.Add = new float[n * 3];
		for (int i = 0; i < n; i++)
		{
			int xx = i % map.W, yy = i / map.W;
			float a = mask[i];
			float lit = Math.Max(0f, a - At(xx - rim, yy - rim));
			float dark = Math.Max(0f, a - At(xx + rim, yy + rim));
			float mul = 1f - strength * a;
			mul *= 1f - 0.62f * shadow[i] * (1f - a);
			mul *= 1f - 0.4f * dark;
			map.Mul[i] = mul;
			float add = strength * a;
			float glow = 0.55f * lit;
			map.Add[i * 3] = paper.R * add + 255f * glow;
			map.Add[i * 3 + 1] = paper.G * add + 255f * glow;
			map.Add[i * 3 + 2] = paper.B * add + 255f * glow;
		}
		return map;
	}

	private static float[] BoxBlur(float[] src, int w, int h, int radius)
	{
		var tmp = new float[src.Length];
		var dst = new float[src.Length];
		float norm = 1f / (radius * 2 + 1);
		for (int y = 0; y < h; y++)
		{
			float sum = 0f;
			for (int x = -radius; x <= radius; x++)
			{
				sum += src[y * w + Math.Clamp(x, 0, w - 1)];
			}
			for (int x = 0; x < w; x++)
			{
				tmp[y * w + x] = sum * norm;
				sum += src[y * w + Math.Min(w - 1, x + radius + 1)] - src[y * w + Math.Max(0, x - radius)];
			}
		}
		for (int x = 0; x < w; x++)
		{
			float sum = 0f;
			for (int y = -radius; y <= radius; y++)
			{
				sum += tmp[Math.Clamp(y, 0, h - 1) * w + x];
			}
			for (int y = 0; y < h; y++)
			{
				dst[y * w + x] = sum * norm;
				sum += tmp[Math.Min(h - 1, y + radius + 1) * w + x] - tmp[Math.Max(0, y - radius) * w + x];
			}
		}
		return dst;
	}

	/// <summary>Reworks the frame's pixels under the map: each channel becomes pixel × Mul + Add.</summary>
	private void ApplyRelief(Graphics g, ReliefMap map)
	{
		var area = Rectangle.Intersect(new Rectangle(map.X, map.Y, map.W, map.H), new Rectangle(0, 0, _canvas!.Width, _canvas.Height));
		if (area.Width <= 0 || area.Height <= 0)
		{
			return;
		}
		g.Flush(FlushIntention.Sync);
		BitmapData data = _canvas.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
		unsafe
		{
			for (int y = 0; y < area.Height; y++)
			{
				byte* row = (byte*)data.Scan0 + y * data.Stride;
				int my = area.Y - map.Y + y;
				for (int x = 0; x < area.Width; x++)
				{
					int i = my * map.W + (area.X - map.X + x);
					float mul = map.Mul[i];
					if (mul == 1f && map.Add[i * 3] == 0f)
					{
						continue;
					}
					byte* p = row + x * 4;
					p[0] = (byte)Math.Clamp(p[0] * mul + map.Add[i * 3 + 2], 0f, 255f);
					p[1] = (byte)Math.Clamp(p[1] * mul + map.Add[i * 3 + 1], 0f, 255f);
					p[2] = (byte)Math.Clamp(p[2] * mul + map.Add[i * 3], 0f, 255f);
				}
			}
		}
		_canvas.UnlockBits(data);
	}
}
