using System;
using System.Linq;

namespace LcdStudio.Devices;

public sealed class FanGroup
{
	public byte[] Mac = new byte[6];

	public byte Channel;

	public byte RxType;

	public byte DeviceType;

	public byte FanCount;

	public byte FanTypeByte;

	public int[] Rpm = new int[4];

	public byte[] Pwm = new byte[4];

	public byte[] EffectIndex = new byte[4];

	public byte[] Record = new byte[42];

	public byte Slot;

	public bool MotherboardSync => (Record[28] & 0x40) != 0;

	public byte CmdSeq => Record[40];

	public string Id => Convert.ToHexString(Mac);

	public string Model
	{
		get
		{
			byte fanTypeByte = FanTypeByte;
			if (fanTypeByte <= 42)
			{
				if (fanTypeByte > 26)
				{
					if (fanTypeByte <= 35)
					{
						if (fanTypeByte >= 32 || fanTypeByte == 27)
						{
							return "TL Wireless LCD";
						}
						return "TL Wireless";
					}
					if (fanTypeByte <= 39)
					{
						return "SL-INF Wireless";
					}
					return "CL Wireless";
				}
				if (fanTypeByte >= 20)
				{
					if (fanTypeByte <= 22)
					{
						return "SL Wireless (v3)";
					}
					return "SL Wireless LCD (v3)";
				}
			}
			else
			{
				if (fanTypeByte <= 58)
				{
					if (fanTypeByte <= 50)
					{
						if ((uint)(fanTypeByte - 43) <= 1u || (uint)(fanTypeByte - 47) <= 1u)
						{
							return "SL-INF Wireless LCD (v3)";
						}
						return "SL-INF Wireless (v3)";
					}
					if ((uint)(fanTypeByte - 51) <= 1u || (uint)(fanTypeByte - 55) <= 1u)
					{
						return "TL Wireless LCD (v3)";
					}
					return "TL Wireless (v3)";
				}
				if (fanTypeByte <= 62)
				{
					return "SL Wireless (v4)";
				}
				switch (fanTypeByte)
				{
				case 63:
					return "P28 (v2)";
				case 126:
				case 127:
					return "CL Wireless (v2)";
				}
			}
			return $"Lian Li wireless device (type {FanTypeByte})";
		}
	}

	public int LedsPerFan
	{
		get
		{
			switch (FanTypeByte)
			{
			case 27:
			case 28:
			case 29:
			case 30:
			case 31:
			case 32:
			case 33:
			case 34:
			case 35:
			case 51:
			case 52:
			case 53:
			case 54:
			case 55:
			case 56:
			case 57:
			case 58:
				return 26;
			case 20:
			case 21:
			case 22:
			case 23:
			case 24:
			case 25:
			case 26:
				return 40;
			case 36:
			case 37:
			case 38:
			case 39:
			case 43:
			case 44:
			case 45:
			case 46:
			case 47:
			case 48:
			case 49:
			case 50:
				return 44;
			case 59:
			case 60:
			case 61:
			case 62:
				return 52;
			case 40:
			case 41:
			case 42:
			case 126:
			case 127:
				return 24;
			case 63:
				return 9;
			default:
				return 0;
			}
		}
	}

	public bool RgbSupported => LedsPerFan > 0;

	public int Leds => FanCount * LedsPerFan;

	public int AverageRpm
	{
		get
		{
			if (FanCount != 0)
			{
				return (int)Rpm.Take(FanCount).Average();
			}
			return 0;
		}
	}

	public int DutyPercent
	{
		get
		{
			if (FanCount != 0)
			{
				return (int)Math.Round(Pwm.Take(FanCount).Average((byte b) => b) * 100.0 / 255.0);
			}
			return 0;
		}
	}

	public string RpmText => string.Join(" / ", Rpm.Take(FanCount));
}
