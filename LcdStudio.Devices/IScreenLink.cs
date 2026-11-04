using LcdStudio.Native;

namespace LcdStudio.Devices;

/// <summary>What the screen loop needs from whichever panel is connected, whichever protocol it speaks.</summary>
public interface IScreenLink : IFanLink
{
	/// <summary>Turns the panel's own clock display on or off, if it has one. Returns the reply for testing; empty when unsupported.</summary>
	string Clock(bool on, byte style = 2);

	/// <summary>Lets go of the device without telling it to stop, so the last frame (or its own clock) stays up.</summary>
	void Leave();
}
