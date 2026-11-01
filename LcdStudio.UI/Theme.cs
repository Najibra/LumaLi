using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace LcdStudio.UI;

public static class Theme
{
	public static class Glyph
	{
		public const string Home = "\ue80f";

		public const string Screen = "\ue7f4";

		public const string Templates = "\uf0e2";

		public const string Fan = "\ue9ca";

		public const string Light = "\ue781";

		public const string Cool = "\ue9ca";

		public const string Settings = "\ue713";

		public const string Undo = "\ue7a7";

		public const string Redo = "\ue7a6";

		public const string Add = "\ue710";

		public const string Delete = "\ue74d";

		public const string Copy = "\ue8c8";

		public const string Save = "\ue74e";

		public const string Upload = "\ue898";

		public const string Folder = "\ue838";

		public const string Refresh = "\ue72c";

		public const string Eye = "\ue7b3";

		public const string Warning = "\ue7ba";

		public const string Snow = "\ue9ca";

		public const string Bulb = "\uea80";

		public const string Gauge = "\uec4a";
	}

	// a quiet, near-black palette: neutral greys, white text and one red-orange accent (the logo's dot)

	public static readonly Color Window = Color.FromArgb(11, 11, 13);

	public static readonly Color WindowBottom = Color.FromArgb(11, 11, 13);

	public static readonly Color Sidebar = Color.FromArgb(15, 15, 18);

	public static readonly Color Card = Color.FromArgb(20, 21, 24);

	public static readonly Color CardEdge = Color.FromArgb(33, 34, 39);

	public static readonly Color Field = Color.FromArgb(27, 28, 32);

	public static readonly Color FieldEdge = Color.FromArgb(44, 46, 52);

	public static readonly Color Hover = Color.FromArgb(36, 38, 43);

	public static readonly Color Accent = Color.FromArgb(255, 75, 43);

	/// <summary>The accent's lighter tint, for links, small highlights and the focus edge.</summary>
	public static readonly Color Cyan = Color.FromArgb(255, 124, 96);

	public static readonly Color Text = Color.FromArgb(242, 242, 243);

	public static readonly Color SubText = Color.FromArgb(150, 152, 160);

	public static readonly Color Faint = Color.FromArgb(92, 95, 103);

	public static readonly Color Good = Color.FromArgb(52, 211, 153);

	public static readonly Color Bad = Color.FromArgb(255, 99, 99);

	public static readonly Color Warn = Color.FromArgb(250, 190, 70);

	/// <summary>The main button: white with black text, so the one thing to press stands out without colour.</summary>
	public static readonly Color PrimaryFill = Color.FromArgb(242, 242, 243);

	public static readonly Color PrimaryText = Color.FromArgb(11, 11, 13);

	public static readonly string TextFamily = Has("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

	public static readonly string TextBoldFamily = Has("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";

	public static readonly string DisplayFamily = Has("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";

	public static Font Body = new Font(TextFamily, 9.5f);

	public static Font BodyBold = new Font(TextBoldFamily, 9.5f);

	public static Font Small = new Font(TextFamily, 8.25f);

	public static Font SmallBold = new Font(TextBoldFamily, 8.25f);

	public static Font Heading = new Font(TextBoldFamily, 8.25f);

	public static Font Title = new Font(TextBoldFamily, 18f);

	public static Font Stat = new Font(DisplayFamily, 20f);

	public static Font Icons = new Font(IconFamily(), 11f);

	private static bool Has(string family)
	{
		using InstalledFontCollection fonts = new InstalledFontCollection();
		return fonts.Families.Any((FontFamily f) => f.Name == family);
	}
	public static float Scale { get; set; } = 1f;


	public static int S(float px)
	{
		return (int)Math.Round(px * Scale);
	}

	private static string IconFamily()
	{
		using InstalledFontCollection installedFontCollection = new InstalledFontCollection();
		return installedFontCollection.Families.Any((FontFamily f) => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
	}

	public static GraphicsPath Round(RectangleF r, float radius)
	{
		return Imaging.Round(r, radius);
	}

	public static void Smooth(Graphics g)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
	}

	public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? edge = null, float edgeWidth = 1f)
	{
		using GraphicsPath path = Round(r, radius);
		using (SolidBrush brush = new SolidBrush(fill))
		{
			g.FillPath(brush, path);
		}
		if (edge.HasValue)
		{
			Color valueOrDefault = edge.GetValueOrDefault();
			using Pen pen = new Pen(valueOrDefault, edgeWidth);
			g.DrawPath(pen, path);
			return;
		}
	}

	public static void DrawText(Graphics g, string text, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center, bool wrap = false)
	{
		using SolidBrush brush = new SolidBrush(c);
		using StringFormat stringFormat = new StringFormat
		{
			Alignment = h,
			LineAlignment = v,
			Trimming = StringTrimming.EllipsisCharacter
		};
		if (!wrap)
		{
			stringFormat.FormatFlags |= StringFormatFlags.NoWrap;
		}
		g.DrawString(text, f, brush, r, stringFormat);
	}
}
