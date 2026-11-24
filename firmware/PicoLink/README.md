# PicoLink: Wren's Pico programmer sketch

`PicoLink.ino` turns a Raspberry Pi Pico, or a Pico-pinout RP2040 board such as the
Waveshare RP2040 Plus, into Wren's SWIM programmer.

- **SWIM:** Richard Hulme's pico-swimlib does all of it (`swimlib.c`, `swim.h`,
  `swim.pio.h`; BSD 3-Clause, `LICENSE-pico-swimlib.txt`): its PIO program times the bits and
  DMA, under its interrupt, chains each frame to the next. `swimlib.c` is pico-swimlib's
  `swim.c` with its interrupt defaults set in the file (its header says which); `swim.pio.h`
  is its `swim.pio` assembled. The sketch adds Wren's serial protocol and NRST.
- **Wiring:** the same as for pico-swimlib's own firmware: SWIM on GP16 with a 470 Ω
  pull-up, NRST on GP17.
- **Protocol:** Wren talks to it with its own protocol (`src/Wren.Link/PicoLink.cs`).

## Putting it on the board

Either way gives the same firmware.

**Drag and drop:**
1. Hold **BOOTSEL** while plugging the board into USB. A drive called **RPI-RP2** appears.
2. Drag `PicoLink.uf2` onto it. This is the sketch built with the Pico SDK.

**Arduino IDE:** open `PicoLink.ino` with Earle Philhower's "Raspberry Pi
Pico/RP2040/RP2350" core installed, choose the board, and upload.

## How PicoLink.uf2 was built

`sdk/` builds the unchanged sketch and `swimlib.c` with Pico SDK 2.1.0 (`PICO_BOARD=pico`).
The Arduino IDE needs the Philhower core version 4 or later (Pico SDK 2), as `swim.pio.h`
does.
`arduino_shim.h` stands in for the Arduino serial port with the SDK's USB serial.
