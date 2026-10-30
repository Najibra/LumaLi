using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public class Stack : FlowLayoutPanel
{
	public Stack(bool horizontal = false)
	{
		base.FlowDirection = ((!horizontal) ? FlowDirection.TopDown : FlowDirection.LeftToRight);
		base.WrapContents = horizontal;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		BackColor = Color.Transparent;
		base.Margin = Padding.Empty;
		base.Padding = Padding.Empty;
	}
}
