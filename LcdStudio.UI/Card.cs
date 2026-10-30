using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LcdStudio.UI;

public class Card : Panel
{
	private string? _title;

	public string? RightText { get; set; }

	public Color? Border { get; set; }

	[DefaultValue(null)]
	public string? Title
	{
		get
		{
			return _title;
		}
		set
		{
			_title = value;
			base.Padding = new Padding(Theme.S(16f), Theme.S((value == null) ? 14 : 40), Theme.S(16f), Theme.S(14f));
			Invalidate();
		}
	}

	public Card(string? title = null)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Theme.Card;
		ForeColor = Theme.Text;
		Font = Theme.Body;
		Title = title;
		base.Margin = new Padding(0, 0, Theme.S(14f), Theme.S(14f));
	}

	protected override void OnControlAdded(ControlEventArgs e)
	{
		base.OnControlAdded(e);
		if (base.Controls.Count == 1 && e.Control.Dock == DockStyle.None && e.Control.Location == Point.Empty)
		{
			e.Control.Location = new Point(base.Padding.Left, base.Padding.Top);
			e.Control.Margin = Padding.Empty;
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(base.Parent?.BackColor ?? Theme.Window);
		Theme.Smooth(graphics);
		using (GraphicsPath path = Theme.Round(new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f), Theme.S(14f)))
		{
			using (SolidBrush brush = new SolidBrush(Theme.Card))
			{
				graphics.FillPath(brush, path);
			}
			using Pen pen = new Pen(Border ?? Theme.CardEdge, (!Border.HasValue) ? 1 : 2);
			graphics.DrawPath(pen, path);
		}
		if (_title != null)
		{
			Theme.DrawText(graphics, _title.ToUpperInvariant(), Theme.Heading, Color.FromArgb(225, 232, 255), new RectangleF(Theme.S(16f), Theme.S(10f), base.Width - Theme.S(32f), Theme.S(24f)));
		}
		if (RightText != null)
		{
			Theme.DrawText(graphics, RightText, Theme.Small, Theme.Cyan, new RectangleF(Theme.S(16f), Theme.S(10f), base.Width - Theme.S(32f), Theme.S(24f)), StringAlignment.Far);
		}
	}
}
