using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LcdStudio.Core;

public sealed class Extrude : IDisposable
{
	public sealed class Outline : IDisposable
	{
		public GraphicsPath Face = new GraphicsPath();

		public RectangleF Bounds;

		public PointF[] A = Array.Empty<PointF>();

		public PointF[] B = Array.Empty<PointF>();

		public PointF[] N = Array.Empty<PointF>();

		public bool[] Crease = Array.Empty<bool>();

		public void Dispose()
		{
			Face.Dispose();
		}
	}

	private readonly Dictionary<string, Outline> _cache = new Dictionary<string, Outline>();

	private readonly LinkedList<string> _order = new LinkedList<string>();

	public Outline Get(string text, string font, bool bold, float emPx)
	{
		string text2 = $"{text}\u0001{font}\u0001{bold}\u0001{emPx:0.#}";
		if (_cache.TryGetValue(text2, out Outline value))
		{
			return value;
		}
		value = Build(text, font, bold, emPx);
		_cache[text2] = value;
		_order.AddLast(text2);
		if (_order.Count > 64)
		{
			string value2 = _order.First.Value;
			_order.RemoveFirst();
			if (_cache.Remove(value2, out Outline value3))
			{
				value3.Dispose();
			}
		}
		return value;
	}

	private static Outline Build(string text, string fontName, bool bold, float emPx)
	{
		Outline outline = new Outline();
		FontFamily fontFamily;
		try
		{
			fontFamily = new FontFamily(fontName);
		}
		catch
		{
			fontFamily = new FontFamily("Segoe UI");
		}
		FontStyle style = ((bold && fontFamily.IsStyleAvailable(FontStyle.Bold)) ? FontStyle.Bold : FontStyle.Regular);
		using (fontFamily)
		{
			outline.Face.AddString(text, fontFamily, (int)style, emPx, PointF.Empty, StringFormat.GenericTypographic);
		}
		outline.Face.FillMode = FillMode.Winding;
		outline.Face.Flatten(null, 0.45f);
		outline.Bounds = outline.Face.GetBounds();
		if (outline.Bounds.Width <= 0f)
		{
			return outline;
		}
		PointF[] pathPoints = outline.Face.PathPoints;
		byte[] pathTypes = outline.Face.PathTypes;
		List<PointF> list = new List<PointF>();
		List<PointF> list2 = new List<PointF>();
		List<PointF> list3 = new List<PointF>();
		List<bool> list4 = new List<bool>();
		int from = 0;
		for (int i = 0; i < pathPoints.Length; i++)
		{
			if (i != pathPoints.Length - 1 && (pathTypes[i + 1] & 7u) != 0)
			{
				continue;
			}
			List<PointF> list5 = Simplify(pathPoints, from, i);
			from = i + 1;
			if (list5.Count < 3)
			{
				continue;
			}
			for (int j = 0; j < list5.Count; j++)
			{
				PointF pointF = list5[(j + list5.Count - 1) % list5.Count];
				PointF item = list5[j];
				PointF item2 = list5[(j + 1) % list5.Count];
				float num = item2.X - item.X;
				float num2 = item2.Y - item.Y;
				float num3 = MathF.Sqrt(num * num + num2 * num2);
				if (num3 < 0.01f)
				{
					continue;
				}
				PointF item3 = new PointF(num2 / num3, (0f - num) / num3);
				PointF pointF2 = new PointF((item.X + item2.X) / 2f, (item.Y + item2.Y) / 2f);
				bool flag = outline.Face.IsVisible(pointF2.X + item3.X * 0.8f, pointF2.Y + item3.Y * 0.8f);
				bool flag2 = outline.Face.IsVisible(pointF2.X - item3.X * 0.8f, pointF2.Y - item3.Y * 0.8f);
				if (flag != flag2)
				{
					if (flag)
					{
						item3 = new PointF(0f - item3.X, 0f - item3.Y);
					}
					float num4 = item.X - pointF.X;
					float num5 = item.Y - pointF.Y;
					float num6 = MathF.Sqrt(num4 * num4 + num5 * num5);
					bool item4 = num6 > 0.01f && (num4 * num + num5 * num2) / (num6 * num3) < 0.85f;
					list.Add(item);
					list2.Add(item2);
					list3.Add(item3);
					list4.Add(item4);
				}
			}
		}
		outline.A = list.ToArray();
		outline.B = list2.ToArray();
		outline.N = list3.ToArray();
		outline.Crease = list4.ToArray();
		return outline;
	}

