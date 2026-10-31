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


	public bool StartWithWindows { get; set; }

	public bool CloseToTray { get; set; } = true;


	public bool StartMinimized { get; set; }

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
