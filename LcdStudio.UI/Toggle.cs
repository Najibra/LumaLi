using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class Toggle : NavyControl
{
	private bool _on;

	public bool Checked
	{
		get
		{
			return _on;
		}
		set
		{
			if (_on != value)
			{
				_on = value;
				Invalidate();
				this.CheckedChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? CheckedChanged;

	public Toggle(string text, bool on = false)
	{
		Text = text;
		_on = on;
		base.Size = new Size(Theme.S(46f) + ((text.Length > 0) ? (TextRenderer.MeasureText(text, Font).Width + Theme.S(8f)) : 0), Theme.S(28f));
		base.Margin = new Padding(0, 0, Theme.S(12f), Theme.S(4f));
	}

	public void SetSilently(bool on)
	{
		_on = on;
		Invalidate();
	}

	protected override void OnClick(EventArgs e)
	{
		Checked = !Checked;
		base.OnClick(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		PaintParent(graphics);
		Theme.Smooth(graphics);
		RectangleF r = new RectangleF(Theme.S(2f), (float)(base.Height - Theme.S(20f)) / 2f, Theme.S(38f), Theme.S(20f));
		Theme.FillRound(graphics, r, r.Height / 2f, _on ? (base.Enabled ? Theme.Accent : Theme.Faint) : Theme.Field, _on ? null : new Color?(Theme.FieldEdge));
		float num = r.Height - (float)Theme.S(6f);
		using (SolidBrush brush = new SolidBrush(_on ? Color.White : Theme.SubText))
		{
			graphics.FillEllipse(brush, _on ? (r.Right - num - (float)Theme.S(3f)) : (r.X + (float)Theme.S(3f)), r.Y + (float)Theme.S(3f), num, num);
		}
		if (Text.Length > 0)
		{
			Theme.DrawText(graphics, Text, Font, base.Enabled ? Theme.Text : Theme.Faint, new RectangleF(r.Right + (float)Theme.S(8f), 0f, (float)base.Width - r.Right - (float)Theme.S(8f), base.Height));
		}
	}
}
