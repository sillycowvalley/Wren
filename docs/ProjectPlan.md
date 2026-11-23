# Wren project plan

**Status: approved. Step 1 (assembler, emulator and app) is built and tested; the hardware is on its way.**

Wren is a C# / .NET 8 development environment for STM8S003F3-class microcontrollers.
It is driven through an ST-LINK V2 over SWIM. The first target is the Digicool
"8-bit OLED Thing" (STM8S003F3U, 64x32 SSD1315 OLED over I2C on PB4/PB5). Licence: MIT.
The Thing's pins are in [OledThing.md](OledThing.md).

## Strategy: protect the Things' flash

A Thing's flash is rated for only ~100 writes, so firmware reaches it last:

1. **Emulator.** Both the MCU and the OLED are emulated in Wren. Rewrites are unlimited.
2. **Development board.** An STM8S103F3 board with a real 0.49" 64x32 OLED module wired
   to it. The firmware keeps to the S003's limits: 8 KB flash, 1 KB RAM, 128 B EEPROM.
3. **The OLED Thing.** Each Thing is flashed once with the finished firmware. After that,
   changes go to its EEPROM only.

## Hardware (ordered)

- **Development board:** STM8S103F3P6 "minimum system" boards. They have the same core,
  peripherals and pins as the S003, including I2C on PB4/PB5, so the same binary runs on
  both. 8 KB flash like the S003, but rated ~10k writes instead of ~100, and 640 B
  EEPROM. The board's test LED is usually on PB5 (SDA); remove it if I2C misbehaves.
  Cheap boards sometimes carry a relabelled S003; it would still work, with an
  unguaranteed flash rating.
- **OLED:** 0.49" 64x32 I2C modules, the same as the Thing's.
- **Programmer:** ST-LINK V2 clones. They supply 3.3 V or 5 V to the target. Check the
  case's pin labels against the real wiring before first use. ST-LINK V3 models are not
  used: most have no SWIM, and the V3SET's protocol differs.

## Parts

| Project | What it is | State |
|---|---|---|
| **Wren** | The Avalonia app (Windows, Mac, Linux). | Runs firmware on the emulated Thing. |
| **Wren.Asm** | An STM8 assembler, ST's syntax (PM0044). | Done. Every instruction form checked against sdasstm8. |
| **Wren.Emu** | An STM8 emulator, the SSD1315 model and the OLED Thing board. | Done for the Thing's needs. CPU checked against ucsim. |
| **Wren.Link** | USB and SWIM to the ST-LINK V2, replacing STVP. | Next, when the hardware arrives. |
| **Wren.Script** | The token language and its compiler to a 128-byte EEPROM image. | Later. |

Each library has its own test project (`Wren.Asm.Tests` and so on) and stays UI-free.
Test firmware lives in `firmware/`; `firmware/demo/HexDemo.asm` shows b0-b7 as two
large hex digits and is what Wren opens on first run.

## Build order and what each step proves

1. **Wren.Asm and Wren.Emu with the OLED view (done).** Firmware can be written and run
   with unlimited rewrites. Assembler output is checked byte for byte against sdasstm8
   for 577 instruction forms. The CPU is checked against ucsim on 1,568 single
   instructions and on a program using calls, interrupts, pointers and far addressing.
   Board tests run HexDemo and compare the OLED's pixels with the font.
2. **Wren.Link.** Program a Thing from C# without STVP, and write the EEPROM (rated
   ~100k writes) without touching the flash (rated ~100). Own code, with the open-source
   stm8flash and OpenOCD as protocol references. Michael tests it on Windows against an
   S103 board first, then a Thing.
3. **The firmware: the graphics library and the interpreter.** It proves the design fits
   in 8 KB flash and 1 KB RAM. Developed in the emulator, then run on the development
   board with a real OLED, then flashed once to each Thing.
