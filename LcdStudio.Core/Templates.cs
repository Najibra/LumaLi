using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LcdStudio.Core;

public static partial class Templates
{
	private static Widget T(string text, int x, int y, int w, int h, float size, string color = "#ffffff", string align = "left", bool bold = true, string font = "Segoe UI")
	{
		return new Widget
		{
			Kind = WidgetKind.Text,
			Text = text,
			X = x,
			Y = y,
			W = w,
			H = h,
			Size = size,
			Color = color,
			Align = align,
			Bold = bold,
			Font = font
		};
	}

	private static IEnumerable<Widget> Row(string label, string value, int x, int y, int w, float size = 20f, string valueColor = "#ffffff")
	{
		Widget widget = T(label, x, y, w / 2, (int)((double)size * 1.6), size, "#8fa3d9", "left", bold: false);
		Widget v = T(value, x + w / 2, y, w / 2, (int)((double)size * 1.6), size, valueColor, "right");
		bool hideWhenMissing = true;
		v.HideWhenMissing = true;
		widget.HideWhenMissing = hideWhenMissing;
		widget.Sensor = value;
		yield return widget;
		yield return v;
	}

	private static Widget V(string sensor, int x, int y, int w, int h, float size, string color = "#ffffff", string format = "0", string align = "left")
	{
		return new Widget
		{
			Kind = WidgetKind.Value,
			Sensor = sensor,
			X = x,
			Y = y,
			W = w,
			H = h,
			Size = size,
			Color = color,
			Format = format,
			Align = align,
			Text = ""
		};
	}

	private static Widget Panel(int x, int y, int w, int h, string title, string fill = "#401a2a6b", string fill2 = "#26101840", float radius = 22f)
	{
		return new Widget
		{
			Kind = WidgetKind.Panel,
			X = x,
			Y = y,
			W = w,
			H = h,
			Color = fill,
			Color2 = fill2,
			Track = "#3384b4ff",
			Thickness = 1.5f,
			Radius = radius,
			Text = title,
			Size = 18f,
			WarnColor = "#5fd0ff",
			Bold = true
		};
	}

	private static Widget Bar(string sensor, int x, int y, int w, int h, string c1, string c2, float max = 100f)
	{
		return new Widget
		{
			Kind = WidgetKind.Bar,
			Sensor = sensor,
			X = x,
			Y = y,
			W = w,
			H = h,
			Color = c1,
			Color2 = c2,
			Max = max,
			Radius = (float)h / 2f
		};
	}

	private static Widget Graph(string sensor, int x, int y, int w, int h, string color, float max = 100f)
	{
		return new Widget
		{
			Kind = WidgetKind.Graph,
			Sensor = sensor,
			X = x,
			Y = y,
			W = w,
			H = h,
			Color = color,
			Max = max,
			Thickness = 6f
		};
	}

	public static List<ScreenLayout> All()
	{
		return new List<ScreenLayout>
		{
			CommandCenter(),
			Lightbeam(),
			Standby(),
			Constellation(),
			Void(),
			Instrument(),
			SpectrumDeck(),
			Spectrum(),
			Reeded(),
			Headline(),
			PaperCut(),
			Lighthouse(),
			Carousel(),
			Mechanica(),
			Astro(),
			Deer(),
			Neon(),
			Pixotype(),
			Pixel(),
			Prism(),
			RightNow(),
			Eclipse(),
			Stretch(),
			Gap(),
			Blade(),
			Vinyl(),
			Balloon(),
			Classic(),
			NeonGrid(),
			HeatWave(),
			GlassTunnel(),
			Carved(),
			MinimalClock(),
			FocusGpu(),
			SilkTower(),
			NeonColumn(),
			AuroraRings(),
			TallClock()
		};
	}

