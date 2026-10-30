using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

public sealed class FansPage : Page
{
	private readonly List<FanCard> _cards = new List<FanCard>();

	private readonly Toggle _allOn;

	private readonly Caption _none;

	private List<string> _built = new List<string>();

	public override string Title => "Fan screens";

	public override string Subtitle => "Each fan screen has its own face, sensor, colours and background.";

	public FansPage(Engine e, MainForm m)
		: base(e, m)
	{
		_allOn = new Toggle("Fan screens on", E.Settings.FanScreensOn);
		_allOn.CheckedChanged += delegate
		{
			lock (E.Settings)
			{
				E.Settings.FanScreensOn = _allOn.Checked;
			}
			E.SaveSoon();
		};
		_none = new Caption("No fan screens found. Close L-Connect, then use Settings › Rescan devices. You can still prepare faces below.", Theme.Warn)
		{
			AutoSize = false,
			Size = new Size(Theme.S(1000f), Theme.S(26f))
		};
		Stack stack = new Stack(horizontal: true)
		{
			Controls = 
			{
				(Control?)_allOn,
				(Control?)new Btn("Identify fans", delegate
				{
					E.Identify(TimeSpan.FromSeconds(4.0));
					Main.Toast("Each fan shows its number");
				}, primary: false, "\ue7b3"),
				(Control?)new Caption("Brightness and frame rate for the fan screens are in Settings.", Theme.SubText)
			}
		};
		Body.Controls.Add(stack);
		Body.SetFlowBreak(stack, value: true);
		Body.Controls.Add(_none);
		Body.SetFlowBreak(_none, value: true);
	}

	private IEnumerable<(string Serial, string Name)> Fans()
	{
		if (E.FanOutputs.Count > 0)
		{
			return E.FanOutputs.Select((OutputStatus f) => (Serial: f.Serial, Name: f.Name));
		}
		return E.Settings.Fans.Select((FanScreen f, int i) => (Serial: f.Serial, $"Fan {i + 1}"));
	}

	public override void Refresh0()
	{
		_allOn.SetSilently(E.Settings.FanScreensOn);
		_none.Visible = E.FanOutputs.Count == 0;
		List<(string, string)> list = Fans().ToList();
		if (!list.Select<(string, string), string>(((string Serial, string Name) f) => f.Serial).SequenceEqual(_built))
		{
			Body.SuspendLayout();
			foreach (FanCard card in _cards)
			{
				Body.Controls.Remove(card);
				card.Dispose();
			}
			_cards.Clear();
			foreach (var item3 in list)
			{
				string item = item3.Item1;
				string item2 = item3.Item2;
				FanCard fanCard = new FanCard(E, Main, item, item2, this);
				Body.Controls.Add(fanCard);
				_cards.Add(fanCard);
			}
			_built = list.Select<(string, string), string>(((string Serial, string Name) f) => f.Serial).ToList();
			Body.ResumeLayout();
		}
		foreach (FanCard card2 in _cards)
		{
			card2.RefreshValues();
		}
	}

	public override void DevicesChanged()
	{
		Refresh0();
	}

	public void CopyLookToAll(FanScreen source)
	{
		FanScreen source2 = source;
		lock (E.Settings)
		{
			foreach (FanScreen item in E.Settings.Fans.Where((FanScreen f) => f != source2))
			{
				item.CopyLookFrom(source2);
			}
		}
		E.SaveSoon();
		foreach (FanCard card in _cards)
		{
			card.RefreshValues();
		}
		Main.Toast("Applied to all fans");
	}

	public override void Tick()
	{
		foreach (FanCard card in _cards)
		{
			card.Tick();
		}
	}
}