4. **Wren.Script.** A useful program fits in 128 bytes of EEPROM and can be changed
   without reflashing.
5. **Later and optional:** a SWIM on-chip debugger (breakpoints through the debug
   module, as OpenOCD's STM8 support does), and a home-made SWIM programmer on a Teensy
   4.1 or an RP2040 (whose PIO suits SWIM's timing; run the Thing at 3.3 V with it).

## The firmware in outline (to be settled at step 3)

- **Flash, written once:** an I2C/SSD1315 driver with a 256-byte frame buffer. Fonts:
  5x7 (10x4 text), 3x5 (16x5 text) and large 16x32 digits. Drawing primitives. Number
  formats: hex, decimal, binary, ASCII, bar and a history graph. Input reads. The token
  interpreter.
- **EEPROM:** settings and token programs.
- **Spare pins:** six besides b0-b7. PA1, PA2 and PA3 (the mode pins) have internal
  pull-ups, so a button to GND needs nothing else. PD2 and PD3 on J5 can have their
  pull-ups turned on too, and both have ADC channels. PD1 (SWIM) can become a GPIO by
  setting CFG_GCR.SWD; programming still works because a reset restores SWIM, but
  nothing may drive PD1 while the ST-LINK is connected, so it is the last one to use.
- **Latches:** a latch pin's falling edge copies b0-b7 into a row on the display, so
  several pins can each latch a row of hex digits. The STM8 has one external interrupt
  per port, not per pin: PD2 and PD3 share port D's handler (which checks which pin
  fell) and the same edge setting, as do PA1-PA3. The host must hold b0-b7 steady for
  the few microseconds the handler takes.
- **Phase 1:** built-in views chosen from a 3-button menu (Up, Down, Select on PA1-PA3).
- **Phase 2:** a host such as the Reggie drives the Thing as a tiny display terminal,
  sending command bytes on b0-b7 strobed by PD3. The same path can write token programs
  into EEPROM without SWIM.
- **Held in reserve:** an external 24LC256 on the OLED's I2C pins, and the unguaranteed
  640 B EEPROM of the S103 die.

## The emulator's limits

- Modelled: the CPU (all instructions), GPIO with pull-ups and true open-drain on
  PB4/PB5, EXTI, the interrupt controller with priorities, the clock dividers, TIM4,
  the I2C peripheral in master mode, flash and EEPROM programming with write counts,
  and the SSD1315.
- Not modelled: TIM1, TIM2, UART1, SPI, ADC1, the watchdogs, AWU and BEEP. Their
  registers hold values; Wren lists any the firmware touches. I2C bit-banged on PB4/PB5
  is not decoded. EXTI's "falling edge and low level" mode acts as falling edge only.
- Cycle counts are approximate. Timing is right in order, not to the cycle.

## Open questions

1. **The USB driver (first).** Can Wren open the ST-LINK through ST's installed Windows
   driver, via WinUSB P/Invoke or LibUsbDotNet? Or does it need a WinUSB driver
   installed with Zadig, which would then stop STVP working? Mac and Linux would use
   libusb. To be answered with a small spike on Michael's machine.
2. **ST-LINK clones.** Do the clones behave like a genuine V2 (firmware version, SWIM
   speed)?
3. **The OLED module's wiring.** The model shows RAM columns 32-95 and rows 0-31, and
   assumes no flip. The real module may be mirrored or offset; check on the development
   board and adjust `Ssd1315.ColumnOffset` and the remap handling to match.
4. **Extra EEPROM.** Is the 640 B EEPROM real on Michael's Things? A one-Thing test in
   STVP: set the device to STM8S103F3, then write, power-cycle and read back
   0x4080-0x427F.
5. **DIV and the Z flag.** ucsim 0.6.4 sets Z after DIV/DIVW when the quotient is not
   zero; ST's PM0044 says Z is set when it is zero. Wren follows PM0044. Confirm on the
   development board with a two-line test.