	private static List<PointF> Simplify(PointF[] pts, int from, int to)
	{
		List<PointF> list = new List<PointF>();
		for (int i = from; i <= to; i++)
		{
			if (list.Count != 0)
			{
				if (!(Dist(list[list.Count - 1], pts[i]) > 0.3f))
				{
					continue;
				}
			}
			list.Add(pts[i]);
		}
		if (list.Count > 2)
		{
			if (Dist(list[0], list[list.Count - 1]) < 0.3f)
			{
				list.RemoveAt(list.Count - 1);
			}
		}
		bool flag = true;
		while (flag && list.Count > 3)
		{
			flag = false;
			for (int j = 0; j < list.Count; j++)
			{
				if (list.Count <= 3)
				{
					break;
				}
				PointF a = list[(j + list.Count - 1) % list.Count];
				PointF pointF = list[j];
				PointF b = list[(j + 1) % list.Count];
				if (Math.Abs((pointF.X - a.X) * (b.Y - a.Y) - (pointF.Y - a.Y) * (b.X - a.X)) / Math.Max(0.01f, Dist(a, b)) < 0.12f)
				{
					list.RemoveAt(j);
					flag = true;
					j--;
				}
			}
		}
		return list;
	}

	private static float Dist(PointF a, PointF b)
	{
		return MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
	}

