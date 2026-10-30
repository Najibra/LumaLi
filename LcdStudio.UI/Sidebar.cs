using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class Sidebar : Control
{
	private readonly Engine _engine;

	private readonly string[] _items;

	private string _selected = "Home";

	private int _hot = -1;

	private static readonly string[] Icons = new string[7] { "\ue80f", "\ue7f4", "\uf0e2", "\ue9ca", "\ue781", "\uec4a", "\ue713" };

	private int _tick;

	private static readonly Font LogoIcon = new Font(Theme.Icons.FontFamily, 8.5f);

	private static readonly Font LogoFont = new Font("Segoe UI Semibold", 12.5f);

	public string Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			_selected = value;
			Invalidate();
		}
	}

	private static Color Sidebar0 => Theme.Sidebar;

	public event Action<string>? Navigate;

	public Sidebar(Engine engine, string[] items)
	{
		_engine = engine;
		_items = items;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Cursor = Cursors.Hand;
	}

	private Rectangle Item(int i)
	{
		return new Rectangle(Theme.S(12f), Theme.S(70f) + i * Theme.S(40f), base.Width - Theme.S(24f), Theme.S(34f));
	}

	public void Tick()
	{
		if (++_tick % 7 == 0)
		{
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(Sidebar0);
		Theme.Smooth(graphics);
		using (SolidBrush brush = new SolidBrush(Theme.Cyan))
		{
			graphics.FillEllipse(brush, Theme.S(18f), Theme.S(22f), Theme.S(22f), Theme.S(22f));
		}
		Theme.DrawText(graphics, "\ue7f4", LogoIcon, Theme.Window, new RectangleF(Theme.S(18f), Theme.S(22f), Theme.S(22f), Theme.S(22f)), StringAlignment.Center);
		Theme.DrawText(graphics, "LumaLi", LogoFont, Theme.Text, new RectangleF(Theme.S(48f), Theme.S(16f), base.Width - Theme.S(50f), Theme.S(34f)));
		for (int i = 0; i < _items.Length; i++)
		{
			Rectangle rectangle = Item(i);
			bool flag = _items[i] == _selected;
			if (flag)
			{
				Theme.FillRound(graphics, rectangle, (float)rectangle.Height / 2f, Theme.Accent);
			}
			else if (i == _hot)
			{
				Theme.FillRound(graphics, rectangle, (float)rectangle.Height / 2f, Color.FromArgb(40, 52, 120));
			}
			Theme.DrawText(graphics, Icons[i], Theme.Icons, flag ? Color.White : Theme.SubText, new RectangleF(rectangle.X + Theme.S(8f), rectangle.Y, Theme.S(26f), rectangle.Height), StringAlignment.Center);
			Theme.DrawText(graphics, _items[i], flag ? Theme.BodyBold : Theme.Body, flag ? Color.White : Color.FromArgb(215, 222, 250), new RectangleF(rectangle.X + Theme.S(40f), rectangle.Y, rectangle.Width - Theme.S(44f), rectangle.Height));
		}
		List<(string, string, Color)> list = new List<(string, string, Color)>
		{
			("Screen", (!_engine.Screen.Present) ? "none found" : ((!_engine.Screen.Running) ? _engine.Screen.State.Split(':')[0] : (_engine.Screen.State.Contains("redraws") ? "on · still layout" : $"{_engine.Screen.Fps:0} fps")), Dot(_engine.Screen)),
			("Fan screens", (_engine.FanOutputs.Count == 0) ? "none found" : $"{_engine.FanOutputs.Count((OutputStatus f) => f.Running)} of {_engine.FanOutputs.Count}", _engine.FanOutputs.Any((OutputStatus f) => f.Running) ? Theme.Good : Theme.Faint),
			("Light strip", (!_engine.Strip.Present) ? "none found" : (_engine.Settings.Lighting.StripOn ? "on" : "off"), Dot(_engine.Strip)),
			("Fan lights", (_engine.Hub == null) ? "—" : (_engine.Settings.Lighting.FansOn ? _engine.Settings.Lighting.FanEffect : "off"), (_engine.Hub != null) ? Theme.Good : Theme.Faint),
			("Fan speed", (_engine.Hub == null) ? "—" : ((_engine.Settings.Cooling.Mode == "Hold") ? "not controlled" : "controlled"), (_engine.Hub != null && _engine.Settings.Cooling.Mode != "Hold") ? Theme.Good : Theme.Faint)
		};
		float num = base.Height - Theme.S(26f) - list.Count * Theme.S(19f);
		if (_engine.Released)
		{
			Theme.DrawText(graphics, "Devices released", Theme.SmallBold, Theme.Warn, new RectangleF(Theme.S(18f), num - (float)Theme.S(22f), base.Width - Theme.S(30f), Theme.S(18f)));
		}
		foreach (var item in list)
		{
			var (text, text2, _) = item;
			using (SolidBrush brush2 = new SolidBrush(item.Item3))
			{
				graphics.FillEllipse(brush2, Theme.S(18f), num + (float)Theme.S(6f), Theme.S(6f), Theme.S(6f));
			}
			Theme.DrawText(graphics, text, Theme.Small, Theme.SubText, new RectangleF(Theme.S(30f), num, Theme.S(80f), Theme.S(18f)));
			Theme.DrawText(graphics, text2, Theme.Small, Theme.Text, new RectangleF(Theme.S(100f), num, base.Width - Theme.S(108f), Theme.S(18f)), StringAlignment.Far);
			num += (float)Theme.S(19f);
		}
		Theme.DrawText(graphics, "v" + Application.ProductVersion.Split('+')[0], Theme.Small, Theme.Faint, new RectangleF(Theme.S(18f), base.Height - Theme.S(24f), base.Width, Theme.S(18f)));
	}

	private static Color Dot(OutputStatus s)
	{
		if (s.Present)
		{
			if (!s.Running)
			{
				if (!s.State.StartsWith("error"))
				{
					return Theme.Warn;
				}
				return Theme.Bad;
			}
			return Theme.Good;
		}
		return Theme.Faint;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		MouseEventArgs e2 = e;
		int num = Enumerable.Range(0, _items.Length).FirstOrDefault((int i) => Item(i).Contains(e2.Location), -1);
		if (num != _hot)
		{
			_hot = num;
			Invalidate();
		}
		base.OnMouseMove(e2);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hot = -1;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		for (int i = 0; i < _items.Length; i++)
		{
			if (Item(i).Contains(e.Location))
			{
				this.Navigate?.Invoke(_items[i]);
			}
		}
		base.OnMouseDown(e);
	}
}
