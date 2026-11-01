namespace LcdStudio.Core;

public sealed class Widget
{
	public WidgetKind Kind { get; set; }

	public int X { get; set; }

	public int Y { get; set; }

	public int W { get; set; } = 200;


	public int H { get; set; } = 80;


	public string Text { get; set; } = "";


	public string Sensor { get; set; } = "cpu.load";


	public float Min { get; set; }

	public float Max { get; set; } = 100f;


	public string Format { get; set; } = "0";


	public string Color { get; set; } = "#ffffff";


	public string Color2 { get; set; } = "#3fa9ff";


	public string Track { get; set; } = "#22ffffff";


	public string Font { get; set; } = "Segoe UI";


	public float Size { get; set; } = 32f;


	public bool Bold { get; set; } = true;


	public string Align { get; set; } = "left";


	public float Thickness { get; set; } = 10f;


	public float Radius { get; set; } = 18f;


	public float WarnAt { get; set; }

	public string WarnColor { get; set; } = "#ff4d4d";


	public string? Image { get; set; }

	public bool Visible { get; set; } = true;


	public bool Glow { get; set; }

	public bool HideWhenMissing { get; set; }

	public string Effect { get; set; } = "";


	public string Print { get; set; } = "";


	public bool Float { get; set; }

	/// <summary>An image widget's Lottie plays as a gauge of <see cref="Sensor"/> between Min and Max instead of looping.</summary>
	public bool FollowsReading { get; set; }

	/// <summary>Draws the widget at its size divided by this, then scales it down (or up) to its box. Lets a tile laid
	/// out in fixed pixels for the wide screen fit a narrower box intact. Unset means 1.</summary>
	public float? Zoom { get; set; }

	public Widget Clone()
	{
		return (Widget)MemberwiseClone();
	}
}
