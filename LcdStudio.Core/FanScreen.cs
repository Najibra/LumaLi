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

	public void CopyLookFrom(FanScreen o)
	{
		Face = o.Face;
		Color1 = o.Color1;
		Color2 = o.Color2;
		Background = o.Background;
	}
}
