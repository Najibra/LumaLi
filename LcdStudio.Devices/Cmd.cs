namespace LcdStudio.Devices;

public static class Cmd
{
	public const byte GetVer = 10;

	public const byte Rotate = 13;

	public const byte Brightness = 14;

	public const byte FrameRate = 15;

	public const byte GetH264Block = 17;

	public const byte SetClock = 51;

	public const byte StopClock = 52;

	public const byte PushJpg = 101;

	public const byte PushPng = 102;

	public const byte ClearPng = 103;

	public const byte HideShowFrames = 104;

	public const byte QueryBlock = 122;

	public const byte StopPlay = 123;
}
