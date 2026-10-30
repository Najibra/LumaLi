namespace LcdStudio.Core;

/// <summary>When the 8.8" screen switches to its standby face, and how it looks there.</summary>
public sealed class StandbySettings
{
	public bool Enabled { get; set; } = true;


	/// <summary>Minutes without mouse or keyboard input before standby; 0 never goes to standby for idleness.</summary>
	public int IdleMinutes { get; set; } = 10;


	public bool WhenLocked { get; set; } = true;


	/// <summary>Leave the standby face on the screen as the PC goes to sleep, hibernates or shuts down.</summary>
	public bool BeforeSleep { get; set; } = true;


	/// <summary>Also turn on the screen's own clock as the PC goes down, if its firmware shows one.</summary>
	public bool FirmwareClock { get; set; }


	public string Layout { get; set; } = "Standby";


	/// <summary>An image of the user's own for the logo spot; null shows the LumaLi mark.</summary>
	public string? Logo { get; set; }


	public int Brightness { get; set; } = 45;
}
