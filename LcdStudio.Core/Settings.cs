using System;
using System.Collections.Generic;
using System.Linq;

namespace LcdStudio.Core;

public sealed class Settings
{
	public int Version { get; set; } = 3;


	public bool ScreenOn { get; set; } = true;


	public string Layout { get; set; } = "Command Center";


	public List<string> HiddenTemplates { get; set; } = new List<string>();


	public int ScreenBrightness { get; set; } = 87;


	public string Orientation { get; set; } = "landscape";


	public bool Flip { get; set; }

	// the 8.8" screen takes about 32 frames a second, so 30 is the smoothest it can show with a little headroom
	public int ScreenFps { get; set; } = 30;


	public int ScreenQuality { get; set; } = 90;


	public bool FanScreensOn { get; set; } = true;


	public int FanBrightness { get; set; } = 80;


	public int FanFps { get; set; } = 12;


	public List<FanScreen> Fans { get; set; } = new List<FanScreen>();

	/// <summary>Layouts the screen switches to while a chosen game runs, and back when it closes.</summary>
	public List<GameProfile> GameProfiles { get; set; } = new List<GameProfile>();

	/// <summary>Fan screens' serials in the order the owner set (Fan 1 first); fans not listed follow in serial order.</summary>
	public List<string> FanOrder { get; set; } = new List<string>();


	public LightingSettings Lighting { get; set; } = new LightingSettings();


	public CoolingSettings Cooling { get; set; } = new CoolingSettings();


	public StandbySettings Standby { get; set; } = new StandbySettings();

	/// <summary>Auto mode, colours from the monitor and the health watch.</summary>
	public SmartSettings Smart { get; set; } = new SmartSettings();


	public bool StartWithWindows { get; set; }

	public bool CloseToTray { get; set; } = true;


	public bool StartMinimized { get; set; }

	/// <summary>The Home page's note on how to get the CPU temperature was hidden.</summary>
	public bool HideCpuTempHint { get; set; }

	/// <summary>The place whose weather the layouts and fan faces show.</summary>
	public WeatherSettings Weather { get; set; } = new WeatherSettings();

	/// <summary>Layouts the screen takes turns with, crossfading.</summary>
	public RotationSettings Rotation { get; set; } = new RotationSettings();

	/// <summary>
	/// More than one 8.8" screen: the main screen (by serial) shows the chosen layout with game profiles, auto mode and
	/// rotation; each other screen shows its own layout here (or the same as the main one) and has its own flip.
	/// </summary>
	public string MainScreen { get; set; } = "";

	public List<ScreenSettings> Screens { get; set; } = new List<ScreenSettings>();

	public ScreenSettings ScreenFor(string serial)
	{
		ScreenSettings? s = Screens.FirstOrDefault((ScreenSettings x) => x.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
		if (s == null)
		{
			s = new ScreenSettings { Serial = serial, Flip = Flip };
			Screens.Add(s);
		}
		return s;
	}

	/// <summary>Look for a newer LumaLi on GitHub once a day.</summary>
	public bool CheckUpdates { get; set; } = true;

	/// <summary>The update note for this version was hidden ("1.4.0").</summary>
	public string HiddenUpdate { get; set; } = "";

	public FanScreen FanFor(string serial)
	{
		string serial2 = serial;
		FanScreen fanScreen = Fans.FirstOrDefault((FanScreen x) => x.Serial.Equals(serial2, StringComparison.OrdinalIgnoreCase));
		if (fanScreen != null)
		{
			return fanScreen;
		}
		string[] array = new string[5] { "cpu.load", "gpu.load", "gpu.power", "gpu.temp", "ram.load" };
		fanScreen = new FanScreen
		{
			Serial = serial2,
			Sensor = array[Fans.Count % array.Length]
		};
		Fans.Add(fanScreen);
		return fanScreen;
	}
}

/// <summary>An extra 8.8" screen's own settings.</summary>
public sealed class ScreenSettings
{
	public string Serial { get; set; } = "";

	/// <summary>The layout it shows; empty = the same as the main screen.</summary>
	public string Layout { get; set; } = "";

	/// <summary>Turned 180° (mounted the other way up).</summary>
	public bool Flip { get; set; }
}

/// <summary>Layout rotation: the screen shows each listed layout in turn for a few minutes.</summary>
public sealed class RotationSettings
{
	public bool Enabled { get; set; }

	public int Minutes { get; set; } = 5;

	public List<string> Layouts { get; set; } = new List<string>();

	/// <summary>Which listed layout is up now: by the clock, so every part of the app agrees without sharing a timer.</summary>
	public string? Now(DateTime now, Func<string, bool> exists)
	{
		List<string> names = Layouts.Where(exists).ToList();
		if (!Enabled || names.Count < 2)
		{
			return null;
		}
		long slot = (long)Math.Floor((now - new DateTime(2000, 1, 1)).TotalMinutes / Math.Max(1, Minutes));
		return names[(int)(slot % names.Count)];
	}
}
