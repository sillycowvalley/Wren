# The 8-bit LED Thing v1.0

Digicool Things' second board, from its schematic (Rev 1.0, 2019-12-12) and its factory
firmware (`firmware/digicool/8-bit_led_thing.s19`). Wren's model is `Wren.Emu.LedThing`;
**Board → LED Thing** builds and runs programs for it (below, "Wren on the LED Thing").

## Parts

- **U2:** STM8S003F3U6TR (UFQFPN20), the same chip as the OLED Thing. No crystal: it runs on
  the 16 MHz HSI.
- **U1:** an 8-LED DIL bar: pins 1-8 the anodes, 9-16 the cathodes.
- **R1-R4:** 180 Ω, one in each LED drive line (PD3, PD4, PD5, PD6 in that order).
- **C1** 680 nF on VCAP, **C2** 100 nF on NRST, **C3** 100 nF on VDD.
- Power: VDD 2.95-5.5 V (typically 3.3 V or 5 V). No pull-up resistors anywhere.

## Pins

| Signal | MCU pin | Notes |
|---|---|---|
| b0 | PD2 | Floating input, ~1 µA |
| b1 | PC7 | |
| b2 | PC6 | |
| b3 | PC5 | |
| b4 | PC4 | |
| b5 | PC3 | |
| b6 | PB4 | True open-drain pin: it has no pull-up, so it can only float |
| b7 | PB5 | True open-drain pin, as b6 |
| M2 | PA1 | Mode input, internal pull-up (~55 kΩ) |
| M1 | PA2 | |
| M0 | PA3 | |
| LEDs | PD3-PD6 | Charlieplexed, below |
| SWIM | PD1 | Programming header with VDD and NRST |

Unused b inputs need an external pull-up or pull-down, or a tie to GND. The STM8S has no
internal pull-downs: an input floats or is pulled up.

## The LEDs

The eight LEDs are charlieplexed on PD3-PD6: each sits between two of the four pins, lit
when its anode pin drives high and its cathode pin drives low, with the other two pins
left as inputs. Each LED's current passes through two of the 180 Ω resistors (about 8 mA
at 5 V, 4 mA at 3.3 V for a red bar). Read from the factory firmware running in Wren's
emulator, one b input high at a time:

| LED | Anode (high) | Cathode (low) |
|---|---|---|
| b0 | PD3 | PD4 |
| b1 | PD3 | PD5 |
| b2 | PD4 | PD3 |
| b3 | PD4 | PD5 |
| b4 | PD5 | PD3 |
| b5 | PD5 | PD4 |
| b6 | PD3 | PD6 |
| b7 | PD6 | PD3 |

Only one LED is lit at a time; the firmware scans them, each lit about one eighth of the
time.

## Digicool's firmware

