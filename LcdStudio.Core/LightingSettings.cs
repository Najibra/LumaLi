using System.Collections.Generic;

namespace LcdStudio.Core;

public sealed class LightingSettings
{
	public bool Applied { get; set; }

	public bool FansOn { get; set; } = true;


	public string FanEffect { get; set; } = "Rainbow spin";


	public Dictionary<string, EffectSettings> Effects { get; set; } = new Dictionary<string, EffectSettings>();


	public bool StripOn { get; set; } = true;


	public bool StripFollowsFans { get; set; } = true;


	public string StripEffect { get; set; } = "Rainbow";


	/// <summary>The strip takes its colours and movement from the screen layout itself; overrides the effect and following the fans.</summary>
	public bool StripMatchesScreen { get; set; }


	/// <summary>Runs the matched colours the other way along the strip, for strips wired from the right.</summary>
	public bool StripMatchReversed { get; set; }


	public EffectSettings Strip { get; set; } = new EffectSettings
	{
		Speed = 32,
		Brightness = 70
	};


	public EffectSettings For(string effect)
	{
		if (!Effects.TryGetValue(effect, out EffectSettings value))
		{
			value = (Effects[effect] = Lighting.DefaultsFor(effect));
		}
		return value;
	}
}
