# LumaLi 1.4.0

Music, weather and a screen that takes turns with your favourite layouts. For earlier changes, see
[RELEASE-NOTES-1.3.2.md](RELEASE-NOTES-1.3.2.md).

## Now Playing

- **Now Playing layout:** the song playing in Spotify, a browser tab or any media player. Its cover glows on a blurred,
  slowly drifting wash of itself. Alongside are the title (it scrolls when long), artist and album, an equaliser that
  dances while music plays and settles when it's paused, and the progress bar with time gone and time left. The clock,
  the date, the weather and five readings (CPU and GPU load and temperature, memory) sit on the right. The colours
  follow the cover.
- **Now Playing fan face:** the cover in a circle, with a progress ring round the edge in the cover's colours, plus
  the title, artist and time.
- **New readings for any layout:** `{media.title}`, `{media.artist}`, `{media.album}`, `{media.app}`,
  `{media.state}`, `{media.elapsed}`, `{media.length}`, and **Song progress** (`media.progress`).
- LumaLi reads what Windows shows in its volume flyout. It never controls playback, and it needs no extra install.

## Weather

- **Settings › Weather:** type your city and press Find. The weather comes from [Open-Meteo](https://open-meteo.com)
  (free, no account) every 15 minutes, and only the place's position is sent. You can show °F instead of °C.
- **Weather fan face:** the sky changes with the weather and time of day. The icon is animated: a sun with turning
  rays, the moon and twinkling stars, drifting clouds, rain, snow, a storm whose lightning lights the sky, or fog. The
  temperature is in white, with the sky in words and the day's high and low.
- **New readings:** Outdoor temperature, humidity and wind speed, plus `{weather.degrees}`, `{weather.text}`,
  `{weather.place}`, `{weather.high}` and `{weather.low}`.

## Layout rotation and crossfades

- **Settings › Layout rotation:** list two or more layouts, and the screen shows each in turn for the minutes you set.
  A game profile and auto mode still come first.
- **Every change of layout on the screen now crossfades.** This covers rotation, game profiles, auto mode and picking
  a new layout.

## Telemetry

- **Telemetry layout:** a sage, orange and charcoal mission dashboard. It has eight live bars, a scrolling 60-second
  CPU/GPU history, temperature cards with hazard stripes, and network, fan and frame-rate stats.
- **Telemetry** and **Telemetry Dark** fan faces: the reading in big rolling figures, with a 40-second bar history and
  a peak line.

## Two (or more) 8.8" screens

- **Every 8.8" screen connected now runs at once.** Each extra screen gets a row on Home under **More 8.8" screens**,
  with its own preview, its own layout (or "Same as screen 1") and its own **Upside down** switch. **Make it screen 1**
  picks which one follows game profiles, auto mode and rotation. **Identify screens** shows a big number on each.
- Each screen's light strip runs too; they all match screen 1's colours.

## Fixed

- **"Screen light strip is in use by another program" with L-Connect closed** ([#1](https://github.com/Najibra/LumaLi/issues/1)):
  only one program can drive a USB device at a time, and the message always blamed L-Connect. LumaLi now says what
  Windows actually reported, and when another program holds the device it names that program (for example an RGB
  app), so you know what to close. The device report also lists other lighting and monitoring software that's running.

## Also new

- **Update notice:** LumaLi checks GitHub once a day and shows a note on Home when a newer version is out, with a
  button to its download page. Nothing is downloaded by the app. You can turn this off in Settings › App.
- **Warning colour on Neon Ring and Reactor:** like the gauge face, these faces now turn to your warning colour past
  the fan's **Warn at** level, and breathe while it lasts.

## Removed

- The short-lived Mixdown layout.

Not affiliated with or endorsed by Lian Li.
