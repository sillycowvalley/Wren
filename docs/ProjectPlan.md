# Wren project plan

**Status: approved. Step 1 (assembler, emulator, app) and the firmware (kernel, Wren BASIC, debugger) are built and tested. Wren's kernel and `OledThing.bas` run on a real OLED Thing, installed through a Raspberry Pi Pico programmer, and programs are debugged on it from the main window. The LED Thing's kernel and `LedThing.bas` run on a real LED Thing too.**

Wren is a C# / .NET 8 development environment for STM8S003F3-class microcontrollers.
It is driven through an ST-LINK V2 over SWIM. Its targets are Digicool's "8-bit OLED
Thing" (STM8S003F3U, 64x32 SSD1315 OLED over I2C on PB4/PB5) and "8-bit LED Thing" (the
same chip, eight charlieplexed LEDs). Licence: MIT. Their pins are in
[OledThing.md](OledThing.md) and [LedThing.md](LedThing.md).

## Strategy: protect the Things' flash

A Thing's flash is rated for only ~100 writes, so firmware reaches it last:

1. **Emulator.** The MCU, the OLED and the LEDs are emulated in Wren. Rewrites are unlimited.
2. **Development board.** An STM8S103F3 board with a real 0.49" 64x32 OLED module wired
   to it. The firmware keeps to the S003's limits: 8 KB flash, 1 KB RAM, 128 B EEPROM.
3. **The Things.** Each Thing is flashed once with the finished kernel. After that,
   programs go to its program store, reusing what it already holds.

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
| **Wren** | The Avalonia app (Windows, Mac, Linux). | Runs and debugs Wren BASIC on the emulated Thing. |
| **Wren.Asm** | An STM8 assembler, ST's syntax (PM0044). | Done. Every instruction form checked against sdasstm8. |
| **Wren.Emu** | An STM8 emulator, the SSD1315 model and the OLED Thing and LED Thing boards. | Done for the Thing's needs. CPU checked against ucsim. |
| **Wren.Link** | SWIM through an ST-LINK V2 (USB) or a Pico (serial), replacing STVP. | With Wren's PicoLink sketch on a Pico it installs both kernels, uploads and debugs programs, and writes Digicool's firmware back, on real OLED Things and LED Things. The ST-LINK is not yet tried. See [PicoLink.md](PicoLink.md). |
| **Wren.Script** | The Wren BASIC compiler, the FAT planner and the debugging session. | Done. Tested against the kernel on the emulator. |

Each library has its own test project (`Wren.Asm.Tests` and so on) and stays UI-free.
The kernel lives in `firmware/kernel/`; BASIC programs in `programs/`, a folder for each
Thing. `programs/OledThing/OledThing.bas` recreates the Thing's own behaviour and is what Wren opens on
first run. The firmware design is in [Design.md](Design.md) and the bytecode in
[Bytecode.md](Bytecode.md).

## Build order and what each step proves

1. **Wren.Asm and Wren.Emu with the OLED view (done).** Firmware can be written and run
   with unlimited rewrites. Assembler output is checked byte for byte against sdasstm8
   for 577 instruction forms. The CPU is checked against ucsim on 1,568 single
   instructions and on a program using calls, interrupts, pointers and far addressing.
   Board tests run HexDemo and compare the OLED's pixels with the font.
2. **Wren.Link.** Program a Thing from C# without STVP, and write the EEPROM (rated
   ~100k writes) without touching the flash (rated ~100). Own code, with the open-source
   stm8flash and OpenOCD as protocol references. Michael tests it on Windows against an
   S103 board first, then a Thing. A Raspberry Pi Pico does the same job at low SWIM
   speed and has programmed a Thing ([PicoLink.md](PicoLink.md)).