	private static ScreenLayout CommandCenter()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Command Center",
			Description = "Processor, graphics, time, network and cooling",
			BuiltIn = true,
			Background = "aurora",
			Color1 = "#060a22",
			Color2 = "#1b2fa0",
			Accent = "#1f8bff"
		};
		List<Widget> widgets = screenLayout.Widgets;
		widgets.Add(Panel(14, 14, 620, 452, "PROCESSOR"));
		widgets.Add(T("{cpu.name:upper}", 300, 22, 316, 32, 19f, "#cfe3ff", "right"));
		widgets.Add(V("cpu.load", 34, 54, 300, 120, 104f));
		widgets.Last().Glow = true;
		widgets.Last().Color2 = "#3fa9ff";
		widgets.AddRange(Row("Clock", "{cpu.clock:0} MHz", 340, 70, 276));
		widgets.AddRange(Row("Busiest thread", "{cpu.busiest:0}%", 340, 106, 276, 20f, "#5fd0ff"));
		widgets.AddRange(Row("Temperature", "{cpu.temp:0}°C", 340, 142, 276));
		widgets.AddRange(Row("Power", "{cpu.power:0} W", 34, 186, 290));
		widgets.AddRange(Row("Running for", "{uptime}", 340, 186, 276));
		widgets.Add(new Widget
		{
			Kind = WidgetKind.CoreBars,
			X = 34,
			Y = 236,
			W = 582,
			H = 110,
			Color = "#2f8cff",
			Color2 = "#ff5a3c",
			Track = "#14ffffff"
		});
		widgets.Add(Graph("cpu.load", 34, 362, 582, 88, "#3fa9ff"));
		widgets.Add(Panel(650, 14, 620, 452, "GRAPHICS"));
		widgets.Add(T("{gpu.name:upper}", 930, 22, 320, 32, 19f, "#cfe3ff", "right"));
		widgets.Add(new Widget
		{
			Kind = WidgetKind.Gauge,
			Sensor = "gpu.load",
			X = 664,
			Y = 58,
			W = 300,
			H = 300,
			Thickness = 16f,
			Size = 72f,
			Color = "#29d3ff",
			Color2 = "#2f6bff",
			Text = "LOAD",
			Glow = true,
			Align = "center"
		});
		widgets.AddRange(Row("Temperature", "{gpu.temp:0}°C", 980, 72, 270));
		widgets.Add(Bar("gpu.temp", 980, 108, 270, 8, "#ffb02e", "#3fd68a", 95f));
		widgets.AddRange(Row("Power", "{gpu.power:0} W", 980, 128, 270, 20f, "#ff6b8b"));
		widgets.Add(Bar("gpu.power", 980, 164, 270, 8, "#ff4d7a", "#ff9d4d", 575f));
		widgets.AddRange(Row("Fan", "{gpu.fan:0}%", 980, 184, 270));
		widgets.AddRange(Row("Video memory", "{gpu.vram:0.0} GB", 980, 222, 270, 20f, "#7cf0b0"));
		widgets.AddRange(Row("Core clock", "{gpu.clock:0} MHz", 680, 372, 280, 19f));
		widgets.AddRange(Row("Memory clock", "{gpu.memclock:0} MHz", 680, 408, 280, 19f));
		widgets.Add(Graph("gpu.load", 980, 290, 270, 160, "#29d3ff"));
		widgets.Add(Panel(1286, 14, 620, 452, ""));
		widgets.Add(T("{time:HH:mm}", 1306, 20, 300, 110, 92f));
		widgets[widgets.Count - 1].Glow = true;
		widgets[widgets.Count - 1].Color2 = "#5fb4ff";
		widgets.Add(T("{date:dddd d MMMM}", 1580, 34, 310, 36, 20f, "#cfe3ff", "right"));
		widgets.Add(T("NETWORK  ↓ {net.down:0.0}  ↑ {net.up:0.0} MB/s", 1580, 76, 310, 36, 17f, "#8fa3d9", "right", bold: false));
		widgets.Add(Graph("net.down", 1306, 134, 580, 40, "#5fd0ff", 20f));
		widgets.AddRange(Row("Memory", "{ram.used:0.0} of {ram.total:0} GB", 1306, 186, 580));
		widgets.Add(Bar("ram.load", 1306, 222, 580, 10, "#7c5cff", "#3fa9ff"));
		widgets.Add(T("COOLING", 1306, 250, 300, 30, 17f, "#5fd0ff"));
		for (int i = 1; i <= 3; i++)
		{
			int num = 282 + (i - 1) * 60;
			widgets.AddRange(Row($"{{group{i}.name}} · {{group{i}.fans:0}} fans", $"{{group{i}.rpm:0}} RPM", 1306, num, 580, 18f));
			widgets.Add(Bar($"group{i}.duty", 1306, num + 32, 580, 8, "#3fd6a5", "#3fa9ff"));
			widgets[widgets.Count - 1].HideWhenMissing = true;
		}
		return screenLayout;
	}

	private static ScreenLayout RightNow()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Right Now",
			Description = "Yellow poster numbers with hard black shadows that stamp on every change",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#ff3f1f",
			Color2 = "#ff3f1f"
		};
		(string, string)[] array = new(string, string)[5]
		{
			("cpu.load", "CPU"),
			("gpu.load", "GPU"),
			("gpu.temp", "HEAT"),
			("gpu.power", "WATTS"),
			("ram.load", "RAM")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int x = 22 + i * 378;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Effect = "block",
				Float = true,
				Text = ((array[i].Item1 == "gpu.temp") ? "°" : ""),
				X = x,
				Y = 12,
				W = 360,
				H = 300,
				Size = 250f,
				Font = "Impact",
				Bold = false,
				Align = "center",
				Color = "#ffc805",
				Color2 = "#050505",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 83 : 0),
				WarnColor = "#ffffff"
			});
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Text,
				Text = array[i].Item2,
				Effect = "block",
				Float = true,
				X = x,
				Y = 322,
				W = 360,
				H = 140,
				Size = 128f,
				Font = "Impact",
				Bold = false,
				Align = "center",
				Color = "#ffc805",
				Color2 = "#050505"
			});
		}
		return screenLayout;
	}

	/// <summary>
	/// Lightbeam: a bundle of light strands arching over a black horizon and landing in a flare, rising with the
	/// reading in focus, pulses racing down it as fast as it is busy, one thin number under the arch and a gear-style
	/// selector sliding between channels.
	/// </summary>
	private static ScreenLayout Lightbeam()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Lightbeam",
			Description = "A beam of light strands arching over a mirrored horizon, rising and racing with the load",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#020306",
			Color2 = "#020306"
		};
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "lightbeam", X = 0, Y = 0, W = 1920, H = 480, Text = "", Float = true });
		return l;
	}

	/// <summary>
	/// Standby: the quiet face LumaLi switches to when the PC is idle or locked, and leaves on the screen as it goes
	/// to sleep or shuts down: a logo, a large clock and the date. The panel's image is the logo; LumaLi fills it
	/// from Settings › Standby screen.
	/// </summary>
	private static ScreenLayout Standby()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Standby",
			Description = "A logo, a large clock and the date: shown when the PC is idle, locked or going to sleep",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#030407",
			Color2 = "#030407"
		};
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "standby", X = 0, Y = 0, W = 1920, H = 480, Text = "", Float = true });
		return l;
	}
	/// <summary>
	/// Constellation: every sensor as a glossy orb on a tilted ring round a golden core, spokes carrying data ticks,
	/// value bars and comets; every so often the ring folds into a network of category hubs tied to the core by
	/// golden beams, and a warning pulls it into the network at once.
	/// </summary>
	private static ScreenLayout Constellation()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Constellation",
			Description = "Every sensor an orb round a golden core; the ring folds into a network of hubs",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#030607",
			Color2 = "#030607"
		};
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "constellation", X = 0, Y = 0, W = 1920, H = 480, Text = "", Float = true });
		return l;
	}
	/// <summary>
	/// Void: a triangular tunnel of LED dots flying towards the viewer, faster the busier the processor, its walls
	/// standing for processor, graphics and memory and warming from ice blue to pink as they climb, with the time in
	/// the black at its heart; and dot-matrix panels either side whose bent grids show history as rows of lit squares.
	/// </summary>
	private static ScreenLayout Void()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Void",
			Description = "Fly into a tunnel of LED dots; dot-matrix history panels on either side",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#000000",
			Color2 = "#000000"
		};
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "voidtunnel", X = 0, Y = 0, W = 1920, H = 480, Text = "", Float = true });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "voidpanel", Text = "left", X = 0, Y = 0, W = 600, H = 480, Float = true });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "voidpanel", Text = "right", X = 1320, Y = 0, W = 600, H = 480, Float = true });
		return l;
	}
	/// <summary>
	/// Instrument: six pale grey gauge tiles on black in black and burnt orange, the big numbers with faded decimals.
	/// Processor load against its two-minute range, graphics load with a dial turning with the clock, memory on an
	/// arc, the time with a line per second, graphics power in bars, and a sage network panel that turns coral and
	/// names the problem when something needs attention.
	/// </summary>
	private static ScreenLayout Instrument()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Instrument",
			Description = "Pale instrument tiles in black and burnt orange; an alert panel that turns coral",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#000000",
			Color2 = "#000000"
		};
		string[,] tiles = { { "cpu", "gpu", "mem" }, { "time", "power", "status" } };
		for (int row = 0; row < 2; row++)
		{
			for (int col = 0; col < 3; col++)
			{
				l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "instrument", Text = tiles[row, col], X = 16 + col * 634, Y = 16 + row * 231, W = 620, H = 217, Float = true });
			}
		}
		return l;
	}

	/// <summary>
	/// Spectrum Deck: a command centre of dark glass cards on the prism page. A warm prism-glass panel holds the time
	/// in slab letters, the machine's mood and the date; then processor (its state word and a live waveform),
	/// graphics (load, a tick meter with peak hold, power and a temperature pill), memory (turning rings and
	/// pressure), thermals (live temperature lines and the trend) and network (download, a mirrored up/down graph).
	/// The busiest card is in the spotlight, and a card glows red while its part runs hot.
	/// </summary>
	private static ScreenLayout SpectrumDeck()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Spectrum Deck",
			Description = "Live instrument cards on prism glass; the busiest card takes the spotlight",
			BuiltIn = true,
			Background = "deck",
			Color1 = "#050608",
			Color2 = "#2f6bff",
			Accent = "#e8873a"
		};
		Widget Card(string kind, int x, int w) => new Widget { Kind = WidgetKind.Panel, Effect = "deck", Text = kind, X = x, Y = 14, W = w, H = 452, Float = true };
		l.Widgets.Add(Card("hero", 14, 372));
		string[] cards = { "cpu", "gpu", "mem", "therm", "net" };
		for (int i = 0; i < cards.Length; i++)
		{
			l.Widgets.Add(Card(cards[i], 398 + i * 304, 292));
		}
		return l;
	}

	/// <summary>
	/// Spectrum: a black page of rainbow glass slats with a dome of glowing blue rings in the middle. Three columns of
	/// heavy tilted slab letters — the time, processor load inside the dome and graphics load — each under a fine
	/// heading and ornament, that jostle harder as the machine works and drop in digit by digit as they change;
	/// sonar pulses run out through the dome at a rate set by the processor; and spectrum bars along the foot for
	/// memory, network, graphics temperature and power.
	/// </summary>
	private static ScreenLayout Spectrum()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Spectrum",
			Description = "Tilted slab readings over rainbow glass and a pulsing dome of blue rings",
			BuiltIn = true,
			Background = "spectrum",
			Color1 = "#050608",
			Color2 = "#2f6bff",
			Accent = "#e8873a"
		};
		const string ink = "#ece7da", blue = "#2f6bff";
		Widget Heading(string text, int cx, int y) => T(text, cx - 300, y, 600, 44, 30f, ink, "center", bold: false, "Segoe UI Semibold");
		Widget Ornament(int cx, int y) => new Widget { Kind = WidgetKind.Panel, Effect = "ornament", X = cx - 130, Y = y, W = 260, H = 12, Color = ink, Text = "" };
		Widget Slabs(string sensor, int x, int y, int w, int h) => new Widget
		{
			Kind = WidgetKind.Value, Sensor = sensor, Effect = "slabs", Format = "0", X = x, Y = y, W = w, H = h, Size = 270f,
			Font = "Impact", Bold = false, Align = "center", Color = ink, Float = true
		};
		Widget Bar(string label, string sensor, int x, float max, string format = "0") => new Widget
		{
			Kind = WidgetKind.Value, Sensor = sensor, Effect = "prismbar", Text = label, Format = format, X = x, Y = 404, W = 230, H = 62, Size = 30f,
			Font = "Segoe UI Semibold", Bold = false, Color = ink, Color2 = "#9aece7da", Track = blue, Thickness = 4f, Max = max, Float = true
		};
		// the dome's pulses, under everything else
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "sonar", Sensor = "cpu.load", X = 0, Y = 0, W = 1920, H = 480, Color2 = "#4a8cff", Text = "", Float = true });
		// the time at the left
		l.Widgets.Add(Heading("{date:dddd d MMMM}", 290, 30));
		l.Widgets.Add(Ornament(290, 80));
		Widget time = Slabs("cpu.load", 30, 104, 520, 290);
		time.Kind = WidgetKind.Text;
		time.Text = "{time:HH:mm}";
		l.Widgets.Add(time);
		// the processor inside the dome
		l.Widgets.Add(Heading("PROCESSOR", 960, 30));
		l.Widgets.Add(Ornament(960, 80));
		l.Widgets.Add(Slabs("cpu.load", 680, 104, 560, 290));
		// graphics at the right
		l.Widgets.Add(Heading("GRAPHICS", 1630, 30));
		l.Widgets.Add(Ornament(1630, 80));
		l.Widgets.Add(Slabs("gpu.load", 1370, 104, 520, 290));
		// spectrum bars along the foot
		l.Widgets.Add(Bar("MEMORY", "ram.load", 46, 100f));
		l.Widgets.Add(Bar("NETWORK", "net.down", 310, 50f, "0.0"));
		Widget temp = Bar("GPU TEMP", "gpu.temp", 1390, 100f);
		temp.WarnAt = 83f;
		temp.WarnColor = "#ffb066";
		l.Widgets.Add(temp);
		l.Widgets.Add(Bar("GPU POWER", "gpu.power", 1654, 350f));
		return l;
	}

	/// <summary>
	/// Reeded glass: a deep red page behind vertical glass ribs, with the readings as dark shapes seen through
	/// the glass, each rib showing its own waving slice and the letters smearing down into the dark below.
	/// Processor and graphics load large at the left, graphics temperature, memory and power at the right, and
	/// the date and time in fine light type along the bottom.
	/// </summary>
	private static ScreenLayout Reeded()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Reeded",
			Description = "Dark readings seen through red reeded glass, smearing down into shadow",
			BuiltIn = true,
			Background = "reeded",
			Color1 = "#d0060f",
			Color2 = "#3a0205"
		};
		const string ink = "#080202", label = "#ffc4bd";
		Widget Label(string text, int x, int y) => T(text, x, y, 460, 30, 21f, label, "left", bold: false, "Bahnschrift Light");
		Widget Shape(string sensor, int x, int y, int w, int h, float size, float smear) => new Widget
		{
			Kind = WidgetKind.Value, Sensor = sensor, Effect = "reeded", Format = "0", X = x, Y = y, W = w, H = h, Size = size,
			Font = "Segoe UI Black", Bold = false, Align = "left", Color = ink, WarnColor = "#ffe2dc", Thickness = smear, Float = true
		};
		// a caped figure behind the glass between the two big readings
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "reedfigure", X = 566, Y = 26, W = 410, H = 454, Color = ink, Size = 220f, Text = "" });
		l.Widgets.Add(Label("P R O C E S S O R", 62, 30));
		l.Widgets.Add(Shape("cpu.load", 40, 80, 500, 230, 220f, 190f));
		l.Widgets.Add(Label("G R A P H I C S", 1016, 30));
		l.Widgets.Add(Shape("gpu.load", 1000, 80, 370, 230, 220f, 190f));
		(string Label, string Sensor)[] side = { ("G P U   T E M P", "gpu.temp"), ("M E M O R Y", "ram.load"), ("G P U   P O W E R", "gpu.power") };
		for (int i = 0; i < side.Length; i++)
		{
			int y = 22 + i * 146;
			l.Widgets.Add(Label(side[i].Label, 1454, y));
			Widget v = Shape(side[i].Sensor, 1438, y + 28, 450, 100, 96f, 42f);
			if (side[i].Sensor == "gpu.temp")
			{
				v.WarnAt = 83f;
			}
			l.Widgets.Add(v);
		}
		l.Widgets.Add(T("{date:dddd d MMMM}   ·   {time:HH:mm}", 70, 428, 900, 34, 24f, label, "left", bold: false, "Bahnschrift Light"));
		// the front of the glass over everything
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "reedlines", X = 0, Y = 0, W = 1920, H = 480, Text = "" });
		return l;
	}

	/// <summary>
	/// A newsprint collage: torn clippings at the edges of a white page and a sweep of red and navy hatched ink
	/// across it. Processor and graphics load in big engraved press type on the sweep, labelled by vertical
	/// strips; the masthead, time and network on clippings at the left; graphics temperature, memory and power on
	/// clippings at the right; and a news ticker along the bottom.
	/// </summary>
	private static ScreenLayout Headline()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Headline",
			Description = "Newsprint collage with a red and navy ink sweep, readings printed as headlines",
			BuiltIn = true,
			Background = "newsprint",
			Color1 = "#f6f3ec",
			Color2 = "#d02a42",
			Accent = "#1d2b52"
		};
		const string navy = "#1d2b52", red = "#c8243a", paper = "#faf7f0";
		Widget Clip(string text, int x, int y, int w, int h, float size, float tilt, string font = "Impact", string align = "left") => new Widget
		{
			Kind = WidgetKind.Text, Effect = "clipping", Text = text, X = x, Y = y, W = w, H = h, Size = size, Font = font, Bold = font != "Impact",
			Align = align, Color = navy, Color2 = paper, Track = red, WarnColor = red, Thickness = 18f, Radius = tilt, Float = true
		};
		Widget Press(string sensor, int x, int y) => new Widget
		{
			Kind = WidgetKind.Value, Sensor = sensor, Effect = "presstype", Format = "0", X = x, Y = y, W = 520, H = 200, Size = 210f,
			Font = "Impact", Bold = false, Align = "left", Color = "#fbf8f1", Color2 = navy, Float = true
		};
		l.Widgets.Add(Clip("Late edition  ·  {date:d MMMM yyyy}|The Daily Readout", 30, 16, 520, 100, 50f, -1.2f, "Georgia"));
		l.Widgets.Add(Clip("{date:dddd}|{time:HH:mm}", 44, 132, 410, 196, 132f, 1.4f));
		l.Widgets.Add(Clip("Network|↓ {net.down:0.0} MB/s", 40, 342, 340, 92, 42f, -2f));
		// processor and graphics in press type on the sweep, each labelled by a vertical strip
		l.Widgets.Add(Clip("PROCESSOR", 478, 330, 190, 40, 26f, -90f, align: "center"));
		l.Widgets.Add(Press("cpu.load", 600, 238));
		l.Widgets.Add(Clip("GRAPHICS", 806, 112, 190, 40, 26f, -90f, align: "center"));
		l.Widgets.Add(Press("gpu.load", 930, 28));
		Widget temp = Clip("GPU temp|{gpu.temp:0}°C", 1540, 20, 350, 128, 76f, 1.8f);
		temp.Sensor = "gpu.temp";
		temp.WarnAt = 83f;
		l.Widgets.Add(temp);
		l.Widgets.Add(Clip("Memory|{ram.load:0}%", 1568, 160, 330, 128, 76f, -1.4f));
		l.Widgets.Add(Clip("GPU power|{gpu.power:0} W", 1536, 300, 356, 130, 76f, 1f));
		l.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text, Effect = "ticker", X = 0, Y = 446, W = 1920, H = 34, Size = 19f, Font = "Bahnschrift SemiBold", Bold = false,
			Color = "#ffffff", Color2 = navy, Track = red, Thickness = 80f, Float = true,
			Text = "PROCESSOR {cpu.load:0}%  ·  BUSIEST THREAD {cpu.busiest:0}%      •      GRAPHICS {gpu.load:0}%  {gpu.temp:0}°C  {gpu.power:0} W      •      MEMORY {ram.used:0.0} OF {ram.total:0} GB      •      NETWORK ↓ {net.down:0.0}  ↑ {net.up:0.0} MB/s      •      {cpu.name:upper}  ·  {gpu.name:upper}      •      UP {uptime}"
		});
		return l;
	}

	/// <summary>
	/// A paper-cut sea: nested blue paper layers undulating under a pale paper sky, bubbles bobbing and a little
	/// boat rowing at the crest; the title and clock in the sky, the readings in the deep water.
	/// </summary>
	private static ScreenLayout PaperCut()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Paper Cut",
			Description = "Layered paper-cut sea with a rowing boat, readings in the deep water",
			BuiltIn = true,
			Background = "papersea",
			Color1 = "#93c6ea",
			Color2 = "#5d9fe0",
			Accent = "#1b2c66"
		};
		const string navy = "#233b7d", ink = "#2c4c8c", paper = "#f2f7ff", label = "#c4d8f5";
		l.Widgets.Add(T("A B S T R A C T   ·   L U M A L I", 70, 30, 700, 30, 20f, ink, "left", bold: false, "Segoe UI Light"));
		l.Widgets.Add(T("PAPER CUT", 64, 58, 700, 70, 56f, navy, "left", bold: false, "Bahnschrift SemiBold"));
		l.Widgets.Add(T("{time:HH:mm}  ·  {date:dddd d MMMM}", 70, 128, 700, 34, 22f, ink, "left", bold: false, "Segoe UI"));
		(string Label, string Value)[] readings =
		{
			("PROCESSOR", "{cpu.load:0}%"),
			("GRAPHICS", "{gpu.load:0}%"),
			("GPU TEMP", "{gpu.temp:0}°C"),
			("MEMORY", "{ram.load:0}%"),
			("GPU POWER", "{gpu.power:0} W")
		};
		for (int i = 0; i < readings.Length; i++)
		{
			int x = 90 + i * 236;
			l.Widgets.Add(T(readings[i].Label, x, 372, 220, 26, 17f, label, "left", bold: false, "Segoe UI Semibold"));
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Text, Effect = "glow", Text = readings[i].Value, X = x, Y = 396, W = 220, H = 66, Size = 52f,
				Font = "Segoe UI Semibold", Bold = false, Align = "left", Color = paper, Color2 = "#0a1636"
			});
		}
		return l;
	}

	/// <summary>
	/// The paper-art lighthouse filling the screen, its beam sweeping across the red sky, with the readings in
	/// sliced, rippling lettering like bent paper strips: processor and graphics left of the lamp, the clock to
	/// its right, and smaller readings along a soft dark fade at the bottom.
	/// </summary>
	private static ScreenLayout Lighthouse()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Lighthouse",
			Description = "Paper-art lighthouse clip with readings pressed into the scene as paper relief",
			BuiltIn = true,
			Background = "video",
			Image = Path.Combine(Store.Uploads, "lighthouse-wide"),
			VideoFps = 12,
			ImageOpacity = 1f,
			Color1 = "#000000",
			Color2 = "#000000"
		};
		const string cream = "#fff1e2", soft = "#ffd9c9", font = "Bahnschrift SemiBold";
		Widget Sliced(string sensor, string text, int x, int y, int w, int h, float size, string align = "left") => new Widget
		{
			Kind = sensor.Length > 0 ? WidgetKind.Value : WidgetKind.Text, Sensor = sensor, Text = text, Effect = "relief", Float = true, Thickness = 58f,
			X = x, Y = y, W = w, H = h, Size = size, Font = "Segoe UI Black", Bold = false, Align = align, Color = "#f6e8d4", Color2 = "#9a1812", Track = "#18485a",
			Format = "0", WarnColor = "#ff9a3c"
		};
		Widget Label(string text, int x, int y, int w, string align = "left") => new Widget
		{
			Kind = WidgetKind.Text, Effect = "relief", Text = text, X = x, Y = y, W = w, H = 34, Size = 19f, Font = "Segoe UI Black", Bold = false,
			Align = align, Color = "#f6e8d4", Thickness = 50f
		};
		// a soft dark fade along the bottom for the small readings
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, X = 0, Y = 330, W = 1920, H = 150, Color = "#00000000", Color2 = "#c8140405", Thickness = 0f, Radius = 0f, Text = "" });
		l.Widgets.Add(Label("PROCESSOR", 56, 22, 400));
		l.Widgets.Add(Sliced("cpu.load", "", 50, 58, 600, 230, 190f));
		l.Widgets.Add(Label("GRAPHICS", 676, 22, 400));
		l.Widgets.Add(Sliced("gpu.load", "", 670, 58, 540, 230, 190f));
		l.Widgets.Add(Label("TIME", 1490, 40, 400, "right"));
		l.Widgets.Add(Sliced("", "{time:HH:mm}", 1440, 76, 452, 170, 128f, "right"));
		(string Label, string Value)[] small =
		{
			("GPU TEMP", "{gpu.temp:0}°C"),
			("MEMORY", "{ram.load:0}%"),
			("GPU POWER", "{gpu.power:0} W"),
			("NETWORK", "↓ {net.down:0.0} MB/s")
		};
		for (int i = 0; i < small.Length; i++)
		{
			int x = 62 + i * 270;
			l.Widgets.Add(Label(small[i].Label, x, 376, 260));
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Text, Effect = "relief", Text = small[i].Value, X = x, Y = 410, W = 262, H = 56, Size = 40f, Font = "Segoe UI Black", Bold = false,
				Align = "left", Color = cream, Thickness = 58f
			});
		}
		l.Widgets.Add(Label("{date:dddd d MMMM}", 1440, 420, 450, "right"));
		return l;
	}
	/// <summary>
	/// A music-player look: warm colour drifting behind fluted glass, a 3D carousel of glass cards (one per
	/// reading, each with a colourful cover, live value and history) that turns every six seconds, and a glass
	/// player bar whose "now playing" chip shows the time and date.
	/// </summary>
	private static ScreenLayout Carousel()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Carousel",
			Description = "Glass card carousel of readings over fluted glass, with a player bar",
			BuiltIn = true,
			Background = "fluted",
			Color1 = "#e89634",
			Color2 = "#8a1d10"
		};
		l.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Panel, Effect = "carousel", Float = true, X = 0, Y = 14, W = 1920, H = 360, Thickness = 6f, Font = "Segoe UI Semibold", Bold = false,
			Text = "cpu.load|Processor|#ffb35c|#c2410c;gpu.load|Graphics|#22d3ee|#1e3a5f;gpu.temp|Temperature|#fb7185|#7f1d1d;"
				+ "ram.load|Memory|#fde047|#a16207;gpu.power|Graphics power|#a3e635|#166534;net.down|Network|#93c5fd|#1e3a8a"
		});
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "player", Float = true, X = 410, Y = 390, W = 1100, H = 78, Font = "Segoe UI", Bold = false, Text = "" });
		return l;
	}

	/// <summary>
	/// A machined instrument panel in graphite and orange: an analog clock with a turning bezel, needle gauges
	/// for processor and graphics load, gears that spin with graphics power, an LED per processor thread, a
	/// small temperature gauge and an inset readout screen, all on bevelled plates with screws.
	/// </summary>
	private static ScreenLayout Mechanica()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Mechanica",
			Description = "Machined instrument panel: analog clock, needle gauges, gears and LEDs in graphite and orange",
			BuiltIn = true,
			Background = "machined",
			Color1 = "#45484e",
			Color2 = "#2c2e33"
		};
		const string orange = "#ff7a1a", grey = "#cfd3d9", mono = "Consolas", font = "Bahnschrift";
		Widget Plate(int x, int y, int w, int h, string title) => new Widget
		{
			Kind = WidgetKind.Panel, Effect = "plate", X = x, Y = y, W = w, H = h, Radius = 14f, Thickness = 16f, Text = title, Size = 20f,
			Font = font, Bold = true, Color = "#383b41", Color2 = "#2b2d32", WarnColor = grey
		};
		Widget Gauge(string sensor, int x, int y, int size, string label, float min, float max) => new Widget
		{
			Kind = WidgetKind.Gauge, Effect = "mech", Sensor = sensor, X = x, Y = y, W = size, H = size, Min = min, Max = max, Print = label,
			Font = font, Bold = true, Format = "0", WarnColor = "#ff3c28"
		};
		Widget Engraved(string text, int x, int y, int w, string align = "left") => T(text, x, y, w, 24, 16f, "#b4b9c1", align, bold: false, mono);
		Widget Bolt(int x, int y, int size = 16, float turn = 15f) => new Widget { Kind = WidgetKind.Panel, Effect = "bolt", X = x, Y = y, W = size, H = size, Radius = turn, Text = "" };
		Widget Knob(string sensor, int x, int y, int size, float max = 100f) => new Widget { Kind = WidgetKind.Ring, Effect = "knob", Float = true, Sensor = sensor, X = x, Y = y, W = size, H = size, Min = 0f, Max = max };

		// the clock, with rotary knobs and bolts in the corners of its plate
		l.Widgets.Add(Plate(12, 12, 472, 456, ""));
		l.Widgets[^1].Thickness = 0f;
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "mechclock", Float = true, X = 46, Y = 30, W = 404, H = 420, Font = font, Bold = true, Text = "" });
		l.Widgets.Add(Knob("gpu.fan", 22, 22, 64));
		l.Widgets.Add(Knob("ram.load", 410, 22, 64));
		l.Widgets.Add(Knob("cpu.load", 410, 394, 64));
		l.Widgets.Add(Bolt(28, 404, 18, 10f));
		l.Widgets.Add(Bolt(58, 430, 14, 40f));
		l.Widgets.Add(Engraved("CHRONO", 18, 88, 90));
		l.Widgets.Add(Engraved("MK-II", 410, 88, 70));

		// processor and graphics gauges with a signal rail between them, details and vents
		l.Widgets.Add(Plate(496, 12, 700, 456, "I N S T R U M E N T S"));
		l.Widgets.Add(Engraved("SER 0471 · AXIS 02", 900, 24, 270, "right"));
		l.Widgets.Add(Gauge("cpu.load", 512, 46, 306, "CPU LOAD", 0f, 100f));
		l.Widgets.Add(Gauge("gpu.load", 876, 46, 306, "GPU LOAD", 0f, 100f));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Divider, Effect = "rail", X = 840, Y = 70, W = 12, H = 280, Thickness = 2f });
		(string Label, string Value, int X, int Y)[] details =
		{
			("BUSY THREAD", "{cpu.busiest:0}%", 522, 356),
			("MEMORY", "{ram.used:0.0} GB", 522, 392),
			("GPU CLOCK", "{gpu.clock:0} MHz", 886, 356),
			("VRAM", "{gpu.vram:0.0} GB", 886, 392)
		};
		foreach (var d in details)
		{
			l.Widgets.Add(T(d.Label, d.X, d.Y, 170, 34, 20f, grey, "left", bold: false, mono));
			l.Widgets.Add(T(d.Value, d.X + 130, d.Y, 170, 34, 25f, orange, "right", bold: true, mono));
		}
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "vent", X = 526, Y = 436, W = 640, H = 18, Thickness = 4f, Text = "" });

		// drive gears, thread LEDs, indicator lamps, switches, temperature and the readout screen
		l.Widgets.Add(Plate(1208, 12, 700, 456, "D R I V E   ·   T H R E A D S   ·   T E L E M E T R Y"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Ring, Effect = "gear", Float = true, Sensor = "gpu.power", X = 1226, Y = 48, W = 150, H = 150, Thickness = 18f, Min = 0f, Max = 600f });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Ring, Effect = "gear", Float = true, Sensor = "gpu.power", Print = "reverse", X = 1362, Y = 112, W = 98, H = 98, Thickness = 12f, Min = 0f, Max = 600f });
		l.Widgets.Add(Engraved("GPU POWER", 1228, 206, 220));
		l.Widgets.Add(new Widget { Kind = WidgetKind.CoreBars, Effect = "leds", X = 1490, Y = 50, W = 262, H = 112 });
		l.Widgets.Add(Engraved("CPU THREADS", 1490, 160, 220));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "indicators", Text = "cpu.load,gpu.load,ram.load,gpu.temp,net.down", X = 1774, Y = 50, W = 112, H = 154, Thickness = 22f });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "switches", Text = "CPU:cpu.load:50|GPU:gpu.load:50|FAN:gpu.fan:1|NET:net.down:0.5", X = 1490, Y = 186, W = 280, H = 68, Size = 16f, Font = mono });
		l.Widgets.Add(Gauge("gpu.temp", 1224, 260, 190, "GPU TEMP", 20f, 100f));
		l.Widgets[^1].WarnAt = 83f;
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "screen", X = 1432, Y = 264, W = 456, H = 186, Radius = 6f, Text = "" });
		(string Label, string Value)[] screen =
		{
			("GPU POWER", "{gpu.power:0} W"),
			("GPU FAN", "{gpu.fan:0} %"),
			("NETWORK", "↓{net.down:0.0} ↑{net.up:0.0}"),
			("SYSTEM TIME", "{time:HH:mm:ss}")
		};
		for (int i = 0; i < screen.Length; i++)
		{
			int y = 276 + i * 42;
			l.Widgets.Add(T(screen[i].Label, 1450, y, 200, 34, 21f, grey, "left", bold: false, mono));
			l.Widgets.Add(T(screen[i].Value, 1600, y - 2, 270, 36, 26f, orange, "right", bold: true, mono));
		}
		return l;
	}

	/// <summary>
	/// A cyber HUD: a sci-fi targeting ring holding the processor load, a "cyber display" title with teal and
	/// coral extrude and circuit cuts, running chevrons, a ruler-tick history, a cyber clock, and four bracketed
	/// readouts with circuit tags, over a dark grid with drifting bokeh lights.
	/// </summary>
	private static ScreenLayout Astro()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Astro",
			Description = "Cyber HUD: targeting ring, extruded cyber type, circuit tags and a ruler graph",
			BuiltIn = true,
			Background = "hud",
			Color1 = "#081420",
			Color2 = "#020407"
		};
		const string white = "#f2f6f8", teal = "#29c9c1", coral = "#f05a46", page = "#07101a", font = "Bahnschrift SemiBold";
		Widget Cyber(string text, int x, int y, int w, int h, float size, string align = "left") => new Widget
		{
			Kind = WidgetKind.Text, Effect = "cyber", Text = text, X = x, Y = y, W = w, H = h, Size = size, Font = font, Bold = false,
			Align = align, Color = white, Color2 = teal, Track = coral, WarnColor = page, Thickness = 118f
		};
		Widget Tag(string text, int x, int y, int w, float lead = 40f, string color = teal) => new Widget
		{
			Kind = WidgetKind.Text, Effect = "circuittag", Text = text, X = x, Y = y, W = w, H = 34, Size = 13f, Font = font, Color = color, Thickness = lead
		};

		// the targeting ring and an edge tab
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, X = 0, Y = 150, W = 6, H = 180, Color = teal, Color2 = teal, Radius = 0f, Thickness = 0f, Text = "" });
		l.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Ring, Effect = "hud", Float = true, Sensor = "cpu.load", X = 34, Y = 10, W = 460, H = 460, Size = 62f, Font = font, Bold = false,
			Color = white, Color2 = teal, Track = coral, WarnColor = page, Thickness = 118f, Print = "PROCESSOR", Min = 0f, Max = 100f, Format = "0"
		});

		// title, chevrons, subtitle, the ruler history and the clock
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "chevrons", Text = "Live telemetry", X = 546, Y = 20, W = 640, H = 34, Size = 17f, Font = font, Color = white });
		l.Widgets.Add(Cyber("LUMALI", 540, 62, 650, 118, 88f));
		l.Widgets.Add(T("A   C Y B E R   S Y S T E M   D I S P L A Y", 548, 180, 640, 30, 18f, "#cfe6ea", "left", bold: false, "Bahnschrift Light"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "ruler", Sensor = "gpu.load", X = 548, Y = 232, W = 620, H = 74, Color = coral, Thickness = 6f, Min = 0f, Max = 100f });
		l.Widgets.Add(Tag("GPU load history", 548, 314, 300, 24f, coral));
		l.Widgets.Add(Cyber("{time:HH:mm}", 546, 356, 340, 104, 76f));
		l.Widgets.Add(Tag("{date:ddd d MMM}", 930, 390, 240, 30f));

		// four bracketed readouts
		(string Label, string Sensor, string Unit)[] readouts =
		{
			("GPU load", "gpu.load", "%"),
			("GPU temp", "gpu.temp", "°C"),
			("Memory", "ram.load", "%"),
			("GPU power", "gpu.power", "W")
		};
		for (int i = 0; i < readouts.Length; i++)
		{
			int x = 1226 + i % 2 * 340, y = 18 + i / 2 * 228;
			l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, Effect = "brackets", X = x, Y = y, W = 322, H = 214, Color = coral, Thickness = 18f, Text = "" });
			l.Widgets.Add(Tag(readouts[i].Label, x + 22, y + 20, 290, 36f));
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value, Effect = "cyber", Sensor = readouts[i].Sensor, Text = readouts[i].Unit, X = x + 22, Y = y + 72, W = 290, H = 118,
				Size = 84f, Font = font, Bold = false, Align = "left", Color = white, Color2 = teal, Track = coral, WarnColor = page, Thickness = 112f, Format = "0",
				WarnAt = readouts[i].Sensor == "gpu.temp" ? 83 : 0, Print = "#ff5a46"
			});
		}
		l.Widgets.Add(new Widget { Kind = WidgetKind.Panel, X = 1914, Y = 120, W = 6, H = 240, Color = teal, Color2 = teal, Radius = 0f, Thickness = 0f, Text = "" });
		return l;
	}

	/// <summary>
	/// A dotted device dashboard on a warm gradient: a dot-matrix title column with the clock and a typed
	/// tagline, then five dark cards (processor, graphics, memory, thermals, network) drawn in LED dots, with
	/// grey data and a red-orange accent on whatever is newest or notable.
	/// </summary>
	private static ScreenLayout Deer()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Deer",
			Description = "Dotted LED dashboard cards on a warm gradient",
			BuiltIn = true,
			Background = "warm",
			Color1 = "#e9a375",
			Color2 = "#2a2523",
			Accent = "#ffb27a"
		};
		const string ink = "#ececec", grey = "#8f8f8f", red = "#ff4b2b", cream = "#f7efe8", mono = "Consolas";
		Widget Dots(string text, int x, int y, int w, int h, float cell, string color, string align = "left") =>
			new Widget { Kind = WidgetKind.Text, Effect = "dots", Text = text, X = x, Y = y, W = w, H = h, Size = cell, Align = align, Color = color };
		Widget DotValue(string sensor, int x, int y, int w, int h, float cell, string unit = "", string align = "left", string format = "0") =>
			new Widget { Kind = WidgetKind.Value, Effect = "dots", Sensor = sensor, Text = unit, X = x, Y = y, W = w, H = h, Size = cell, Align = align, Color = ink, Format = format };
		Widget Label(string text, int x, int y, int w, string color = grey, float size = 17f, string align = "left") =>
			T(text, x, y, w, 28, size, color, align, bold: false, mono);
		Widget Card(int x, string title) => new Widget
		{
			Kind = WidgetKind.Panel, Effect = "card", X = x, Y = 28, W = 310, H = 424, Radius = 16f, Text = title, Size = 18f,
			Font = mono, Color = "#1f1f20", Color2 = "#161617", WarnColor = "#dadada"
		};
		Widget Rule(int x, int y, int h) => new Widget { Kind = WidgetKind.Divider, Effect = "dashed", X = x, Y = y, W = 2, H = h, Color = "#40ffffff", Thickness = 1f };

		// the title column
		l.Widgets.Add(Dots("LUMALI", 32, 32, 254, 36, 4.2f, cream));
		l.Widgets.Add(Dots("DASHBOARD", 32, 78, 254, 32, 3.8f, "#e8d9cc"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "bullet", Text = "All systems operational", X = 36, Y = 124, W = 250, H = 26, Size = 17f, Font = mono, Color = "#efe4da", Color2 = red });
		l.Widgets.Add(Dots("{time:HH:mm}", 34, 196, 250, 60, 8f, cream));
		l.Widgets.Add(T("{date:dddd d MMMM}", 36, 262, 250, 28, 18f, "#e2d4c8", "left", bold: false, mono));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "type", Text = "MONITOR EVERYTHING", X = 36, Y = 382, W = 250, H = 30, Size = 20f, Font = mono, Color = "#efe4da" });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "type", Text = "WITH LUMALI", X = 36, Y = 414, W = 250, H = 30, Size = 20f, Font = mono, Color = "#efe4da" });

		// 01 processor: a status word, a live waveform and three readings
		int c1 = 296;
		l.Widgets.Add(Card(c1, "01  PROCESSOR"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "status", Sensor = "cpu.load", Print = "IDLE|WORKING...|BUSY...", X = c1 + 22, Y = 68, W = 266, H = 48, Size = 5.2f, Align = "left", Color = ink });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "wave", Sensor = "cpu.load", X = c1 + 22, Y = 132, W = 268, H = 160, Thickness = 7f, Min = 0f, Max = 100f, Color = "#bdbdbd", Color2 = red });
		string[] cpuLabels = { "LOAD", "THREAD", "CLOCK" };
		for (int i = 0; i < 3; i++)
		{
			int x = c1 + 22 + i * 92;
			l.Widgets.Add(Label(cpuLabels[i], x, 330, 86));
			if (i > 0)
			{
				l.Widgets.Add(Rule(x - 10, 336, 64));
			}
		}
		l.Widgets.Add(DotValue("cpu.load", c1 + 22, 360, 86, 36, 4.2f, "%"));
		l.Widgets.Add(DotValue("cpu.busiest", c1 + 114, 360, 86, 36, 4.2f, "%"));
		l.Widgets.Add(Dots("{cpu.clock:0}", c1 + 206, 360, 86, 36, 3.8f, ink));

		// 02 graphics: the load in big dots over a dotted history, power and temperature underneath
		int c2 = c1 + 326;
		l.Widgets.Add(Card(c2, "02  GRAPHICS"));
		l.Widgets.Add(DotValue("gpu.load", c2 + 22, 64, 266, 64, 8f, "%"));
		l.Widgets.Add(Label("LOAD", c2 + 22, 134, 200));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "dotbars", Sensor = "gpu.load", X = c2 + 22, Y = 168, W = 268, H = 128, Thickness = 8f, Min = 0f, Max = 100f, Color = "#9a9a9a", Color2 = red });
		l.Widgets.Add(Label("POWER", c2 + 22, 330, 120));
		l.Widgets.Add(Dots("{gpu.power:0}W", c2 + 22, 360, 150, 36, 4.2f, ink));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "pill", Text = "{gpu.temp:0}°C", X = c2 + 176, Y = 356, W = 112, H = 40, Size = 19f, Font = mono, Color = red, Color2 = red });

		// 03 memory: a dotted ring with the share in use, and a legend
		int c3 = c2 + 326;
		l.Widgets.Add(Card(c3, "03  MEMORY"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Ring, Effect = "dotring", Float = true, Sensor = "ram.load", X = c3 + 18, Y = 78, W = 188, H = 188, Thickness = 16f, Min = 0f, Max = 100f, Color = "#ececec", Color2 = red });
		l.Widgets.Add(DotValue("ram.load", c3 + 18, 142, 188, 40, 5f, "%", "center"));
		l.Widgets.Add(Label("IN USE", c3 + 18, 190, 188, grey, 16f, "center"));
		(string Name, string Value, string Dot)[] legend =
		{
			("USED", "{ram.used:0}G", "#ffffff"),
			("TOTAL", "{ram.total:0}G", red),
			("VRAM", "{gpu.vram:0}G", red)
		};
		for (int i = 0; i < legend.Length; i++)
		{
			int y = 92 + i * 62;
			l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "bullet", Text = legend[i].Name, X = c3 + 216, Y = y, W = 92, H = 22, Size = 15f, Font = mono, Color = grey, Color2 = legend[i].Dot });
			l.Widgets.Add(Dots(legend[i].Value, c3 + 218, y + 24, 88, 28, 3.4f, ink));
		}
		l.Widgets.Add(Label("PRESSURE", c3 + 22, 330, 150));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "pill", Text = "HEALTHY", Print = "dot", X = c3 + 22, Y = 358, W = 156, H = 40, Size = 18f, Font = mono, Color = "#d8d8d8", Color2 = red });

		// 04 thermals: a heartbeat trace that beats harder as the card gets hotter, and three readings
		int c4 = c3 + 326;
		l.Widgets.Add(Card(c4, "04  THERMALS"));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "pulse", Sensor = "gpu.temp", X = c4 + 16, Y = 78, W = 278, H = 176, Thickness = 6f, Min = 20f, Max = 95f, Color = "#d0d0d0", Color2 = red });
		string[] heatLabels = { "GPU", "POWER", "FAN" };
		string[] heatSensors = { "gpu.temp", "gpu.power", "gpu.fan" };
		for (int i = 0; i < 3; i++)
		{
			int x = c4 + 22 + i * 92;
			l.Widgets.Add(Label(heatLabels[i], x, 270, 86));
			l.Widgets.Add(DotValue(heatSensors[i], x, 298, 86, 36, 4.2f, i == 0 ? "°" : (i == 1 ? "W" : "%")));
		}
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "pill", Text = "STABLE", Print = "dot", X = c4 + 22, Y = 360, W = 146, H = 40, Size = 18f, Font = mono, Color = "#d8d8d8", Color2 = red });

		// 05 network: download in big dots with a live dotted line, upload and the date underneath
		int c5 = c4 + 326;
		l.Widgets.Add(Card(c5, "05  NETWORK"));
		l.Widgets.Add(Label("LIVE", c5 + 190, 44, 98, red, 18f, "right"));
		l.Widgets.Add(DotValue("net.down", c5 + 22, 68, 266, 56, 7f, "", "left", "0.0"));
		l.Widgets.Add(Label("DOWNLOAD  MB/S", c5 + 22, 134, 240));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "dotline", Sensor = "net.down", Format = "auto", X = c5 + 22, Y = 176, W = 262, H = 116, Thickness = 9f, Min = 0f, Max = 20f, Color = "#cfcfcf", Color2 = red });
		l.Widgets.Add(Label("UPLOAD", c5 + 22, 330, 140));
		l.Widgets.Add(Dots("{net.up:0.0}", c5 + 22, 360, 140, 36, 4.2f, ink));
		l.Widgets.Add(Dots("{date:dd.MM}", c5 + 140, 360, 148, 36, 4.2f, red, "right"));
		return l;
	}

	/// <summary>
	/// A neon dashboard: dark panels with lit edges, segmented gradient rings for processor, graphics and
	/// memory, a glowing bar chart of processor history, a multi-line chart, a month calendar under a neon
	/// clock, and a bar chart of graphics power.
	/// </summary>
	private static ScreenLayout Neon()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Neon",
			Description = "Neon dashboard with gradient rings, glowing charts and a calendar",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#04060c",
			Color2 = "#04060c"
		};
		const string label = "#aeb9d8", font = "Bahnschrift", numbers = "Bahnschrift SemiBold";
		const string cyan = "#22e3ff", green = "#4ade80", pink = "#ff2d95", amber = "#facc15";
		Widget Box(int x, int y, int w, int h, string title, string accent) => new Widget
		{
			Kind = WidgetKind.Panel, Effect = "neon", X = x, Y = y, W = w, H = h, Text = title, Print = accent,
			Color = "#0b1120", Color2 = "#070b16", Track = "#22304f", WarnColor = label, Radius = 8f, Size = 15f, Font = font, Thickness = 1f
		};
		Widget Ring(string sensor, int x, int y, int size, string palette, string name) => new Widget
		{
			Kind = WidgetKind.Ring, Effect = "neon", Float = true, Sensor = sensor, X = x, Y = y, W = size, H = size, Thickness = 12f, Size = 46f,
			Font = numbers, Bold = false, Align = "center", Color = "#ffffff", Text = palette, Print = name, Format = "0", Min = 0f, Max = 100f,
			Track = "#1a2440"
		};
		Widget Stat(string text, int x, int y, int w, string color, string align = "left", float size = 21f) =>
			T(text, x, y, w, (int)(size * 1.5f), size, color, align, bold: false, font);

		// processor and graphics rings, with their details underneath
		l.Widgets.Add(Box(12, 12, 452, 456, "Processor · Graphics", pink));
		l.Widgets.Add(Ring("cpu.load", 26, 44, 206, "#ff2d95,#a855f7,#3b82f6", "PROCESSOR"));
		l.Widgets.Add(Ring("gpu.load", 244, 44, 206, "#22d3ee,#34d399,#a3e635", "GRAPHICS"));
		(string Label, string Value, string Color)[] stats =
		{
			("Processor clock", "{cpu.clock:0} MHz", pink),
			("Busiest thread", "{cpu.busiest:0}%", "#c084fc"),
			("Graphics temperature", "{gpu.temp:0}°C", cyan),
			("Graphics power", "{gpu.power:0} W", green),
			("Video memory", "{gpu.vram:0.0} GB", amber)
		};
		for (int i = 0; i < stats.Length; i++)
		{
			int y = 268 + i * 38;
			l.Widgets.Add(Stat(stats[i].Label, 32, y, 260, label));
			l.Widgets.Add(Stat(stats[i].Value, 232, y - 1, 212, stats[i].Color, "right", 25f));
			l.Widgets[^1].Effect = "glow";
			l.Widgets[^1].Color2 = stats[i].Color;
			l.Widgets[^1].Font = numbers;
		}

		// processor history as glowing bars, and three readings as lines
		l.Widgets.Add(Box(476, 12, 700, 282, "Processor load · history", cyan));
		l.Widgets.Add(T("{cpu.load:0}%", 940, 12, 220, 50, 40f, cyan, "right", bold: false, numbers));
		l.Widgets[^1].Effect = "glow"; l.Widgets[^1].Color2 = cyan;
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "bars", Sensor = "cpu.load", X = 494, Y = 64, W = 664, H = 212, Text = "#22e3ff,#0e4a7a", Thickness = 9f, Min = 0f, Max = 100f });
		l.Widgets.Add(Box(476, 306, 700, 162, "Live readings", green));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "lines", Sensor = "gpu.load,gpu.temp,ram.load", Text = "#3b82f6,#4ade80,#ff4d6d", X = 494, Y = 344, W = 664, H = 112, Size = 16f, Font = font, Min = 0f, Max = 100f });

		// the clock and this month's calendar
		l.Widgets.Add(Box(1188, 12, 340, 456, "Calendar", pink));
		l.Widgets.Add(T("{time:HH:mm}", 1200, 46, 316, 76, 62f, cyan, "center", bold: false, numbers));
		l.Widgets[^1].Effect = "glow"; l.Widgets[^1].Color2 = cyan;
		l.Widgets.Add(T("{date:dddd d MMMM}", 1200, 118, 316, 32, 21f, label, "center", bold: false, font));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Effect = "calendar", Text = "", X = 1206, Y = 154, W = 304, H = 304, Size = 20f, Font = numbers, Bold = false, Color = "#c8d2eb", Color2 = "#f472b6", WarnColor = pink });

		// memory ring, graphics power bars and the network
		l.Widgets.Add(Box(1540, 12, 368, 456, "Memory · Power", amber));
		l.Widgets.Add(Ring("ram.load", 1552, 40, 168, "#ff4d6d,#fb923c,#facc15", "MEMORY"));
		l.Widgets[^1].Size = 38f; l.Widgets[^1].Thickness = 10f;
		l.Widgets.Add(Stat("{ram.used:0.0} GB", 1726, 72, 172, "#ffffff", "left", 32f));
		l.Widgets[^1].Font = numbers;
		l.Widgets.Add(Stat("of {ram.total:0} GB in use", 1726, 118, 172, label, "left", 19f));
		l.Widgets.Add(Stat("GPU POWER", 1556, 222, 200, label, "left", 18f));
		l.Widgets.Add(Stat("{gpu.power:0} W", 1716, 212, 176, green, "right", 28f));
		l.Widgets[^1].Effect = "glow"; l.Widgets[^1].Color2 = green; l.Widgets[^1].Font = numbers;
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Effect = "bars", Sensor = "gpu.power", X = 1556, Y = 262, W = 336, H = 140, Text = "#4ade80,#14532d", Thickness = 7f, Min = 0f, Max = 600f });
		l.Widgets.Add(Stat("↓ {net.down:0.0}  ↑ {net.up:0.0} MB/s", 1556, 418, 336, cyan, "left", 21f));
		return l;
	}

	/// <summary>
	/// A full command center in the style of a pixel-typeface specimen: a black page ruled into cells by fine
	/// dashed lines, a header band of bold-and-light captions, an open middle band of monoline pixel numbers
	/// over staircase graphs, and a footer of small pixel labels, all over a faint, slowly twisting wireframe.
	/// </summary>
	private static ScreenLayout Pixotype()
	{
		ScreenLayout l = new ScreenLayout
		{
			Name = "Pixotype",
			Description = "Minimal command center: pixel numbers, step graphs and dashed rules on black",
			BuiltIn = true,
			Background = "mesh",
			Color1 = "#060606",
			Color2 = "#060606",
			Accent = "#ffffff"
		};
		const string white = "#f4f4f4", grey = "#9c9c9c", rule = "#5affffff";
		const string bold = "Segoe UI Semibold", light = "Segoe UI Light";
		const int col = 384, top = 92, bottom = 392;
		Widget Rule(int x, int y, int w, int h) => new Widget { Kind = WidgetKind.Divider, Effect = "dashed", X = x, Y = y, W = w, H = h, Color = rule, Thickness = 1f };
		l.Widgets.Add(Rule(0, top - 1, 1920, 2));
		l.Widgets.Add(Rule(0, bottom - 1, 1920, 2));
		for (int i = 1; i < 5; i++)
		{
			l.Widgets.Add(Rule(i * col - 1, 0, 2, top));
			l.Widgets.Add(Rule(i * col - 1, bottom, 2, 480 - bottom));
		}
		(string Title, string Detail, string Sensor, float Max, string Label, string Caption)[] columns =
		{
			("Processor", "{cpu.clock:0} MHz  ·  busiest thread {cpu.busiest:0}%", "cpu.load", 100f, "CPU LOAD", "% · recent history"),
			("Graphics", "{gpu.clock:0} MHz  ·  {gpu.vram:0.0} GB video memory", "gpu.load", 100f, "GPU LOAD", "% · recent history"),
			("Thermals", "fan {gpu.fan:0}%  ·  {gpu.power:0} W", "gpu.temp", 100f, "GPU TEMP", "°C · recent history"),
			("Memory", "{ram.used:0.0} of {ram.total:0} GB in use", "ram.load", 100f, "MEMORY", "% in use · recent history")
		};
		for (int i = 0; i < columns.Length; i++)
		{
			int x = i * col + 24;
			l.Widgets.Add(T(columns[i].Title, x, 16, col - 48, 30, 19f, white, "left", bold: false, bold));
			l.Widgets.Add(T(columns[i].Detail, x, 46, col - 48, 30, 16f, grey, "left", bold: false, light));
			l.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value, Sensor = columns[i].Sensor, Effect = "pixo", Text = "",
				X = x, Y = 106, W = col - 48, H = 104, Size = 14f, Align = "left", Color = white, Format = "0",
				WarnAt = (columns[i].Sensor == "gpu.temp") ? 83 : 0, WarnColor = "#ff7a5c"
			});
			l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Sensor = columns[i].Sensor, Effect = "steps", X = x, Y = 236, W = col - 48, H = 128, Color = white, Min = 0f, Max = columns[i].Max, Thickness = 8f });
			l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Text = columns[i].Label, Effect = "pixo", X = x, Y = 404, W = col - 48, H = 24, Size = 3f, Align = "left", Color = white });
			l.Widgets.Add(T(columns[i].Caption, x, 434, col - 48, 28, 15f, grey, "left", bold: false, light));
		}
		// the last column: who and when in the header, the clock in pixel type, and the network
		int cx = 4 * col + 24;
		l.Widgets.Add(T("LumaLi", cx, 16, col - 48, 30, 19f, white, "right", bold: false, bold));
		l.Widgets.Add(T("{date:dddd d MMMM}", cx, 46, col - 48, 30, 16f, grey, "right", bold: false, light));
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Text = "{time:HH:mm}", Effect = "pixo", X = cx, Y = 106, W = col - 48, H = 104, Size = 14f, Align = "right", Color = white });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Graph, Sensor = "net.down", Effect = "steps", X = cx, Y = 236, W = col - 48, H = 128, Color = white, Min = 0f, Max = 20f, Thickness = 8f });
		l.Widgets.Add(new Widget { Kind = WidgetKind.Text, Text = "NETWORK", Effect = "pixo", X = cx, Y = 404, W = col - 48, H = 24, Size = 3f, Align = "right", Color = white });
		l.Widgets.Add(T("↓ {net.down:0.0}   ↑ {net.up:0.0} MB/s", cx, 434, col - 48, 28, 15f, grey, "right", bold: false, light));
		return l;
	}

	private static ScreenLayout Pixel()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Pixel",
			Description = "White pixel-block numbers that fly together on electric blue",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#0047f5",
			Color2 = "#0047f5"
		};
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text,
			Text = "{time:HH:mm}",
			Effect = "pixel",
			Float = true,
			X = 40,
			Y = 22,
			W = 520,
			H = 92,
			Size = 6f,
			Thickness = 14f,
			Font = "Segoe UI Black",
			Bold = true,
			Align = "left",
			Color = "#ffffff"
		});
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text,
			Text = "{date:dddd d MMMM}",
			Effect = "type",
			X = 330,
			Y = 70,
			W = 600,
			H = 40,
			Size = 26f,
			Font = "Bahnschrift SemiBold",
			Bold = false,
			Color = "#ffffff"
		});
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text,
			Text = "LumaLi",
			Effect = "type",
			X = 1690,
			Y = 34,
			W = 220,
			H = 40,
			Size = 26f,
			Font = "Bahnschrift SemiBold",
			Bold = false,
			Color = "#ffffff"
		});
		(string, string)[] array = new(string, string)[5]
		{
			("cpu.load", "CPU load"),
			("gpu.load", "GPU load"),
			("gpu.temp", "GPU temp"),
			("gpu.power", "GPU power"),
			("ram.load", "Memory")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int x = 40 + i * 376;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Effect = "pixel",
				Float = true,
				Text = ((array[i].Item1 == "gpu.temp") ? "°C" : ""),
				X = x,
				Y = 150,
				W = 350,
				H = 222,
				Size = 16f,
				Thickness = 12f,
				Font = "Segoe UI Black",
				Bold = true,
				Align = "left",
				Color = "#ffffff",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 83 : 0),
				WarnColor = "#ffe14d"
			});
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Text,
				Text = array[i].Item2,
				Effect = "type",
				X = x,
				Y = 392,
				W = 350,
				H = 46,
				Size = 30f,
				Font = "Bahnschrift SemiBold",
				Bold = false,
				Color = "#ffffff"
			});
		}
		return screenLayout;
	}

	private static ScreenLayout Prism()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Prism",
			Description = "Thin white readings over slowly turning iridescent glass",
			BuiltIn = true,
			Background = "video",
			Image = Path.Combine(Store.Uploads, "prism-glass"),
			VideoFps = 12,
			ImageOpacity = 1f,
			Color1 = "#000000",
			Color2 = "#000000"
		};
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Panel,
			X = 0,
			Y = 0,
			W = 1920,
			H = 200,
			Color = "#a8000000",
			Color2 = "#00000000",
			Thickness = 0f,
			Radius = 0f,
			Text = ""
		});
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Panel,
			X = 0,
			Y = 210,
			W = 1920,
			H = 270,
			Color = "#00000000",
			Color2 = "#d0000000",
			Thickness = 0f,
			Radius = 0f,
			Text = ""
		});
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text,
			Text = "{time:HH:mm}",
			Effect = "glass",
			Float = true,
			X = 36,
			Y = 14,
			W = 560,
			H = 160,
			Size = 196f,
			Font = "Bahnschrift SemiBold",
			Bold = false,
			Align = "left",
			Color = "#ffffff"
		});
		screenLayout.Widgets.Add(T("{date:dddd}", 520, 40, 400, 48, 40f, "#ffffff", "left", bold: false, "Bahnschrift Light"));
		screenLayout.Widgets.Add(T("{date:d MMMM}", 520, 88, 400, 48, 40f, "#b9c4e4", "left", bold: false, "Bahnschrift Light"));
		screenLayout.Widgets.Add(T("P R I S M   ·   L U M A L I", 1500, 30, 390, 24, 15f, "#b9c4e4", "right", bold: false, "Bahnschrift"));
		(string, string)[] array = new(string, string)[5]
		{
			("cpu.load", "PROCESSOR"),
			("gpu.load", "GRAPHICS"),
			("gpu.temp", "GPU TEMP"),
			("gpu.power", "GPU POWER"),
			("ram.load", "MEMORY")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int num = 40 + i * 376;
			string text = string.Join("    ", from word in array[i].Item2.Split(' ')
				select string.Join(" ", word.ToCharArray()));
			screenLayout.Widgets.Add(T($"0{i + 1}", num, 258, 60, 26, 17f, "#ffffff", "left", bold: true, "Bahnschrift"));
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Divider,
				X = num + 40,
				Y = 270,
				W = 300,
				H = 2,
				Color = "#50ffffff",
				Thickness = 1f
			});
			screenLayout.Widgets.Add(T(text, num, 284, 340, 24, 15f, "#b9c4e4", "left", bold: false, "Bahnschrift"));
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Effect = "glass",
				Float = true,
				Text = ((array[i].Item1 == "gpu.temp") ? "°C" : ""),
				X = num - 4,
				Y = 312,
				W = 360,
				H = 150,
				Size = 176f,
				Font = "Bahnschrift SemiBold",
				Bold = false,
				Align = "left",
				Color = "#ffffff",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 83 : 0),
				WarnColor = "#ff8f7a"
			});
		}
		return screenLayout;
	}

	private static ScreenLayout Eclipse()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Eclipse",
			Description = "Sunrise over a dark planet's edge, with thin white readings",
			BuiltIn = true,
			Background = "eclipse",
			Color1 = "#000000",
			Color2 = "#3f6f93",
			Accent = "#ff9338"
		};
		screenLayout.Widgets.Add(T("{time:HH:mm}", 40, 18, 520, 130, 112f, "#e9eef5", "left", bold: false, "Bahnschrift Light"));
		screenLayout.Widgets.Add(T("{date:dddd d MMMM}", 48, 146, 520, 30, 19f, "#7f95b0", "left", bold: false, "Bahnschrift Light"));
		(string, string)[] array = new(string, string)[5]
		{
			("cpu.load", "PROCESSOR"),
			("gpu.load", "GRAPHICS"),
			("gpu.temp", "GRAPHICS TEMP"),
			("gpu.power", "GRAPHICS POWER"),
			("ram.load", "MEMORY")
		};
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Divider,
			X = 900,
			Y = 286,
			W = 980,
			H = 2,
			Color = "#30c8dcff",
			Thickness = 1f
		});
		for (int i = 0; i < array.Length; i++)
		{
			int num = 900 + i * 200;
			screenLayout.Widgets.Add(T(array[i].Item2, num, 298, 196, 22, 13f, "#7f95b0", "left", bold: false, "Bahnschrift"));
			screenLayout.Widgets.Add(V(array[i].Item1, num - 4, 318, 196, 110, 80f, "#e9eef5"));
			List<Widget> widgets = screenLayout.Widgets;
			widgets[widgets.Count - 1].Font = "Bahnschrift Light";
			List<Widget> widgets2 = screenLayout.Widgets;
			widgets2[widgets2.Count - 1].Bold = false;
			if (array[i].Item1 == "gpu.temp")
			{
				List<Widget> widgets3 = screenLayout.Widgets;
				widgets3[widgets3.Count - 1].WarnAt = 83f;
				List<Widget> widgets4 = screenLayout.Widgets;
				widgets4[widgets4.Count - 1].WarnColor = "#ffb27a";
			}
		}
		return screenLayout;
	}

	private static ScreenLayout Stretch()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Stretch",
			Description = "Black digits that stretch down the page as the readings rise",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#ecebe5",
			Color2 = "#ecebe5"
		};
		(string, string, string, float)[] array = new(string, string, string, float)[5]
		{
			("cpu.load", "PROCESSOR", "LOAD %", 100f),
			("gpu.load", "GRAPHICS", "LOAD %", 100f),
			("gpu.temp", "GRAPHICS", "TEMP °C", 100f),
			("gpu.power", "GRAPHICS", "POWER W", 575f),
			("ram.load", "MEMORY", "IN USE %", 100f)
		};
		for (int i = 0; i < array.Length; i++)
		{
			int x = 28 + i * 380;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Effect = "stretch",
				Float = true,
				Text = "",
				X = x,
				Y = 24,
				W = 340,
				H = 440,
				Size = 190f,
				Thickness = 70f,
				Font = "Impact",
				Min = 0f,
				Max = array[i].Item4,
				Color = "#111111",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 83 : 0),
				WarnColor = "#c4241a"
			});
			screenLayout.Widgets.Add(T(array[i].Item2, x, 404, 340, 20, 14f, "#111111", "right", bold: false, "Arial"));
			screenLayout.Widgets.Add(T(array[i].Item3, x, 420, 340, 40, 30f, "#111111", "right", bold: true, "Bahnschrift Condensed"));
		}
		screenLayout.Widgets.Add(T("{time:HH:mm}", 1690, 26, 200, 30, 22f, "#111111", "right", bold: true, "Bahnschrift Condensed"));
		return screenLayout;
	}

	private static ScreenLayout Gap()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Gap",
			Description = "Towering black numbers running off a white page, with a swinging yellow sticker",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#f6f5f1",
			Color2 = "#f6f5f1"
		};
		(string, string)[] array = new(string, string)[5]
		{
			("cpu.load", "PROCESSOR LOAD %"),
			("gpu.load", "GRAPHICS LOAD %"),
			("gpu.temp", "GRAPHICS TEMPERATURE °C"),
			("gpu.power", "GRAPHICS POWER W"),
			("ram.load", "MEMORY IN USE %")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int x = 24 + i * 384;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Effect = "poster",
				Float = true,
				Text = "",
				X = x,
				Y = 128,
				W = 336,
				H = 400,
				Size = 100f,
				Font = "Impact",
				Bold = false,
				Align = "left",
				Color = "#0e0e0e",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 83 : 0),
				WarnColor = "#d92b1c"
			});
			screenLayout.Widgets.Add(T(array[i].Item2, x, 96, 336, 26, 15f, "#0e0e0e", "left", bold: true, "Arial"));
		}
		screenLayout.Widgets.Add(T("A SYSTEM OVERVIEW BY", 26, 14, 300, 34, 23f, "#0e0e0e", "left", bold: false, "Arial"));
		screenLayout.Widgets.Add(T("LUMALI", 314, 14, 300, 34, 23f, "#0e0e0e", "left", bold: true, "Arial"));
		string[] array2 = new string[4] { "HOW HARD", "YOUR MACHINE", "IS WORKING", "RIGHT NOW" };
		for (int j = 0; j < array2.Length; j++)
		{
			screenLayout.Widgets.Add(T(array2[j], 1700, 8 + j * 19, 200, 20, 13f, "#0e0e0e", "left", bold: true, "Arial"));
		}
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Text,
			Effect = "tag",
			Float = true,
			Text = "LIVE EDITION WITH\\n{time:HH:mm} SYSTEM CLOCK",
			X = 64,
			Y = 352,
			W = 330,
			H = 78,
			Size = 19f,
			Font = "Arial",
			Bold = true,
			Color = "#0e0e0e",
			Color2 = "#f7c600"
		});
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Panel,
			X = 1850,
			Y = 404,
			W = 50,
			H = 50,
			Color = "#0e0e0e",
			Color2 = "#0e0e0e",
			Thickness = 0f,
			Radius = 0f,
			Text = ""
		});
		screenLayout.Widgets.Add(T("LCD", 1850, 404, 50, 50, 14f, "#ffffff", "center", bold: true, "Arial"));
		return screenLayout;
	}

	private static ScreenLayout Blade()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Blade",
			Description = "Poster-style numbers with long black 3D shafts on coral",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#ff5a45",
			Color2 = "#ff5a45"
		};
		(string, string, string, string)[] array = new(string, string, string, string)[5]
		{
			("cpu.load", "%", "PROCESSOR", "LOAD"),
			("gpu.load", "%", "GRAPHICS", "LOAD"),
			("gpu.temp", "°", "GRAPHICS", "TEMPERATURE"),
			("gpu.power", "W", "GRAPHICS", "POWER"),
			("ram.load", "%", "MEMORY", "IN USE")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int num = 210 + i * 375;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Text = array[i].Item2,
				Effect = "extrude",
				Float = true,
				X = num - 185,
				Y = 196,
				W = 370,
				H = 170,
				Size = 176f,
				Font = "Bahnschrift",
				Bold = true,
				Align = "center",
				Color = "#ff6450",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 83 : 0),
				WarnColor = "#ffd23a"
			});
			screenLayout.Widgets.Add(T(array[i].Item3, num - 185, 392, 370, 22, 15f, "#161012", "center", bold: false, "Bahnschrift"));
			screenLayout.Widgets.Add(T(array[i].Item4, num - 185, 410, 370, 44, 34f, "#161012", "center", bold: true, "Bahnschrift Condensed"));
		}
		screenLayout.Widgets.Add(new Widget
		{
			Kind = WidgetKind.Panel,
			X = 1660,
			Y = 18,
			W = 238,
			H = 96,
			Color = "#ff5a45",
			Color2 = "#ff5a45",
			Track = "#161012",
			Thickness = 3f,
			Radius = 0f,
			Text = ""
		});
		screenLayout.Widgets.Add(T("{time:HH:mm}", 1660, 20, 238, 62, 50f, "#161012", "center", bold: true, "Bahnschrift"));
		screenLayout.Widgets.Add(T("{date:ddd d MMM}", 1660, 80, 238, 28, 15f, "#161012", "center", bold: false, "Bahnschrift"));
		List<Widget> widgets = screenLayout.Widgets;
		widgets[widgets.Count - 1].Text = "RATED · {date:ddd d MMM}";
		return screenLayout;
	}

	private static ScreenLayout Vinyl()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Vinyl",
			Description = "Quilted chrome-vinyl numbers with printed tags over a silk wave",
			BuiltIn = true,
			Background = "video",
			Image = Path.Combine(Store.Uploads, "silk-wave"),
			VideoFps = 24,
			Color1 = "#000000",
			Color2 = "#05070f"
		};
		(string, string, string)[] array = new(string, string, string)[4]
		{
			("cpu.load", "%", "C01\\nPROCESSOR\\nLOAD"),
			("gpu.load", "%", "G02\\nGRAPHICS\\nLOAD"),
			("gpu.temp", "°", "T03\\nGRAPHICS\\nTEMP"),
			("ram.load", "%", "M04\\nMEMORY\\nIN USE")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int num = 240 + i * 480;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Text = array[i].Item2,
				Effect = "vinyl",
				Float = true,
				Print = array[i].Item3,
				X = num - 225,
				Y = 40,
				W = 450,
				H = 360,
				Size = 250f,
				Font = "Segoe UI Black",
				Bold = true,
				Align = "center",
				Color = "#1a2140",
				Format = "0"
			});
		}
		screenLayout.Widgets.Add(T("{time:HH:mm}", 1700, 430, 190, 36, 20f, "#80ffffff", "right", bold: false));
		screenLayout.Widgets.Add(T("lumali.", 30, 430, 300, 36, 20f, "#99ffffff"));
		return screenLayout;
	}

	private static ScreenLayout Balloon()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Balloon",
			Description = "Inflated glossy numbers over a slow silk wave",
			BuiltIn = true,
			Background = "video",
			Image = Path.Combine(Store.Uploads, "silk-wave"),
			VideoFps = 24,
			Color1 = "#000000",
			Color2 = "#05070f"
		};
		(string, string, string, string)[] array = new(string, string, string, string)[4]
		{
			("cpu.load", "%", "CPU", "LOAD"),
			("gpu.load", "%", "GPU", "LOAD"),
			("gpu.temp", "°", "GPU", "TEMP"),
			("ram.load", "%", "MEMORY", "IN USE")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int num = 240 + i * 480;
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Value,
				Sensor = array[i].Item1,
				Text = array[i].Item2,
				Effect = "inflated",
				Float = true,
				X = num - 220,
				Y = 50,
				W = 440,
				H = 300,
				Size = 230f,
				Font = "Segoe UI Black",
				Bold = true,
				Align = "center",
				Color = "#ffb21a",
				Format = "0",
				WarnAt = ((array[i].Item1 == "gpu.temp") ? 80 : 0),
				WarnColor = "#ff5a3c"
			});
			screenLayout.Widgets.Add(new Widget
			{
				Kind = WidgetKind.Panel,
				X = num - 70,
				Y = 372,
				W = 140,
				H = 46,
				Color = "#00000000",
				Color2 = "#00000000",
				Track = "#b3ffffff",
				Thickness = 1.2f,
				Radius = 3f,
				Text = "",
				Size = 12f
			});
			screenLayout.Widgets.Add(T(array[i].Item3, num - 70, 375, 140, 20, 13f, "#e6ffffff", "center"));
			screenLayout.Widgets.Add(T(array[i].Item4, num - 70, 394, 140, 20, 11f, "#99ffffff", "center", bold: false));
		}
		screenLayout.Widgets.Add(T("{time:HH:mm}", 1700, 430, 190, 36, 20f, "#80ffffff", "right", bold: false));
		return screenLayout;
	}

	private static ScreenLayout Classic()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Classic",
			Description = "Clock and four tiles",
			BuiltIn = true,
			Background = "gradient",
			Color1 = "#0b1026",
			Color2 = "#16204a"
		};
		List<Widget> widgets = screenLayout.Widgets;
		widgets.Add(T("{time:HH:mm}", 40, 60, 520, 230, 200f, "#ffffff", "left", bold: false, "Segoe UI Light"));
		widgets.Add(T("{date:dddd d MMMM}", 50, 300, 520, 60, 34f, "#8fa3d9", "left", bold: false));
		(string, string, string)[] array = new(string, string, string)[4]
		{
			("cpu.load", "CPU", "#3fa9ff"),
			("gpu.load", "GPU", "#7cf0b0"),
			("ram.load", "MEMORY", "#b07cff"),
			("gpu.temp", "GPU TEMP", "#ffb02e")
		};
		for (int i = 0; i < 4; i++)
		{
			int num = 620 + i * 322;
			widgets.Add(Panel(num, 60, 300, 360, "", "#26ffffff", "#0dffffff"));
			widgets.Add(T(array[i].Item2, num + 24, 84, 260, 40, 24f, array[i].Item3));
			widgets.Add(V(array[i].Item1, num + 24, 150, 260, 140, 110f));
			widgets.Add(Bar(array[i].Item1, num + 24, 340, 252, 14, array[i].Item3, "#ffffff", (array[i].Item1 == "gpu.temp") ? 95 : 100));
		}
		return screenLayout;
	}

	private static ScreenLayout NeonGrid()
	{
		ScreenLayout obj = new ScreenLayout
		{
			Name = "Neon Grid",
			Description = "Synthwave sun and moving grid",
			BuiltIn = true,
			Background = "grid",
			Color1 = "#12002e",
			Color2 = "#3a0a5e",
			Accent = "#ff2bd6"
		};
		List<Widget> widgets = obj.Widgets;
		Widget widget = T("{time:HH:mm:ss}", 560, 150, 800, 150, 120f, "#ffffff", "center", bold: true, "Consolas");
		widget.Glow = true;
		widget.Color2 = "#ff2bd6";
		widgets.Add(widget);
		widgets.Add(T("CPU", 60, 60, 300, 40, 26f, "#ff2bd6"));
		widgets.Add(V("cpu.load", 60, 100, 400, 130, 110f));
		widgets[widgets.Count - 1].Glow = true;
		widgets[widgets.Count - 1].Color2 = "#ff2bd6";
		widgets.Add(T("GPU", 1560, 60, 300, 40, 26f, "#29d3ff", "right"));
		widgets.Add(V("gpu.load", 1460, 100, 400, 130, 110f, "#ffffff", "0", "right"));
		widgets[widgets.Count - 1].Glow = true;
		widgets[widgets.Count - 1].Color2 = "#29d3ff";
		widgets.Add(T("{gpu.temp:0}°C  ·  {gpu.power:0} W", 1360, 240, 500, 40, 26f, "#29d3ff", "right"));
		widgets.Add(T("{cpu.temp:0}°C  ·  RAM {ram.load:0}%", 60, 240, 500, 40, 26f, "#ff2bd6"));
		return obj;
	}

	private static ScreenLayout HeatWave()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Heat Wave",
			Description = "Big numbers on a warm moving gradient",
			BuiltIn = true,
			Background = "waves",
			Color1 = "#ff7a18",
			Color2 = "#af002d",
			Accent = "#ffd200"
		};
		List<Widget> widgets = screenLayout.Widgets;
		(string, string, string)[] array = new(string, string, string)[5]
		{
			("cpu.load", "CPU", "0"),
			("gpu.load", "GPU", "0"),
			("time", "TIME", "HH:mm"),
			("gpu.temp", "GPU TEMP", "0"),
			("ram.load", "MEMORY", "0")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int x = 30 + i * 376;
			widgets.Add(T(array[i].Item2, x, 90, 360, 40, 26f, "#fff3c4", "center"));
			Widget widget = V(array[i].Item1, x, 140, 360, 200, (array[i].Item1 == "time") ? 110 : 140, "#ffffff", array[i].Item3, "center");
			widget.Font = "Segoe UI Black";
			widgets.Add(widget);
		}
		return screenLayout;
	}

	private static ScreenLayout GlassTunnel()
	{
		ScreenLayout obj = new ScreenLayout
		{
			Name = "Glass Tunnel",
			Description = "A glowing clock flying through a tunnel",
			BuiltIn = true,
			Background = "tunnel",
			Color1 = "#02040c",
			Color2 = "#16306b",
			Accent = "#3df0ff"
		};
		List<Widget> widgets = obj.Widgets;
		Widget widget = T("{time:HH:mm}", 560, 110, 800, 260, 220f, "#bff8ff", "center", bold: false, "Segoe UI Light");
		widget.Glow = true;
		widget.Color2 = "#3df0ff";
		widgets.Add(widget);
		widgets.Add(T("CPU {cpu.load:0}%", 1540, 40, 340, 40, 26f, "#bff8ff", "right"));
		widgets.Add(T("GPU {gpu.load:0}%", 1540, 84, 340, 40, 26f, "#bff8ff", "right"));
		widgets.Add(T("{date:ddd d MMM}", 40, 40, 400, 40, 26f, "#bff8ff"));
		widgets.Add(T("{gpu.temp:0}°C", 40, 400, 400, 50, 32f, "#3df0ff"));
		widgets.Add(T("RAM {ram.load:0}%", 1480, 400, 400, 50, 32f, "#3df0ff", "right"));
		return obj;
	}

	private static ScreenLayout Carved()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Carved",
			Description = "Numbers cut into pale stone",
			BuiltIn = true,
			Background = "carved",
			Color1 = "#e9e6e0",
			Color2 = "#d5d1c9"
		};
		List<Widget> widgets = screenLayout.Widgets;
		(string, string, string, string)[] array = new(string, string, string, string)[5]
		{
			("time", "TIME", "HH:mm", ""),
			("cpu.load", "CPU", "0", "%"),
			("gpu.load", "GPU", "0", "%"),
			("gpu.temp", "GPU TEMP", "0", "°c"),
			("ram.load", "RAM", "0", "%")
		};
		for (int i = 0; i < array.Length; i++)
		{
			int x = 40 + i * 372;
			Widget widget = V(array[i].Item1, x, 110, 340, 230, (array[i].Item1 == "time") ? 120 : 170, "#3b3833", array[i].Item3, "center");
			widget.Font = "Georgia";
			widget.Text = array[i].Item4;
			widgets.Add(widget);
			widgets.Add(T(array[i].Item2, x, 360, 340, 40, 22f, "#7a756c", "center"));
		}
		return screenLayout;
	}

	private static ScreenLayout MinimalClock()
	{
		return new ScreenLayout
		{
			Name = "Minimal Clock",
			Description = "Just the time and date",
			BuiltIn = true,
			Background = "solid",
			Color1 = "#000000",
			Widgets = 
			{
				T("{time:HH:mm}", 0, 40, 1920, 320, 280f, "#ffffff", "center", bold: false, "Segoe UI Light"),
				T("{date:dddd d MMMM}", 0, 360, 1920, 70, 40f, "#888888", "center", bold: false)
			}
		};
	}

	private static ScreenLayout FocusGpu()
	{
		ScreenLayout screenLayout = new ScreenLayout
		{
			Name = "Focus GPU",
			Description = "Large graphics gauges for gaming",
			BuiltIn = true,
			Background = "gradient",
			Color1 = "#020a06",
			Color2 = "#0a2e1a",
			Accent = "#76ff03"
		};
		List<Widget> widgets = screenLayout.Widgets;
		(string, string, float)[] array = new(string, string, float)[4]
		{
			("gpu.load", "LOAD", 100f),
			("gpu.temp", "TEMP °C", 95f),
			("gpu.power", "POWER W", 575f),
			("cpu.load", "CPU", 100f)
		};
		for (int i = 0; i < array.Length; i++)
		{
			widgets.Add(new Widget
			{
				Kind = WidgetKind.Gauge,
				Sensor = array[i].Item1,
				X = 40 + i * 470,
				Y = 30,
				W = 420,
				H = 420,
				Thickness = 22f,
				Size = 96f,
				Max = array[i].Item3,
				Color = "#76ff03",
				Color2 = "#00c853",
				Text = array[i].Item2,
				Glow = true,
				Align = "center"
			});
		}
		return screenLayout;
	}
}
