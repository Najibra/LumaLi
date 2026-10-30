using System.Drawing;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public abstract class Page : Panel
{
	protected readonly Engine E;

	protected readonly MainForm Main;

	protected readonly FlowLayoutPanel Body;

	private readonly Label _title;

	private readonly Label _subtitle;

	public abstract string Title { get; }

	public abstract string Subtitle { get; }

	private static Font Body0 => Theme.Body;

	protected Page(Engine engine, MainForm main)
	{
		E = engine;
		Main = main;
		DoubleBuffered = true;
		AutoScroll = true;
		BackColor = Theme.Window;
		base.Padding = new Padding(Theme.S(26f), Theme.S(18f), Theme.S(18f), Theme.S(18f));
		Body = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = true,
			BackColor = Theme.Window,
			Padding = new Padding(0, Theme.S(6f), 0, Theme.S(10f))
		};
		_subtitle = new Label
		{
			Dock = DockStyle.Top,
			Height = Theme.S(30f),
			ForeColor = Theme.SubText,
			Font = Body0,
			BackColor = Theme.Window,
			UseMnemonic = false
		};
		_title = new Label
		{
			Dock = DockStyle.Top,
			Height = Theme.S(40f),
			ForeColor = Theme.Text,
			Font = Theme.Title,
			BackColor = Theme.Window,
			UseMnemonic = false
		};
		base.Controls.Add(Body);
		base.Controls.Add(_subtitle);
		base.Controls.Add(_title);
		base.HandleCreated += delegate
		{
			_title.Text = Title;
			_subtitle.Text = Subtitle;
		};
	}

	public virtual void Tick()
	{
	}

	public virtual void Refresh0()
	{
	}

	public virtual void DevicesChanged()
	{
	}

	protected Card AddCard(string? title, int width, int height = 0)
	{
		Card card = new Card(title)
		{
			Width = Theme.S(width)
		};
		if (height > 0)
		{
			card.Height = Theme.S(height);
		}
		else
		{
			card.AutoSize = true;
			card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			card.MinimumSize = new Size(Theme.S(width), 0);
			card.MaximumSize = new Size(Theme.S(width), 0);
		}
		Body.Controls.Add(card);
		return card;
	}

	protected static void Break(Control c)
	{
		c.Parent?.Controls.SetChildIndex(c, c.Parent.Controls.GetChildIndex(c));
	}
}