	public static void Shafts(Graphics g, Outline o, float x, float y, PointF d, double sweep, Color? flat = null)
	{
		if (flat.HasValue)
		{
			Color valueOrDefault = flat.GetValueOrDefault();
			Block(g, o, x, y, d, valueOrDefault);
		}
		else
		{
			if (o.A.Length == 0)
			{
				return;
			}
			float num = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
			if (num < 1f)
			{
				return;
			}
			PointF pointF = new PointF(d.X / num, d.Y / num);
			List<int> list = new List<int>(o.A.Length / 2 + 4);
			for (int i = 0; i < o.A.Length; i++)
			{
				if (o.N[i].X * pointF.X + o.N[i].Y * pointF.Y > 0.001f)
				{
					list.Add(i);
				}
			}
			float[] depth = new float[o.A.Length];
			foreach (int item in list)
			{
				depth[item] = ((o.A[item].X + o.B[item].X) * pointF.X + (o.A[item].Y + o.B[item].Y) * pointF.Y) * 0.5f;
			}
			list.Sort((int p, int q) => depth[q].CompareTo(depth[p]));
			GraphicsState gstate = g.Save();
			g.TranslateTransform(x - o.Bounds.X, y - o.Bounds.Y);
			PointF[] array = new PointF[4];
			using Pen pen = new Pen(Color.Black, 1.3f);
			foreach (int item2 in list)
			{
				PointF pointF2 = o.A[item2];
				PointF pointF3 = o.B[item2];
				float num2 = pointF3.X - pointF2.X;
				float num3 = pointF3.Y - pointF2.Y;
				float num4 = MathF.Max(0.01f, MathF.Sqrt(num2 * num2 + num3 * num3));
				num2 *= 0.5f / num4;
				num3 *= 0.5f / num4;
				pointF2 = new PointF(pointF2.X - num2, pointF2.Y - num3);
				pointF3 = new PointF(pointF3.X + num2, pointF3.Y + num3);
				float num5 = Math.Max(0f, 0f - o.N[item2].Y);
				float num6 = Math.Max(0f, o.N[item2].X);
				float num7 = num5 * num5 * 0.85f + num6 * 0.15f;
				Color color = Mix(Color.FromArgb(10, 9, 11), Color.FromArgb(78, 74, 80), num7);
				Color color2 = Mix(Color.FromArgb(4, 4, 5), Color.FromArgb(30, 29, 33), num7);
				array[0] = pointF2;
				array[1] = pointF3;
				array[2] = new PointF(pointF3.X + d.X, pointF3.Y + d.Y);
				array[3] = new PointF(pointF2.X + d.X, pointF2.Y + d.Y);
				PointF pointF4 = new PointF((pointF2.X + pointF3.X) / 2f, (pointF2.Y + pointF3.Y) / 2f);
				using (LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new PointF(pointF4.X - pointF.X, pointF4.Y - pointF.Y), new PointF(pointF4.X + d.X + pointF.X, pointF4.Y + d.Y + pointF.Y), color, color2))
				{
					if (sweep >= 0.0 && num7 > 0.05f)
					{
						float num8 = (float)Math.Clamp(sweep, 0.02, 0.98);
						Color color3 = Mix(color, Color.FromArgb(150, 146, 150), num7 * 0.8f);
						linearGradientBrush.InterpolationColors = new ColorBlend
						{
							Colors = new Color[5]
							{
								color,
								Mix(color, color2, Math.Max(0f, num8 - 0.08f)),
								color3,
								Mix(color, color2, Math.Min(1f, num8 + 0.08f)),
								color2
							},
							Positions = new float[5]
							{
								0f,
								Math.Max(0.001f, num8 - 0.08f),
								num8,
								Math.Min(0.999f, num8 + 0.08f),
								1f
							}
						};
					}
					g.FillPolygon(linearGradientBrush, array);
				}
				if (o.Crease[item2])
				{
					PointF pointF5 = o.A[item2];
					pen.Color = Color.FromArgb((int)(45f + 80f * num7), 176, 170, 174);
					g.DrawLine(pen, pointF5.X, pointF5.Y, pointF5.X + d.X * 0.7f, pointF5.Y + d.Y * 0.7f);
				}
			}
			g.Restore(gstate);
		}
	}

	private static void Block(Graphics g, Outline o, float x, float y, PointF d, Color color)
	{
		if (o.A.Length == 0)
		{
			return;
		}
		GraphicsState gstate = g.Save();
		g.TranslateTransform(x - o.Bounds.X, y - o.Bounds.Y);
		using SolidBrush brush = new SolidBrush(color);
		PointF[] array = new PointF[4];
		for (int i = 0; i < o.A.Length; i++)
		{
			if (!(o.N[i].X * d.X + o.N[i].Y * d.Y <= 0f))
			{
				PointF pointF = o.A[i];
				PointF pointF2 = o.B[i];
				float num = pointF2.X - pointF.X;
				float num2 = pointF2.Y - pointF.Y;
				float num3 = MathF.Max(0.01f, MathF.Sqrt(num * num + num2 * num2));
				num *= 0.5f / num3;
				num2 *= 0.5f / num3;
				pointF = new PointF(pointF.X - num, pointF.Y - num2);
				pointF2 = new PointF(pointF2.X + num, pointF2.Y + num2);
				array[0] = pointF;
				array[1] = pointF2;
				array[2] = new PointF(pointF2.X + d.X, pointF2.Y + d.Y);
				array[3] = new PointF(pointF.X + d.X, pointF.Y + d.Y);
				g.FillPolygon(brush, array);
			}
		}
		g.TranslateTransform(d.X, d.Y);
		g.FillPath(brush, o.Face);
		g.Restore(gstate);
	}

	public static void Face(Graphics g, Outline o, float x, float y, Color face)
	{
		GraphicsState gstate = g.Save();
		g.TranslateTransform(x - o.Bounds.X, y - o.Bounds.Y);
		using (SolidBrush brush = new SolidBrush(face))
		{
			g.FillPath(brush, o.Face);
		}
		g.Restore(gstate);
	}

	private static Color Mix(Color a, Color b, float t)
	{
		t = Math.Clamp(t, 0f, 1f);
		return Color.FromArgb(255, (int)((float)(int)a.R + (float)(b.R - a.R) * t), (int)((float)(int)a.G + (float)(b.G - a.G) * t), (int)((float)(int)a.B + (float)(b.B - a.B) * t));
	}

	public void Dispose()
	{
		foreach (Outline value in _cache.Values)
		{
			value.Dispose();
		}
		_cache.Clear();
		_order.Clear();
	}
}
