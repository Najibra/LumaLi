using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;

namespace LcdStudio.Core;

public sealed partial class Faces : IDisposable
{
	private sealed record FaceContext(Graphics G, FanScreen Fan, string Value, float Fraction, string Label, Color C1, Color C2, double T)
	{
		public string Time => DateTime.Now.ToString("HH:mm");
	}

	public const int Size = 400;

	public static readonly string[] Names =
	{
		"Glass Slab", "Fading", "Molten Glass", "Neon Ring", "Dot Matrix", "Carved", "Liquid", "Reactor", "3D Number", "Light on Text",
		"Prism Glass", "Warp", "Cut Paper", "Balloon", "Clock", "Blade", "Gap", "Deer", "Headline", "Lightbeam", "Lottie Gauge", "Image / GIF"
	};

	private readonly Dictionary<string, Image?> _images = new Dictionary<string, Image>();

	private readonly Dictionary<string, FontFamily> _families = new Dictionary<string, FontFamily>();

	private static readonly Dictionary<char, string[]> Glyphs5x7 = new Dictionary<char, string[]>
	{
		['0'] = new string[7] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
		['1'] = new string[7] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
		['2'] = new string[7] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
		['3'] = new string[7] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
		['4'] = new string[7] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
		['5'] = new string[7] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
		['6'] = new string[7] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
		['7'] = new string[7] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
		['8'] = new string[7] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
		['9'] = new string[7] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
		['%'] = new string[7] { "11001", "11010", "00010", "00100", "01000", "01011", "10011" },
		['°'] = new string[7] { "01100", "10010", "10010", "01100", "00000", "00000", "00000" },
		['W'] = new string[7] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" },
		['G'] = new string[7] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" },
		['.'] = new string[7] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" },
		[':'] = new string[7] { "00000", "01100", "01100", "00000", "01100", "01100", "00000" },
		['-'] = new string[7] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" }
	};

	private readonly Extrude _extrude = new Extrude();

	private readonly Dictionary<string, (string Old, string Now, double Since)> _rolls = new Dictionary<string, (string, string, double)>();

	public static (string First, string Second) ColorLabels(string face)
	{
		return face switch
		{
			"Glass Slab" => (First: "Lettering", Second: "Background"), 
			"Fading" => (First: "Letters", Second: "Light streaks"), 
			"Molten Glass" => (First: "Core", Second: "Edge"), 
			"Neon Ring" => (First: "Start", Second: "End"), 
			"Dot Matrix" => (First: "Dots", Second: "Background"), 
			"Carved" => (First: "Stone", Second: "Shadow"), 
			"Liquid" => (First: "Liquid", Second: "Glass"), 
			"Reactor" => (First: "Core", Second: "Rings"), 
			"3D Number" => (First: "Face", Second: "Depth"), 
			"Light on Text" => (First: "Letters", Second: "Light"), 
			"Prism Glass" => (First: "Tint", Second: "Background"), 
			"Warp" => (First: "Stars", Second: "Number"), 
			"Cut Paper" => (First: "Top layer", Second: "Bottom layer"), 
			"Balloon" => (First: "Balloon", Second: "Background"), 
			"Clock" => (First: "Hands", Second: "Dial"), 
			"Blade" => (First: "Ink", Second: "Poster"), 
			"Gap" => (First: "Ink", Second: "Page"),
			"Headline" => (First: "Paint", Second: "Ink"),
			"Lightbeam" => (First: "Crest", Second: "Flanks"),
			"Lottie Gauge" => (First: "Start", Second: "End"),
			_ => (First: "Text", Second: "Tint"), 
		};
	}

	public static (string, string) DefaultColors(string face)
	{
		return face switch
		{
			"Glass Slab" => ("#0d0d0f", "#ececea"), 
			"Fading" => ("#f2f0f5", "#ffffff"), 
			"Molten Glass" => ("#ffa51e", "#e6230a"), 
			"Neon Ring" => ("#00e5ff", "#ff2bd6"), 
			"Dot Matrix" => ("#ffb000", "#120a00"), 
			"Carved" => ("#c9c4bb", "#3b3833"), 
			"Liquid" => ("#1fa2ff", "#0b1a33"), 
			"Reactor" => ("#7df9ff", "#0080ff"), 
			"3D Number" => ("#ffd84d", "#c2410c"), 
			"Light on Text" => ("#3a3f55", "#fff3c4"), 
			"Prism Glass" => ("#ffffff", "#0b0f24"), 
			"Warp" => ("#9fd8ff", "#ffffff"), 
			"Cut Paper" => ("#ff6b6b", "#2b2d42"), 
			"Balloon" => ("#ff4fa3", "#ffe3f1"), 
			"Clock" => ("#ffffff", "#141a33"), 
			"Blade" => ("#161012", "#ff5a45"), 
			"Gap" => ("#0e0e0e", "#f6f5f1"),
			"Headline" => ("#d02a42", "#1d2b52"),
			"Lightbeam" => ("#ffece0", "#9aa6f2"),
			"Lottie Gauge" => ("#3af0ff", "#ff3d8b"),
			_ => ("#ffffff", "#000000"), 
		};
	}

