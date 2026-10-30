using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LcdStudio.Core;

public sealed partial class ScreenRenderer : IDisposable
{
	public const int W = 1920;

	public const int H = 480;

	private static readonly Regex Token = new Regex("\\{([A-Za-z0-9_.]+)(?::([^}]*))?\\}", RegexOptions.Compiled);

	private readonly Sensors _sensors;

	private readonly Dictionary<(string, float, bool), Font> _fonts = new Dictionary<(string, float, bool), Font>();

	private readonly Dictionary<string, Image?> _images = new Dictionary<string, Image>();

	private Bitmap? _stone;

	private Bitmap? _aurora;

	private VideoFrames? _video;

	private readonly Inflated _inflated = new Inflated();

	private readonly Extrude _extrude = new Extrude();

	private Color _stoneColor;

	private Bitmap? _base;

	private string? _baseKey;

	private double _baseTime;

	private Bitmap? _eclipse;

	private const float PlanetX = 1900f;

	private const float PlanetY = 2300f;

	private const float PlanetR = 2380f;

	private readonly Dictionary<(int, int, string), (string Text, double Since)> _stamps = new Dictionary<(int, int, string), (string, double)>();

	private readonly Dictionary<string, (GraphicsPath Path, RectangleF Bounds)> _posterPaths = new Dictionary<string, (GraphicsPath, RectangleF)>();

	private readonly Dictionary<(int, int, string), (string Old, string Now, double Since)> _rolls = new Dictionary<(int, int, string), (string, string, double)>();

	private readonly Dictionary<(string, string, int), (Point[] Cells, int Cols)> _pixelGlyphs = new Dictionary<(string, string, int), (Point[], int)>();

	private readonly Dictionary<(int, int, string), (string Old, string Now, double Since)> _pixelState = new Dictionary<(int, int, string), (string, string, double)>();

	private readonly Dictionary<(string, string, float), (PointF[] Pts, byte[] Types, RectangleF Bounds, float Split)> _stretchGlyphs = new Dictionary<(string, string, float), (PointF[], byte[], RectangleF, float)>();

	private readonly Dictionary<(int, int, string), (double Fraction, double Time)> _stretchState = new Dictionary<(int, int, string), (double, double)>();

	public ScreenRenderer(Sensors sensors)
	{
		_sensors = sensors;
	}

	public string Resolve(string template)
	{
		return Token.Replace(template, delegate(Match m)
		{
			string value = m.Groups[1].Value;
			string text = (m.Groups[2].Success ? m.Groups[2].Value : null);
			if (value == "time")
			{
				return DateTime.Now.ToString(text ?? "HH:mm", CultureInfo.CurrentCulture);
			}
			if (value == "date")
			{
				return DateTime.Now.ToString(text ?? "dddd d MMMM", CultureInfo.CurrentCulture);
			}
			string text2 = _sensors.Text(value);
			if (text2 != null)
			{
				if (!(text == "upper"))
				{
					return text2;
				}
				return text2.ToUpperInvariant();
			}
			float? num = _sensors.Get(value);
			return (!num.HasValue) ? "--" : num.GetValueOrDefault().ToString(text ?? "0", CultureInfo.InvariantCulture);
		});
	}

