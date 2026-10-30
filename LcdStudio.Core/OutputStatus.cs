using System.Drawing;
using LcdStudio.Native;

namespace LcdStudio.Core;

public sealed class OutputStatus
{
	public string Name = "";

	public string Serial = "";

	public DeviceKind Kind;

	public bool Present;

	public bool Running;

	public string State = "not found";

	public double Fps;

	public string? Firmware;

	public Bitmap? Preview;
}