	private FontFamily Family(string name)
	{
		if (!_families.TryGetValue(name, out FontFamily value))
		{
			try
			{
				value = new FontFamily(name);
			}
			catch
			{
				value = FontFamily.GenericSansSerif;
			}
			_families[name] = value;
		}
		return value;
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

	public static bool IsAnimated(FanScreen f)
	{
		switch (f.Face)
		{
		case "Carved":
		case "Clock":
			return false;
		case "Image / GIF":
			return Imaging.IsMoving(f.Background);
		default:
			return true;
		}
	}

	public static string ValueText(string sensor, float? v)
	{
		if (sensor == "time")
		{
			return DateTime.Now.ToString("HH:mm");
		}
		if (v.HasValue)
		{
			float valueOrDefault = v.GetValueOrDefault();
			return Sensors.Def(sensor).Unit switch
			{
				"%" => $"{valueOrDefault:0}%", 
				"°C" => $"{valueOrDefault:0}°", 
				"W" => $"{valueOrDefault:0}W", 
				"GB" => (valueOrDefault >= 10f) ? $"{valueOrDefault:0}G" : $"{valueOrDefault:0.0}G", 
				"MB/s" => (valueOrDefault >= 10f) ? $"{valueOrDefault:0}" : $"{valueOrDefault:0.0}", 
				"MHz" => (valueOrDefault >= 1000f) ? $"{valueOrDefault / 1000f:0.0}G" : $"{valueOrDefault:0}", 
				_ => valueOrDefault.ToString("0", CultureInfo.InvariantCulture), 
			};
		}
		return "--";
	}

	public static string ShortLabel(string sensor)
	{
		if (sensor.StartsWith("cpu."))
		{
			return "CPU";
		}
		if (sensor.StartsWith("gpu."))
		{
			return "GPU";
		}
		if (sensor.StartsWith("ram."))
		{
			return "RAM";
		}
		if (sensor.StartsWith("net."))
		{
			return "NET";
		}
		if (!sensor.StartsWith("fans."))
		{
			if (sensor == "time")
			{
				return "TIME";
			}
			return "";
		}
		return "FANS";
	}

	public static string Caption(string sensor)
	{
		return sensor switch
		{
			"cpu.load" => "CPU LOAD", 
			"gpu.load" => "GPU LOAD", 
			"ram.load" => "MEMORY IN USE", 
			"gpu.power" => "GPU POWER", 
			"cpu.power" => "CPU POWER", 
			"gpu.temp" => "GPU TEMP", 
			"cpu.temp" => "CPU TEMP", 
			_ => Sensors.Def(sensor).Label.ToUpperInvariant(), 
		};
	}

	public Bitmap Render(FanScreen fan, float? value, double t)
	{
		Bitmap bitmap = new Bitmap(400, 400, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
		graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		if (fan.Rotation % 360 != 0)
		{
			graphics.TranslateTransform(200f, 200f);
			graphics.RotateTransform(fan.Rotation);
			graphics.TranslateTransform(-200f, -200f);
		}
		Color c = Imaging.Hex(fan.Color1, Color.White);
		Color c2 = Imaging.Hex(fan.Color2, Color.Black);
		FaceContext x = new FaceContext(graphics, fan, ValueText(fan.Sensor, value), Fraction(fan.Sensor, value), string.IsNullOrWhiteSpace(fan.Label) ? Caption(fan.Sensor) : fan.Label.ToUpperInvariant(), c, c2, t);
		graphics.Clear(Color.Black);
		Image image = GetImage(fan.Background);
		if (image != null && fan.Face != "Image / GIF" && fan.Face != "Lottie Gauge")
		{
			Imaging.DrawCover(graphics, image, new RectangleF(0f, 0f, 400f, 400f), t, 0.55f);
		}
		switch (fan.Face)
		{
		case "Glass Slab":
			GlassSlab(x);
			break;
		case "Fading":
			Fading(x);
			break;
		case "Molten Glass":
			MoltenSkia(x);
			break;
		case "Neon Ring":
			NeonRing(x);
			break;
		case "Dot Matrix":
			DotMatrixSkia(x);
			break;
		case "Carved":
			Carved(x);
			break;
		case "Liquid":
			Liquid(x);
			break;
		case "Reactor":
			Reactor(x);
			break;
		case "3D Number":
			Extruded(x);
			break;
		case "Light on Text":
			Spotlight(x);
			break;
		case "Prism Glass":
			Prism(x);
			break;
		case "Warp":
			WarpSkia(x);
			break;
		case "Cut Paper":
			CutPaper(x);
			break;
		case "Balloon":
			Balloon(x);
			break;
		case "Clock":
			Clock(x);
			break;
		case "Blade":
			Blade(x);
			break;
		case "Gap":
			Gap(x);
			break;
		case "Deer":
			Deer(x);
			break;
		case "Headline":
			Headline(x);
			break;
		case "Lightbeam":
			Lightbeam(x);
			break;
		case "Lottie Gauge":
			LottieGauge(x, image);
			break;
		default:
			ImageFace(x, image);
			break;
		}
		return bitmap;
	}

	private static float Fraction(string sensor, float? v)
	{
		if (v.HasValue)
		{
			float valueOrDefault = v.GetValueOrDefault();
			SensorDef sensorDef = Sensors.Def(sensor);
			return (float)Math.Clamp((double)(valueOrDefault - sensorDef.Min) / Math.Max(0.001, sensorDef.Max - sensorDef.Min), 0.0, 1.0);
		}
		return 0f;
	}

	private GraphicsPath FitText(string text, string font, FontStyle style, RectangleF box, float stretchY = 1f)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		FontFamily fontFamily = Family(font);
		if (!fontFamily.IsStyleAvailable(style))
		{
			style = FontStyle.Regular;
		}
		graphicsPath.AddString(text, fontFamily, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF bounds = graphicsPath.GetBounds();
		if (bounds.Width < 1f || bounds.Height < 1f)
		{
			return graphicsPath;
		}
		float num = Math.Min(box.Width / bounds.Width, box.Height / (bounds.Height * stretchY));
		using Matrix matrix = new Matrix();
		matrix.Translate(box.X + box.Width / 2f, box.Y + box.Height / 2f);
		matrix.Scale(num, num * stretchY);
		matrix.Translate(0f - (bounds.X + bounds.Width / 2f), 0f - (bounds.Y + bounds.Height / 2f));
		graphicsPath.Transform(matrix);
		return graphicsPath;
	}

	private static void Small(Graphics g, string text, Color c, float y, float size = 15f, bool bold = true, float spacing = 0f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		if (spacing > 0f)
		{
			text = string.Join(" ", text.ToCharArray()).Replace("   ", "  ");
		}
		using Font font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
		using SolidBrush brush = new SolidBrush(c);
		using StringFormat format = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};
		g.DrawString(text, font, brush, new RectangleF(20f, y - size, 360f, size * 2f), format);
	}

	private static void Glow(Graphics g, GraphicsPath p, Color c, int layers = 6, float width = 4f)
	{
		for (int num = layers; num >= 1; num--)
		{
			using Pen pen = new Pen(Color.FromArgb(Math.Clamp(90 / layers, 6, 60), c), width * (float)num)
			{
				LineJoin = LineJoin.Round
			};
			g.DrawPath(pen, p);
		}
	}

	private void GlassSlab(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(Color.Black);
		RectangleF rectangleF = new RectangleF(42f, 42f, 316f, 316f);
		using (GraphicsPath path = Imaging.Round(RectangleF.Inflate(rectangleF, 6f, 6f), 44f))
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(70, x.C2));
			g.FillPath(brush, path);
		}
		using (GraphicsPath path2 = Imaging.Round(rectangleF, 40f))
		{
			using LinearGradientBrush brush2 = new LinearGradientBrush(rectangleF, Imaging.Mix(x.C2, Color.White, 0.35), x.C2, 90f);
			g.FillPath(brush2, path2);
			using Pen pen = new Pen(Color.FromArgb(90, 255, 255, 255), 2f);
			g.DrawPath(pen, path2);
		}
		double num = Math.Sin(x.T * 2.2) * 2.5;
		using (GraphicsPath graphicsPath = FitText(box: new RectangleF(70f, 112f, 260f, 132f), text: x.Value, font: "Segoe UI Black", style: FontStyle.Bold))
		{
			DrawOffset(g, graphicsPath, Color.FromArgb(150, 255, 40, 60), (float)(5.0 + num), 0f);
			DrawOffset(g, graphicsPath, Color.FromArgb(150, 30, 90, 255), (float)(-5.0 - num), 0f);
			using SolidBrush brush3 = new SolidBrush(x.C1);
			g.FillPath(brush3, graphicsPath);
		}
		Small(g, x.Label, Imaging.WithAlpha(x.C1, 150), 268f, 12f, bold: false, 1f);
		Small(g, x.Time, Imaging.WithAlpha(x.C1, 190), 292f, 14f, bold: false);
		using SolidBrush brush4 = new SolidBrush(x.C1);
		g.FillEllipse(brush4, 196, 318, 8, 8);
	}

	private static void DrawOffset(Graphics g, GraphicsPath p, Color c, float dx, float dy)
	{
		using GraphicsPath graphicsPath = (GraphicsPath)p.Clone();
		using Matrix matrix = new Matrix();
		matrix.Translate(dx, dy);
		graphicsPath.Transform(matrix);
		using SolidBrush brush = new SolidBrush(c);
		g.FillPath(brush, graphicsPath);
	}

	private void Fading(FaceContext x)
	{
		Graphics g = x.G;
		using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, 0, 400, 400), Color.FromArgb(16, 16, 22), Color.Black, 90f))
		{
			g.FillRectangle(brush, 0, 0, 400, 400);
		}
		for (int i = 0; i < 4; i++)
		{
			double num = (x.T * 0.35 + (double)i * 0.27) % 1.0;
			float num2 = (float)(-120.0 + num * 640.0);
			float num3 = 128 + i * 9;
			using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(num2 - 90f, num3 - 3f, 180f, 6f), Color.Transparent, Imaging.WithAlpha(x.C2, 230), 0f);
			linearGradientBrush.SetSigmaBellShape(0.5f);
			g.FillRectangle(linearGradientBrush, num2 - 90f, num3 - 3f, 180f, 6f);
		}
		using (GraphicsPath graphicsPath = new GraphicsPath())
		{
			graphicsPath.AddEllipse(150, 110, 100, 40);
			PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
			pathGradientBrush.CenterColor = Imaging.WithAlpha(x.C2, 160);
			pathGradientBrush.SurroundColors = new Color[1] { Color.Transparent };
			using PathGradientBrush brush2 = pathGradientBrush;
			g.FillPath(brush2, graphicsPath);
		}
		using GraphicsPath graphicsPath2 = FitText(box: new RectangleF(70f, 120f, 260f, 150f), text: x.Value, font: "Segoe UI", style: FontStyle.Bold, stretchY: 1.25f);
		RectangleF bounds = graphicsPath2.GetBounds();
		using (LinearGradientBrush brush3 = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1f, bounds.Width, bounds.Height + 2f), x.C1, Imaging.WithAlpha(x.C1, 60), 90f))
		{
			g.FillPath(brush3, graphicsPath2);
		}
		GraphicsState gstate = g.Save();
		g.SetClip(graphicsPath2);
		float num4 = (float)((double)bounds.Bottom - (double)bounds.Height * 0.35 + Math.Sin(x.T * 3.0) * 6.0);
		using (LinearGradientBrush brush4 = new LinearGradientBrush(new RectangleF(0f, num4 - 1f, 400f, bounds.Bottom - num4 + 2f), Color.Transparent, Color.FromArgb(200, 120, 160, 255), 90f))
		{
			g.FillRectangle(brush4, 0f, num4, 400f, bounds.Bottom - num4 + 1f);
		}
		g.Restore(gstate);
		Small(g, x.Label, Imaging.WithAlpha(x.C1, 210), 92f, 13f, bold: true, 1f);
		Small(g, x.Time, Imaging.WithAlpha(x.C1, 170), 108f, 12f, bold: false);
	}

	private static string ShortLabelOr(FaceContext x)
	{
		if (!string.IsNullOrWhiteSpace(x.Fan.Label))
		{
			return x.Fan.Label.ToUpperInvariant();
		}
		return ShortLabel(x.Fan.Sensor);
	}

	private void Carved(FaceContext x)
	{
		Graphics g = x.G;
		using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, 0, 400, 400), Imaging.Mix(x.C1, Color.White, 0.15), Imaging.Mix(x.C1, Color.Black, 0.25), 60f))
		{
			g.FillRectangle(brush, 0, 0, 400, 400);
		}
		Random random = new Random(7);
		for (int i = 0; i < 900; i++)
		{
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(random.Next(10, 35), (random.Next(2) == 0) ? Color.Black : Color.White));
			g.FillRectangle(brush2, random.Next(400), random.Next(400), 2, 2);
		}
		using GraphicsPath graphicsPath = FitText(x.Value, "Georgia", FontStyle.Bold, new RectangleF(70f, 120f, 260f, 150f));
		DrawOffset(g, graphicsPath, Imaging.WithAlpha(Color.White, 120), 3f, 3f);
		DrawOffset(g, graphicsPath, Imaging.WithAlpha(x.C2, 220), -3f, -3f);
		using (SolidBrush brush3 = new SolidBrush(Imaging.Mix(x.C1, x.C2, 0.35)))
		{
			g.FillPath(brush3, graphicsPath);
		}
		Small(g, x.Label, Imaging.WithAlpha(x.C2, 200), 100f, 13f, bold: true, 2f);
		Small(g, x.Time, Imaging.WithAlpha(x.C2, 180), 300f, 14f, bold: false);
	}

	private void Liquid(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		float num = 360f - x.Fraction * 320f;
		using GraphicsPath graphicsPath = new GraphicsPath();
		List<PointF> list = new List<PointF>();
		for (int i = 0; i <= 40; i++)
		{
			float x2 = i * 10;
			list.Add(new PointF(x2, num + (float)(Math.Sin((double)i * 0.45 + x.T * 3.0) * 8.0 + Math.Sin((double)i * 0.2 - x.T * 2.0) * 5.0)));
		}
		list.Add(new PointF(400f, 400f));
		list.Add(new PointF(0f, 400f));
		graphicsPath.AddPolygon(list.ToArray());
		using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(0f, num - 20f, 400f, 400f - num + 21f), Imaging.Mix(x.C1, Color.White, 0.25), Imaging.Mix(x.C1, Color.Black, 0.3), 90f))
		{
			g.FillPath(brush, graphicsPath);
		}
		for (int j = 0; j < 9; j++)
		{
			double num2 = (x.T * 0.4 + (double)j * 0.37) % 1.0;
			float x3 = 60 + j * 37 % 280;
			float y = 400f - (float)num2 * (400f - num);
			using Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 2f);
			g.DrawEllipse(pen, x3, y, 8 + j % 3 * 3, 8 + j % 3 * 3);
		}
		using GraphicsPath graphicsPath2 = FitText(x.Value, "Segoe UI", FontStyle.Bold, new RectangleF(90f, 140f, 220f, 110f));
		using (new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
		{
			DrawOffset(g, graphicsPath2, Color.FromArgb(80, 0, 0, 0), 3f, 4f);
		}
		using (SolidBrush brush2 = new SolidBrush(Color.White))
		{
			g.FillPath(brush2, graphicsPath2);
		}
		Small(g, x.Label, Color.FromArgb(220, 255, 255, 255), 120f, 12f, bold: true, 1f);
		Small(g, x.Time, Color.FromArgb(200, 255, 255, 255), 270f, 13f, bold: false);
	}

	private void Extruded(FaceContext x)
	{
		Graphics g = x.G;
		using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, 0, 400, 400), Color.FromArgb(30, 30, 40), Color.FromArgb(8, 8, 12), 90f))
		{
			g.FillRectangle(brush, 0, 0, 400, 400);
		}
		using GraphicsPath graphicsPath = FitText(x.Value, "Segoe UI Black", FontStyle.Bold, new RectangleF(60f, 115f, 270f, 150f));
		float num = (float)Math.Sin(x.T * 0.9) * 0.6f;
		for (int num2 = 14; num2 >= 1; num2--)
		{
			DrawOffset(g, graphicsPath, Imaging.Mix(x.C2, Color.Black, (double)num2 / 22.0), (float)num2 * (0.9f + num * 0.4f), (float)num2 * 0.9f);
		}
		using (LinearGradientBrush brush2 = new LinearGradientBrush(graphicsPath.GetBounds(), Imaging.Mix(x.C1, Color.White, 0.35), x.C1, 90f))
		{
			g.FillPath(brush2, graphicsPath);
		}
		Small(g, x.Label, Color.FromArgb(200, 255, 255, 255), 96f, 13f, bold: true, 1f);
		Small(g, x.Time, Color.FromArgb(170, 255, 255, 255), 304f, 13f, bold: false);
	}

	private void Spotlight(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(Color.FromArgb(8, 8, 12));
		using GraphicsPath graphicsPath = FitText(x.Value, "Segoe UI", FontStyle.Bold, new RectangleF(60f, 120f, 280f, 150f));
		using (SolidBrush brush = new SolidBrush(x.C1))
		{
			g.FillPath(brush, graphicsPath);
		}
		float num = 200f + (float)Math.Sin(x.T * 1.1) * 140f;
		float num2 = 180f + (float)Math.Cos(x.T * 0.7) * 40f;
		GraphicsState gstate = g.Save();
		g.SetClip(graphicsPath);
		using (GraphicsPath graphicsPath2 = new GraphicsPath())
		{
			graphicsPath2.AddEllipse(num - 130f, num2 - 130f, 260f, 260f);
			PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath2);
			pathGradientBrush.CenterColor = x.C2;
			pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(0, x.C2) };
			using PathGradientBrush brush2 = pathGradientBrush;
			g.FillPath(brush2, graphicsPath2);
		}
		g.Restore(gstate);
		using (GraphicsPath graphicsPath3 = new GraphicsPath())
		{
			graphicsPath3.AddEllipse(num - 160f, num2 - 160f, 320f, 320f);
			PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath3);
			pathGradientBrush.CenterColor = Imaging.WithAlpha(x.C2, 40);
			pathGradientBrush.SurroundColors = new Color[1] { Color.Transparent };
			using PathGradientBrush brush3 = pathGradientBrush;
			g.FillPath(brush3, graphicsPath3);
		}
		Small(g, x.Label, Imaging.WithAlpha(x.C2, 180), 100f, 13f, bold: true, 1f);
		Small(g, x.Time, Imaging.WithAlpha(x.C2, 150), 300f, 13f, bold: false);
	}

	private void Prism(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		using GraphicsPath graphicsPath = FitText(x.Value, "Segoe UI Black", FontStyle.Bold, new RectangleF(60f, 115f, 280f, 150f));
		RectangleF bounds = graphicsPath.GetBounds();
		float num = (float)(x.T * 60.0 % 400.0);
		using (LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(bounds.X - 400f + num, bounds.Y, 400f, bounds.Height), Color.Red, Color.Blue, 30f))
		{
			ColorBlend colorBlend = new ColorBlend();
			colorBlend.Colors = new Color[6]
			{
				Color.FromArgb(255, 70, 70),
				Color.FromArgb(255, 200, 60),
				Color.FromArgb(80, 255, 140),
				Color.FromArgb(60, 180, 255),
				Color.FromArgb(200, 90, 255),
				Color.FromArgb(255, 70, 70)
			};
			colorBlend.Positions = new float[6] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
			ColorBlend interpolationColors = colorBlend;
			linearGradientBrush.InterpolationColors = interpolationColors;
			linearGradientBrush.WrapMode = WrapMode.Tile;
			g.FillPath(linearGradientBrush, graphicsPath);
		}
		GraphicsState gstate = g.Save();
		g.SetClip(graphicsPath);
		using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height / 2f + 1f), Imaging.WithAlpha(x.C1, 150), Color.Transparent, 90f))
		{
			g.FillRectangle(brush, bounds.X, bounds.Y, bounds.Width, bounds.Height / 2f);
		}
		g.Restore(gstate);
		using (Pen pen = new Pen(Imaging.WithAlpha(x.C1, 160), 1.5f))
		{
			g.DrawPath(pen, graphicsPath);
		}
		Small(g, x.Label, Imaging.WithAlpha(x.C1, 200), 100f, 13f, bold: true, 1f);
		Small(g, x.Time, Imaging.WithAlpha(x.C1, 170), 300f, 13f, bold: false);
	}

	private static double Hash01(int i)
	{
		int num = i * -1640531535;
		int num2 = (num ^ (num >>> 13)) * 1540483477;
		return (double)(((uint)num2 ^ ((uint)num2 >> 15)) & 0xFFFFFFu) / 16777215.0;
	}

	private void CutPaper(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		for (int i = 0; i < 4; i++)
		{
			float num = 180 - i * 34;
			float num2 = (float)Math.Sin(x.T * 0.8 + (double)i) * 4f;
			using SolidBrush brush = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
			g.FillEllipse(brush, 200f - num + 4f + num2, 200f - num + 6f, num * 2f, num * 2f);
			using SolidBrush brush2 = new SolidBrush(Imaging.Mix(x.C2, x.C1, (double)(i + 1) / 4.0));
			g.FillEllipse(brush2, 200f - num + num2, 200f - num, num * 2f, num * 2f);
		}
		using GraphicsPath graphicsPath = FitText(x.Value, "Segoe UI Black", FontStyle.Bold, new RectangleF(110f, 150f, 180f, 90f));
		DrawOffset(g, graphicsPath, Color.FromArgb(90, 0, 0, 0), 3f, 5f);
		using (SolidBrush brush3 = new SolidBrush(Color.White))
		{
			g.FillPath(brush3, graphicsPath);
		}
		Small(g, x.Label, Color.White, 132f, 12f, bold: true, 1f);
		Small(g, x.Time, Color.FromArgb(220, 255, 255, 255), 262f, 12f, bold: false);
	}

	private void Balloon(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		float num = (float)Math.Sin(x.T * 1.6) * 6f;
		using GraphicsPath graphicsPath = FitText(x.Value, "Arial Rounded MT Bold", FontStyle.Bold, new RectangleF(60f, 115f + num, 280f, 150f));
		DrawOffset(g, graphicsPath, Color.FromArgb(60, 0, 0, 0), 4f, 8f);
		using (Pen pen = new Pen(x.C1, 16f)
		{
			LineJoin = LineJoin.Round
		})
		{
			g.DrawPath(pen, graphicsPath);
		}
		using (SolidBrush brush = new SolidBrush(x.C1))
		{
			g.FillPath(brush, graphicsPath);
		}
		RectangleF bounds = graphicsPath.GetBounds();
		GraphicsState gstate = g.Save();
		g.SetClip(graphicsPath);
		using (LinearGradientBrush brush2 = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.45f + 1f), Color.FromArgb(150, 255, 255, 255), Color.Transparent, 90f))
		{
			g.FillRectangle(brush2, bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.45f);
		}
		g.Restore(gstate);
		Small(g, x.Label, Imaging.Mix(x.C1, Color.Black, 0.3), 96f, 13f, bold: true, 1f);
		Small(g, x.Time, Imaging.Mix(x.C1, Color.Black, 0.3), 306f, 13f, bold: false);
	}

	private void Clock(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		DateTime now = DateTime.Now;
		using (Pen pen = new Pen(Imaging.WithAlpha(x.C1, 70), 2f))
		{
			g.DrawEllipse(pen, 30, 30, 340, 340);
		}
		for (int i = 0; i < 60; i++)
		{
			double num = (double)i * Math.PI / 30.0;
			float num2 = ((i % 5 == 0) ? 150 : 160);
			using Pen pen2 = new Pen(Imaging.WithAlpha(x.C1, (i % 5 == 0) ? 230 : 90), (i % 5 == 0) ? 4 : 2);
			g.DrawLine(pen2, 200f + (float)Math.Sin(num) * num2, 200f - (float)Math.Cos(num) * num2, 200f + (float)Math.Sin(num) * 168f, 200f - (float)Math.Cos(num) * 168f);
		}
		using (Pen pen3 = new Pen(Imaging.WithAlpha(x.C1, 200), 5f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		})
		{
			g.DrawArc(pen3, 140f, 230f, 120f, 120f, 200f, 140f * Math.Max(0.02f, x.Fraction));
		}
		Small(g, x.Value, x.C1, 290f, 18f);
		Small(g, x.Label, Imaging.WithAlpha(x.C1, 160), 130f, 11f, bold: true, 1f);
		double num3 = (double)now.Second + (double)now.Millisecond / 1000.0;
		Hand(((double)(now.Hour % 12) + (double)now.Minute / 60.0) * Math.PI / 6.0, 90f, 9f, x.C1);
		Hand(((double)now.Minute + num3 / 60.0) * Math.PI / 30.0, 135f, 6f, x.C1);
		Hand(num3 * Math.PI / 30.0, 150f, 2f, Color.FromArgb(255, 80, 80));
		using (SolidBrush brush = new SolidBrush(x.C1))
		{
			g.FillEllipse(brush, 192, 192, 16, 16);
		}
		void Hand(double angle, float len, float width, Color c)
		{
			using Pen pen4 = new Pen(c, width)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			g.DrawLine(pen4, 200f, 200f, 200f + (float)Math.Sin(angle) * len, 200f - (float)Math.Cos(angle) * len);
		}
	}

	private void ImageFace(FaceContext x, Image? bg)
	{
		Graphics g = x.G;
		if (bg != null)
		{
			Imaging.DrawCover(g, bg, new RectangleF(0f, 0f, 400f, 400f), x.T);
			if (string.IsNullOrWhiteSpace(x.Fan.Label) && !(x.Fan.Sensor != "none"))
			{
				return;
			}
			using GraphicsPath graphicsPath = FitText(x.Value, "Segoe UI", FontStyle.Bold, new RectangleF(130f, 300f, 140f, 50f));
			DrawOffset(g, graphicsPath, Color.FromArgb(160, 0, 0, 0), 2f, 3f);
			using SolidBrush brush = new SolidBrush(x.C1);
			g.FillPath(brush, graphicsPath);
			return;
		}
		g.Clear(Color.FromArgb(20, 24, 50));
		Small(g, "Upload an image, GIF or Lottie", Color.FromArgb(200, 255, 255, 255), 200f, 16f, bold: false);
	}

	/// <summary>
	/// Lottie Gauge: an uploaded Lottie stands at the point of its animation the reading has reached, eased on a spring,
	/// instead of looping, so an arc filling or a needle sweeping over its length becomes a gauge. Without one the face
	/// draws a gauge of its own in the chosen style and the fan's two colours (see FacesGauge). A still picture sits
	/// dimmed behind it.
	/// </summary>
	private void LottieGauge(FaceContext x, Image? bg)
	{
		Graphics g = x.G;
		var (s, f, dt) = GlowStep(x, "Lottie Gauge");
		// fans set up on the first version kept its white and pale blue, which were for the number, not a gauge
		if (x.Fan.Color1 == "#ffffff" && x.Fan.Color2 == "#7fdcff")
		{
			(string c1, string c2) = DefaultColors("Lottie Gauge");
			x = x with { C1 = Imaging.Hex(c1, Color.Cyan), C2 = Imaging.Hex(c2, Color.DeepPink) };
		}
		float warn = WarnStep(x, s, dt, out float dim);
		if (warn > 0.002f)
		{
			// the sweep runs from a deep shade of the warning colour to the colour itself, so it stays rich rather than
			// mixing the fan's own colours into a pale wash; then the whole light breathes darker and back
			Color hot = Imaging.Hex(x.Fan.WarnColor, Color.FromArgb(255, 90, 60));
			Color deep = Imaging.Mix(hot, Color.Black, 0.45);
			Color c1 = Imaging.Mix(x.C1, deep, warn), c2 = Imaging.Mix(x.C2, hot, warn);
			x = x with { C1 = Imaging.Mix(c1, Color.Black, dim), C2 = Imaging.Mix(c2, Color.Black, dim) };
		}
		Color now = Imaging.Mix(x.C1, x.C2, f);
		if (bg != null && Lottie.GaugeTime(bg, f, out double at))
		{
			Imaging.DrawCover(g, bg, new RectangleF(0f, 0f, 400f, 400f), at);
			using GraphicsPath number = FitText(x.Value, "Segoe UI", FontStyle.Bold, new RectangleF(120f, 160f, 160f, 64f));
			for (int i = 3; i >= 1; i--)
			{
				using Pen glow = new Pen(Color.FromArgb(28, now), i * 5f) { LineJoin = LineJoin.Round };
				g.DrawPath(glow, number);
			}
			g.FillPath(Brushes.White, number);
			Small(g, x.Label, Color.FromArgb(190, 255, 255, 255), 248f, 13f, bold: true);
			return;
		}
		if (bg != null)
		{
			Imaging.DrawCover(g, bg, new RectangleF(0f, 0f, 400f, 400f), x.T, 0.55f);
		}
		BuiltInGauge(x, s, f, dt, now, over: bg != null);
	}

	private void Blade(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		Extrude.Outline outline = _extrude.Get(x.Value, "Bahnschrift", bold: true, 100f);
		float num = ((outline.Bounds.Width < 1f) ? 120f : MathF.Floor(Math.Min(25000f / outline.Bounds.Width, 12800f / outline.Bounds.Height)));
		Extrude.Outline outline2 = _extrude.Get(x.Value, "Bahnschrift", bold: true, num);
		float num2 = (400f - outline2.Bounds.Width) / 2f + 8f;
		float num3 = 222f - outline2.Bounds.Height / 2f;
		int num4 = x.Fan.Serial.Sum((char c) => c) % 7;
		double num5 = -260.0 + Math.Sin(x.T * 0.21) * 300.0;
		double num6 = -900.0 + Math.Cos(x.T * 0.17) * 120.0;
		double num7 = num2 + outline2.Bounds.Width / 2f;
		double num8 = num3 + outline2.Bounds.Height / 2f;
		double num9 = num5 - num7;
		double num10 = num6 - num8;
		double num11 = Math.Sqrt(num9 * num9 + num10 * num10);
		double num12 = (double)num * 3.2 * (1.0 + 0.05 * Math.Sin(x.T * 0.6 + (double)num4));
		PointF d = new PointF((float)(num9 / num11 * num12), (float)(num10 / num11 * num12));
		Extrude.Shafts(g, outline2, num2, num3, d, 1.0 - (x.T * 0.16 + (double)num4 * 0.13) % 1.0);
		Extrude.Face(g, outline2, num2, num3, Imaging.Mix(x.C2, Color.White, 0.06));
		Color c2 = x.C1;
		string[] array = x.Label.Split(' ', 2);
		using StringFormat format = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};
		using SolidBrush brush = new SolidBrush(c2);
		using (Font font = new Font("Bahnschrift", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
		{
			g.DrawString((array.Length > 1) ? array[0] : "", font, brush, new RectangleF(40f, 296f, 320f, 18f), format);
		}
		using (Font font2 = new Font("Bahnschrift Condensed", 30f, FontStyle.Bold, GraphicsUnit.Pixel))
		{
			g.DrawString(array[^1], font2, brush, new RectangleF(40f, 312f, 320f, 38f), format);
		}
		using Font font3 = new Font("Bahnschrift", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
		RectangleF rectangleF = new RectangleF(166f, 352f, 68f, 22f);
		using (SolidBrush brush2 = new SolidBrush(x.C2))
		{
			g.FillRectangle(brush2, rectangleF);
		}
		using (Pen pen = new Pen(c2, 1.6f))
		{
			g.DrawRectangle(pen, rectangleF.X, rectangleF.Y, rectangleF.Width, rectangleF.Height);
		}
		g.DrawString(x.Time, font3, brush, rectangleF, format);
	}

	private void Gap(FaceContext x)
	{
		Graphics g = x.G;
		g.Clear(x.C2);
		string text = x.Value.TrimEnd('%', '°', 'W');
		if (!_rolls.TryGetValue(x.Fan.Serial, out (string, string, double) value))
		{
			value = (text, text, -10.0);
		}
		if (value.Item2 != text)
		{
			value = (value.Item2, text, x.T);
		}
		_rolls[x.Fan.Serial] = value;
		double num = Math.Clamp((x.T - value.Item3) / 0.7, 0.0, 1.0);
		num = 1.0 - Math.Pow(1.0 - num, 3.0);
		RectangleF rectangleF = new RectangleF(52f, 118f, 296f, 330f);
		float num2 = (float)((1.0 - Math.Cos(x.T * 0.45 + (double)(x.Fan.Serial.Sum((char c) => c) % 7))) * (double)rectangleF.Height * 0.03);
		GraphicsState gstate = g.Save();
		g.SetClip(rectangleF, CombineMode.Intersect);
		using (SolidBrush brush = new SolidBrush(x.C1))
		{
			if (num < 1.0)
			{
				TallNumber(g, value.Item1, rectangleF, num2 - (float)(num * (double)rectangleF.Height * 1.08), brush);
			}
			TallNumber(g, text, rectangleF, num2 + (float)((1.0 - num) * (double)rectangleF.Height * 1.08), brush);
		}
		g.Restore(gstate);
		using StringFormat format = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};
		using (SolidBrush brush2 = new SolidBrush(x.C1))
		{
			using Font font = new Font("Arial", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
			string text2 = (x.Value.EndsWith("%") ? " %" : (x.Value.EndsWith("°") ? " °C" : (x.Value.EndsWith("W") ? " W" : "")));
			g.DrawString(x.Label + text2, font, brush2, new RectangleF(40f, 84f, 320f, 24f), format);
		}
		using (Font font2 = new Font("Arial", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
		{
			using SolidBrush brush3 = new SolidBrush(Imaging.WithAlpha(x.C1, 170));
			g.DrawString("A SYSTEM OVERVIEW BY LUMALI", font2, brush3, new RectangleF(40f, 58f, 320f, 18f), format);
		}
		GraphicsState gstate2 = g.Save();
		g.TranslateTransform(92f, 286f);
		g.RotateTransform(-4f + (float)(Math.Sin(x.T * 0.9) * 1.8 + Math.Sin(x.T * 2.3) * 0.4));
		using (SolidBrush brush4 = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
		{
			g.FillRectangle(brush4, 3, 4, 190, 50);
		}
		using (SolidBrush brush5 = new SolidBrush(Color.FromArgb(247, 198, 0)))
		{
			g.FillRectangle(brush5, 0, 0, 190, 50);
		}
		using (Pen pen = new Pen(x.C1, 1.6f))
		{
			g.DrawRectangle(pen, 5, 5, 180, 40);
		}
		using (Font font3 = new Font("Arial", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
		{
			using SolidBrush brush6 = new SolidBrush(x.C1);
			g.DrawString("LIVE EDITION WITH", font3, brush6, 14f, 10f, StringFormat.GenericTypographic);
			g.DrawString(x.Time + " SYSTEM CLOCK", font3, brush6, 14f, 26f, StringFormat.GenericTypographic);
		}
		g.Restore(gstate2);
	}

	private void TallNumber(Graphics g, string text, RectangleF box, float dy, Brush brush)
	{
		using GraphicsPath graphicsPath = new GraphicsPath
		{
			FillMode = FillMode.Winding
		};
		graphicsPath.AddString(text, Family("Impact"), 0, 100f, PointF.Empty, StringFormat.GenericTypographic);
		RectangleF bounds = graphicsPath.GetBounds();
		if (!(bounds.Width < 1f) && !(bounds.Height < 1f))
		{
			float num = box.Height / bounds.Height;
			float num2 = Math.Min(num, box.Width / bounds.Width);
			GraphicsState gstate = g.Save();
			g.TranslateTransform(box.X + (box.Width - bounds.Width * num2) / 2f, box.Y + dy);
			g.ScaleTransform(num2, num);
			g.TranslateTransform(0f - bounds.X, 0f - bounds.Y);
			g.FillPath(brush, graphicsPath);
			g.Restore(gstate);
		}
	}

	public void Dispose()
	{
		foreach (Image value in _images.Values)
		{
			value?.Dispose();
		}
		_images.Clear();
		_extrude.Dispose();
	}
}
