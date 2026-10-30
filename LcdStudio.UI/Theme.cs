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

	public static readonly Color Window = Color.FromArgb(26, 33, 82);

	public static readonly Color WindowBottom = Color.FromArgb(20, 26, 66);

	public static readonly Color Sidebar = Color.FromArgb(22, 28, 72);

	public static readonly Color Card = Color.FromArgb(42, 56, 128);

	public static readonly Color CardEdge = Color.FromArgb(64, 82, 170);

	public static readonly Color Field = Color.FromArgb(31, 42, 100);

	public static readonly Color FieldEdge = Color.FromArgb(70, 90, 180);

	public static readonly Color Hover = Color.FromArgb(52, 68, 150);

	public static readonly Color Accent = Color.FromArgb(47, 128, 255);

	public static readonly Color Cyan = Color.FromArgb(63, 200, 255);

	public static readonly Color Text = Color.White;

	public static readonly Color SubText = Color.FromArgb(170, 182, 232);

	public static readonly Color Faint = Color.FromArgb(118, 130, 190);

	public static readonly Color Good = Color.FromArgb(70, 220, 150);

	public static readonly Color Bad = Color.FromArgb(255, 110, 110);

	public static readonly Color Warn = Color.FromArgb(255, 196, 70);

	public static Font Body = new Font("Segoe UI", 9.5f);

	public static Font BodyBold = new Font("Segoe UI Semibold", 9.5f);

	public static Font Small = new Font("Segoe UI", 8.25f);

	public static Font SmallBold = new Font("Segoe UI Semibold", 8.25f);

	public static Font Heading = new Font("Segoe UI Semibold", 9f);

	public static Font Title = new Font("Segoe UI Semibold", 17f);

	public static Font Stat = new Font("Segoe UI Semibold", 18f);

	public static Font Icons = new Font(IconFamily(), 11f);

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
