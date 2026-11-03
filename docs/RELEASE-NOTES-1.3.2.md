# LumaLi 1.3.2

Fixes for two reports from the Reddit thread, plus six new screen layouts and four fan faces. For earlier changes, see
[RELEASE-NOTES-1.3.1.md](RELEASE-NOTES-1.3.1.md).

## Fixed

- **CPU temperature on Intel.** LumaLi's sensor library reads the CPU temperature through the free
  [PawnIO](https://pawnio.eu) driver on every processor, Intel and AMD alike. The earlier notes said only Ryzen 9000
  needed it. LumaLi now checks whether PawnIO is installed, and Home and Settings tell you exactly what is missing.
  Fix: `winget install namazso.PawnIO` (or the installer from pawnio.eu), restart the PC, then run LumaLi as
  administrator.
- **Fan screens not found.**
  - LumaLi now also looks for the UNI FAN SL Wireless LCD (SL V3, USB `1CBE:0005`) fan screens. They use the same
    protocol as the TL ones in lian-li-linux. This is experimental and untested here, so reports are welcome.
  - "No fan screens found" now explains that each LCD fan's screen works only over that fan's own USB cable, plugged
    into a motherboard USB 2.0 header or hub. The wireless dongle carries lighting and speed, not pictures.
- **Device report** now shows whether PawnIO is installed and lists every CPU temperature sensor with its value.

## New layouts

- **Machined:** matte orange metal under raised grey plates. The time and date are in chamfered figures, the readings
  are engraved in the metal, discs slide with the CPU and GPU load, and blue light glows along the plate edges, with
  pulses flowing faster under load.
- **Retro Rack:** a wall of orange devices with a technical screen, knobs, tonight's moon in a porthole, temperature
  dials, a speaker and a fan that turns with your fans.
- **Clearance:** a yellow road-signage poster with huge chamfered figures, hazard stripes, a history chart and trucks
  that drive faster with network traffic.
- **Jellyfish:** a glossy deep-sea jellyfish that pulses with the CPU, glows with the temperature and swims playfully.
  Field-guide labels point to what each reading drives.
- **Station Clock** and **Sport Dial:** a railway-station clock with five gauge dials, and a black-and-cream sports
  watch with loads and temperatures. The startup screen now shows a small station clock too.

## New fan faces

- **Station Clock** and **Sport Dial**
- **Rack Porthole:** the moon, turning, with a ring lit up to the reading.
- **Rack Meter:** a VU meter with a sprung needle.

Not affiliated with or endorsed by Lian Li.
