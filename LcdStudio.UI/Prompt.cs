using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public static class Prompt
{
	public static string? Ask(IWin32Window owner, string title, string label, string value)
	{
		Form f = new Form
		{
			Text = title,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			StartPosition = FormStartPosition.CenterParent,
			MinimizeBox = false,
			MaximizeBox = false,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			ClientSize = new Size(Theme.S(380f), Theme.S(130f)),
			Font = Theme.Body
		};
		try
		{
			TextField textField = new TextField(value, 340)
			{
				Location = new Point(Theme.S(20f), Theme.S(40f))
			};
			Btn btn = new Btn("OK", null, primary: true)
			{
				Location = new Point(Theme.S(200f), Theme.S(84f))
			};
			btn.Click += delegate
			{
				f.DialogResult = DialogResult.OK;
			};
			Btn btn2 = new Btn("Cancel")
			{
				Location = new Point(Theme.S(270f), Theme.S(84f))
			};
			btn2.Click += delegate
			{
				f.DialogResult = DialogResult.Cancel;
			};
			f.Controls.Add(new Label
			{
				Text = label,
				Location = new Point(Theme.S(20f), Theme.S(14f)),
				AutoSize = true,
				ForeColor = Theme.SubText
			});
			f.Controls.Add(textField);
			f.Controls.Add(btn);
			f.Controls.Add(btn2);
			textField.Box.KeyDown += delegate(object? _, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Return)
				{
					f.DialogResult = DialogResult.OK;
				}
			};
			return (f.ShowDialog(owner) == DialogResult.OK) ? textField.Text : null;
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
	}
}
