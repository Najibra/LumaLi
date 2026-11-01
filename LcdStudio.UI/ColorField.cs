using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class ColorField : Panel
{
	private sealed class NavySwatch : NavyControl
	{
		public Color Value;

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			PaintParent(graphics);
			Theme.Smooth(graphics);
			Theme.FillRound(graphics, new RectangleF(1f, 1f, base.Width - 3, base.Height - 3), Theme.S(6f), Value, Color.FromArgb(70, 255, 255, 255));
		}
	}

	private readonly TextField _hex;

	private readonly NavySwatch _swatch;

	public string Hex
	{
		get
		{
			return _hex.Text;
		}
		set
		{
			bool num = !string.Equals(_hex.Text, value, StringComparison.OrdinalIgnoreCase) || _swatch.Value != Imaging.Hex(value, _swatch.Value);
			_hex.Text = value;
			_swatch.Value = Imaging.Hex(value, _swatch.Value);
			_swatch.Invalidate();
			if (num)
			{
				this.ColorChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? ColorChanged;

	/// <param name="hexWidth">Width of the hex box; narrower where the field shares a row.</param>
	public ColorField(string hex, int hexWidth = 130)
	{
		base.Size = new Size(Theme.S(38 + hexWidth), Theme.S(30f));
		base.Margin = new Padding(0, 0, Theme.S(8f), Theme.S(4f));
		BackColor = Theme.Card;
		_swatch = new NavySwatch
		{
			Bounds = new Rectangle(0, 0, Theme.S(30f), Theme.S(30f))
		};
		_hex = new TextField(hex, hexWidth)
		{
			Location = new Point(Theme.S(38f), 0)
		};
		_swatch.Value = Imaging.Hex(hex, Color.White);
		_swatch.Click += delegate
		{
			using ColorDialog colorDialog = new ColorDialog
			{
				Color = _swatch.Value,
				FullOpen = true
			};
			if (colorDialog.ShowDialog(FindForm()) == DialogResult.OK)
			{
				Hex = Imaging.Hex(colorDialog.Color);
			}
		};
		_hex.Box.Leave += delegate
		{
			Commit();
		};
		_hex.Box.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				Commit();
				e.SuppressKeyPress = true;
			}
		};
		base.Controls.Add(_swatch);
		base.Controls.Add(_hex);
	}

	private void Commit()
	{
		Color c = Imaging.Hex(_hex.Text, _swatch.Value);
		Hex = Imaging.Hex(c);
	}

	public void SetSilently(string hex)
	{
		_hex.Text = hex;
		_swatch.Value = Imaging.Hex(hex, _swatch.Value);
		_swatch.Invalidate();
	}
}
