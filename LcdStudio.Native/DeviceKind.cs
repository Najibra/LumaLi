namespace LcdStudio.Native;

public enum DeviceKind
{
	Screen,
	ScreenLeds,
	FanLcd,
	WirelessTx,
	WirelessRx,
	/// <summary>A 400 × 400 fan screen that speaks the 8.8" screen's protocol: TL Flex LCD and SL Infinity Flex LCD.</summary>
	FlexLcd,
	/// <summary>A wired TL LCD fan (04FC:7393), 400 × 400, driven over HID.</summary>
	TlLcd,
	/// <summary>
	/// The Lancool 207 Digital's front screen (1CBE:A065), 720 × 1472, speaking the 8.8" screen's protocol. Used as the
	/// screen when no 8.8" screen is connected.
	/// </summary>
	CaseLcd
}