The factory firmware is in `firmware/digicool` (with Digicool's permission; not MIT). It is
compiled C and runs the CPU at 8 MHz with no interrupts,
timers, watchdog or EEPROM use: one busy loop is the time base. It leaves b0-b7 floating
and pulls M0-M2 up.

- **The scan**: one frame (a loop pass) is about 2.9 ms: eight slots of about 357 µs, b0
  first, each LED lit in its slot if its bit is 1. Only one LED is ever on, so a lit LED
  has 1/8 duty whatever else is lit.
- **The mode** (M0 + 2·M1 + 4·M2) is read raw every frame; a change restarts the new
  mode's sequence from its first step. A step is 32 frames, about 91 ms.

| M2 M1 M0 | Mode | Sequence (b7..b0, repeating) | Step |
|---|---|---|---|
| 0 0 0 | Curtain close and open | 81 C3 E7 FF E7 C3 81 00 | 91 ms |
| 0 0 1 | Two LEDs in and out | 81 42 24 18 24 42 81 00 | 91 ms |
| 0 1 0 | Binary counter | 00, 01 ... FF, wrapping | 183 ms |
| 0 1 1 | One LED left and right | 01 02 04 08 10 20 40 80 80 40 20 10 08 04 02 01 | 91 ms |
| 1 0 0 | Fill from the ends, empty from the ends | 81 C3 E7 FF 7E 3C 18 00 | 91 ms |
| 1 0 1 | Sweep | 01 03 07 0F 1F 3F 7F FF FE FC F8 F0 E0 C0 80 00 80 C0 E0 F0 F8 FC FE FF 7F 3F 1F 0F 07 03 01 00 | 91 ms |
| 1 1 0 | Dice Roll | below | |
| 1 1 1 | The b inputs (the default) | below | |

- **Dice Roll** (mode 6): a roll starts when the mode goes from 7 to 6, so M0 falling
  with M1 and M2 high (Digicool's push-to-break switch from M0 to GND: releasing it
  rolls), and at power-up in mode 6. Seven faces, each different from the one before,
  at 0, 183, 367, 550, 734, 1101 and 1468 ms, a blank at 1835 ms, then the 7th face
  again at 2202 ms, held. The faces (1-6 pips) are 10 18 34 3C 7A 7E. Coming to mode 6
  from modes 0-5 shows the last face. The random source is a 16-bit xorshift stepped every
  frame.
- **The b inputs** (mode 7): each LED shows the share of the last frame (about 3 ms) its
  input was high, in 33 steps: a steady high is fully lit, a fast PWM input dimmed.

## Wren on the LED Thing

- **The kernel** is `firmware/kernel/Kernel.asm` built with `BOARD` = 2 (Wren builds both
  boards' kernels as it starts). It has no OLED driver, graphics or fonts: about 2.4 KB, so
  it ends near $89CB. Its program store, program table and info block are the OLED Thing's
  ($9280, 54 blocks), with board 2 in the info block.
- **The LEDs**: the 1 ms tick lights one LED, b0 to b7 in turn, so a frame is 8 ms and a lit
  LED is on one eighth of the time, as on Digicool's firmware (whose frame is 2.9 ms).
  Running under the debugger, the scan goes on (the variables are read beside the running
  CPU). Paused, or stepping, the chip and its tick stop at every bytecode instruction for a
  round trip over SWIM (about 50 ms through a Pico), so the LED of that moment stays lit
  and the scan crawls; with no debugger the scan is full speed.
- **Wren BASIC** on this board: `LEDS v` lights bn's LED for each bit n of v. The mode pins
  are `M0` (PA3), `M1` (PA2) and `M2` (PA1), for `PIN`, `ON PRESS`, `ON RELEASE`, `ON LATCH`
  and `ON PINS`. The display statements, the fonts, `ANALOG` and the OLED Thing's pin
  names are refused when building for this board. `PULLUPS` reaches b0-b5; b6 and b7 have
  no pull-up. At start b0-b5 and M0-M2 are pulled up.
- **A fault** lights b7 with the fault number in b0-b6; with no program, b0 and b7 are lit.
- **Install kernel** installs this kernel on a Thing it recognises as an LED Thing: one
  holding Digicool's LED firmware or Wren's LED kernel. A blank LED Thing cannot be told
  from a blank OLED Thing whose display is not answering, so it is refused: write
  Digicool's LED firmware with **Write image…** first.
- **`programs/LedThing/LedThing.bas`** recreates the eight modes. Its tests run it and Digicool's
  firmware side by side in the emulator: modes 0-5 show the same patterns in the same
  order, and the roll has the same shape. It differs from Digicool's in three ways:
  mode 7 lights each LED fully while its input is high, rather than dimming it by the
  input's duty cycle; a mode change waits for the 20 ms debounce; and the new pattern's
  first step can be shorter than 91 ms, since `ON EVERY` keeps its own time.

## Open issues

- Not yet tried on a real LED Thing.
- The store could be about 4.6 KB larger on this board (the kernel leaves $89CC-$926F
  unused), which needs a second program table layout.
