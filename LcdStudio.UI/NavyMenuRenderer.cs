using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class NavyMenuRenderer : ToolStripProfessionalRenderer
{
	private sealed class Colors : ProfessionalColorTable
	{
		public override Color ToolStripDropDownBackground => Theme.Field;

		public override Color ImageMarginGradientBegin => Theme.Field;

		public override Color ImageMarginGradientMiddle => Theme.Field;

		public override Color ImageMarginGradientEnd => Theme.Field;

		public override Color MenuBorder => Theme.FieldEdge;

		public override Color MenuItemBorder => Color.Transparent;

		public override Color SeparatorDark => Theme.FieldEdge;

		public override Color SeparatorLight => Theme.Field;
	}

	public NavyMenuRenderer()
		: base(new Colors())
	{
		base.RoundedEdges = true;
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		if (e.Item.Selected && e.Item.Enabled)
		{
			Theme.Smooth(e.Graphics);
			Theme.FillRound(e.Graphics, new RectangleF(Theme.S(4f), 1f, e.Item.Width - Theme.S(8f), e.Item.Height - 2), Theme.S(6f), Theme.Hover);
		}
	}

	protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
	{
		Theme.Smooth(e.Graphics);
		using SolidBrush brush = new SolidBrush(Theme.Cyan);
		e.Graphics.FillEllipse(brush, Theme.S(8f), e.Item.Height / 2 - Theme.S(3f), Theme.S(6f), Theme.S(6f));
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		e.TextColor = (e.Item.Enabled ? Theme.Text : Theme.Faint);
		Rectangle textRectangle = e.TextRectangle;
		textRectangle.X = Math.Max(e.TextRectangle.X, Theme.S(20f));
		e.TextRectangle = textRectangle;
		base.OnRenderItemText(e);
	}
}
