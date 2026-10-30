using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcdStudio.UI;

internal sealed class PresetCard : NavyControl
{
	private readonly string _title;

	private readonly string _sub;

	private readonly Func<int> _percent;

	public string Mode { get; }

	public bool Selected { get; set; }

	public PresetCard(string mode, string title, string sub, Func<int> percent)
	{
		Mode = mode;
		_title = title;
		_sub = sub;
		_percent = percent;
		base.Margin = new Padding(0, 0, Theme.S(8f), Theme.S(10f));
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(Theme.Window);
		Theme.Smooth(graphics);
		RectangleF r = new RectangleF(1f, 1f, base.Width - 3, base.Height - 3);
		Theme.FillRound(graphics, r, Theme.S(12f), Hot ? Theme.Hover : Theme.Card, Selected ? Theme.Cyan : Theme.CardEdge, Selected ? 2.5f : 1f);
		Theme.DrawText(graphics, _title, Theme.BodyBold, Theme.Text, new RectangleF(Theme.S(12f), Theme.S(8f), base.Width - Theme.S(20f), Theme.S(22f)));
		int num = _percent();
		string mode = Mode;
		string text = ((mode == "Auto") ? "curve" : ((!(mode == "Hold")) ? (num + "%") : "as is"));
		string text2 = text;
		Theme.DrawText(graphics, text2, Theme.SmallBold, Theme.Cyan, new RectangleF(Theme.S(12f), Theme.S(30f), base.Width - Theme.S(20f), Theme.S(18f)));
		Theme.DrawText(graphics, _sub, Theme.Small, Theme.SubText, new RectangleF(Theme.S(12f), Theme.S(48f), base.Width - Theme.S(20f), Theme.S(20f)));
	}
}
