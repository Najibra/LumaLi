using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class Btn : NavyControl
{
	public bool Primary { get; set; }

	public string? Icon { get; set; }

	public Btn(string text, Action? onClick = null, bool primary = false, string? icon = null)
	{
		Action onClick2 = onClick;
		Text = text;
		Primary = primary;
		Icon = icon;
		if (onClick2 != null)
		{
			base.Click += delegate
			{
				onClick2();
			};
		}
		SetStyle(ControlStyles.StandardClick | ControlStyles.Selectable, value: true);
		base.Size = GetPreferredSize(Size.Empty);
		base.Margin = new Padding(0, 0, Theme.S(8f), Theme.S(4f));
	}

	public override Size GetPreferredSize(Size proposed)
	{
		Size size = TextRenderer.MeasureText(Text, Font);
		int num = ((Icon != null) ? Theme.S(22f) : 0);
		return new Size(Math.Max(Theme.S(40f), size.Width + Theme.S(28f) + num - ((Text.Length == 0) ? Theme.S(14f) : 0)), Theme.S(32f));
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		PaintParent(graphics);
		Theme.Smooth(graphics);
		RectangleF r = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
		Color fill = ((!base.Enabled) ? Theme.Card : ((!Primary) ? (Down ? Theme.CardEdge : (Hot ? Theme.Hover : Theme.Field)) : (Down ? Color.FromArgb(205, 205, 208) : (Hot ? Color.White : Theme.PrimaryFill))));
		Theme.FillRound(graphics, r, Theme.S(8f), fill, (Primary && base.Enabled) ? null : new Color?(Hot ? Theme.FieldEdge : Theme.CardEdge));
		Color ink = !base.Enabled ? Theme.Faint : (Primary ? Theme.PrimaryText : Theme.Text);
		float num = Theme.S(12f);
		if (Icon != null)
		{
			Theme.DrawText(graphics, Icon, Theme.Icons, ink, new RectangleF(num - (float)Theme.S(2f), 0f, Theme.S(22f), base.Height), StringAlignment.Center);
			num += (float)Theme.S(20f);
		}
		if (Text.Length > 0)
		{
			Theme.DrawText(graphics, Text, Primary ? Theme.BodyBold : Font, ink, new RectangleF(num, 0f, (float)base.Width - num - (float)Theme.S(10f), base.Height), (Icon == null) ? StringAlignment.Center : StringAlignment.Near);
		}
	}

	protected override void OnKeyUp(KeyEventArgs e)
	{
		Keys keyCode = e.KeyCode;
		if ((keyCode == Keys.Return || keyCode == Keys.Space) ? true : false)
		{
			OnClick(EventArgs.Empty);
		}
		base.OnKeyUp(e);
	}
}
