using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public abstract class NavyControl : Control
{
	protected bool Hot;

	protected bool Down;

	protected NavyControl()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		Font = Theme.Body;
		ForeColor = Theme.Text;
		Cursor = Cursors.Hand;
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		Hot = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		Hot = false;
		Down = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			Down = true;
			Invalidate();
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		Down = false;
		Invalidate();
		base.OnMouseUp(e);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		Invalidate();
		base.OnEnabledChanged(e);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		Invalidate();
		base.OnTextChanged(e);
	}

	protected void PaintParent(Graphics g)
	{
		Color color = ((base.Parent is Card) ? Theme.Card : (base.Parent?.BackColor ?? Theme.Window));
		if (color == Color.Transparent)
		{
			color = Theme.Card;
		}
		g.Clear(color);
	}
}
