using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;

namespace LcdStudio.Core;

public sealed class Inflated : IDisposable
{
	private readonly Dictionary<string, InflatedGlyphs> _cache = new Dictionary<string, InflatedGlyphs>();

	private readonly LinkedList<string> _order = new LinkedList<string>();

	private const int MaxCached = 48;

	public InflatedGlyphs Get(string text, string font, bool bold, float emPx, Color color, string material = "balloon", string? print = null)
	{
		string text2 = $"{text}|{font}|{bold}|{emPx:0.#}|{color.ToArgb()}|{material}|{print}";
		if (_cache.TryGetValue(text2, out InflatedGlyphs value))
		{
			return value;
		}
		InflatedGlyphs inflatedGlyphs = Render(text, font, bold, emPx, color, material, print);
		_cache[text2] = inflatedGlyphs;
		_order.AddLast(text2);
		while (_order.Count > 48)
		{
			string value2 = _order.First.Value;
			_order.RemoveFirst();
			if (_cache.Remove(value2, out InflatedGlyphs value3))
			{
				value3.Dispose();
			}
		}
		return inflatedGlyphs;
	}

	public static InflatedGlyphs Render(string text, string font, bool bold, float emPx, Color color, string material = "balloon", string? print = null)
	{
		bool flag = material == "vinyl";
		if (string.IsNullOrEmpty(text))
		{
			text = " ";
		}
		using FontFamily fontFamily = Family(font);
		FontStyle style = ((bold && fontFamily.IsStyleAvailable(FontStyle.Bold)) ? FontStyle.Bold : FontStyle.Regular);
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddString(text, fontFamily, (int)style, emPx, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF b = graphicsPath.GetBounds();
		if (b.Width < 1f)
		{
			b = new RectangleF(0f, 0f, emPx * 0.3f, emPx * 0.7f);
		}
		float num = emPx * (flag ? 0.028f : 0.05f);
		float num2 = emPx * (flag ? 0.17f : 0.15f);
		float num3 = (flag ? (emPx * 0.022f) : 0f);
		int num4 = (int)Math.Ceiling(emPx * 0.16f);
		int num5 = (int)Math.Ceiling(b.Width + num * 2f) + num4 * 2;
		int num6 = (int)Math.Ceiling(b.Height + num * 2f) + num4 * 2;
		float num7 = (float)num4 + num - b.X;
		float num8 = (float)num4 + num - b.Y;
		float[] array = new float[num5 * num6];
		using (Bitmap bitmap = new Bitmap(num5, num6, PixelFormat.Format32bppArgb))
		{
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.Clear(Color.Black);
				graphics.TranslateTransform(num7, num8);
				graphics.FillPath(Brushes.White, graphicsPath);
				using Pen pen = new Pen(Color.White, num * 2f)
				{
					LineJoin = LineJoin.Round,
					StartCap = LineCap.Round,
					EndCap = LineCap.Round
				};
				graphics.DrawPath(pen, graphicsPath);
			}
			byte[] array2 = Pixels(bitmap);
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = (float)(int)array2[i * 4 + 1] / 255f;
			}
		}
		float[] array3 = Chamfer(array, num5, num6);
		float[] array4 = new float[num5 * num6];
		for (int j = 0; j < array4.Length; j++)
		{
			if (!(array[j] <= 0.01f))
			{
				float num9 = Math.Max(0f, array3[j] - 0.5f + array[j] * 0.5f);
				float num10 = Math.Min(1f, Math.Max(0f, num9 - num3) / num2);
				array4[j] = num2 * MathF.Sqrt(1f - (1f - num10) * (1f - num10));
			}
		}
		float[] seam = null;
		float[] crease = null;
		if (flag && emPx >= 140f)
		{
			(seam, crease) = Quilt(graphicsPath, num7, num8, array, num5, num6, emPx, num2, num);
		}
		else if (flag)
		{
			seam = new float[num5 * num6];
			crease = new float[num5 * num6];
		}
		int r = Math.Max(1, (int)(emPx * 0.012f));
		Blur(array4, num5, num6, r);
		Blur(array4, num5, num6, r);
		Blur(array4, num5, num6, r);
		(float, float, float) tuple2 = Normalize(-0.45f, -0.75f, 0.55f);
		(float, float, float) tuple3 = Normalize(tuple2.Item1, tuple2.Item2, tuple2.Item3 + 1f);
		float[] array5 = new float[num5 * num6];
		int num11 = (int)(emPx * 0.025f);
		int num12 = (int)(emPx * 0.06f);
		for (int k = 0; k < num6; k++)
		{
			for (int l = 0; l < num5; l++)
			{
				int num13 = l - num11;
				int num14 = k - num12;
				if (num13 >= 0 && num14 >= 0 && num13 < num5 && num14 < num6)
				{
					array5[k * num5 + l] = array[num14 * num5 + num13];
				}
			}
		}
		int r2 = Math.Max(2, (int)(emPx * 0.035f));
		Blur(array5, num5, num6, r2);
		Blur(array5, num5, num6, r2);
		byte[] array6 = new byte[num5 * num6 * 4];
		if (flag)
		{
			ShadeVinyl(array, array3, seam, crease, array5, array6, num5, num6, num3, emPx, color);
			return Finish(array6, num5, num6, num4, b, num, print, graphicsPath, num7, num8, emPx, array);
		}
		float num15 = (float)(int)color.R / 255f;
		float num16 = (float)(int)color.G / 255f;
		float num17 = (float)(int)color.B / 255f;
		float b2 = num15 * 0.78f;
		float b3 = num16 * 0.42f;
		float b4 = num17 * 0.25f;
		for (int m = 1; m < num6 - 1; m++)
		{
			for (int n = 1; n < num5 - 1; n++)
			{
				int num18 = m * num5 + n;
				float num19 = array[num18];
				float num20 = array5[num18] * 0.55f;
				float num21 = 0f;
				float num22 = 0f;
				float num23 = 0f;
				float num24 = num20;
				if (num19 > 0.003f)
				{
					float x = (0f - (array4[num18 + 1] - array4[num18 - 1])) * 0.5f;
					float y = (0f - (array4[num18 + num5] - array4[num18 - num5])) * 0.5f;
					(float, float, float) tuple4 = Normalize(x, y, 1f);
					float num25 = Math.Max(0f, tuple4.Item1 * tuple2.Item1 + tuple4.Item2 * tuple2.Item2 + tuple4.Item3 * tuple2.Item3);
					float x2 = Math.Max(0f, tuple4.Item1 * tuple3.Item1 + tuple4.Item2 * tuple3.Item2 + tuple4.Item3 * tuple3.Item3);
					float num26 = MathF.Pow(x2, 70f) * 0.75f + MathF.Pow(x2, 14f) * 0.22f + MathF.Pow(x2, 4f) * 0.08f;
					float num27 = 0.5f + 0.62f * num25;
					float num28 = 1f - tuple4.Item3;
					float num29 = Lerp(num15, b2, num28 * 1.2f) * num27 + num26;
					float num30 = Lerp(num16, b3, num28 * 1.2f) * num27 + num26;
					float num31 = Lerp(num17, b4, num28 * 1.2f) * num27 + num26;
					num21 = num29 * num19;
					num22 = num30 * num19;
					num23 = num31 * num19;
					num24 = num19 + num20 * (1f - num19);
				}
				int num32 = num18 * 4;
				if (!(num24 <= 0f))
				{
					array6[num32 + 2] = ToByte(num21 / num24);
					array6[num32 + 1] = ToByte(num22 / num24);
					array6[num32] = ToByte(num23 / num24);
					array6[num32 + 3] = ToByte(num24);
				}
			}
		}
		return Finish(array6, num5, num6, num4, b, num, print, graphicsPath, num7, num8, emPx, array);
	}

	private static InflatedGlyphs Finish(byte[] outPx, int w, int h, int pad, RectangleF b, float rounding, string? print, GraphicsPath path, float ox, float oy, float emPx, float[] mask)
	{
		Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
		BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		for (int i = 0; i < h; i++)
		{
			Marshal.Copy(outPx, i * w * 4, bitmapData.Scan0 + i * bitmapData.Stride, w * 4);
		}
		bitmap.UnlockBits(bitmapData);
		if (!string.IsNullOrWhiteSpace(print))
		{
			PrintLabel(bitmap, print, path, ox, oy, emPx, b, mask);
		}
		return new InflatedGlyphs(bitmap, new RectangleF(pad, pad, b.Width + rounding * 2f, b.Height + rounding * 2f));
	}

	private static (float[] Seams, float[] Creases) Quilt(GraphicsPath glyphs, float ox, float oy, float[] mask, int w, int h, float em, float radius, float rounding)
	{
		using (GraphicsPath graphicsPath = (GraphicsPath)glyphs.Clone())
		{
			using (Matrix matrix = new Matrix())
			{
				matrix.Translate(ox, oy);
				graphicsPath.Transform(matrix);
			}
			graphicsPath.Flatten(null, 0.5f);
			PointF[] pathPoints = graphicsPath.PathPoints;
			byte[] pathTypes = graphicsPath.PathTypes;
			using Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			using Bitmap bitmap2 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			using (Graphics graphics2 = Graphics.FromImage(bitmap))
			{
				using Graphics graphics = Graphics.FromImage(bitmap2);
				SmoothingMode smoothingMode2 = (graphics.SmoothingMode = SmoothingMode.AntiAlias);
				graphics2.SmoothingMode = smoothingMode2;
				graphics2.Clear(Color.Black);
				graphics.Clear(Color.Black);
				using Pen pen = new Pen(Color.White, Math.Max(1.6f, em * 0.009f))
				{
					StartCap = LineCap.Round,
					EndCap = LineCap.Round
				};
				using Pen pen2 = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(0.7f, em * 0.0025f))
				{
					StartCap = LineCap.Round,
					EndCap = LineCap.Round
				};
				float spacing = em * 0.2f;
				List<PointF[]> list = new List<PointF[]>();
				int num = 0;
				for (int i = 0; i < pathPoints.Length; i++)
				{
					if (i == pathPoints.Length - 1 || (pathTypes[i + 1] & 7) == 0)
					{
						if (i + 1 - num >= 3)
						{
							list.Add(pathPoints[num..(i + 1)]);
						}
						num = i + 1;
					}
				}
				float num2 = Math.Sign((list.Count == 0) ? 1f : Area(list.OrderByDescending((PointF[] p) => Math.Abs(Area(p))).First()));
				List<PointF> list2 = new List<PointF>();
				foreach (PointF[] item4 in list)
				{
					if ((float)Math.Sign(Area(item4)) != num2)
					{
						continue;
					}
					float num3 = 0f;
					for (int j = 0; j < item4.Length; j++)
					{
						num3 += Dist(item4[j], item4[(j + 1) % item4.Length]);
					}
					if (num3 < spacing * 1.5f)
					{
						continue;
					}
					int num4 = Math.Max(2, (int)Math.Round(num3 / spacing));
					float num5 = num3 / (float)num4;
					float num6 = 0f;
					float num7 = num5 * 0.5f;
					for (int k = 0; k < item4.Length; k++)
					{
						PointF a = item4[k];
						PointF b = item4[(k + 1) % item4.Length];
						float num8 = Dist(a, b);
						while (num8 > 0f && num7 <= num6 + num8)
						{
							float num9 = (num7 - num6) / num8;
							PointF p2 = new PointF(a.X + (b.X - a.X) * num9, a.Y + (b.Y - a.Y) * num9);
							float num10 = (b.X - a.X) / num8;
							float num11 = 0f - (b.Y - a.Y) / num8;
							float num12 = num10;
							int num13 = (int)(p2.X + num11 * 4f);
							int num14 = (int)(p2.Y + num12 * 4f);
							if (num13 < 0 || num14 < 0 || num13 >= w || num14 >= h || mask[num14 * w + num13] < 0.5f)
							{
								num11 = 0f - num11;
								num12 = 0f - num12;
							}
							if (list2.Any((PointF e) => Dist(e, p2) < spacing * 0.6f))
							{
								num7 += num5;
								continue;
							}
							float num15;
							for (num15 = 4f; num15 < radius * 4f; num15 += 1f)
							{
								int num16 = (int)(p2.X + num11 * num15);
								int num17 = (int)(p2.Y + num12 * num15);
								if (num16 < 0 || num17 < 0 || num16 >= w || num17 >= h || mask[num17 * w + num16] < 0.5f)
								{
									break;
								}
							}
							PointF pt = new PointF(p2.X - num11 * rounding * 0.6f, p2.Y - num12 * rounding * 0.6f);
							PointF pt2 = new PointF(p2.X + num11 * (num15 + rounding * 0.6f), p2.Y + num12 * (num15 + rounding * 0.6f));
							graphics2.DrawLine(pen, pt, pt2);
							list2.Add(new PointF(p2.X + num11 * num15, p2.Y + num12 * num15));
							(PointF, float, float)[] array = new(PointF, float, float)[2]
							{
								(new PointF(p2.X + num11 * num15 * 0.18f, p2.Y + num12 * num15 * 0.18f), 0f - num11, 0f - num12),
								(new PointF(p2.X + num11 * num15 * 0.82f, p2.Y + num12 * num15 * 0.82f), num11, num12)
							};
							for (int l = 0; l < array.Length; l++)
							{
								(PointF, float, float) tuple = array[l];
								PointF item = tuple.Item1;
								float item2 = tuple.Item2;
								float item3 = tuple.Item3;
								for (int m = -2; m <= 2; m++)
								{
									if (m != 0)
									{
										double num18 = Math.Atan2(item3, item2) + Math.PI / 2.0 * (double)Math.Sign(m) + (double)m * 0.12;
										float num19 = radius * (0.35f + 0.1f * (float)Math.Abs(m));
										graphics.DrawLine(pen2, item, new PointF(item.X + (float)Math.Cos(num18) * num19, item.Y + (float)Math.Sin(num18) * num19));
									}
								}
							}
							num7 += num5;
						}
						num6 += num8;
					}
				}
			}
			float[] array2 = Channel(bitmap);
			float[] array3 = Channel(bitmap2);
			Blur(array2, w, h, Math.Max(1, (int)(em * 0.006f)));
			Blur(array3, w, h, 1);
			return (Seams: array2, Creases: array3);
		}
		static float Area(PointF[] p)
		{
			float num20 = 0f;
			for (int n = 0; n < p.Length; n++)
			{
				PointF pointF = p[(n + 1) % p.Length];
				num20 += p[n].X * pointF.Y - pointF.X * p[n].Y;
			}
			return num20 / 2f;
		}
	}

	private static float[] MaxFilter(float[] v, int w, int h, int r)
	{
		float[] array = new float[v.Length];
		float[] array2 = new float[v.Length];
		for (int i = 0; i < h; i++)
		{
			for (int j = 0; j < w; j++)
			{
				float num = 0f;
				for (int k = Math.Max(0, j - r); k <= Math.Min(w - 1, j + r); k++)
				{
					num = Math.Max(num, v[i * w + k]);
				}
				array[i * w + j] = num;
			}
		}
		for (int l = 0; l < w; l++)
		{
			for (int m = 0; m < h; m++)
			{
				float num2 = 0f;
				for (int n = Math.Max(0, m - r); n <= Math.Min(h - 1, m + r); n++)
				{
					num2 = Math.Max(num2, array[n * w + l]);
				}
				array2[m * w + l] = num2;
			}
		}
		return array2;
	}

	private static float[] Channel(Bitmap bmp)
	{
		byte[] array = Pixels(bmp);
		float[] array2 = new float[bmp.Width * bmp.Height];
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i] = (float)(int)array[i * 4 + 1] / 255f;
		}
		return array2;
	}

	private static float Dist(PointF a, PointF b)
	{
		return MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
	}

	private static float Smooth(float e0, float e1, float x)
	{
		float num = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
		return num * num * (3f - 2f * num);
	}

	private static void ShadeVinyl(float[] mask, float[] dist, float[] seam, float[] crease, float[] shadow, byte[] outPx, int w, int h, float weld, float em, Color tint)
	{
		float[] array = MaxFilter(dist, w, h, Math.Max(2, (int)(em * 0.09f)));
		float[] array2 = (float[])dist.Clone();
		Blur(array2, w, h, Math.Max(1, (int)(em * 0.01f)));
		Blur(array, w, h, Math.Max(1, (int)(em * 0.02f)));
		float[] array3 = new float[w * h];
		for (int i = 0; i < array3.Length; i++)
		{
			array3[i] = ((!(seam[i] > 0.45f)) ? 1 : 0);
		}
		float[] array4 = Chamfer(array3, w, h);
		Blur(array4, w, h, Math.Max(1, (int)(em * 0.01f)));
		float num = em * 0.1f;
		(float, float, float) tuple = Normalize(-0.35f, -0.8f, 0.5f);
		(float, float, float) tuple2 = Normalize(tuple.Item1, tuple.Item2, tuple.Item3 + 1f);
		float num2 = 0.018f + (float)(int)tint.R / 255f * 0.05f;
		float num3 = 0.022f + (float)(int)tint.G / 255f * 0.05f;
		float num4 = 0.035f + (float)(int)tint.B / 255f * 0.05f;
		for (int j = 1; j < h - 1; j++)
		{
			for (int k = 1; k < w - 1; k++)
			{
				int num5 = j * w + k;
				float num6 = mask[num5];
				float num7 = shadow[num5] * 0.35f;
				float num8 = 0f;
				float num9 = 0f;
				float num10 = 0f;
				float num11 = num7;
				if (num6 > 0.003f)
				{
					float num12 = Math.Max(0f, dist[num5] - weld * 0.6f);
					float num13 = Math.Max(1.5f, array[num5] - weld * 0.6f);
					float num14 = Math.Clamp(num12 / num13, 0f, 1f);
					float num15 = array2[num5 + 1] - array2[num5 - 1];
					float num16 = array2[num5 + w] - array2[num5 - w];
					float num17 = MathF.Sqrt(num15 * num15 + num16 * num16);
					if (num17 > 0.0001f)
					{
						num15 /= num17;
						num16 /= num17;
					}
					else
					{
						num15 = (num16 = 0f);
					}
					float num18 = 1f - num14;
					float num19 = Math.Min(3.5f, num18 / MathF.Sqrt(Math.Max(0.02f, 1f - num18 * num18)));
					float num20 = array4[num5 + 1] - array4[num5 - 1];
					float num21 = array4[num5 + w] - array4[num5 - w];
					float num22 = MathF.Sqrt(num20 * num20 + num21 * num21);
					float num23 = 0f;
					float num24 = 0f;
					if (num22 > 0.0001f && array4[num5] < num * 1.5f)
					{
						float num25 = Math.Clamp(array4[num5] / num, 0f, 1f);
						float num26 = 1f - num25;
						float num27 = Math.Min(2f, num26 / MathF.Sqrt(Math.Max(0.02f, 1f - num26 * num26))) * 0.35f * MathF.Sqrt(num14);
						num23 = (0f - num20) / num22 * num27;
						num24 = (0f - num21) / num22 * num27;
					}
					(float, float, float) tuple3 = Normalize((0f - num15) * num19 + num23, (0f - num16) * num19 + num24, 1f);
					float num28 = 2f * tuple3.Item3 * tuple3.Item2;
					float num29 = 1f - tuple3.Item3;
					float num30 = Smooth(-0.55f, -0.32f, num28) * (1f - Smooth(-0.24f, -0.1f, num28)) * 0.85f;
					float num31 = MathF.Pow(Math.Max(0f, num28), 1.5f) * MathF.Pow(num29, 0.9f) * 0.8f;
					float num32 = MathF.Pow(num29, 2.4f) * 0.45f;
					float num33 = (tuple3.Item1 * 2.4f - tuple3.Item2 * 1.3f) * (float)Math.PI;
					float num34 = MathF.Pow(Math.Max(0f, tuple3.Item1 * tuple2.Item1 + tuple3.Item2 * tuple2.Item2 + tuple3.Item3 * tuple2.Item3), 80f) * 0.9f;
					float num35 = Math.Max(0f, tuple3.Item1 * tuple.Item1 + tuple3.Item2 * tuple.Item2 + tuple3.Item3 * tuple.Item3) * 0.06f;
					float num36 = 0.55f + 0.45f * num29;
					float num37 = num2 + num35 + num36 * (num30 * 0.95f + num31 * 0.3f + num32 * (0.16f + 0.1f * MathF.Sin(num33))) + num34;
					float num38 = num3 + num35 + num36 * (num30 * 0.8f + num31 * 0.6f + num32 * (0.6f + 0.18f * MathF.Sin(num33 + 2.1f))) + num34;
					float num39 = num4 + num35 * 1.2f + num36 * (num30 * 0.62f + num31 * 1.2f + num32 * (0.75f + 0.18f * MathF.Sin(num33 + 4.2f))) + num34;
					float num40 = seam[num5];
					float num41 = crease[num5];
					float num42 = 1f - 0.85f * num40;
					float num43 = Math.Max(0f, num40 - 0.55f) * 0.25f * (0.4f + num14);
					num37 = num37 * num42 + num43 * 0.8f;
					num38 = num38 * num42 + num43 * 0.85f;
					num39 = num39 * num42 + num43;
					num37 = num37 * (1f - 0.35f * num41) + num41 * 0.06f;
					num38 = num38 * (1f - 0.35f * num41) + num41 * 0.08f;
					num39 = num39 * (1f - 0.35f * num41) + num41 * 0.12f;
					float num44 = dist[num5];
					float num45 = ((num44 < weld * 1.4f) ? (Smooth(0f, weld * 0.5f, num44) * (1f - Smooth(weld * 0.8f, weld * 1.4f, num44))) : 0f);
					num37 += num45 * 0.22f;
					num38 += num45 * 0.34f;
					num39 += num45 * 0.5f;
					float num46 = num6 * (0.82f + 0.18f * Smooth(0f, weld, num44));
					num8 = num37 * num46;
					num9 = num38 * num46;
					num10 = num39 * num46;
					num11 = num46 + num7 * (1f - num46);
				}
				if (!(num11 <= 0f))
				{
					int num47 = num5 * 4;
					outPx[num47 + 2] = ToByte(num8 / num11);
					outPx[num47 + 1] = ToByte(num9 / num11);
					outPx[num47] = ToByte(num10 / num11);
					outPx[num47 + 3] = ToByte(num11);
				}
			}
		}
	}

	private static void PrintLabel(Bitmap bmp, string print, GraphicsPath glyphs, float ox, float oy, float em, RectangleF bounds, float[] mask)
	{
		float[] mask2 = mask;
		Graphics g = Graphics.FromImage(bmp);
		string[] lines;
		Font big;
		Font small;
		int w;
		int y0;
		int y1;
		try
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
			using GraphicsPath graphicsPath = (GraphicsPath)glyphs.Clone();
			using (Matrix matrix = new Matrix())
			{
				matrix.Translate(ox, oy);
				graphicsPath.Transform(matrix);
			}
			g.SetClip(graphicsPath);
			lines = print.Split('\n');
			big = new Font("Consolas", Math.Max(8f, em * 0.1f), FontStyle.Bold, GraphicsUnit.Pixel);
			try
			{
				small = new Font("Consolas", Math.Max(7f, em * 0.056f), FontStyle.Bold, GraphicsUnit.Pixel);
				try
				{
					using SolidBrush brush = new SolidBrush(Color.FromArgb(245, 244, 240, 230));
					w = bmp.Width;
					int height = bmp.Height;
					int num = Math.Max(0, (int)(ox + bounds.X));
					int num2 = Math.Min(w, (int)(ox + bounds.X + bounds.Width));
					y0 = Math.Max(0, (int)(oy + bounds.Y + bounds.Height * 0.4f));
					y1 = Math.Min(height, (int)(oy + bounds.Y + bounds.Height * 0.88f));
					int num3 = -1;
					int num4 = 0;
					int j = num;
					while (j < num2)
					{
						if (!SolidColumn(j))
						{
							j++;
							continue;
						}
						int num5 = j;
						for (; j < num2 && SolidColumn(j); j++)
						{
						}
						if (j - num5 > num4 && !((float)num4 >= em * 0.16f))
						{
							num4 = j - num5;
							num3 = num5;
						}
					}
					float num6 = 0f;
					for (int k = 0; k < lines.Length; k++)
					{
						num6 += ((k == 0) ? big : small).Size * 1.05f;
					}
					if ((float)num4 < num6 * 0.9f)
					{
						float num7 = lines.Max((string l) => g.MeasureString(l, (l == lines[0]) ? big : small).Width);
						int num8 = (int)(oy + bounds.Y + bounds.Height * 0.5f);
						int num9 = Math.Min(height, (int)(oy + bounds.Y + bounds.Height));
						int num10 = -1;
						int num11 = 0;
						int num12 = -1;
						int num13 = 0;
						for (int m = num8; m < num9; m++)
						{
							int num14 = 0;
							int num15 = 0;
							int n = 0;
							while (n < w)
							{
								if (mask2[m * w + n] <= 0.6f)
								{
									n++;
									continue;
								}
								int num16 = n;
								for (; n < w && mask2[m * w + n] > 0.6f; n++)
								{
								}
								if (n - num16 > num15)
								{
									num15 = n - num16;
									num14 = num16;
								}
							}
							if ((float)num15 >= num7 * 1.05f)
							{
								if (num12 < 0)
								{
									num12 = m;
									num13 = num14;
								}
							}
							else
							{
								num12 = -1;
							}
							if (num12 >= 0 && m - num12 + 1 > num11)
							{
								num11 = m - num12 + 1;
								num10 = num12;
							}
						}
						if (num11 > 0)
						{
							List<int> list = Fit(num11);
							float num17 = list.Sum((int i) => ((i == 0) ? big : small).Size * 1.05f);
							float num18 = (float)num10 + Math.Max(0f, ((float)num11 - num17) / 2f);
							float x = (float)num13 + em * 0.06f;
							{
								foreach (int item in list)
								{
									Font font = ((item == 0) ? big : small);
									g.DrawString(lines[item], font, brush, x, num18);
									num18 += font.Size * 1.05f;
								}
								return;
							}
						}
					}
					List<int> list2 = Fit(Math.Max(num4, 1));
					num6 = list2.Sum((int i) => ((i == 0) ? big : small).Size * 1.05f);
					float dx = ((num4 > 0) ? ((float)num3 + ((float)num4 - num6) / 2f) : (ox + bounds.X + em * 0.035f));
					float dy = oy + bounds.Y + bounds.Height * 0.86f;
					g.TranslateTransform(dx, dy);
					g.RotateTransform(-90f);
					float num19 = 0f;
					foreach (int item2 in list2)
					{
						Font font2 = ((item2 == 0) ? big : small);
						g.DrawString(lines[item2], font2, brush, 0f, num19);
						num19 += font2.Size * 1.05f;
					}
				}
				finally
				{
					if (small != null)
					{
						((IDisposable)small).Dispose();
					}
				}
			}
			finally
			{
				if (big != null)
				{
					((IDisposable)big).Dispose();
				}
			}
		}
		finally
		{
			if (g != null)
			{
				((IDisposable)g).Dispose();
			}
		}
		List<int> Fit(float room)
		{
			List<int> list3 = new List<int>();
			float num20 = 0f;
			for (int num21 = 0; num21 < lines.Length; num21++)
			{
				float num22 = ((num21 == 0) ? big : small).Size * 1.05f;
				if (num20 + num22 > room && list3.Count > 0)
				{
					break;
				}
				list3.Add(num21);
				num20 += num22;
			}
			return list3;
		}
		bool SolidColumn(int cx)
		{
			int num23 = 0;
			for (int num24 = y0; num24 < y1; num24++)
			{
				if (mask2[num24 * w + cx] > 0.6f)
				{
					num23++;
				}
			}
			return (float)num23 > (float)(y1 - y0) * 0.9f;
		}
	}

	private static FontFamily Family(string name)
	{
		try
		{
			return new FontFamily(name);
		}
		catch
		{
			return new FontFamily("Segoe UI");
		}
	}

	private static byte[] Pixels(Bitmap bmp)
	{
		BitmapData bitmapData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		byte[] array = new byte[bmp.Width * bmp.Height * 4];
		for (int i = 0; i < bmp.Height; i++)
		{
			Marshal.Copy(bitmapData.Scan0 + i * bitmapData.Stride, array, i * bmp.Width * 4, bmp.Width * 4);
		}
		bmp.UnlockBits(bitmapData);
		return array;
	}

	private static float[] Chamfer(float[] mask, int w, int h)
	{
		float[] array = new float[w * h];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = ((mask[i] > 0.5f) ? 1000000f : 0f);
		}
		for (int j = 1; j < h; j++)
		{
			for (int k = 1; k < w - 1; k++)
			{
				int num = j * w + k;
				if (array[num] != 0f)
				{
					array[num] = Math.Min(array[num], Math.Min(Math.Min(array[num - 1] + 1f, array[num - w] + 1f), Math.Min(array[num - w - 1] + 1.41421f, array[num - w + 1] + 1.41421f)));
				}
			}
		}
		for (int num2 = h - 2; num2 >= 0; num2--)
		{
			for (int num3 = w - 2; num3 >= 1; num3--)
			{
				int num4 = num2 * w + num3;
				if (array[num4] != 0f)
				{
					array[num4] = Math.Min(array[num4], Math.Min(Math.Min(array[num4 + 1] + 1f, array[num4 + w] + 1f), Math.Min(array[num4 + w + 1] + 1.41421f, array[num4 + w - 1] + 1.41421f)));
				}
			}
		}
		return array;
	}

	private static void Blur(float[] v, int w, int h, int r)
	{
		float[] array = new float[v.Length];
		float num = 1f / (float)(2 * r + 1);
		for (int i = 0; i < h; i++)
		{
			float num2 = 0f;
			int num3 = i * w;
			for (int j = -r; j <= r; j++)
			{
				num2 += v[num3 + Math.Clamp(j, 0, w - 1)];
			}
			for (int k = 0; k < w; k++)
			{
				array[num3 + k] = num2 * num;
				num2 += v[num3 + Math.Min(k + r + 1, w - 1)] - v[num3 + Math.Max(k - r, 0)];
			}
		}
		for (int l = 0; l < w; l++)
		{
			float num4 = 0f;
			for (int m = -r; m <= r; m++)
			{
				num4 += array[Math.Clamp(m, 0, h - 1) * w + l];
			}
			for (int n = 0; n < h; n++)
			{
				v[n * w + l] = num4 * num;
				num4 += array[Math.Min(n + r + 1, h - 1) * w + l] - array[Math.Max(n - r, 0) * w + l];
			}
		}
	}

	private static (float x, float y, float z) Normalize(float x, float y, float z)
	{
		float num = MathF.Sqrt(x * x + y * y + z * z);
		return (x: x / num, y: y / num, z: z / num);
	}

	private static float Lerp(float a, float b, float t)
	{
		return a + (b - a) * Math.Clamp(t, 0f, 1f);
	}

	private static byte ToByte(float v)
	{
		return (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
	}

	public void Dispose()
	{
		foreach (InflatedGlyphs value in _cache.Values)
		{
			value.Dispose();
		}
		_cache.Clear();
	}
}
