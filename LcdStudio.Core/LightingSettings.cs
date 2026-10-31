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


	/// <summary>
	/// The fan lights follow a reading: calm in the effect's own colours and pace below ReactLow, turning step by step
	/// toward the hot colours and a quicker pace as the reading climbs to ReactHigh.
	/// </summary>
	public bool React { get; set; }

	public string ReactSensor { get; set; } = "gpu.temp";

	public float ReactLow { get; set; } = 45f;

	public float ReactHigh { get; set; } = 80f;

	public string ReactHot1 { get; set; } = "#ffb21e";

	public string ReactHot2 { get; set; } = "#ff1f4b";

	/// <summary>
	/// Per wireless group (by its id), the controller's fan numbers in the order the fans sit, such as "2,1,3", so an
	/// effect that runs across the fans runs in their real order. Missing or empty is the controller's own order.
	/// </summary>
	public Dictionary<string, string> GroupOrders { get; set; } = new Dictionary<string, string>();

	public EffectSettings For(string effect)
	{
		if (!Effects.TryGetValue(effect, out EffectSettings value))
		{
			value = (Effects[effect] = Lighting.DefaultsFor(effect));
		}
		return value;
	}
}
