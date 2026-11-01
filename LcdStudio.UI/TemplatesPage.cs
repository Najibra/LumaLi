using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class TemplatesPage : Page
{
	private readonly List<(Card Card, ScreenPreview Preview, string Name)> _cards = new List<(Card, ScreenPreview, string)>();

	private List<string> _built = new List<string>();

	public override string Title => "Templates";

	public override string Subtitle => "Layouts for the 8.8\" screen. Use one as it is, or edit it — built-ins are copied the first time you change them.";

	public TemplatesPage(Engine e, MainForm m)
		: base(e, m)
	{
	}

	public override void Refresh0()
	{
		List<string> list = (from l in E.VisibleLayouts()
			select l.Name).Append("hidden:" + E.Settings.HiddenTemplates.Count).ToList();
		if (_built.SequenceEqual(list))
		{
			Highlight();
			return;
		}
		_built = list;
		Body.SuspendLayout();
		List<Control> list2 = Body.Controls.Cast<Control>().ToList();
		Body.Controls.Clear();
		foreach (Control item in list2)
		{
			item.Dispose();
		}
		_cards.Clear();
		List<ScreenLayout> wide = E.VisibleLayouts().Where((ScreenLayout l) => !l.Portrait).ToList();
		List<ScreenLayout> tall = E.VisibleLayouts().Where((ScreenLayout l) => l.Portrait).ToList();
		if (tall.Count > 0)
		{
			Section("Wide layouts", wide.Count, first: true);
		}
		foreach (ScreenLayout item2 in wide.Concat(tall))
		{
			if (item2 == tall.FirstOrDefault())
			{
				Section("Vertical layouts — for a screen stood on its end", tall.Count, first: wide.Count == 0);
			}
			bool portrait = item2.Portrait;
			Card card = AddCard(null, portrait ? 256 : 530);
			string name = item2.Name;
			ScreenPreview screenPreview = new ScreenPreview(E)
			{
				Size = portrait ? new Size(Theme.S(150f), Theme.S(596f)) : new Size(Theme.S(498f), Theme.S(126f)),
				Anchor = AnchorStyles.Top | AnchorStyles.Left,
				Source = () => E.Layouts.FirstOrDefault((ScreenLayout l) => l.Name == name) ?? E.CurrentLayout,
				IntervalMs = 1500
			};
			screenPreview.Click += delegate
			{
				Use(name);
			};
			Caption value = new Caption(name, Theme.Text, Theme.BodyBold)
			{
				Margin = new Padding(0, Theme.S(8f), Theme.S(8f), 0)
			};
			Caption value2 = new Caption((item2.BuiltIn ? "Built-in" : "Yours") + ((item2.Description.Length > 0) ? (" · " + item2.Description) : ""), Theme.SubText, Theme.Small)
			{
				Margin = new Padding(0, 0, 0, Theme.S(6f))
			};
			Stack stack = new Stack(horizontal: true)
			{
				Controls = 
				{
					(Control?)new Btn("Use", delegate
					{
						Use(name);
					}, primary: true),
					(Control?)new Btn("Edit", delegate
					{
						Use(name);
						Main.Show("Screen layout");
					})
				}
			};
			stack.Controls.Add(new Btn("Remove", delegate
			{
				Remove(Main, E, this, name);
				Refresh0();
			}, primary: false, "\ue74d"));
			// long descriptions wrap inside the card rather than widening the column (which pushed the preview right)
			value2.MaximumSize = new Size(Theme.S(498f), 0);
			if (portrait)
			{
				// the narrow card wraps its description and buttons
				value.MaximumSize = new Size(Theme.S(228f), 0);
				value2.MaximumSize = new Size(Theme.S(228f), 0);
				stack.MaximumSize = new Size(Theme.S(232f), 0);
			}
			card.Controls.Add(new Stack
			{
				Controls = 
				{
					(Control?)screenPreview,
					(Control?)value,
					(Control?)value2,
					(Control?)stack
				}
			});
			_cards.Add((card, screenPreview, name));
		}
		if (Body.Controls.Count > 0)
		{
			Body.SetFlowBreak(Body.Controls[Body.Controls.Count - 1], value: true);
		}
		Body.Controls.Add(new Btn("Import layout…", delegate
		{
			Main.Get<LayoutPage>("Screen layout").Import();
		}, primary: false, "\ue838"));
		if (E.Settings.HiddenTemplates.Count > 0)
		{
			Body.Controls.Add(new Btn($"Restore removed templates ({E.Settings.HiddenTemplates.Count})", delegate
			{
				E.RestoreTemplates();
				Refresh0();
				Main.Toast("Removed templates are back");
			}, primary: false, ""));
		}
		Body.ResumeLayout();
		Highlight();
	}

	/// <summary>A heading across the page that starts a new group of cards.</summary>
	private void Section(string title, int count, bool first)
	{
		// as wide as two cards, so it takes a row to itself (a flow break would give its row the next row's height)
		Caption heading = new Caption($"{title}  ·  {count}", Theme.Text, Theme.BodyBold)
		{
			AutoSize = false,
			Size = new Size(Theme.S(1074f), Theme.S(28f)),
			TextAlign = ContentAlignment.BottomLeft,
			Margin = new Padding(Theme.S(2f), Theme.S(first ? 2f : 22f), 0, Theme.S(6f))
		};
		Body.Controls.Add(heading);
	}

	private void Highlight()
	{
		foreach (var card in _cards)
		{
			Card item = card.Card;
			ScreenPreview item2 = card.Preview;
			bool flag = card.Name == E.Settings.Layout;
			item.Border = (flag ? new Color?(Theme.Cyan) : null);
			item2.Selected = flag;
			item.Invalidate();
			item2.Invalidate();
		}
	}

	private void Use(string name)
	{
		lock (E.Settings)
		{
			E.Settings.Layout = name;
		}
		E.SaveSoon();
		Highlight();
		Main.Toast("“" + name + "” is on the screen");
	}

	/// <summary>
	/// Removes a layout after asking: a built-in is only hidden (Templates → Restore removed templates brings it
	/// back), while one of the user's own is deleted for good. Shared by the home page, this page and the editor.
	/// Returns true if it was removed.
	/// </summary>
	public static bool Remove(MainForm main, Engine e, IWin32Window owner, string name)
	{
		ScreenLayout? layout = e.Layouts.FirstOrDefault((ScreenLayout x) => x.Name == name);
		if (layout == null)
		{
			return false;
		}
		if (e.VisibleLayouts().Count <= 1)
		{
			main.Toast("That is the last layout — keep at least one");
			return false;
		}
		string question = layout.BuiltIn
			? "Remove “" + name + "” from your layouts?\n\nIt is a built-in template, so you can bring it back any time from Templates → Restore removed templates."
			: "Delete “" + name + "”?\n\nThis is your own layout, so it cannot be brought back.";
		if (MessageBox.Show(owner, question, "Remove layout", MessageBoxButtons.OKCancel, layout.BuiltIn ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.OK || !e.RemoveLayout(name))
		{
			return false;
		}
		main.Toast("Removed “" + name + "” — “" + e.Settings.Layout + "” is on the screen");
		return true;
	}

	public override void Tick()
	{
		foreach (var card in _cards)
		{
			ScreenPreview item = card.Preview;
			if (item.Visible && Main.ClientRectangle.IntersectsWith(Main.RectangleToClient(item.RectangleToScreen(item.ClientRectangle))))
			{
				item.Tick();
			}
		}
	}
}
