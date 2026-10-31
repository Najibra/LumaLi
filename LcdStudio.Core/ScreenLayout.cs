using System.Collections.Generic;
using System.Linq;

namespace LcdStudio.Core;

public sealed class ScreenLayout
{
	public string Name { get; set; } = "My layout";


	public string Description { get; set; } = "";


	public string Background { get; set; } = "gradient";


	public string Color1 { get; set; } = "#0a0f2c";


	public string Color2 { get; set; } = "#1b2a6b";


	public string Accent { get; set; } = "#3fa9ff";


	public string? Image { get; set; }

	public float ImageOpacity { get; set; } = 1f;


	public int VideoFps { get; set; } = 24;


	public bool BuiltIn { get; set; }

	/// <summary>Drawn upright for a screen stood on its end: 480 wide and 1920 tall.</summary>
	public bool Portrait { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public int Width => Portrait ? 480 : 1920;

	[System.Text.Json.Serialization.JsonIgnore]
	public int Height => Portrait ? 1920 : 480;

	public List<Widget> Widgets { get; set; } = new List<Widget>();


	public ScreenLayout Clone()
	{
		ScreenLayout obj = (ScreenLayout)MemberwiseClone();
		obj.Widgets = Widgets.Select((Widget w) => w.Clone()).ToList();
		return obj;
	}
}
