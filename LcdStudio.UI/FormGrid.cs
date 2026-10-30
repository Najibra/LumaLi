using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class FormGrid : TableLayoutPanel
{
	public FormGrid(int labelWidth = 120)
	{
		base.ColumnCount = 2;
		base.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(labelWidth)));
		base.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		BackColor = Color.Transparent;
		base.Margin = Padding.Empty;
	}

	public T Add<T>(string label, T control) where T : Control
	{
		Caption value = new Caption(label)
		{
			Margin = new Padding(0, Theme.S(7f), Theme.S(6f), Theme.S(2f))
		};
		base.Controls.Add(value);
		base.Controls.Add(control);
		return control;
	}

	public void AddWide(Control c)
	{
		base.Controls.Add(c);
		SetColumnSpan(c, 2);
	}
}
