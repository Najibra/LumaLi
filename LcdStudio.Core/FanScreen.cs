using System;

namespace LcdStudio.Core;

public sealed class FanScreen
{
	public string Serial { get; set; } = "";


	public bool Enabled { get; set; } = true;


	public string Face { get; set; } = "Glass Slab";


	public string Sensor { get; set; } = "cpu.load";


	public string Label { get; set; } = "";


	public string Color1 { get; set; } = "#0d0d0f";


	public string Color2 { get; set; } = "#ececea";


	public int Rotation { get; set; }

	public string? Background { get; set; }

	/// <summary>The gauge the Lottie Gauge face draws itself when no Lottie is uploaded: one of Faces.GaugeStyles.</summary>
	public string GaugeStyle { get; set; } = "Arc";

	/// <summary>The reading, in the sensor's own units, past which the gauge turns to WarnColor; 0 leaves it off.</summary>
	public float WarnAt { get; set; }

	public string WarnColor { get; set; } = "#ff5a3c";

	/// <summary>
	/// Uploads (pictures, GIFs, Lotties, videos) the background takes turns between, each for PlaylistSeconds; with two
	/// or more it replaces Background. Shuffle plays them in a random order that never repeats one straight after itself.
	/// </summary>
	public System.Collections.Generic.List<string> Playlist { get; set; } = new System.Collections.Generic.List<string>();

	public bool Shuffle { get; set; } = true;

	public int PlaylistSeconds { get; set; } = 30;

	[System.Text.Json.Serialization.JsonIgnore]
	public bool HasPlaylist => Playlist.Count >= 2;

	public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

	/// <summary>The background showing at this moment: the playlist's turn, or Background.</summary>
	public string? ActiveBackground(double unixSeconds)
	{
		var list = Playlist;
		int n = list.Count;
		if (n < 2)
		{
			return n == 1 ? list[0] : Background;
		}
		long turn = (long)Math.Floor(unixSeconds / Math.Clamp(PlaylistSeconds, 3, 3600));
		return list[Shuffle ? ShuffledIndex(turn, n) : (int)(turn % n)];
	}

	/// <summary>
	/// The turn's item in a shuffled order: each round of n turns is its own random order, and a round never starts
	/// with the item the last one ended on, so nothing plays twice in a row.
	/// </summary>
	public static int ShuffledIndex(long turn, int n)
	{
		if (n <= 2)
		{
			return (int)(turn % Math.Max(1, n));
		}
		long round = turn / n;
		int[] order = Round(round, n);
		// the round before ends on its own last item (the swap below never moves a last item), so starting on a
		// different one is enough to never show one twice in a row
		if (round > 0 && order[0] == Round(round - 1, n)[n - 1])
		{
			(order[0], order[1]) = (order[1], order[0]);
		}
		return order[(int)(turn % n)];
	}

	private static int[] Round(long round, int n)
	{
		var rnd = new Random(unchecked((int)(round * 2654435761L ^ 0x5bd1e995)));
		int[] order = new int[n];
		for (int i = 0; i < n; i++)
		{
			order[i] = i;
		}
		for (int i = n - 1; i > 0; i--)
		{
			int j = rnd.Next(i + 1);
			(order[i], order[j]) = (order[j], order[i]);
		}
		return order;
	}

	/// <summary>A full copy, with its own playlist, for drawing a frame while the settings may change.</summary>
	public FanScreen Snapshot()
	{
		FanScreen copy = (FanScreen)MemberwiseClone();
		copy.Playlist = new System.Collections.Generic.List<string>(Playlist);
		return copy;
	}

	/// <summary>Copies the look, but not WarnAt, which belongs with the fan's own sensor.</summary>
	public void CopyLookFrom(FanScreen o)
	{
		Face = o.Face;
		GaugeStyle = o.GaugeStyle;
		WarnColor = o.WarnColor;
		Color1 = o.Color1;
		Color2 = o.Color2;
		Background = o.Background;
		Playlist = new System.Collections.Generic.List<string>(o.Playlist);
		Shuffle = o.Shuffle;
		PlaylistSeconds = o.PlaylistSeconds;
	}
}