	public Bitmap Render(ScreenLayout layout, double t)
	{
		Bitmap bitmap = new Bitmap(1920, 480, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		Setup(graphics);
		// relief text reworks the pixels already drawn beneath it, so it needs the frame itself
		_canvas = bitmap;
		_canvasGraphics = graphics;
		bool flag;
		switch (layout.Background)
		{
		case "grid":
		case "waves":
		case "tunnel":
		case "video":
		case "eclipse":
		case "mesh":
		case "hud":
		case "fluted":
		case "papersea":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			if (layout.Background == "image")
			{
				string? image = layout.Image;
				if (image != null && image.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_00ae;
				}
			}
			HashSet<Widget> hashSet = StillWidgets(layout);
			string text = BaseKey(layout, hashSet);
			if (_base == null || text != _baseKey || (layout.Background == "aurora" && Math.Abs(t - _baseTime) >= 1.0 / 6.0))
			{
				if (_base == null)
				{
					_base = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
				}
				using Graphics g = Graphics.FromImage(_base);
				Setup(g);
				Background(g, layout, t);
				foreach (Widget item in hashSet)
				{
					DrawWidget(g, item, t);
				}
				_baseKey = text;
				_baseTime = t;
			}
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.DrawImageUnscaled(_base, 0, 0);
			graphics.CompositingMode = CompositingMode.SourceOver;
			ExtrudeShafts(graphics, layout, t);
			foreach (Widget widget in layout.Widgets)
			{
				if (!hashSet.Contains(widget))
				{
					DrawWidget(graphics, widget, t);
				}
			}
			return bitmap;
		}
		goto IL_00ae;
		IL_00ae:
		Draw(graphics, layout, t);
		return bitmap;
	}

	private static HashSet<Widget> StillWidgets(ScreenLayout l)
	{
		HashSet<Widget> hashSet = new HashSet<Widget>();
		List<RectangleF> list = new List<RectangleF>();
		bool flag = l.Widgets.Any((Widget w) => w.Visible && IsExtruded(w));
		foreach (Widget widget in l.Widgets)
		{
			if (!widget.Visible)
			{
				continue;
			}
			RectangleF r = new RectangleF(widget.X, widget.Y, widget.W, widget.H);
			bool flag2 = !widget.HideWhenMissing;
			if (flag2)
			{
				bool flag3;
				switch (widget.Effect)
				{
				case "extrude":
				case "stretch":
				case "block":
				case "glass":
				case "pixel":
				case "poster":
				case "tag":
				case "type":
				case "pixo":
				case "dots":
				case "status":
				case "pill":
				case "glow":
				case "calendar":
				case "cyber":
				case "chevrons":
				case "mechclock":
				case "slices":
				case "paperstack":
				case "papertag":
				case "relief":
				case "clipping":
				case "presstype":
				case "ticker":
				case "reeded":
				case "reedlines":
				case "reedfigure":
				case "slabs":
				case "sonar":
				case "prismbar":
				case "deck":
				case "instrument":
				case "voidtunnel":
				case "voidpanel":
				case "constellation":
				case "lightbeam":
				case "standby":
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = !flag3;
			}
			bool flag4 = flag2;
			if (flag4)
			{
				WidgetKind kind = widget.Kind;
				bool flag3 = ((kind == WidgetKind.Panel || kind == WidgetKind.Divider) ? true : false);
				int num;
				if (!flag3 && (widget.Kind != 0 || widget.Text.Contains('{') || flag))
				{
					if (widget.Kind == WidgetKind.Image)
					{
						string? image = widget.Image;
						num = ((image != null && !image.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
					}
					else
					{
						num = 0;
					}
				}
				else
				{
					num = 1;
				}
				flag4 = (byte)num != 0;
			}
			if (flag4 && !list.Any((RectangleF m) => m.IntersectsWith(r)))
			{
				hashSet.Add(widget);
			}
			else
			{
				list.Add(r);
			}
		}
		return hashSet;
	}

	private static string BaseKey(ScreenLayout l, HashSet<Widget> still)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(l.Background).Append(l.Color1).Append(l.Color2)
			.Append(l.Accent)
			.Append(l.Image)
			.Append(l.ImageOpacity);
		foreach (Widget item in still)
		{
			stringBuilder.Append('|').Append((int)item.Kind).Append(',')
				.Append(item.X)
				.Append(',')
				.Append(item.Y)
				.Append(',')
				.Append(item.W)
				.Append(',')
				.Append(item.H)
				.Append(item.Text)
				.Append(item.Color)
				.Append(item.Color2)
				.Append(item.Track)
				.Append(item.WarnColor)
				.Append(item.Font)
				.Append(item.Size)
				.Append(item.Bold)
				.Append(item.Align)
				.Append(item.Thickness)
				.Append(item.Radius)
				.Append(item.Glow)
				.Append(item.Image);
		}
		return stringBuilder.ToString();
	}

	private static void Setup(Graphics g)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		g.InterpolationMode = InterpolationMode.HighQualityBilinear;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
	}

	public void Draw(Graphics g, ScreenLayout layout, double t)
	{
		Setup(g);
		Background(g, layout, t);
		ExtrudeShafts(g, layout, t);
		foreach (Widget widget in layout.Widgets)
		{
			DrawWidget(g, widget, t);
		}
	}

	private void DrawWidget(Graphics g, Widget w, double t)
	{
		if (!w.Visible || w.W < 2 || w.H < 2 || (w.HideWhenMissing && Missing(w)))
		{
			return;
		}
		RectangleF r = new RectangleF(w.X, w.Y, w.W, w.H);
		try
		{
			if (DrawPixotype(g, w, r, t))
			{
				return;
			}
		}
		catch (Exception e)
		{
			Log.Write($"widget {w.Kind} failed: {e.Message}");
			return;
		}
		try
		{
			int num;
			switch (w.Kind)
			{
			case WidgetKind.Text:
				num = 0;
				goto IL_0090;
			case WidgetKind.Value:
				num = 7;
				goto IL_0090;
			case WidgetKind.Gauge:
				Gauge(g, w, r, 135f, 270f);
				break;
			case WidgetKind.Ring:
				Gauge(g, w, r, -90f, 360f);
				break;
			case WidgetKind.Bar:
				Bar(g, w, r);
				break;
			case WidgetKind.Graph:
				Graph(g, w, r);
				break;
			case WidgetKind.Panel:
				Panel(g, w, r);
				break;
			case WidgetKind.CoreBars:
				CoreBars(g, w, r);
				break;
			case WidgetKind.Image:
			{
				Image image = GetImage(w.Image);
				if (image != null)
				{
					Imaging.DrawCover(g, image, r, t);
				}
				break;
			}
			case WidgetKind.Divider:
				{
					using Pen pen = new Pen(Imaging.Hex(w.Color, Color.White), Math.Max(1f, w.Thickness));
					g.DrawLine(pen, r.X, r.Y + r.Height / 2f, r.Right, r.Y + r.Height / 2f);
					break;
				}
				IL_0141:
				GlassText(g, w, r, t);
				break;
				IL_0090:
				if (IsExtruded(w))
				{
					goto IL_009f;
				}
				if (num != 0)
				{
					if (num != 7)
					{
						goto IL_009f;
					}
					num = 8;
				}
				else
				{
					num = 1;
				}
				if (w.Effect == "poster")
				{
					goto IL_00ce;
				}
				if (num != 1)
				{
					if (num != 8)
					{
						goto IL_00ce;
					}
					if (w.Effect == "stretch")
					{
						Stretched(g, w, r, t);
						break;
					}
					num = 10;
				}
				else
				{
					if (w.Effect == "tag")
					{
						Tag(g, w, r, t);
						break;
					}
					num = 3;
				}
				if (!(w.Effect == "glass"))
				{
					if (num != 3)
					{
						if (num != 10)
						{
							goto IL_0141;
						}
						num = 11;
					}
					else
					{
						num = 4;
					}
					if (!(w.Effect == "pixel"))
					{
						switch (num)
						{
						case 4:
						{
							if (w.Effect == "type")
							{
								TypedText(g, w, r, t);
								return;
							}
							string effect = w.Effect;
							if ((effect == "inflated" || effect == "vinyl") ? true : false)
							{
								InflatedText(g, w, r, Resolve(w.Text), t);
							}
							else
							{
								Text(g, w, r, Resolve(w.Text));
							}
							return;
						}
						case 11:
						{
							string effect = w.Effect;
							if ((effect == "inflated" || effect == "vinyl") ? true : false)
							{
								InflatedValue(g, w, r, t);
							}
							else
							{
								Value(g, w, r);
							}
							return;
						}
						}
					}
					PixelText(g, w, r, t);
					break;
				}
				goto IL_0141;
				IL_009f:
				ExtrudeFaces(g, w, r, t);
				break;
				IL_00ce:
				PosterText(g, w, r, t);
				break;
			}
		}
		catch (Exception ex)
		{
			Log.Write($"widget {w.Kind} failed: {ex.Message}");
		}
	}

	private bool Missing(Widget w)
	{
		WidgetKind kind = w.Kind;
		if ((uint)(kind - 1) <= 4u)
		{
			if (w.Sensor != "time")
			{
				return !_sensors.Get(w.Sensor).HasValue;
			}
			return false;
		}
		foreach (Match item in Token.Matches(w.Text + (w.Sensor.Contains('{') ? w.Sensor : "")))
		{
			string value = item.Groups[1].Value;
			bool flag = ((value == "time" || value == "date") ? true : false);
			if (!flag && _sensors.Text(value) == null && !_sensors.Get(value).HasValue)
			{
				return true;
			}
		}
		return false;
	}

	private void Background(Graphics g, ScreenLayout l, double t)
	{
		Color color = Imaging.Hex(l.Color1, Color.Black);
		Color color2 = Imaging.Hex(l.Color2, color);
		Color color3 = Imaging.Hex(l.Accent, Color.DeepSkyBlue);
		RectangleF rectangleF = new RectangleF(0f, 0f, 1920f, 480f);
		switch (l.Background)
		{
		case "solid":
			g.Clear(color);
			break;
		case "grid":
		{
			using (LinearGradientBrush brush3 = new LinearGradientBrush(rectangleF, color, color2, 90f))
			{
				g.FillRectangle(brush3, rectangleF);
			}
			float num5 = 249.59999f;
			using (LinearGradientBrush brush4 = new LinearGradientBrush(new RectangleF(810f, num5 - 260f, 300f, 262f), Color.FromArgb(255, 230, 90), Color.FromArgb(255, 60, 160), 90f))
			{
				GraphicsState gstate = g.Save();
				g.SetClip(new RectangleF(0f, 0f, 1920f, num5));
				g.FillEllipse(brush4, 810f, num5 - 150f, 300f, 300f);
				using SolidBrush brush5 = new SolidBrush(color2);
				for (int k = 0; k < 6; k++)
				{
					g.FillRectangle(brush5, 800f, num5 - 70f + (float)(k * 13), 320f, 3 + k);
				}
				g.Restore(gstate);
			}
			using (SolidBrush brush6 = new SolidBrush(Imaging.Mix(color, Color.Black, 0.4)))
			{
				g.FillRectangle(brush6, 0f, num5, 1920f, 480f - num5);
			}
			using Pen pen2 = new Pen(Imaging.WithAlpha(color3, 200), 2f);
			double num6 = t * 0.6 % 1.0;
			for (int m = 0; m < 14; m++)
			{
				double num7 = ((double)m + num6) / 14.0;
				float num8 = num5 + (float)(num7 * num7 * (double)(480f - num5));
				g.DrawLine(pen2, 0f, num8, 1920f, num8);
			}
			for (int n = -24; n <= 24; n++)
			{
				g.DrawLine(pen2, 960f + (float)(n * 22), num5, 960f + (float)(n * 260), 480f);
			}
			break;
		}
		case "waves":
		{
			using (LinearGradientBrush brush8 = new LinearGradientBrush(rectangleF, color, color2, 0f))
			{
				g.FillRectangle(brush8, rectangleF);
			}
			for (int num10 = 0; num10 < 5; num10++)
			{
				List<PointF> list = new List<PointF>();
				float num11 = 480f * (0.45f + (float)num10 * 0.12f);
				for (int num12 = 0; num12 <= 1920; num12 += 24)
				{
					list.Add(new PointF(num12, num11 + (float)(Math.Sin((double)num12 / 140.0 + t * (1.0 + (double)num10 * 0.3) + (double)num10) * 26.0 + Math.Sin((double)num12 / 63.0 - t * 1.7) * 9.0)));
				}
				list.Add(new PointF(1920f, 480f));
				list.Add(new PointF(0f, 480f));
				using GraphicsPath graphicsPath2 = new GraphicsPath();
				graphicsPath2.AddPolygon(list.ToArray());
				using SolidBrush brush9 = new SolidBrush(Imaging.WithAlpha(Imaging.Mix(color3, color2, (double)num10 / 5.0), 70));
				g.FillPath(brush9, graphicsPath2);
			}
			break;
		}
		case "tunnel":
		{
			g.Clear(color);
			for (int i = 0; i < 14; i++)
			{
				double num = ((double)i / 14.0 + t * 0.12) % 1.0;
				float num2 = (float)Math.Pow(num, 2.2);
				RectangleF r = new RectangleF(960f - 1056f * num2, 240f - 360f * num2, 2112f * num2, 720f * num2);
				if (r.Width < 4f)
				{
					continue;
				}
				using Pen pen = new Pen(Imaging.WithAlpha(Imaging.Mix(color2, color3, num), (int)(30.0 + 160.0 * num)), 1.5f + num2 * 5f);
				using GraphicsPath path = Imaging.Round(r, 40f * num2 + 4f);
				g.DrawPath(pen, path);
			}
			break;
		}
		case "aurora":
		{
			if (_aurora == null)
			{
				_aurora = new Bitmap(480, 120, PixelFormat.Format32bppPArgb);
			}
			using (Graphics graphics = Graphics.FromImage(_aurora))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.Clear(color);
				graphics.ScaleTransform(0.25f, 0.25f);
				for (int j = 0; j < 5; j++)
				{
					float num3 = (float)(1920.0 * (0.15 + 0.18 * (double)j) + Math.Sin(t * 0.35 + (double)j * 1.3) * 140.0);
					float num4 = (float)(240.0 + Math.Cos(t * 0.27 + (double)j) * 110.0);
					using GraphicsPath graphicsPath = new GraphicsPath();
					graphicsPath.AddEllipse(num3 - 360f, num4 - 220f, 720f, 440f);
					PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
					pathGradientBrush.CenterColor = Imaging.WithAlpha((j % 2 == 0) ? color3 : color2, 90);
					pathGradientBrush.SurroundColors = new Color[1] { Color.Transparent };
					using PathGradientBrush brush2 = pathGradientBrush;
					graphics.FillPath(brush2, graphicsPath);
				}
			}
			InterpolationMode interpolationMode = g.InterpolationMode;
			g.InterpolationMode = InterpolationMode.Bilinear;
			g.DrawImage(_aurora, new RectangleF(-2f, -2f, 1924f, 484f));
			g.InterpolationMode = interpolationMode;
			break;
		}
		case "carved":
			if (_stone == null || _stoneColor != color)
			{
				_stone?.Dispose();
				_stoneColor = color;
				_stone = new Bitmap(1920, 480, PixelFormat.Format32bppPArgb);
				using Graphics graphics2 = Graphics.FromImage(_stone);
				using (LinearGradientBrush brush7 = new LinearGradientBrush(rectangleF, Imaging.Mix(color, Color.White, 0.1), Imaging.Mix(color, Color.Black, 0.12), 80f))
				{
					graphics2.FillRectangle(brush7, rectangleF);
				}
				Random random = new Random(3);
				using SolidBrush solidBrush = new SolidBrush(Color.FromArgb(16, Color.Black));
				using SolidBrush solidBrush2 = new SolidBrush(Color.FromArgb(16, Color.White));
				for (int num9 = 0; num9 < 6000; num9++)
				{
					graphics2.FillRectangle((random.Next(2) == 0) ? solidBrush : solidBrush2, random.Next(1920), random.Next(480), 2, 2);
				}
			}
			g.DrawImageUnscaled(_stone, 0, 0);
			break;
		case "eclipse":
			Eclipse(g, color, color2, color3, t);
			break;
		case "mesh":
			Mesh(g, color, color3, t);
			break;
		case "warm":
			Warm(g, color, color2, color3);
			break;
		case "hud":
			Hud(g, color, color2, t);
			break;
		case "machined":
			Machined(g, color, color2);
			break;
		case "fluted":
			Fluted(g, t);
			break;
		case "papersea":
			PaperSea(g, color, color2, color3, t);
			break;
		case "newsprint":
			Newsprint(g, color, color2, color3);
			break;
		case "reeded":
			Reeded(g, color, color2);
			break;
		case "spectrum":
			Spectrum(g, color, color2, color3);
			break;
		case "deck":
			Spectrum(g, color, color2, color3, dome: false);
			break;
		case "video":
			g.Clear(color);
			if (_video == null)
			{
				_video = new VideoFrames();
			}
			_video.Draw(g, l.Image, l.VideoFps, t, rectangleF, l.ImageOpacity);
			break;
		case "image":
		{
			g.Clear(color);
			Image image = GetImage(l.Image);
			if (image != null)
			{
				Imaging.DrawCover(g, image, rectangleF, t, l.ImageOpacity);
			}
			break;
		}
		default:
		{
			using LinearGradientBrush brush = new LinearGradientBrush(rectangleF, color, color2, 20f);
			g.FillRectangle(brush, rectangleF);
			break;
		}
		}
	}

	private void Eclipse(Graphics g, Color space, Color air, Color sun, double t)
	{
		if (_eclipse == null)
		{
			_eclipse = new Bitmap(480, 120, PixelFormat.Format32bppPArgb);
		}
		double num = 242.0 + Math.Sin(t * 0.06) * 2.5;
		float num2 = (float)Math.Clamp(0.8 + 0.15 * Math.Sin(t * 0.45) + 0.05 * Math.Sin(t * 1.7), 0.0, 1.0);
		PointF pointF = RimPoint(num);
		using (Graphics graphics = Graphics.FromImage(_eclipse))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.Clear(space);
			graphics.ScaleTransform(0.25f, 0.25f);
			RectangleF rect = new RectangleF(-480f, -80f, 4760f, 4760f);
			using (SolidBrush brush = new SolidBrush(Imaging.Mix(space, air, 0.05)))
			{
				graphics.FillEllipse(brush, rect);
			}
			for (int i = 0; i < 22; i++)
			{
				float num3 = 6 + i * 11;
				using Pen pen = new Pen(Color.FromArgb((int)(20.0 * (1.0 - (double)i / 22.0)), air), 14f);
				graphics.DrawEllipse(pen, RectangleF.Inflate(rect, 0f - num3, 0f - num3));
			}
			Blob(graphics, pointF.X + 190f, pointF.Y + 210f, 760f * num2, Color.FromArgb((int)(150f * num2), air));
			Blob(graphics, pointF.X - 90f, pointF.Y - 70f, 480f * num2, Color.FromArgb((int)(170f * num2), sun));
			Blob(graphics, pointF.X + 14f, pointF.Y + 18f, 170f * num2, Color.FromArgb((int)(230f * num2), Imaging.Mix(sun, Color.White, 0.7)));
		}
		InterpolationMode interpolationMode = g.InterpolationMode;
		g.InterpolationMode = InterpolationMode.Bilinear;
		g.DrawImage(_eclipse, new RectangleF(-2f, -2f, 1924f, 484f));
		g.InterpolationMode = interpolationMode;
		Random random = new Random(11);
		for (int j = 0; j < 70; j++)
		{
			float num4 = random.Next(1920);
			float num5 = random.Next(480);
			double num6 = random.NextDouble();
			double num7 = 0.55 + 0.45 * Math.Sin(t * (0.6 + random.NextDouble() * 1.4) + (double)j);
			if (!(Math.Pow(num4 - 1900f, 2.0) + Math.Pow(num5 - 2300f, 2.0) < 5687057.6))
			{
				using SolidBrush brush2 = new SolidBrush(Color.FromArgb((int)(40.0 + 150.0 * num6 * num7), 220, 230, 255));
				float num8 = ((num6 > 0.85) ? 2.2f : 1.4f);
				g.FillEllipse(brush2, num4, num5, num8, num8);
			}
		}
		RectangleF rect2 = new RectangleF(-480f, -80f, 4760f, 4760f);
		using (Pen pen2 = new Pen(Color.FromArgb(120, Imaging.Mix(air, Color.White, 0.55)), 1.6f))
		{
			g.DrawArc(pen2, rect2, 222f, 50f);
		}
		for (int k = 0; k < 4; k++)
		{
			using Pen pen3 = new Pen(Color.FromArgb((int)((float)(60 + k * 45) * num2), Imaging.Mix(sun, Color.White, 0.5 + (double)k * 0.12)), 2.4f - (float)k * 0.4f);
			g.DrawArc(pen3, rect2, (float)num - 9f + (float)k * 2.6f, 18f - (float)k * 5.2f);
		}
		double num9 = 224.0 + t * 3.2 % 60.0;
		for (int l = 0; l < 3; l++)
		{
			using Pen pen4 = new Pen(Color.FromArgb(70 + l * 60, 235, 240, 255), 2.6f - (float)l * 0.6f);
			g.DrawArc(pen4, rect2, (float)num9 - 2.4f + (float)l * 0.8f, 4.8f - (float)l * 1.6f);
		}
	}

