namespace LcdStudio.Core;

/// <summary>While the game's program runs, the 8.8" screen shows this layout instead of the chosen one.</summary>
public sealed class GameProfile
{
	/// <summary>The game's program name without ".exe", as Windows lists it (for example "Carpenter").</summary>
	public string Game { get; set; } = "";

	/// <summary>A name to show for it, such as its window title.</summary>
	public string Title { get; set; } = "";

	public string Layout { get; set; } = "";
}
