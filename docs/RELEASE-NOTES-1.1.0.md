# LumaLi 1.1.0

## New
- **Lottie animations** (`.json` and `.lottie`) wherever GIFs work. On the screen, an image can **follow a reading**, so the animation scrubs with your load instead of looping.
- **Lottie Gauge fan face** with four built-in styles in the fan's own colours (Arc, Needle, Liquid, LED ring), or your own Lottie.
- **Gauge warning:** set a "Warn at" level and colour per fan. Above it the gauge eases into the warning colour and breathes, with a tick marking the level.
- **Reactive fan lighting:** the fans can follow a reading (GPU temperature by default). Between "calm below" and "hot at" they warm from your colours toward amber and red in four steps. Steps are spaced at least 20 seconds apart to spare the wireless controller.
- **Aurora and Comet trail** effects for the fans and the light strip.
- **Standby screen:** logo, large clock and date while the PC is idle, locked or going to sleep.

## Better
- New drawing engine (SkiaSharp) for soft glow and blur. Glass Tunnel, Neon, Warp, Dot Matrix and Molten Glass are redrawn with real glow and smoother motion.
- The 8.8" screen now runs at up to 30 fps (was 21).
- Longer, smoother fan lighting loops (up to 52 frames, as much as the fans' memory holds).

## Fixed
- Light strip timing.
- A "°C" glitch in the Void layout.

Needs the .NET 8 Desktop Runtime (x64). Close L-Connect before running LumaLi.
