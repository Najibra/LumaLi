using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class Caption : Label
{
	public Caption(string text, Color? color = null, Font? font = null, int width = 0)
	{
		Text = text;
		ForeColor = color ?? Theme.SubText;
		Font = font ?? Theme.Body;
		BackColor = Color.Transparent;
		AutoSize = width == 0;
		if (width > 0)
		{
			base.Size = new Size(Theme.S(width), Theme.S(30f));
			TextAlign = ContentAlignment.MiddleLeft;
			base.AutoEllipsis = true;
		}
		base.Margin = new Padding(0, Theme.S(6f), Theme.S(8f), Theme.S(4f));
		base.UseMnemonic = false;
	}
}
