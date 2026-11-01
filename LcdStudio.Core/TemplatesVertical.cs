namespace LcdStudio.Core;

/// <summary>Templates for the 8.8" screen stood on its end (480 wide, 1920 tall).</summary>
public static partial class Templates
{
	private const string Numbers = "Bahnschrift SemiBold", Plain = "Bahnschrift";

	/// <summary>White lettering with a soft halo in the given colour (the Neon glow).</summary>
	private static Widget G(string text, int x, int y, int w, int h, float size, string halo, string align = "left")
	{
		Widget g = T(text, x, y, w, h, size, "#ffffff", align, bold: false, Numbers);
		g.Effect = "glow";
		g.Color2 = halo;
		return g;
	}

	private static Widget NeonRing(string sensor, int x, int y, int size, string palette, string name, float valueSize, float thickness)
	{
		return new Widget
		{
			Kind = WidgetKind.Ring, Effect = "neon", Float = true, Sensor = sensor, X = x, Y = y, W = size, H = size, Thickness = thickness, Size = valueSize,
			Font = Numbers, Bold = false, Align = "center", Color = "#ffffff", Text = palette, Print = name, Format = "0", Min = 0f, Max = 100f,
			Track = "#1a2440"
		};
	}

	private static Widget NeonBox(int x, int y, int w, int h, string title, string accent) => new Widget
	{
		Kind = WidgetKind.Panel, Effect = "neon", X = x, Y = y, W = w, H = h, Text = title, Print = accent,
		Color = "#0b1120", Color2 = "#070b16", Track = "#22304f", WarnColor = "#aeb9d8", Radius = 10f, Size = 15f, Font = Plain, Thickness = 1f
	};