	private static PointF RimPoint(double deg)
	{
		double num = deg * Math.PI / 180.0;
		return new PointF((float)(1900.0 + Math.Cos(num) * 2380.0), (float)(2300.0 + Math.Sin(num) * 2380.0));
	}

	private static void Blob(Graphics g, float cx, float cy, float radius, Color c)
	{
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddEllipse(cx - radius, cy - radius, radius * 2f, radius * 2f);
		PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
		pathGradientBrush.CenterColor = c;
		pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(0, c) };
		using PathGradientBrush pathGradientBrush2 = pathGradientBrush;
		Blend blend = new Blend(9);
		for (int i = 0; i < 9; i++)
		{
			float num = (float)i / 8f;
			blend.Positions[i] = num;
			blend.Factors[i] = MathF.Pow(num, 2.6f);
		}
		pathGradientBrush2.Blend = blend;
		g.FillPath(pathGradientBrush2, graphicsPath);
	}

	private Font GetFont(Widget w, float size)
	{
		(string, float, bool) key = (w.Font, (float)Math.Round(size, 1), w.Bold);
		if (!_fonts.TryGetValue(key, out Font value))
		{
			try
			{
				value = new Font(w.Font, Math.Max(4f, size), w.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
			}
			catch
			{
				value = new Font("Segoe UI", Math.Max(4f, size), GraphicsUnit.Pixel);
			}
			_fonts[key] = value;
		}
		return value;
	}

	private static StringAlignment Align(string a)
	{
		if (!(a == "center"))
		{
			if (a == "right")
			{
				return StringAlignment.Far;
			}
			return StringAlignment.Near;
		}
		return StringAlignment.Center;
	}

	private void Text(Graphics g, Widget w, RectangleF r, string text, Color? color = null, float? size = null)
	{
		Color color2 = color ?? Imaging.Hex(w.Color, Color.White);
		using StringFormat format = new StringFormat
		{
			Alignment = Align(w.Align),
			LineAlignment = StringAlignment.Center,
			Trimming = StringTrimming.None,
			FormatFlags = (StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
		};
		Font font = GetFont(w, size ?? w.Size);
		if (w.Glow)
		{
			using SolidBrush brush = new SolidBrush(Imaging.WithAlpha(Imaging.Hex(w.Color2, color2), 60));
			for (int i = 1; i <= 3; i++)
			{
				(int, int)[] array = new(int, int)[4]
				{
					(-i * 2, 0),
					(i * 2, 0),
					(0, -i * 2),
					(0, i * 2)
				};
				for (int j = 0; j < array.Length; j++)
				{
					var (num, num2) = array[j];
					g.DrawString(text, font, brush, new RectangleF(r.X + (float)num, r.Y + (float)num2, r.Width, r.Height), format);
				}
			}
		}
		using SolidBrush brush2 = new SolidBrush(color2);
		g.DrawString(text, font, brush2, r, format);
	}

	private string Formatted(Widget w)
	{
		if (w.Sensor == "time")
		{
			return DateTime.Now.ToString((string.IsNullOrEmpty(w.Format) || w.Format == "0") ? "HH:mm" : w.Format);
		}
		float? num = _sensors.Get(w.Sensor);
		if (!num.HasValue)
		{
			return "--";
		}
		return num.GetValueOrDefault().ToString(w.Format, CultureInfo.InvariantCulture);
	}

	private Color ValueColor(Widget w)
	{
		Color result = Imaging.Hex(w.Color, Color.White);
		if (!(w.WarnAt > 0f) || !(_sensors.Get(w.Sensor) >= w.WarnAt))
		{
			return result;
		}
		return Imaging.Hex(w.WarnColor, Color.Red);
	}

	private double Fraction(Widget w)
	{
		float? num = _sensors.Get(w.Sensor);
		if (num.HasValue)
		{
			float valueOrDefault = num.GetValueOrDefault();
			return Math.Clamp((double)(valueOrDefault - w.Min) / Math.Max(0.0001, w.Max - w.Min), 0.0, 1.0);
		}
		return 0.0;
	}

	private static float FloatOffset(Widget w, double t)
	{
		if (!w.Float)
		{
			return 0f;
		}
		return (float)(Math.Sin(t * 1.1 + (double)w.X * 0.0045 + (double)w.Y * 0.01) * (double)w.Size * 0.03);
	}

	private static string Material(Widget w)
	{
		if (!(w.Effect == "vinyl"))
		{
			return "balloon";
		}
		return "vinyl";
	}

	private void InflatedText(Graphics g, Widget w, RectangleF r, string text, double t)
	{
		InflatedGlyphs inflatedGlyphs = _inflated.Get(text, w.Font, w.Bold, w.Size, Imaging.Hex(w.Color, Color.Gold), Material(w), Resolve(w.Print));
		float num = Align(w.Align) switch
		{
			StringAlignment.Center => r.X + (r.Width - inflatedGlyphs.Glyphs.Width) / 2f, 
			StringAlignment.Far => r.Right - inflatedGlyphs.Glyphs.Width, 
			_ => r.X, 
		};
		float num2 = r.Y + (r.Height - inflatedGlyphs.Glyphs.Height) / 2f + FloatOffset(w, t);
		g.DrawImage(inflatedGlyphs.Bitmap, num - inflatedGlyphs.Glyphs.X, num2 - inflatedGlyphs.Glyphs.Y);
	}

	private void InflatedValue(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = ((w.Sensor == "time") ? "" : ((w.Text.Length > 0) ? w.Text : Sensors.Def(w.Sensor).Unit));
		Color color = ValueColor(w);
		InflatedGlyphs inflatedGlyphs = _inflated.Get(Formatted(w), w.Font, w.Bold, w.Size, color, Material(w), Resolve(w.Print).Replace("\\n", "\n"));
		InflatedGlyphs inflatedGlyphs2 = ((text.Length > 0) ? _inflated.Get(text, w.Font, w.Bold, w.Size * 0.42f, color, Material(w)) : null);
		float num = w.Size * 0.05f;
		float num2 = inflatedGlyphs.Glyphs.Width + ((inflatedGlyphs2 != null) ? (num + inflatedGlyphs2.Glyphs.Width) : 0f);
		float num3 = Align(w.Align) switch
		{
			StringAlignment.Center => r.X + (r.Width - num2) / 2f, 
			StringAlignment.Far => r.Right - num2, 
			_ => r.X, 
		};
		float num4 = r.Y + (r.Height - inflatedGlyphs.Glyphs.Height) / 2f + FloatOffset(w, t);
		g.DrawImage(inflatedGlyphs.Bitmap, num3 - inflatedGlyphs.Glyphs.X, num4 - inflatedGlyphs.Glyphs.Y);
		if (inflatedGlyphs2 != null)
		{
			float num5 = (text.StartsWith("°") ? num4 : (num4 + inflatedGlyphs.Glyphs.Height - inflatedGlyphs2.Glyphs.Height));
			g.DrawImage(inflatedGlyphs2.Bitmap, num3 + inflatedGlyphs.Glyphs.Width + num - inflatedGlyphs2.Glyphs.X, num5 - inflatedGlyphs2.Glyphs.Y);
		}
	}

	private List<(Extrude.Outline O, float X, float Y)> ExtrudePieces(Widget w)
	{
		RectangleF rectangleF = new RectangleF(w.X, w.Y, w.W, w.H);
		List<(Extrude.Outline, float, float)> list = new List<(Extrude.Outline, float, float)>();
		if (w.Kind == WidgetKind.Text)
		{
			Extrude.Outline outline = _extrude.Get(Resolve(w.Text), w.Font, w.Bold, w.Size);
			list.Add((outline, Align(w.Align) switch
			{
				StringAlignment.Center => rectangleF.X + (rectangleF.Width - outline.Bounds.Width) / 2f, 
				StringAlignment.Far => rectangleF.Right - outline.Bounds.Width, 
				_ => rectangleF.X, 
			}, rectangleF.Y + (rectangleF.Height - outline.Bounds.Height) / 2f));
			return list;
		}
		string text = ((w.Sensor == "time") ? "" : ((w.Text.Length > 0) ? w.Text : Sensors.Def(w.Sensor).Unit));
		Extrude.Outline outline2 = _extrude.Get(Formatted(w), w.Font, w.Bold, w.Size);
		Extrude.Outline outline3 = ((text.Length > 0) ? _extrude.Get(text, w.Font, w.Bold, w.Size * 0.4f) : null);
		float num = w.Size * 0.06f;
		float num2 = outline2.Bounds.Width + ((outline3 != null) ? (num + outline3.Bounds.Width) : 0f);
		float num3 = Align(w.Align) switch
		{
			StringAlignment.Center => rectangleF.X + (rectangleF.Width - num2) / 2f, 
			StringAlignment.Far => rectangleF.Right - num2, 
			_ => rectangleF.X, 
		};
		float item = rectangleF.Y + (rectangleF.Height - outline2.Bounds.Height) / 2f;
		list.Add((outline2, num3, item));
		if (outline3 != null)
		{
			list.Add((outline3, num3 + outline2.Bounds.Width + num, item));
		}
		return list;
	}

	private PointF ShaftVector(Widget w, double t)
	{
		if (w.Effect == "block")
		{
			double num = (135.0 + (w.Float ? (Math.Sin(t * 0.35 + (double)w.X * 0.002) * 7.0) : 0.0)) * Math.PI / 180.0;
			double num2 = (double)w.Size * 0.12 * Stamp(w, t);
			return new PointF((float)(Math.Cos(num) * num2), (float)(Math.Sin(num) * num2));
		}
		double num3 = -420.0;
		double num4 = -2300.0;
		if (w.Float)
		{
			num3 += Math.Sin(t * 0.21) * 520.0;
			num4 += Math.Cos(t * 0.17) * 260.0;
		}
		double num5 = (double)w.X + (double)w.W / 2.0;
		double num6 = (double)w.Y + (double)w.H / 2.0;
		double num7 = num3 - num5;
		double num8 = num4 - num6;
		double num9 = Math.Sqrt(num7 * num7 + num8 * num8);
		double num10 = (double)w.Size * 3.6 * (w.Float ? (1.0 + 0.05 * Math.Sin(t * 0.6 + (double)w.X * 0.004)) : 1.0);
		return new PointF((float)(num7 / num9 * num10), (float)(num8 / num9 * num10));
	}

	private static bool IsExtruded(Widget w)
	{
		string effect = w.Effect;
		if (effect == "extrude" || effect == "block")
		{
			return true;
		}
		return false;
	}

	private double Stamp(Widget w, double t)
	{
		string text = ((w.Kind == WidgetKind.Value) ? Formatted(w) : Resolve(w.Text));
		(int, int, string) key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_stamps.TryGetValue(key, out (string, double) value))
		{
			value = (_stamps[key] = (text, -10.0));
		}
		else if (value.Item1 != text)
		{
			value = (_stamps[key] = (text, t));
		}
		double num = t - value.Item2;
		if (!(num < 0.0) && !(num > 3.0))
		{
			return 1.0 + 1.1 * Math.Exp((0.0 - num) * 4.5) * Math.Cos(num * 11.0);
		}
		return 1.0;
	}

	private void ExtrudeShafts(Graphics g, ScreenLayout l, double t)
	{
		foreach (Widget widget in l.Widgets)
		{
			bool flag = !widget.Visible || !IsExtruded(widget);
			if (!flag)
			{
				WidgetKind kind = widget.Kind;
				bool flag2 = (uint)kind <= 1u;
				flag = !flag2;
			}
			if (flag || (widget.HideWhenMissing && Missing(widget)))
			{
				continue;
			}
			try
			{
				PointF d = ShaftVector(widget, t);
				double sweep = (widget.Float ? (1.0 - (t * 0.16 + (double)widget.X * 0.0003) % 1.0) : (-1.0));
				Color? flat = ((widget.Effect == "block") ? new Color?(Imaging.Hex(widget.Color2, Color.Black)) : null);
				PointF pointF = FaceLift(widget, d);
				foreach (var (o, num, num2) in ExtrudePieces(widget))
				{
					Extrude.Shafts(g, o, num + pointF.X, num2 + pointF.Y, d, sweep, flat);
				}
			}
			catch (Exception ex)
			{
				Log.Write("extrude failed: " + ex.Message);
			}
		}
	}

	private void ExtrudeFaces(Graphics g, Widget w, RectangleF r, double t)
	{
		Color face = ((w.Kind == WidgetKind.Value) ? ValueColor(w) : Imaging.Hex(w.Color, Color.Coral));
		PointF pointF = FaceLift(w, ShaftVector(w, t));
		foreach (var (o, num, num2) in ExtrudePieces(w))
		{
			Extrude.Face(g, o, num + pointF.X, num2 + pointF.Y, face);
		}
	}

	private static PointF FaceLift(Widget w, PointF d)
	{
		if (w.Effect != "block")
		{
			return PointF.Empty;
		}
		float num = w.Size * 0.12f;
		float num2 = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
		if (num2 <= num + 0.5f)
		{
			return PointF.Empty;
		}
		float num3 = (0f - (num2 - num)) / num2 * 0.5f;
		return new PointF(d.X * num3, d.Y * num3);
	}

	private (GraphicsPath Path, RectangleF Bounds) PosterPath(string text, string font, bool bold)
	{
		string key = text + "\u0001" + font + "\u0001" + bold;
		if (_posterPaths.TryGetValue(key, out (GraphicsPath, RectangleF) value))
		{
			return value;
		}
		if (_posterPaths.Count > 96)
		{
			foreach (var value2 in _posterPaths.Values)
			{
				value2.Path.Dispose();
			}
			_posterPaths.Clear();
		}
		GraphicsPath graphicsPath = new GraphicsPath
		{
			FillMode = FillMode.Winding
		};
		FontFamily fontFamily;
		try
		{
			fontFamily = new FontFamily(font);
		}
		catch
		{
			fontFamily = new FontFamily("Segoe UI");
		}
		using (fontFamily)
		{
			FontStyle style = ((bold && fontFamily.IsStyleAvailable(FontStyle.Bold)) ? FontStyle.Bold : FontStyle.Regular);
			if (!fontFamily.IsStyleAvailable(style))
			{
				style = FontStyle.Regular;
			}
			graphicsPath.AddString(text, fontFamily, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
		}
		return _posterPaths[key] = (graphicsPath, graphicsPath.GetBounds());
	}

	private void PosterText(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = ((w.Kind == WidgetKind.Value) ? Formatted(w) : Resolve(w.Text));
		(int, int, string) key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_rolls.TryGetValue(key, out (string, string, double) value))
		{
			value = (text, text, -10.0);
		}
		if (value.Item2 != text)
		{
			value = (value.Item2, text, t);
		}
		_rolls[key] = value;
		double num = Math.Clamp((t - value.Item3) / 0.7, 0.0, 1.0);
		num = 1.0 - Math.Pow(1.0 - num, 3.0);
		float num2 = (w.Float ? ((float)((1.0 - Math.Cos(t * 0.45 + (double)w.X * 0.0031)) * (double)r.Height * 0.03)) : 0f);
		GraphicsState gstate = g.Save();
		g.SetClip(r, CombineMode.Intersect);
		Color color = ((w.Kind == WidgetKind.Value) ? ValueColor(w) : Imaging.Hex(w.Color, Color.Black));
		if (num < 1.0)
		{
			DrawPoster(g, w, r, value.Item1, num2 - (float)(num * (double)r.Height * 1.08), color);
		}
		DrawPoster(g, w, r, text, num2 + (float)((1.0 - num) * (double)r.Height * 1.08), color);
		g.Restore(gstate);
	}

	private void DrawPoster(Graphics g, Widget w, RectangleF r, string text, float dy, Color color)
	{
		var (path, rectangleF) = PosterPath(text, w.Font, w.Bold);
		if (!(rectangleF.Width < 1f) && !(rectangleF.Height < 1f))
		{
			float num = r.Height / rectangleF.Height;
			float num2 = Math.Min(num * w.Size / 100f, r.Width / rectangleF.Width);
			float num3 = rectangleF.Width * num2;
			float dx = Align(w.Align) switch
			{
				StringAlignment.Center => r.X + (r.Width - num3) / 2f, 
				StringAlignment.Far => r.Right - num3, 
				_ => r.X, 
			};
			GraphicsState gstate = g.Save();
			g.TranslateTransform(dx, r.Y + dy);
			g.ScaleTransform(num2, num);
			g.TranslateTransform(0f - rectangleF.X, 0f - rectangleF.Y);
			using (SolidBrush brush = new SolidBrush(color))
			{
				g.FillPath(brush, path);
			}
			g.Restore(gstate);
		}
	}

	private (Point[] Cells, int Cols) PixelCells(string text, string font, int rows)
	{
		(string, string, int) key = (text, font, rows);
		if (_pixelGlyphs.TryGetValue(key, out (Point[], int) value))
		{
			return value;
		}
		if (_pixelGlyphs.Count > 160)
		{
			_pixelGlyphs.Clear();
		}
		(GraphicsPath Path, RectangleF Bounds) tuple = PosterPath(text, font, bold: true);
		GraphicsPath item = tuple.Path;
		RectangleF item2 = tuple.Bounds;
		List<Point> list = new List<Point>();
		int num = 0;
		if (item2.Height >= 1f)
		{
			float num2 = (float)rows / item2.Height;
			float num3 = num2 * 0.74f;
			num = Math.Max(1, (int)Math.Ceiling(item2.Width * num3));
			using Bitmap bitmap = new Bitmap(num * 4, rows * 4, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.ScaleTransform(num3 * 4f, num2 * 4f);
				graphics.TranslateTransform(0f - item2.X, 0f - item2.Y);
				graphics.FillPath(Brushes.White, item);
			}
			for (int i = 0; i < rows; i++)
			{
				for (int j = 0; j < num; j++)
				{
					int num4 = 0;
					for (int k = 0; k < 4; k++)
					{
						for (int l = 0; l < 4; l++)
						{
							num4 += bitmap.GetPixel(j * 4 + l, i * 4 + k).A;
						}
					}
					if ((double)num4 > 1713.6)
					{
						list.Add(new Point(j, i));
					}
				}
			}
		}
		return _pixelGlyphs[key] = (list.ToArray(), num);
	}

	private static double Hash01(int i)
	{
		int num = i * -1640531535;
		int num2 = (num ^ (num >>> 13)) * 1540483477;
		return (double)(((uint)num2 ^ ((uint)num2 >> 15)) & 0xFFFFFFu) / 16777215.0;
	}

	private static double ShowStart(Widget w, double t)
	{
		double num = ((double)w.X * 0.0021 + (double)w.Y * 0.003) % 30.0;
		return Math.Floor((t - num) / 30.0) * 30.0 + num;
	}

	private void PixelText(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = ((w.Kind == WidgetKind.Value) ? Formatted(w) : Resolve(w.Text));
		int num = ((w.Thickness >= 3f) ? ((int)w.Thickness) : 10);
		float num2 = Math.Max(2f, w.Size);
		(int, int, string) key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_pixelState.TryGetValue(key, out (string, string, double) value))
		{
			value = ("", text, ShowStart(w, t));
		}
		if (value.Item2 != text)
		{
			value = (value.Item2, text, t);
		}
		_pixelState[key] = value;
		double num3 = Math.Max(value.Item3, ShowStart(w, t));
		bool flag = value.Item3 >= ShowStart(w, t) && value.Item1.Length > 0;
		Color color = ((w.Kind == WidgetKind.Value) ? ValueColor(w) : Imaging.Hex(w.Color, Color.White));
		(Point[] Cells, int Cols) tuple = PixelCells(text, w.Font, num);
		Point[] item = tuple.Cells;
		float num4 = (float)tuple.Cols * num2;
		float num5 = Align(w.Align) switch
		{
			StringAlignment.Center => r.X + (r.Width - num4) / 2f, 
			StringAlignment.Far => r.Right - num4, 
			_ => r.X, 
		};
		float num6 = r.Bottom - (float)num * num2;
		double num7 = t - num3;
		int num8 = w.X * 31 + w.Y * 17 + text.Length;
		if (flag && num7 < 0.6)
		{
			(Point[] Cells, int Cols) tuple2 = PixelCells(value.Item1, w.Font, num);
			Point[] item2 = tuple2.Cells;
			int item3 = tuple2.Cols;
			float num9 = Align(w.Align) switch
			{
				StringAlignment.Center => r.X + (r.Width - (float)item3 * num2) / 2f, 
				StringAlignment.Far => r.Right - (float)item3 * num2, 
				_ => r.X, 
			};
			double num10 = Math.Clamp(num7 / 0.6, 0.0, 1.0);
			for (int i = 0; i < item2.Length; i++)
			{
				double num11 = Hash01(num8 + i * 7) * Math.PI * 2.0;
				double num12 = (120.0 + 420.0 * Hash01(num8 + i * 13)) * num10 * num10;
				float x = num9 + (float)item2[i].X * num2 + (float)(Math.Cos(num11) * num12);
				float y = num6 + (float)item2[i].Y * num2 + (float)(Math.Sin(num11) * num12 * 0.6);
				using SolidBrush brush = new SolidBrush(Color.FromArgb((int)(255.0 * (1.0 - num10)), color));
				g.FillRectangle(brush, x, y, num2, num2);
			}
		}
		using SolidBrush brush2 = new SolidBrush(color);
		for (int j = 0; j < item.Length; j++)
		{
			double num13 = Hash01(num8 + j * 3) * 0.35;
			double num14 = Math.Clamp((num7 - (flag ? 0.15 : 0.0) - num13) / 0.8, 0.0, 1.0);
			double num15 = 1.0 - Math.Pow(1.0 - num14, 3.0);
			float num16 = num5 + (float)item[j].X * num2;
			float num17 = num6 + (float)item[j].Y * num2;
			if (num15 >= 1.0)
			{
				g.FillRectangle(brush2, num16, num17, num2, num2);
				continue;
			}
			double num18 = Hash01(num8 + j * 11) * Math.PI * 2.0;
			double num19 = 160.0 + 520.0 * Hash01(num8 + j * 5);
			float num20 = num16 + (float)(Math.Cos(num18) * num19);
			float num21 = num17 + (float)(Math.Sin(num18) * num19 * 0.55);
			float num22 = num20 + (num16 - num20) * (float)num15;
			float num23 = num21 + (num17 - num21) * (float)num15;
			float num24 = num2 * (float)(1.0 + 1.6 * (1.0 - num15) * Hash01(num8 + j * 19));
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb((int)(255.0 * Math.Clamp(0.35 + 0.65 * num15, 0.0, 1.0) * Math.Clamp(num14 * 4.0, 0.0, 1.0)), color));
			g.FillRectangle(brush3, num22 - (num24 - num2) / 2f, num23 - (num24 - num2) / 2f, num24, num24);
		}
		string text2 = ((w.Kind != WidgetKind.Value || !(w.Sensor != "time")) ? "" : ((w.Text.Length > 0) ? w.Text : Sensors.Def(w.Sensor).Unit));
		if (text2.Length > 0)
		{
			int num25 = Math.Clamp((int)((num7 - 0.9) * 14.0), 0, text2.Length);
			if (num25 > 0)
			{
				Font font = GetFont(w, num2 * (float)num * 0.3f);
				g.DrawString(text2.Substring(0, num25), font, brush2, num5 + num4 + num2 * 0.6f, num6 - num2 * 0.4f, StringFormat.GenericTypographic);
			}
		}
	}

	private void TypedText(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = Resolve(w.Text);
		double num = t - ShowStart(w, t) - 0.7;
		int length = Math.Clamp((int)(num * 22.0), 0, text.Length);
		Color color = Imaging.Hex(w.Color, Color.White);
		Font font = GetFont(w, w.Size);
		using SolidBrush brush = new SolidBrush(color);
		string text2 = text.Substring(0, length);
		g.DrawString(text2, font, brush, r.X, r.Y + (r.Height - w.Size * 1.25f) / 2f, StringFormat.GenericTypographic);
		if (num > 0.0 && num < (double)text.Length / 22.0 + 1.2 && (int)(t * 4.0) % 2 == 0)
		{
			float num2 = ((text2.Length == 0) ? 0f : g.MeasureString(text2, font, PointF.Empty, StringFormat.GenericTypographic).Width);
			g.FillRectangle(brush, r.X + num2 + 3f, r.Y + (r.Height - w.Size) / 2f, w.Size * 0.5f, w.Size);
		}
	}

	private void GlassText(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = ((w.Kind == WidgetKind.Value) ? Formatted(w) : Resolve(w.Text));
		string unit = ((w.Kind != WidgetKind.Value || !(w.Sensor != "time")) ? "" : ((w.Text.Length > 0) ? w.Text : Sensors.Def(w.Sensor).Unit));
		(int, int, string) key = (w.X, w.Y, w.Sensor + w.Text);
		if (!_rolls.TryGetValue(key, out (string, string, double) value))
		{
			value = (text, text, -10.0);
		}
		if (value.Item2 != text)
		{
			value = (value.Item2, text, t);
		}
		_rolls[key] = value;
		double num = Math.Clamp((t - value.Item3) / 0.6, 0.0, 1.0);
		num = 1.0 - Math.Pow(1.0 - num, 3.0);
		GraphicsState gstate = g.Save();
		g.SetClip(r, CombineMode.Intersect);
		if (num < 1.0)
		{
			DrawGlass(g, w, r, value.Item1, unit, 0f - (float)(num * (double)r.Height * 0.9), t);
		}
		DrawGlass(g, w, r, text, unit, (float)((1.0 - num) * (double)r.Height * 0.9), t);
		g.Restore(gstate);
	}

	private void DrawGlass(Graphics g, Widget w, RectangleF r, string text, string unit, float dy, double t)
	{
		var (graphicsPath, rectangleF) = PosterPath(text, w.Font, w.Bold);
		if (rectangleF.Width < 1f)
		{
			return;
		}
		float num = w.Size / 100f;
		float num2 = num * 0.34f;
		float num3 = w.Size * 0.05f;
		(GraphicsPath, RectangleF)? tuple2 = ((unit.Length > 0) ? new(GraphicsPath, RectangleF)?(PosterPath(unit, "Bahnschrift Light", bold: false)) : null);
		float num4 = rectangleF.Width * num + ((!tuple2.HasValue) ? 0f : (num3 + tuple2.GetValueOrDefault().Item2.Width * num2));
		float num5 = Align(w.Align) switch
		{
			StringAlignment.Center => r.X + (r.Width - num4) / 2f, 
			StringAlignment.Far => r.Right - num4, 
			_ => r.X, 
		};
		float num6 = r.Bottom - rectangleF.Height * num + dy;
		using GraphicsPath graphicsPath3 = new GraphicsPath
		{
			FillMode = FillMode.Winding
		};
		using (GraphicsPath graphicsPath2 = (GraphicsPath)graphicsPath.Clone())
		{
			using Matrix matrix = new Matrix();
			matrix.Translate(num5, num6);
			matrix.Scale(num, num);
			matrix.Translate(0f - rectangleF.X, 0f - rectangleF.Y);
			graphicsPath2.Transform(matrix);
			graphicsPath3.AddPath(graphicsPath2, connect: false);
		}
		if (tuple2.HasValue)
		{
			(GraphicsPath, RectangleF) valueOrDefault = tuple2.GetValueOrDefault();
			using GraphicsPath graphicsPath4 = (GraphicsPath)valueOrDefault.Item1.Clone();
			using Matrix matrix2 = new Matrix();
			matrix2.Translate(num5 + rectangleF.Width * num + num3, num6 + w.Size * 0.02f);
			matrix2.Scale(num2, num2);
			matrix2.Translate(0f - valueOrDefault.Item2.X, 0f - valueOrDefault.Item2.Y);
			graphicsPath4.Transform(matrix2);
			graphicsPath3.AddPath(graphicsPath4, connect: false);
		}
		using (GraphicsPath graphicsPath5 = (GraphicsPath)graphicsPath3.Clone())
		{
			using Matrix matrix3 = new Matrix();
			matrix3.Translate(0f, w.Size * 0.035f);
			graphicsPath5.Transform(matrix3);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
			g.FillPath(brush, graphicsPath5);
		}
		if (w.WarnAt > 0f && _sensors.Get(w.Sensor) >= w.WarnAt)
		{
			using (SolidBrush brush2 = new SolidBrush(Imaging.Hex(w.WarnColor, Color.Red)))
			{
				g.FillPath(brush2, graphicsPath3);
				return;
			}
		}
		float num7 = Math.Max(400f, w.Size * 4f);
		using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(0f, 0f, num7, num7), Color.White, Color.White, 18f)
		{
			WrapMode = WrapMode.TileFlipX
		};
		Color color = Imaging.Hex(w.Color, Color.White);
		linearGradientBrush.InterpolationColors = new ColorBlend
		{
			Colors = new Color[5]
			{
				color,
				Imaging.Mix(color, Color.FromArgb(120, 170, 255), 0.55),
				Imaging.Mix(color, Color.FromArgb(255, 110, 210), 0.45),
				Imaging.Mix(color, Color.FromArgb(110, 250, 255), 0.5),
				color
			},
			Positions = new float[5] { 0f, 0.3f, 0.55f, 0.8f, 1f }
		};
		linearGradientBrush.TranslateTransform((float)(t * 70.0 % (double)(num7 * 2f)) + (float)w.X * 0.7f, 0f);
		g.FillPath(linearGradientBrush, graphicsPath3);
	}

	private (PointF[] Pts, byte[] Types, RectangleF Bounds, float Split) StretchGlyph(string ch, string font, float height, float squeeze)
	{
		(string, string, float) key = (ch, font, MathF.Round(height * 10f + squeeze));
		if (_stretchGlyphs.TryGetValue(key, out (PointF[], byte[], RectangleF, float) value))
		{
			return value;
		}
		if (_stretchGlyphs.Count > 200)
		{
			_stretchGlyphs.Clear();
		}
		using GraphicsPath graphicsPath = new GraphicsPath
		{
			FillMode = FillMode.Winding
		};
		FontFamily fontFamily;
		try
		{
			fontFamily = new FontFamily(font);
		}
		catch
		{
			fontFamily = new FontFamily("Segoe UI");
		}
		using (fontFamily)
		{
			graphicsPath.AddString(ch, fontFamily, (!fontFamily.IsStyleAvailable(FontStyle.Regular)) ? 1 : 0, 100f, PointF.Empty, StringFormat.GenericTypographic);
		}
		RectangleF bounds = graphicsPath.GetBounds();
		if (bounds.Height < 1f)
		{
			return _stretchGlyphs[key] = (Array.Empty<PointF>(), Array.Empty<byte>(), RectangleF.Empty, 0f);
		}
		float num = height / bounds.Height;
		using (Matrix matrix = new Matrix())
		{
			matrix.Scale(num * squeeze / 100f, num);
			matrix.Translate(0f - bounds.X, 0f - bounds.Y);
			graphicsPath.Transform(matrix);
		}
		graphicsPath.Flatten(null, 0.3f);
		PointF[] pathPoints = graphicsPath.PathPoints;
		RectangleF bounds2 = graphicsPath.GetBounds();
		return _stretchGlyphs[key] = (pathPoints, graphicsPath.PathTypes, bounds2, StretchLine(pathPoints, graphicsPath.PathTypes, bounds2));
	}

	private static float StretchLine(PointF[] pts, byte[] types, RectangleF b)
	{
		float result = b.Y + b.Height * 0.5f;
		float num = float.MaxValue;
		for (float num2 = b.Y + b.Height * 0.18f; num2 <= b.Y + b.Height * 0.8f; num2 += 1f)
		{
			float num3 = 0f;
			int num4 = 0;
			int num5 = 0;
			for (int i = 0; i < pts.Length; i++)
			{
				bool num6 = i == pts.Length - 1 || (types[i + 1] & 7) == 0;
				PointF pointF = pts[i];
				PointF pointF2 = (num6 ? pts[num5] : pts[i + 1]);
				if ((pointF.Y - num2) * (pointF2.Y - num2) < 0f)
				{
					num4++;
					float num7 = Math.Abs(pointF2.X - pointF.X) / Math.Max(0.05f, Math.Abs(pointF2.Y - pointF.Y));
					num3 += num7 + num7 * num7 * 4f;
				}
				if (num6)
				{
					num5 = i + 1;
				}
			}
			if (num4 != 0)
			{
				num3 += Math.Abs(num2 - (b.Y + b.Height * 0.5f)) / b.Height * 0.3f;
				if (num3 < num)
				{
					num = num3;
					result = num2;
				}
			}
		}
		return result;
	}

	private void Stretched(Graphics g, Widget w, RectangleF r, double t)
	{
		string text = Formatted(w);
		(int, int, string) key = (w.X, w.Y, w.Sensor);
		double num = Fraction(w);
		if (!_stretchState.TryGetValue(key, out (double, double) value))
		{
			value = (num, t);
		}
		double num2 = Math.Clamp(t - value.Item2, 0.0, 1.0);
		double num3 = value.Item1 + (num - value.Item1) * (1.0 - Math.Exp((0.0 - num2) * 2.2));
		_stretchState[key] = (num3, t);
		float squeeze = ((w.Thickness > 0f) ? w.Thickness : 78f);
		float num4 = Math.Max(0f, r.Height - w.Size);
		float num5 = r.X;
		float num6 = w.Size * 0.035f;
		using SolidBrush brush = new SolidBrush(ValueColor(w));
		for (int i = 0; i < text.Length; i++)
		{
			var (array, types, rectangleF, num7) = StretchGlyph(text[i].ToString(), w.Font, w.Size, squeeze);
			if (array.Length == 0)
			{
				num5 += w.Size * 0.25f;
				continue;
			}
			double num8 = 0.72 + 0.28 * Math.Sin((double)i * 2.3 + (double)w.X * 0.01) + (w.Float ? (0.07 * Math.Sin(t * 0.8 + (double)i * 1.7 + (double)w.X * 0.005)) : 0.0);
			float num9 = (float)((double)num4 * Math.Clamp((0.45 + 0.55 * num3) * num8, 0.0, 1.0));
			PointF[] array2 = new PointF[array.Length];
			for (int j = 0; j < array.Length; j++)
			{
				array2[j] = new PointF(array[j].X + num5, array[j].Y + r.Y + ((array[j].Y > num7) ? num9 : 0f));
			}
			using (GraphicsPath path = new GraphicsPath(array2, types, FillMode.Winding))
			{
				g.FillPath(brush, path);
			}
			num5 += rectangleF.Width + num6;
		}
	}

	private void Tag(Graphics g, Widget w, RectangleF r, double t)
	{
		string[] array = Resolve(w.Text).Replace("\\n", "\n").Split('\n');
		float angle = -3.5f + (w.Float ? ((float)(Math.Sin(t * 0.9) * 1.6 + Math.Sin(t * 2.3) * 0.4)) : 0f);
		GraphicsState gstate = g.Save();
		g.TranslateTransform(r.X, r.Y);
		g.RotateTransform(angle);
		RectangleF rect = new RectangleF(0f, 0f, r.Width, r.Height);
		using (SolidBrush brush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
		{
			g.FillRectangle(brush, 4f, 5f, r.Width, r.Height);
		}
		using (SolidBrush brush2 = new SolidBrush(Imaging.Hex(w.Color2, Color.Gold)))
		{
			g.FillRectangle(brush2, rect);
		}
		Color color = Imaging.Hex(w.Color, Color.Black);
		using (Pen pen = new Pen(color, 2f))
		{
			g.DrawRectangle(pen, 7f, 7f, r.Width - 14f, r.Height - 14f);
		}
		Font font = GetFont(w, w.Size);
		using SolidBrush brush3 = new SolidBrush(color);
		float num = w.Size * 1.2f;
		float num2 = (r.Height - num * (float)array.Length) / 2f;
		for (int i = 0; i < array.Length; i++)
		{
			g.DrawString(array[i], font, brush3, 20f, num2 + (float)i * num, StringFormat.GenericTypographic);
		}
		g.Restore(gstate);
	}

	private void Value(Graphics g, Widget w, RectangleF r)
	{
		string text = ((w.Text.Length > 0) ? w.Text : Sensors.Def(w.Sensor).Unit);
		if (w.Sensor == "time")
		{
			text = "";
		}
		string text2 = Formatted(w);
		Font font = GetFont(w, w.Size);
		Font font2 = GetFont(w, w.Size * 0.42f);
		SizeF sizeF = g.MeasureString(text2, font, PointF.Empty, StringFormat.GenericTypographic);
		SizeF sizeF2 = ((text.Length > 0) ? g.MeasureString(text, font2, PointF.Empty, StringFormat.GenericTypographic) : SizeF.Empty);
		float num = sizeF.Width + ((text.Length > 0) ? (sizeF2.Width + w.Size * 0.06f) : 0f);
		float num2 = Align(w.Align) switch
		{
			StringAlignment.Center => r.X + (r.Width - num) / 2f, 
			StringAlignment.Far => r.Right - num, 
			_ => r.X, 
		};
		float num3 = r.Y + (r.Height - sizeF.Height) / 2f;
		Color color = ValueColor(w);
		if (w.Glow)
		{
			using SolidBrush brush = new SolidBrush(Imaging.WithAlpha(Imaging.Hex(w.Color2, color), 55));
			for (int i = 1; i <= 3; i++)
			{
				g.DrawString(text2, font, brush, num2 - (float)(i * 2), num3, StringFormat.GenericTypographic);
				g.DrawString(text2, font, brush, num2 + (float)(i * 2), num3, StringFormat.GenericTypographic);
			}
		}
		using (SolidBrush brush2 = new SolidBrush(color))
		{
			g.DrawString(text2, font, brush2, num2, num3, StringFormat.GenericTypographic);
		}
		if (text.Length > 0)
		{
			using (SolidBrush brush3 = new SolidBrush(Imaging.WithAlpha(color, 190)))
			{
				g.DrawString(text, font2, brush3, num2 + sizeF.Width + w.Size * 0.06f, num3 + sizeF.Height - sizeF2.Height - w.Size * 0.08f, StringFormat.GenericTypographic);
			}
		}
	}

	private void Gauge(Graphics g, Widget w, RectangleF r, float start, float sweep)
	{
		float num = Math.Min(r.Width, r.Height);
		RectangleF rect = new RectangleF(r.X + (r.Width - num) / 2f, r.Y + (r.Height - num) / 2f, num, num);
		float num2 = Math.Max(2f, w.Thickness);
		RectangleF rect2 = RectangleF.Inflate(rect, (0f - num2) / 2f - 2f, (0f - num2) / 2f - 2f);
		using (Pen pen = new Pen(Imaging.Hex(w.Track, Color.FromArgb(40, 255, 255, 255)), num2)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		})
		{
			g.DrawArc(pen, rect2, start, sweep);
		}
		if (sweep < 360f)
		{
			using Pen pen2 = new Pen(Color.FromArgb(70, 255, 255, 255), 2f);
			float num3 = rect2.Width / 2f - num2 * 1.3f;
			float num4 = rect2.X + rect2.Width / 2f;
			float num5 = rect2.Y + rect2.Height / 2f;
			for (int i = 0; i <= 10; i++)
			{
				double num6 = ((double)start + (double)(sweep * (float)i) / 10.0) * Math.PI / 180.0;
				g.DrawLine(pen2, num4 + (float)Math.Cos(num6) * num3, num5 + (float)Math.Sin(num6) * num3, num4 + (float)Math.Cos(num6) * (num3 - 8f), num5 + (float)Math.Sin(num6) * (num3 - 8f));
			}
		}
		float num7 = (float)Fraction(w) * sweep;
		if (num7 > 0.5f)
		{
			Color color = ValueColor(w);
			if (w.Glow)
			{
				for (int num8 = 3; num8 >= 1; num8--)
				{
					using Pen pen3 = new Pen(Imaging.WithAlpha(color, 35), num2 + (float)(num8 * 6))
					{
						StartCap = LineCap.Round,
						EndCap = LineCap.Round
					};
					g.DrawArc(pen3, rect2, start, num7);
				}
			}
			using LinearGradientBrush brush = new LinearGradientBrush(rect, Imaging.Hex(w.Color2, color), color, 0f);
			using Pen pen4 = new Pen(brush, num2)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			g.DrawArc(pen4, rect2, start, num7);
		}
		string text = Formatted(w) + ((Sensors.Def(w.Sensor).Unit == "%") ? "%" : "");
		RectangleF r2 = new RectangleF(rect.X, rect.Y + rect.Height * 0.32f, rect.Width, rect.Height * 0.3f);
		Text(g, w, r2, text, Color.White, w.Size);
		string align = w.Align;
		w.Align = "center";
		if (w.Text.Length > 0)
		{
			Text(g, w, new RectangleF(rect.X, rect.Y + rect.Height * 0.6f, rect.Width, rect.Height * 0.14f), Resolve(w.Text), Color.FromArgb(170, 255, 255, 255), w.Size * 0.32f);
		}
		w.Align = align;
	}

	private void Bar(Graphics g, Widget w, RectangleF r)
	{
		float radius = Math.Min(r.Height / 2f, w.Radius);
		using (SolidBrush brush = new SolidBrush(Imaging.Hex(w.Track, Color.FromArgb(40, 255, 255, 255))))
		{
			using GraphicsPath path = Imaging.Round(r, radius);
			g.FillPath(brush, path);
		}
		float num = (float)Fraction(w);
		if (num <= 0.002f)
		{
			return;
		}
		RectangleF rectangleF = r;
		rectangleF.Width = Math.Max(r.Height, r.Width * num);
		RectangleF r2 = rectangleF;
		Color color = ValueColor(w);
		using LinearGradientBrush brush2 = new LinearGradientBrush(new RectangleF(r.X - 1f, r.Y, r.Width + 2f, r.Height), Imaging.Hex(w.Color2, color), color, 0f);
		using GraphicsPath path2 = Imaging.Round(r2, radius);
		g.FillPath(brush2, path2);
	}

	private void Graph(Graphics g, Widget w, RectangleF r)
	{
		float[] array = _sensors.History(w.Sensor);
		if (array.Length < 2)
		{
			return;
		}
		float[] array2;
		lock (array)
		{
			array2 = (float[])array.Clone();
		}
		int num = Math.Min(array2.Length, Math.Max(10, (int)(r.Width / 6f)));
		PointF[] array3 = new PointF[num];
		for (int i = 0; i < num; i++)
		{
			float num2 = (float)Math.Clamp((double)(array2[array2.Length - num + i] - w.Min) / Math.Max(0.001, w.Max - w.Min), 0.0, 1.0);
			array3[i] = new PointF(r.X + r.Width * (float)i / (float)(num - 1), r.Bottom - num2 * r.Height);
		}
		Color color = Imaging.Hex(w.Color, Color.DeepSkyBlue);
		using (GraphicsPath graphicsPath = new GraphicsPath())
		{
			graphicsPath.AddLines(array3);
			graphicsPath.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
			graphicsPath.CloseFigure();
			using LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), Imaging.WithAlpha(color, 110), Imaging.WithAlpha(color, 0), 90f);
			g.FillPath(brush, graphicsPath);
		}
		using Pen pen = new Pen(color, Math.Max(1.5f, w.Thickness / 3f))
		{
			LineJoin = LineJoin.Round
		};
		g.DrawLines(pen, array3);
	}

	private void Panel(Graphics g, Widget w, RectangleF r)
	{
		using GraphicsPath path = Imaging.Round(r, w.Radius);
		Color color = Imaging.Hex(w.Color, Color.FromArgb(60, 255, 255, 255));
		Color color2 = Imaging.Hex(w.Color2, color);
		using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1f, r.Width, r.Height + 2f), color, color2, 90f))
		{
			g.FillPath(brush, path);
		}
		if (w.Thickness > 0f)
		{
			using Pen pen = new Pen(Imaging.Hex(w.Track, Color.FromArgb(50, 255, 255, 255)), Math.Min(w.Thickness, 4f));
			g.DrawPath(pen, path);
		}
		if (w.Text.Length > 0)
		{
			RectangleF r2 = new RectangleF(r.X + 18f, r.Y + 10f, r.Width - 36f, w.Size * 1.4f);
			Text(g, w, r2, Resolve(w.Text), Imaging.Hex(w.WarnColor, Color.FromArgb(120, 220, 255)), w.Size);
		}
	}

	private void CoreBars(Graphics g, Widget w, RectangleF r)
	{
		int coreCount = _sensors.CoreCount;
		if (coreCount == 0)
		{
			return;
		}
		float num = 3f;
		float num2 = (r.Width - num * (float)(coreCount - 1)) / (float)coreCount;
		Color a = Imaging.Hex(w.Color, Color.DeepSkyBlue);
		Color b = Imaging.Hex(w.Color2, Color.OrangeRed);
		using SolidBrush brush = new SolidBrush(Imaging.Hex(w.Track, Color.FromArgb(30, 255, 255, 255)));
		for (int i = 0; i < coreCount; i++)
		{
			float valueOrDefault = _sensors.Get($"cpu.core{i + 1}").GetValueOrDefault();
			float x = r.X + (float)i * (num2 + num);
			g.FillRectangle(brush, x, r.Y, num2, r.Height);
			float num3 = r.Height * Math.Clamp(valueOrDefault / 100f, 0.02f, 1f);
			using SolidBrush brush2 = new SolidBrush(Imaging.Mix(a, b, (double)valueOrDefault / 100.0));
			g.FillRectangle(brush2, x, r.Bottom - num3, num2, num3);
		}
	}

	private Image? GetImage(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
		if (!_images.TryGetValue(path, out Image value))
		{
			value = (_images[path] = Imaging.Load(path));
		}
		return value;
	}

	public void Forget(string? path)
	{
		if (path != null && _images.Remove(path, out Image value))
		{
			value?.Dispose();
		}
	}

	public void Dispose()
	{
		foreach (Font value in _fonts.Values)
		{
			value.Dispose();
		}
		foreach (Image value2 in _images.Values)
		{
			value2?.Dispose();
		}
		_stone?.Dispose();
		_aurora?.Dispose();
		_eclipse?.Dispose();
		_base?.Dispose();
		_video?.Dispose();
		_inflated.Dispose();
		_extrude.Dispose();
		foreach (var value3 in _posterPaths.Values)
		{
			value3.Path.Dispose();
		}
	}

	public static bool IsAnimated(ScreenLayout l)
	{
		bool flag;
		switch (l.Background)
		{
		case "aurora":
		case "grid":
		case "waves":
		case "tunnel":
		case "video":
		case "eclipse":
		case "mesh":
		case "hud":
		case "fluted":
		case "papersea":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag && !l.Widgets.Any((Widget w) => w.Visible && w.Float) && (!(l.Background == "image") || !IsGif(l.Image)))
		{
			return l.Widgets.Any((Widget w) => w.Visible && w.Kind == WidgetKind.Image && IsGif(w.Image));
		}
		return true;
	}

	public static bool ShowsSeconds(ScreenLayout l)
	{
		return l.Widgets.Any((Widget w) => w.Visible && (w.Text.Contains(":ss") || w.Text.Contains("mm:ss") || w.Format.Contains("ss")));
	}

	private static bool IsGif(string? path)
	{
		return path?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ?? false;
	}
}
