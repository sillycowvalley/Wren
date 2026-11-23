# Wren project plan

**Status: draft for Michael's approval. No code until it is approved.**

Wren is a C# / .NET 8 development environment for STM8S003F3-class microcontrollers.
It is driven through an ST-LINK V2 over SWIM. The first target is the Digicool
"8-bit OLED Thing" (STM8S003F3U, 64x32 SSD1315 OLED over I2C on PB4/PB5). Licence: MIT.

## Strategy: protect the Things' flash

A Thing's flash is rated for only ~100 writes, so firmware reaches it last:

1. **Emulator.** Both the MCU and the OLED are emulated in Wren. Rewrites are unlimited.
2. **Development board.** A roomier STM8 board with a real 0.49" 64x32 OLED module wired
   to it. The firmware keeps to the S003's limits: 8 KB flash, 1 KB RAM, 128 B EEPROM.
3. **The OLED Thing.** Each Thing is flashed once with the finished firmware. After that,
   changes go to its EEPROM only.

## Hardware (ordered)

- **Development board:** STM8S103F3P6 "minimum system" boards. They have the same core,
  peripherals and pins as the S003, including I2C on PB4/PB5, so the same binary runs on
  both. 10 KB flash rated ~10k writes. The board's test LED is usually on PB5 (SDA);
  remove it if I2C misbehaves. On arrival, confirm each chip is a real S103: in STVP with
  the device set to STM8S103F3, write flash above 8 KB and read it back.
- **OLED:** 0.49" 64x32 I2C modules, the same as the Thing's.
- **Programmer:** ST-LINK V2 clones. They supply 3.3 V or 5 V to the target. Check the
  case's pin labels against the real wiring before first use.

## Parts

| Project | What it is |
|---|---|
| **Wren** | The Avalonia app (Windows, Mac, Linux). |
| **Wren.Link** | USB and SWIM to the ST-LINK V2. Reads and writes flash, EEPROM and option bytes, so Wren replaces STVP. |
| **Wren.Asm** | An STM8 assembler. |
| **Wren.Emu** | An STM8 emulator plus an SSD1315 OLED model that the app renders. |
| **Wren.Script** | The token language and its compiler, which builds a 128-byte EEPROM image. |

Each library has its own test project (`Wren.Link.Tests` and so on) and stays UI-free.

## Build order and what each step proves

1. **Wren.Link.** It proves we can program a Thing from C# without STVP, and write the
   EEPROM (rated ~100k cycles) without touching the flash (rated ~100). We write our own
   code, using the open-source stm8flash and OpenOCD to learn the protocol. Michael tests
   it on Windows against an S103 board first, then a Thing.
2. **Wren.Asm and Wren.Emu together, with the OLED view.** They prove firmware can be
   written and run with unlimited rewrites. Tests compare frame-buffer images. Assembler
   output is checked byte for byte against known encodings.
3. **The firmware: the graphics library and the interpreter.** It proves the design fits
   in 8 KB flash and 1 KB RAM. It is developed in the emulator, then run on the
   development board with a real OLED, then flashed once to each Thing.
4. **Wren.Script.** It proves a useful program fits in 128 bytes of EEPROM and can be
   changed without reflashing.
5. **Later and optional:** a SWIM on-chip debugger (breakpoints through the debug
   module, as OpenOCD's STM8 support does).

## The firmware in outline (to be settled at step 3)

- **Flash, written once:** an I2C/SSD1315 driver with a 256-byte frame buffer. Fonts:
  5x7 (10x4 text), 3x5 (16x5 text) and large 16x32 digits. Drawing primitives. Number
  formats: hex, decimal, binary, ASCII, bar and a history graph. Input reads. The token
  interpreter.
- **EEPROM:** settings and token programs.
- **Phase 1:** built-in views chosen from a 3-button menu (Up, Down, Select on PA1-PA3).
- **Phase 2:** a host such as the Reggie drives the Thing as a tiny display terminal,
  sending command bytes on b0-b7 strobed by PD3.
- **Held in reserve:** an external 24LC256 on the OLED's I2C pins, and the unguaranteed
  640 B EEPROM of the S103 die.

## Open questions

1. **The USB driver (first).** Can Wren open the ST-LINK through ST's installed Windows
   driver, via WinUSB P/Invoke or LibUsbDotNet? Or does it need a WinUSB driver
   installed with Zadig, which would then stop STVP working? Mac and Linux would use
   libusb. To be answered with a small spike on Michael's machine.
2. **ST-LINK clones.** Do the clones Michael owns behave like a genuine V2 (firmware
   version, SWIM speed)?
3. **Extra EEPROM.** Is the 640 B EEPROM real on Michael's Things? A one-Thing test in
   STVP: set the device to STM8S103F3, then write, power-cycle and read back
   0x4080-0x427F.
4. **Emulator scope.** Should it model only the peripherals the Thing uses (GPIO, I2C,
   TIM4, EXTI, EEPROM) and leave the rest out?
5. **Assembler syntax.** ST's assembler syntax, the SDCC/sdas syntax, or Wren's own?
