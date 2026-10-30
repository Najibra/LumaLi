using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LcdStudio.UI;

public sealed class Select : NavyControl
{
	private int _index = -1;

	public List<string> Items { get; } = new List<string>();


	public Func<string, string>? Display { get; set; }

	public int SelectedIndex
	{
		get
		{
			return _index;
		}
		set
		{
			if (_index != value)
			{
				_index = value;
				Invalidate();
				this.SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public string? SelectedItem
	{
		get
		{
			if (_index < 0 || _index >= Items.Count)
			{
				return null;
			}
			return Items[_index];
		}
		set
		{
			int num = ((value == null) ? (-1) : Items.IndexOf(value));
			if (value != null && num < 0)
			{
				Items.Add(value);
				num = Items.Count - 1;
			}
			SelectedIndex = num;
		}
	}

	public event EventHandler? SelectedIndexChanged;

	private string Show(string item)
	{
		return Display?.Invoke(item) ?? item;
	}

	public Select(IEnumerable<string>? items = null, string? selected = null)
	{
		if (items != null)
		{
			Items.AddRange(items);
		}
		if (selected != null)
		{
			_index = Items.IndexOf(selected);
		}
		SetStyle(ControlStyles.Selectable, value: true);
		base.Size = new Size(Theme.S(200f), Theme.S(30f));
		base.Margin = new Padding(0, 0, Theme.S(8f), Theme.S(4f));
	}

	public void SetSilently(string? value)
	{
		_index = ((value == null) ? (-1) : Items.IndexOf(value));
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		PaintParent(graphics);
		Theme.Smooth(graphics);
		RectangleF r = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
		Theme.FillRound(graphics, r, Theme.S(7f), Hot ? Theme.Hover : Theme.Field, Theme.FieldEdge);
		string selectedItem = SelectedItem;
		Theme.DrawText(graphics, (selectedItem != null) ? Show(selectedItem) : "", Font, base.Enabled ? Theme.Text : Theme.Faint, new RectangleF(Theme.S(10f), 0f, base.Width - Theme.S(34f), base.Height));
		float num = base.Width - Theme.S(15f);
		float num2 = (float)base.Height / 2f;
		using Pen pen = new Pen(Theme.SubText, Theme.S(1.6f))
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		};
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(num - (float)Theme.S(4f), num2 - (float)Theme.S(2f)),
			new PointF(num, num2 + (float)Theme.S(2f)),
			new PointF(num + (float)Theme.S(4f), num2 - (float)Theme.S(2f))
		});
	}

	protected override void OnClick(EventArgs e)
	{
		base.OnClick(e);
		ContextMenuStrip menu = new ContextMenuStrip
		{
			Renderer = new NavyMenuRenderer(),
			Font = Font,
			ShowImageMargin = false,
			MaximumSize = new Size(0, Theme.S(460f))
		};
		for (int i = 0; i < Items.Count; i++)
		{
			int idx = i;
			ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(Show(Items[i]))
			{
				Checked = (i == _index),
				AutoSize = false,
				Height = Theme.S(28f)
			};
			toolStripMenuItem.Width = Math.Max(base.Width, TextRenderer.MeasureText(Show(Items[i]), Font).Width + Theme.S(40f));
			toolStripMenuItem.Click += delegate
			{
				SelectedIndex = idx;
			};
			menu.Items.Add(toolStripMenuItem);
		}
		menu.Closed += delegate
		{
			BeginInvoke(menu.Dispose);
		};
		menu.Show(this, new Point(0, base.Height));
	}
}
