using System.Collections.Generic;

namespace LcdStudio.Core;

public sealed class CoolingSettings
{
	public string Mode { get; set; } = "Hold";


	public int CustomPercent { get; set; } = 50;


	public string AutoSensor { get; set; } = "gpu.temp";


	public int MinPercent { get; set; } = 25;


	public int StartAt { get; set; } = 40;


	public int FullAt { get; set; } = 85;


	public int Response { get; set; } = 8;


	public int ExitPercent { get; set; } = 50;


	public Dictionary<string, string> GroupNames { get; set; } = new Dictionary<string, string>();


	public Dictionary<string, int> GroupTrim { get; set; } = new Dictionary<string, int>();


	public Dictionary<string, int[]> Calibration { get; set; } = new Dictionary<string, int[]>();


	public static int PresetPercent(string mode, int custom)
	{
		return mode switch
		{
			"Silent" => 30, 
			"Balanced" => 45, 
			"Performance" => 75, 
			"Full" => 100, 
			"Custom" => custom, 
			_ => -1, 
		};
	}
}
