# LumaLi 1.2.0

This update is built from your feedback on r/lianli. Thank you to everyone who commented.

## New

- **Vertical layouts.** Stand the 8.8" screen on its end: a layout can now be vertical (480 × 1920), with an upright
  editor and four new templates: **Silk Tower**, **Neon Column**, **Aurora Rings** and **Tall Clock**. If yours shows
  upside down, turn on *Flip 180°* in Settings.
- **Silk background.** Fine strands of light flowing along the screen, with soft orbs behind them, moving a little
  faster as your CPU works. Works on wide and vertical layouts.
- **Per-game layouts.** Settings → Game profiles: pick a running game (or its .exe) and a layout. While the game runs
  the screen shows that layout, and it switches back when you quit.
- **Game frame rate.** A `game.fps` reading for screen layouts and fan faces (run LumaLi as administrator).
- **Fan order.** If effects run across your fans in the wrong order, the Lighting page's *Fan order* card lets you
  press **Identify** (each fan lights its own colour) and type the order they sit in. Fan screens can be renumbered
  with the *Position* setting on each fan card.
- **Videos and playlists on fan screens.** Upload MP4 and other video files as a fan background, and give a fan a
  playlist of uploads that takes turns, in order or shuffled (never the same one twice in a row).
- **Copy device report** (Settings → Devices) for bug reports and hardware requests, and GitHub issue templates.

## Experimental hardware

Written from the protocol work in [sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) and **not yet
tested on real hardware**. If you own one, please try it and open an issue with your device report.

- **TL Flex LCD** (`1CBE:A018`) and **SL Infinity Flex LCD** (`1CBE:A019`) fan screens.
- **Wired TL LCD** fans (`04FC:7393`).

## Fixed

- Fan playlists, the gauge style and the *Warn at* level now reach the fans themselves (only the previews showed them).
- Glowing numbers: the "4" no longer has square notches, and the glow fades smoothly around very large numerals.

## Coming later

- Turzx screens (planned for 1.3).