3. **The firmware (done).** The kernel (VM, graphics, events, the program table) fits in
   8 KB flash and 1 KB RAM beside a 3,456-byte program store, and ends with an info block
   that tells Wren its version, board and the Thing's EEPROM. It runs on a real OLED Thing.
   Like Digicool's firmware, it sets the OLED up with segment remap and reversed COM
   scan, because the Thing's module is mounted rotated 180°. It also turns on pull-ups on
   b0–b7, so inputs left open read 1 (`PULLUPS` changes that). Programs reach a Thing over
   SWIM (Wren.Link). Install kernel first tells the board from its firmware or by asking
   for the OLED on I2C, and installs that board's kernel.
4. **Wren BASIC (done).** The OLED Thing's own behaviour is 502 bytes of BASIC, the LED
   Thing's 625.
5. **Debugging on the Thing (built).** On **Debug → Target → Thing on the programmer** the
   main window runs and debugs a program on a real Thing with the same keys as the
   emulator, through the chip's debug module and no kernel code (`ThingDebugger`):
   - a line breakpoint is a data-read breakpoint on the line's first bytecode in flash,
     checked to be the VM fetching it; the chip has two, so two lines at a time;
   - stepping and Break All use an instruction breakpoint on the kernel's `vm_next`;
   - the variables are read with the CPU stalled briefly, a few times a second, since SWIM
     cannot read RAM while the kernel idles in WFI; TIM4 is frozen meanwhile, so `MILLIS`
     and `EVERY` pause with it.
   The bench checks on a real Thing found all of this as UM0470 describes, and the
   emulator models the debug module the same way.

## The firmware in outline

The detailed design, for approval, is in [Design.md](Design.md).

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
  ADC1 (single and continuous conversion, with analog levels set per pin), and the
  SSD1315.
- Not modelled: TIM1, TIM2, UART1, SPI, ADC1's buffered and scan modes, the watchdogs,
  AWU and BEEP. Their
  registers hold values; Wren lists any the firmware touches. I2C bit-banged on PB4/PB5
  is not decoded. EXTI's "falling edge and low level" mode acts as falling edge only.
- Cycle counts are approximate. Timing is right in order, not to the cycle.

## Open questions

1. **The USB driver (first).** Wren.Link opens the ST-LINK through WinUSB, which ST's
   own ST-LINK driver (STSW-LINK009) uses, so it should work beside STVP without Zadig.
   To check, with the ST-LINK plugged in and nothing else using it:
   1. In Visual Studio, run **Wren** and choose **Tools → Firmware…**.
   2. The log should list the ST-LINK. If it says "No ST-LINK found", open Device
      Manager and note where the ST-LINK appears and what driver it shows.
   3. Wire the ST-LINK to the S103 board (RST, SWIM, GND, and 3.3 V to the board), click
      **Connect**, and copy the log back: version, target voltage, and what the board holds.
   Mac and Linux need libusb, which is not written yet. The Pico programmer works
   anywhere.
2. **ST-LINK clones.** Do the clones behave like a genuine V2 (firmware version, SWIM
   speed)?
3. **Extra EEPROM.** Do all of Michael's Things have the S103's 640 B of EEPROM
   ($4000-$427F) rather than the 128 B the S003's datasheet promises? The first OLED Thing
   checked does, and keeps it through a power cycle. Install kernel writes and reads
   back one block at $4080 and, if it holds, gives the program 512 setting bytes there
   instead of 12 (the kernel's info block records which). Install the kernel on the other
   Things and note the EEPROM size the Firmware window shows for each.
4. **DIV and the Z flag.** ucsim 0.6.4 sets Z after DIV/DIVW when the quotient is not
   zero; ST's PM0044 says Z is set when it is zero. Wren follows PM0044. Confirm on the
   development board with a two-line test.
5. **The LED Thing.** Built and tested in the emulator and running on a real LED Thing (`docs/LedThing.md`): the kernel's
   LED build, `LEDS` and the mode pins `M0`-`M2`, **Board → LED Thing**, Install kernel and
   debugging through the programmer, and `programs/LedThing/LedThing.bas` checked against
   what Digicool's firmware showed. Open: a larger program store
   on this board (its kernel leaves about 4.6 KB unused), which needs a second table layout.
   Digicool's firmware is fetched from Digicool's GitHub when the Firmware window opens
   (`docs/Design.md`).
