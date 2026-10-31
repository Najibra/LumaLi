namespace LcdStudio.Core;

public sealed class FanScreen
{
	public string Serial { get; set; } = "";


	public bool Enabled { get; set; } = true;


	public string Face { get; set; } = "Glass Slab";


	public string Sensor { get; set; } = "cpu.load";


	public string Label { get; set; } = "";


	public string Color1 { get; set; } = "#0d0d0f";


	public string Color2 { get; set; } = "#ececea";


	public int Rotation { get; set; }

	public string? Background { get; set; }

	/// <summary>The gauge the Lottie Gauge face draws itself when no Lottie is uploaded: one of Faces.GaugeStyles.</summary>
	public string GaugeStyle { get; set; } = "Arc";

	/// <summary>The reading, in the sensor's own units, past which the gauge turns to WarnColor; 0 leaves it off.</summary>
	public float WarnAt { get; set; }

	public string WarnColor { get; set; } = "#ff5a3c";

	/// <summary>Copies the look, but not WarnAt, which belongs with the fan's own sensor.</summary>
	public void CopyLookFrom(FanScreen o)
	{
		Face = o.Face;
		GaugeStyle = o.GaugeStyle;
		WarnColor = o.WarnColor;
		Color1 = o.Color1;
		Color2 = o.Color2;
		Background = o.Background;
	}
}