	/// <summary>Silk flowing up the screen behind the time and five readings, each a big white number with a slim bar.</summary>
	private static ScreenLayout SilkTower()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Silk Tower",
			Description = "Vertical: silk strands flowing behind the time and five big readings",
			BuiltIn = true,
			Portrait = true,
			Background = "silk",
			Color1 = "#04050d",
			Color2 = "#16207a",
			Accent = "#2fd4ff"
		};
		l.Widgets.Add(G("{time:HH:mm}", 10, 60, 460, 170, 128f, "#2fd4ff", "center"));
		l.Widgets.Add(T("{date:dddd d MMMM}", 10, 232, 460, 40, 24f, "#c3cdf0", "center", bold: false, Plain));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Divider, X = 120, Y = 300, W = 240, H = 10, Color = "#40ffffff", Thickness = 1.5f });
		(string Label, string Value, string Sensor, string Color)[] rows =
		{
			("PROCESSOR", "{cpu.load:0}%", "cpu.load", "#2fd4ff"),
			("PROCESSOR TEMPERATURE", "{cpu.temp:0}°", "cpu.temp", "#9d7bff"),
			("GRAPHICS", "{gpu.load:0}%", "gpu.load", "#ff4fd8"),
			("GRAPHICS TEMPERATURE", "{gpu.temp:0}°", "gpu.temp", "#ff9a5c"),
			("MEMORY", "{ram.load:0}%", "ram.load", "#4ade80")
		};
		for (int i = 0; i < rows.Length; i++)
		{
			int y = 360 + i * 300;
			l.Widgets.Add(T(rows[i].Label, 44, y, 400, 34, 19f, rows[i].Color, "left", bold: true, Plain));
			l.Widgets.Add(G(rows[i].Value, 34, y + 34, 420, 150, 112f, rows[i].Color));
			l.Widgets.Add(Bar(rows[i].Sensor, 44, y + 200, 392, 8, rows[i].Color, "#ffffff"));
			l.Widgets[^1].Track = "#1cffffff";
		}
		l.Widgets.Add(T("{cpu.name}", 10, 1840, 460, 30, 16f, "#7f8bb3", "center", bold: false, Plain));
		l.Widgets.Add(T("{gpu.name}", 10, 1870, 460, 30, 16f, "#7f8bb3", "center", bold: false, Plain));
		return l;
	}

	/// <summary>The Neon dashboard stacked for a tall screen: time, processor, graphics, memory and network panels.</summary>
	private static ScreenLayout NeonColumn()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Neon Column",
			Description = "Vertical: neon panels with gradient rings and glowing charts",
			BuiltIn = true,
			Portrait = true,
			Background = "solid",
			Color1 = "#04060c",
			Color2 = "#04060c"
		};
		const string label = "#aeb9d8", cyan = "#22e3ff", green = "#4ade80", pink = "#ff2d95", amber = "#facc15", violet = "#c084fc";
		Widget Stat(string text, int x, int y, int w, string color, string align = "left", float size = 21f) =>
			T(text, x, y, w, (int)(size * 1.5f), size, color, align, bold: false, Plain);
		void Pair(string name, string value, int y, string color)
		{
			l.Widgets.Add(Stat(name, 36, y, 240, label));
			l.Widgets.Add(G(value, 196, y - 3, 248, 38, 26f, color, "right"));
		}

		l.Widgets.Add(NeonBox(12, 12, 456, 236, "Time", cyan));
		l.Widgets.Add(G("{time:HH:mm}", 20, 44, 440, 130, 104f, cyan, "center"));
		l.Widgets.Add(Stat("{date:dddd d MMMM}", 20, 180, 440, label, "center", 22f));

		l.Widgets.Add(NeonBox(12, 260, 456, 540, "Processor", pink));
		l.Widgets.Add(NeonRing("cpu.load", 110, 296, 260, "#ff2d95,#a855f7,#3b82f6", "LOAD", 58f, 14f));
		Pair("Temperature", "{cpu.temp:0}°C", 576, pink);
		Pair("Clock", "{cpu.clock:0} MHz", 616, violet);
		Pair("Power", "{cpu.power:0} W", 656, "#60a5fa");
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "bars", Sensor = "cpu.load", X = 32, Y = 704, W = 416, H = 82, Text = "#ff2d95,#4a0e33", Thickness = 7f, Min = 0f, Max = 100f });

		l.Widgets.Add(NeonBox(12, 812, 456, 540, "Graphics", cyan));
		l.Widgets.Add(NeonRing("gpu.load", 110, 848, 260, "#22d3ee,#34d399,#a3e635", "LOAD", 58f, 14f));
		Pair("Temperature", "{gpu.temp:0}°C", 1128, cyan);
		Pair("Power", "{gpu.power:0} W", 1168, green);
		Pair("Video memory", "{gpu.vram:0.0} GB", 1208, amber);
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "lines", Sensor = "gpu.load,gpu.temp", Text = "#22d3ee,#4ade80", X = 32, Y = 1252, W = 416, H = 88, Size = 15f, Font = Plain, Min = 0f, Max = 100f });

		l.Widgets.Add(NeonBox(12, 1364, 456, 300, "Memory", amber));
		l.Widgets.Add(NeonRing("ram.load", 26, 1402, 220, "#ff4d6d,#fb923c,#facc15", "IN USE", 46f, 12f));
		l.Widgets.Add(G("{ram.used:0.0} GB", 252, 1460, 200, 60, 38f, amber));
		l.Widgets.Add(Stat("of {ram.total:0} GB", 252, 1526, 200, label, "left", 20f));

		l.Widgets.Add(NeonBox(12, 1676, 456, 232, "Network", green));
		l.Widgets.Add(G("↓ {net.down:0.0}", 32, 1712, 200, 52, 36f, green));
		l.Widgets.Add(G("↑ {net.up:0.0}", 248, 1712, 200, 52, 36f, cyan, "right"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "bars", Sensor = "net.down", X = 32, Y = 1782, W = 416, H = 110, Text = "#4ade80,#14532d", Thickness = 6f, Min = 0f, Max = 20f });
		return l;
	}

	/// <summary>Three big glowing rings stacked over a drifting aurora, each with its temperature or amount beneath.</summary>
	private static ScreenLayout AuroraRings()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Aurora Rings",
			Description = "Vertical: three big gradient rings over a drifting aurora",
			BuiltIn = true,
			Portrait = true,
			Background = "aurora",
			Color1 = "#050816",
			Color2 = "#3b1d8f",
			Accent = "#16c2ff"
		};
		l.Widgets.Add(G("{time:HH:mm}", 10, 40, 460, 150, 112f, "#16c2ff", "center"));
		l.Widgets.Add(T("{date:dddd d MMMM}", 10, 190, 460, 40, 24f, "#c9d3f5", "center", bold: false, Plain));
		(string Sensor, string Palette, string Name, string Under, string UnderLabel, string Halo)[] rings =
		{
			("cpu.load", "#22d3ee,#6366f1,#d946ef", "PROCESSOR", "{cpu.temp:0}°C", "TEMPERATURE", "#22d3ee"),
			("gpu.load", "#f472b6,#a855f7,#38bdf8", "GRAPHICS", "{gpu.temp:0}°C", "TEMPERATURE", "#f472b6"),
			("ram.load", "#34d399,#22d3ee,#818cf8", "MEMORY", "{ram.used:0.0} GB", "IN USE", "#34d399")
		};
		for (int i = 0; i < rings.Length; i++)
		{
			int y = 270 + i * 545;
			l.Widgets.Add(NeonRing(rings[i].Sensor, 60, y, 360, rings[i].Palette, rings[i].Name, 84f, 18f));
			l.Widgets.Add(G(rings[i].Under, 10, y + 368, 460, 76, 56f, rings[i].Halo, "center"));
			l.Widgets.Add(T(rings[i].UnderLabel, 10, y + 442, 460, 30, 17f, "#9aa6cf", "center", bold: true, Plain));
		}
		return l;
	}

	/// <summary>Right Now stood up: five poster numbers stacked, each over its word, stamping on every change.</summary>
	private static ScreenLayout RightNowTower()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Right Now Tower",
			Description = "Vertical: yellow poster numbers stacked on red, stamping on every change",
			BuiltIn = true,
			Portrait = true,
			Background = "solid",
			Color1 = "#ff3f1f",
			Color2 = "#ff3f1f"
		};
		(string Sensor, string Word)[] rows = { ("cpu.load", "CPU"), ("gpu.load", "GPU"), ("gpu.temp", "HEAT"), ("gpu.power", "WATTS"), ("ram.load", "RAM") };
		for (int i = 0; i < rows.Length; i++)
		{
			int y = 14 + i * 380;
			bool temp = rows[i].Sensor == "gpu.temp";
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value, Sensor = rows[i].Sensor, Effect = "block", Float = true, Text = temp ? "°" : "", X = 10, Y = y, W = 460, H = 250,
				Size = 220f, Font = "Impact", Bold = false, Align = "center", Color = "#ffc805", Color2 = "#050505", Format = "0",
				WarnAt = temp ? 83 : 0, WarnColor = "#ffffff"
			});
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Text, Text = rows[i].Word, Effect = "block", Float = true, X = 10, Y = y + 250, W = 460, H = 110, Size = 96f,
				Font = "Impact", Bold = false, Align = "center", Color = "#ffc805", Color2 = "#050505"
			});
		}
		return l;
	}

	/// <summary>Instrument stood up: the time in pale type, the five gauge tiles (drawn at their wide-screen size and
	/// scaled to the column) and the processor and graphics history in burnt orange.</summary>
	private static ScreenLayout InstrumentColumn()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Instrument Column",
			Description = "Vertical: pale instrument tiles in black and burnt orange; an alert tile that turns coral",
			BuiltIn = true,
			Portrait = true,
			Background = "solid",
			Color1 = "#000000",
			Color2 = "#000000"
		};
		const string pale = "#e9e8e2", fade = "#8a8a86", orange = "#b0552a";
		l.Widgets.Add(T("{time:HH:mm}", 0, 34, 480, 190, 140f, pale, "center", bold: false, "Segoe UI Semibold"));
		l.Widgets.Add(T("{date:dddd d MMMM}", 0, 226, 480, 44, 28f, fade, "center", bold: false, "Segoe UI"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Divider, X = 160, Y = 288, W = 160, H = 10, Color = orange, Thickness = 3f });
		const float zoom = 456f / 620f;
		string[] tiles = { "cpu", "gpu", "mem", "power", "status" };
		for (int i = 0; i < tiles.Length; i++)
		{
			l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "instrument", Text = tiles[i], X = 12, Y = 326 + i * 174, W = 456, H = 160, Float = true, Zoom = zoom });
		}
		l.Widgets.Add(T("PROCESSOR · LAST TWO MINUTES", 24, 1210, 440, 30, 17f, fade, "left", bold: false, "Segoe UI"));
		l.Widgets.Add(Graph("cpu.load", 24, 1244, 432, 270, orange));
		l.Widgets.Add(T("GRAPHICS · LAST TWO MINUTES", 24, 1548, 440, 30, 17f, fade, "left", bold: false, "Segoe UI"));
		l.Widgets.Add(Graph("gpu.load", 24, 1582, 432, 270, pale));
		return l;
	}

	/// <summary>Spectrum Deck stood up: the hero card on top, the processor, graphics, memory and thermal cards in
	/// pairs and the network card across the foot, each drawn at its wide-screen size and scaled to fit.</summary>
	private static ScreenLayout DeckTower()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Deck Tower",
			Description = "Vertical: live glass cards; the busiest one takes the spotlight",
			BuiltIn = true,
			Portrait = true,
			Background = "gradient",
			Color1 = "#050608",
			Color2 = "#0d1630",
			Accent = "#e8873a"
		};
		Widget Card(string kind, int x, int y, int w, int h, float zoom) => new Widget { Kind = WidgetKind.Panel, Effect = "deck", Text = kind, X = x, Y = y, W = w, H = h, Float = true, Zoom = zoom };
		l.Widgets.Add(Card("hero", 12, 12, 456, 554, 456f / 372f));
		string[] cards = { "cpu", "gpu", "mem", "therm" };
		for (int i = 0; i < cards.Length; i++)
		{
			l.Widgets.Add(Card(cards[i], 12 + (i % 2) * 234, 580 + (i / 2) * 358, 222, 344, 222f / 292f));
		}
		l.Widgets.Add(Card("net", 12, 1296, 456, 612, 1.4f));
		return l;
	}

	/// <summary>Stretch stood up: black digits on paper that stretch down as the readings rise, five rows.</summary>
	private static ScreenLayout StretchTower()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Stretch Tower",
			Description = "Vertical: black digits that stretch down the page as the readings rise",
			BuiltIn = true,
			Portrait = true,
			Background = "solid",
			Color1 = "#ecebe5",
			Color2 = "#ecebe5"
		};
		(string Sensor, string Part, string What, float Max)[] rows =
		{
			("cpu.load", "PROCESSOR", "LOAD %", 100f), ("gpu.load", "GRAPHICS", "LOAD %", 100f), ("gpu.temp", "GRAPHICS", "TEMP °C", 100f),
			("gpu.power", "GRAPHICS", "POWER W", 575f), ("ram.load", "MEMORY", "IN USE %", 100f)
		};
		l.Widgets.Add(T("{time:HH:mm}", 24, 18, 432, 40, 30f, "#111111", "right", bold: true, "Bahnschrift Condensed"));
		for (int i = 0; i < rows.Length; i++)
		{
			int y = 70 + i * 368;
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value, Sensor = rows[i].Sensor, Effect = "stretch", Float = true, Text = "", X = 24, Y = y, W = 432, H = 340, Size = 170f,
				Thickness = 70f, Font = "Impact", Min = 0f, Max = rows[i].Max, Color = "#111111", Format = "0",
				WarnAt = rows[i].Sensor == "gpu.temp" ? 83 : 0, WarnColor = "#c4241a"
			});
			l.Widgets.Add(T(rows[i].Part, 24, y + 282, 432, 20, 14f, "#111111", "right", bold: false, "Arial"));
			l.Widgets.Add(T(rows[i].What, 24, y + 298, 432, 40, 30f, "#111111", "right", bold: true, "Bahnschrift Condensed"));
		}
		return l;
	}

	/// <summary>Gap stood up: towering black numbers on a white page, with the swinging yellow sticker.</summary>
	private static ScreenLayout GapTower()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Gap Tower",
			Description = "Vertical: towering black numbers on a white page, with a swinging yellow sticker",
			BuiltIn = true,
			Portrait = true,
			Background = "solid",
			Color1 = "#f6f5f1",
			Color2 = "#f6f5f1"
		};
		(string Sensor, string Label)[] rows =
		{
			("cpu.load", "PROCESSOR LOAD %"), ("gpu.load", "GRAPHICS LOAD %"), ("gpu.temp", "GRAPHICS TEMPERATURE °C"),
			("gpu.power", "GRAPHICS POWER W"), ("ram.load", "MEMORY IN USE %")
		};
		l.Widgets.Add(T("A SYSTEM OVERVIEW BY", 24, 20, 300, 34, 23f, "#0e0e0e", "left", bold: false, "Arial"));
		l.Widgets.Add(T("LUMALI", 312, 20, 150, 34, 23f, "#0e0e0e", "left", bold: true, "Arial"));
		for (int i = 0; i < rows.Length; i++)
		{
			int y = 90 + i * 360;
			l.Widgets.Add(T(rows[i].Label, 24, y, 432, 26, 15f, "#0e0e0e", "left", bold: true, "Arial"));
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value, Sensor = rows[i].Sensor, Effect = "poster", Float = true, Text = "", X = 24, Y = y + 32, W = 432, H = 330,
				Size = 100f, Font = "Impact", Bold = false, Align = "left", Color = "#0e0e0e", Format = "0",
				WarnAt = rows[i].Sensor == "gpu.temp" ? 83 : 0, WarnColor = "#d92b1c"
			});
		}
		l.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text, Effect = "tag", Float = true, Text = "LIVE EDITION WITH\\n{time:HH:mm} SYSTEM CLOCK", X = 120, Y = 1810, W = 330, H = 78,
			Size = 19f, Font = "Arial", Bold = true, Color = "#0e0e0e", Color2 = "#f7c600"
		});
		return l;
	}
	/// <summary>A tall clock: hours over minutes in big glowing numerals, the date, and five readings in rows below.</summary>
	private static ScreenLayout TallClock()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Tall Clock",
			Description = "Vertical: hours over minutes on warm silk, with five readings beneath",
			BuiltIn = true,
			Portrait = true,
			Background = "silk",
			Color1 = "#0a0410",
			Color2 = "#4a1640",
			Accent = "#ff8a3d"
		};
		l.Widgets.Add(G("{time:HH}", 0, 70, 480, 360, 300f, "#ff8a3d", "center"));
		l.Widgets.Add(G("{time:mm}", 0, 400, 480, 360, 300f, "#ff4fa3", "center"));
		l.Widgets.Add(T("{date:dddd}", 0, 790, 480, 50, 34f, "#ffe1cf", "center", bold: true, Plain));
		l.Widgets.Add(T("{date:d MMMM}", 0, 840, 480, 44, 28f, "#d8b4c8", "center", bold: false, Plain));
		(string Label, string Value, string Color)[] rows =
		{
			("PROCESSOR", "{cpu.load:0}%", "#ff8a3d"),
			("CPU TEMP", "{cpu.temp:0}°", "#ffb347"),
			("GRAPHICS", "{gpu.load:0}%", "#ff4fa3"),
			("GPU TEMP", "{gpu.temp:0}°", "#c77dff"),
			("MEMORY", "{ram.load:0}%", "#7aa2ff")
		};
		for (int i = 0; i < rows.Length; i++)
		{
			int y = 960 + i * 170;
			l.Widgets.Add(new Widget { Kind = WidgetKind.Divider, X = 40, Y = y - 10, W = 400, H = 10, Color = "#28ffffff", Thickness = 1.2f });
			l.Widgets.Add(T(rows[i].Label, 44, y + 44, 220, 40, 22f, rows[i].Color, "left", bold: true, Plain));
			l.Widgets.Add(G(rows[i].Value, 190, y + 14, 250, 100, 72f, rows[i].Color, "right"));
		}
		l.Widgets.Add(new Widget { Kind = WidgetKind.Divider, X = 40, Y = 1800, W = 400, H = 10, Color = "#28ffffff", Thickness = 1.2f });
		return l;
	}
}
