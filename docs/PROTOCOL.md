# Protocol notes

The protocol work comes from sgtaziz/lian-li-linux (MIT); tinyuz is by sisong (MIT).

- **8.8" screen (1CBE:A088):**
  - Each command is a 512-byte DES-CBC header (key and IV `slv3tuzx`, 500 → 504 bytes, trailer `A1 1A`), followed by a JPEG of 480×1920. Landscape content is rotated 90° clockwise.
  - Init sequence: StopPlay 7B, GetVer 0A, FrameRate 0F=120, GetH264Block 11, SetClock 33, StopClock 34, then a transparent PNG (66) and a black JPEG (65).
  - The reply's byte 8 is how many frames are queued. Above 3, poll QueryBlock 7A every 50 ms until it is ≤ 2.
  - **Read exactly one packet of the endpoint's size, and skip zero-length packets.** On the screen that is 512 bytes: a shorter read overflows the IN pipe and the screen stops accepting data until its power is cut, which is what stalled the earlier build. Its 512-byte replies are followed by a zero-length packet; if that is not skipped, every later reply is read one command late.
  - Verified 2026-10-23: 3 minutes of continuous Command Center streaming at about 16 fps, with no errors, alongside all five fan LCDs. Firmware `lianli88_0001_0023`.
- **Fan LCDs (1CBE:0006):** use the legacy 504-byte DES header with no trailer and a fixed 102,400-byte transfer of a 400×400 JPEG. Brightness follows the curve 0/25/50/75/100 → 0/10/30/40/100.
- **Light strip (0416:8050):** 60 LEDs, sent as three 64-byte packets `11 start 00 00 RGB×20`.
- **Wireless dongle (TX 0416:8040, RX 0416:8041):**
  - The dongle is a full-speed device: read 64 bytes at a time. A 512-byte read waits for a short packet that never comes and loses the reply.
  - RX `10 03` returns 42-byte group records: channel [12], rx [13], fan count [19], type [24..27], RPM [28..35], PWM [36..39].
  - RF frames are 240 bytes, sent as four 64-byte chunks `10 idx channel rx`.
  - PWM frame: `12 10 mac master rx channel slot pwm×4`. Duty has a floor of 35/255 unless it is 0.
  - RGB loops: `12 20 …`, tinyuz-literal. Send the header 4 times, 20 ms apart. **The raw loop (frames × LEDs × 3) must be at most 8 192 bytes**: measured on TL fans, 8 190 bytes applied and 8 424 bytes were silently refused. The compressed size can be up to 12 KB.
  - A group confirms an upload by reporting the new effect id (record bytes 20–23). LumaLi checks for that and resends up to 3 times.

LumaLi is not affiliated with Lian Li.
