using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LcdStudio.Core;

namespace LcdStudio.UI;

/// <summary>Picks the uploads a fan screen's background takes turns between, how long each shows and whether to shuffle.</summary>
internal static class PlaylistDialog
{
	public static bool Edit(IWin32Window owner, FanScreen fan, IEnumerable<string> uploads)
	{
		using Form f = new Form
		{
			Text = "Background playlist",
			FormBorderStyle = FormBorderStyle.FixedDialog,
			StartPosition = FormStartPosition.CenterParent,
			MinimizeBox = false,
			MaximizeBox = false,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			ClientSize = new Size(Theme.S(440f), Theme.S(420f)),
			Font = Theme.Body
		};
		var list = new CheckedListBox
		{
			Location = new Point(Theme.S(20f), Theme.S(44f)),
			Size = new Size(Theme.S(400f), Theme.S(250f)),
			BackColor = Theme.Field,
			ForeColor = Theme.Text,
			BorderStyle = BorderStyle.FixedSingle,
			CheckOnClick = true,
			IntegralHeight = false
		};
		List<string> items = uploads.ToList();
		foreach (string path in fan.Playlist.Where((string p) => !items.Contains(p, StringComparer.OrdinalIgnoreCase)))
		{
			items.Add(path);
		}
		foreach (string path in items)
		{
			list.Items.Add(new Item(path), fan.Playlist.Contains(path, StringComparer.OrdinalIgnoreCase));
		}
		var shuffle = new CheckBox
		{
			Text = "Shuffle",
			Checked = fan.Shuffle,
			AutoSize = true,
			Location = new Point(Theme.S(20f), Theme.S(306f)),
			ForeColor = Theme.Text
		};
		var seconds = new NumericUpDown
		{
			Minimum = 3,
			Maximum = 3600,
			Value = Math.Clamp(fan.PlaylistSeconds, 3, 3600),
			Location = new Point(Theme.S(250f), Theme.S(304f)),
			Width = Theme.S(80f),
			BackColor = Theme.Field,
			ForeColor = Theme.Text
		};
		Btn ok = new Btn("OK", null, primary: true)
		{
			Location = new Point(Theme.S(250f), Theme.S(366f))
		};
		ok.Click += delegate
		{
			f.DialogResult = DialogResult.OK;
		};
		Btn cancel = new Btn("Cancel")
		{
			Location = new Point(Theme.S(330f), Theme.S(366f))
		};
		cancel.Click += delegate
		{
			f.DialogResult = DialogResult.Cancel;
		};
		f.Controls.Add(new Label
		{
			Text = "Tick two or more uploads; the background takes turns between them.",
			Location = new Point(Theme.S(20f), Theme.S(14f)),
			AutoSize = true,
			ForeColor = Theme.SubText
		});
		f.Controls.Add(new Label
		{
			Text = "Each for (seconds)",
			Location = new Point(Theme.S(130f), Theme.S(308f)),
			AutoSize = true,
			ForeColor = Theme.SubText
		});
		f.Controls.AddRange(new Control[] { list, shuffle, seconds, ok, cancel });
		if (f.ShowDialog(owner) != DialogResult.OK)
		{
			return false;
		}
		fan.Playlist = list.CheckedItems.Cast<Item>().Select((Item i) => i.Path).ToList();
		fan.Shuffle = shuffle.Checked;
		fan.PlaylistSeconds = (int)seconds.Value;
		return true;
	}

	private sealed class Item
	{
		public readonly string Path;

		public Item(string path)
		{
			Path = path;
		}

		public override string ToString() => System.IO.Path.GetFileName(Path);
	}
}
