using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class Slider : NavyControl
{
	private int _value;

	private bool _drag;

	public int Minimum { get; set; }

	public int Maximum { get; set; } = 100;


	public Func<int, string> Format { get; set; } = (int v) => v.ToString();


	public int Value
	{
		get
		{
			return _value;
		}
		set
		{
			int num = Math.Clamp(value, Minimum, Maximum);
			if (num != _value)
			{
				_value = num;
				Invalidate();
				this.ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	private int LabelWidth => Theme.S(48f);

	private RectangleF Track => new RectangleF(Theme.S(8f), (float)base.Height / 2f - (float)Theme.S(1.5f), base.Width - Theme.S(16f) - LabelWidth, Theme.S(3f));

	public event EventHandler? ValueChanged;

	public event EventHandler? Committed;

	public Slider(int min = 0, int max = 100, int value = 0)
	{
		Minimum = min;
		Maximum = max;
		_value = Math.Clamp(value, min, max);
		base.Size = new Size(Theme.S(260f), Theme.S(28f));
		base.Margin = new Padding(0, 0, 0, Theme.S(2f));
	}

	public void SetSilently(int v)
	{
		_value = Math.Clamp(v, Minimum, Maximum);
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		PaintParent(graphics);
		Theme.Smooth(graphics);
		RectangleF track = Track;
		Theme.FillRound(graphics, track, track.Height / 2f, Theme.FieldEdge);
		float num = ((Maximum > Minimum) ? ((float)(_value - Minimum) / (float)(Maximum - Minimum)) : 0f);
		if (num > 0f)
		{
			RectangleF r = track;
			r.Width = Math.Max(track.Height, track.Width * num);
			Theme.FillRound(graphics, r, track.Height / 2f, base.Enabled ? Theme.Accent : Theme.Faint);
		}
		float num2 = Theme.S(14f);
		using (SolidBrush brush = new SolidBrush(Color.White))
		{
			graphics.FillEllipse(brush, track.X + track.Width * num - num2 / 2f, (float)base.Height / 2f - num2 / 2f, num2, num2);
		}
		Theme.DrawText(graphics, Format(_value), Theme.SmallBold, Theme.SubText, new RectangleF(base.Width - LabelWidth, 0f, LabelWidth, base.Height), StringAlignment.Far);
	}

	private void SetFromX(int x)
	{
		RectangleF track = Track;
		Value = Minimum + (int)Math.Round(((float)x - track.X) / track.Width * (float)(Maximum - Minimum));
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		Focus();
		_drag = true;
		SetFromX(e.X);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (_drag)
		{
			SetFromX(e.X);
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		if (_drag)
		{
			this.Committed?.Invoke(this, EventArgs.Empty);
		}
		_drag = false;
		base.OnMouseUp(e);
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		Value += Math.Sign(e.Delta);
		this.Committed?.Invoke(this, EventArgs.Empty);
		base.OnMouseWheel(e);
	}

	protected override bool IsInputKey(Keys k)
	{
		if ((k != Keys.Left && k != Keys.Right) || 1 == 0)
		{
			return base.IsInputKey(k);
		}
		return true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Left)
		{
			Value--;
			this.Committed?.Invoke(this, EventArgs.Empty);
		}
		if (e.KeyCode == Keys.Right)
		{
			Value++;
			this.Committed?.Invoke(this, EventArgs.Empty);
		}
		base.OnKeyDown(e);
	}
}
