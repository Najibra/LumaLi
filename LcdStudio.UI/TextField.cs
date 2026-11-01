using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class TextField : Panel
{
	public TextBox Box { get; } = new TextBox
	{
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Field,
		ForeColor = Theme.Text,
		Font = Theme.Body
	};


	public override string Text
	{
		get
		{
			return Box.Text;
		}
		[param: AllowNull]
		set
		{
			Box.Text = value ?? "";
		}
	}

	public TextField(string text = "", int width = 200)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		base.Size = new Size(Theme.S(width), Theme.S(30f));
		base.Margin = new Padding(0, 0, Theme.S(8f), Theme.S(4f));
		Box.Text = text;
		base.Controls.Add(Box);
	}

	protected override void OnLayout(LayoutEventArgs e)
	{
		base.OnLayout(e);
		Box.SetBounds(Theme.S(10f), (base.Height - Box.PreferredHeight) / 2, base.Width - Theme.S(20f), Box.PreferredHeight);
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear((base.Parent is Card) ? Theme.Card : (base.Parent?.BackColor ?? Theme.Window));
		Theme.Smooth(graphics);
		Theme.FillRound(graphics, new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f), Theme.S(8f), Theme.Field, Box.Focused ? Theme.Cyan : Theme.CardEdge);
	}
}
