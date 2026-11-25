# Wren BASIC videos

The Wren BASIC series and its YouTube playlist: videos about Wren, made as
[How_We_Make_Videos.md](../How_We_Make_Videos.md) says. Each video is a script here,
`Wren_NN_Name.md`, and a folder of slides, `Infographics/Wren_NN`. A recorded video's
script and slides are left as they were recorded.

| Video | Script | Slides | State |
|---|---|---|---|
| 1. Snake and Pong in BASIC on an 8 KB chip (the overview) | [Wren_01_Overview.md](Wren_01_Overview.md) | `Infographics/Wren_01` | Recorded |
| 2. A Raspberry Pi Pico as the programmer | [Wren_02_Pico.md](Wren_02_Pico.md) | `Infographics/Wren_02` | Recorded |
| 3. BASIC in 1 KB of RAM | [Wren_03_Basic.md](Wren_03_Basic.md) | `Infographics/Wren_03` | Recorded, built round `programs/OledThing/Stopwatch.bas` |
| 4. Snake, inside the emulator | [Wren_04_Emulator.md](Wren_04_Emulator.md) | `Infographics/Wren_04` | Script and slides ready; not yet recorded |
| 5. A hundred writes (wear levelling) | [Wren_05_Wear.md](Wren_05_Wear.md) | `Infographics/Wren_05` | Script and slides ready; not yet recorded |
| 6. A voltmeter in BASIC | [Wren_06_Voltmeter.md](Wren_06_Voltmeter.md) | `Infographics/Wren_06` | Script and slides ready, built round `programs/OledThing/Voltmeter.bas`; not yet recorded |
| 7. Catch a byte on an edge (LATCH) | [Wren_07_Latch.md](Wren_07_Latch.md) | `Infographics/Wren_07` | Script and slides ready, built round `programs/OledThing/Latch.bas` and `LatchTest.bas`; not yet recorded |
| 8. A frequency counter in BASIC | [Wren_08_Frequency.md](Wren_08_Frequency.md) | `Infographics/Wren_08` | Script and slides ready, built round `programs/OledThing/Frequency.bas`; not yet recorded |
| 9. An 8-channel logic analyzer | [Wren_09_Logic.md](Wren_09_Logic.md) | `Infographics/Wren_09` | Script and slides ready, built round `programs/OledThing/LogicAnalyzer.bas` and `firmware/LogicSource`; not yet recorded |
| 10. A bus sniffer | [Wren_10_Bus.md](Wren_10_Bus.md) | `Infographics/Wren_10` | Script and slides ready, built round `programs/OledThing/BusSniffer.bas` and `firmware/BusSource`; not yet recorded |

## The playlist, planned

Hands-on videos first, so a viewer can get a board running and follow along; the deep
dives after, for those who stay. Each headline leads with a name or a claim, never "how"
(How_We_Make_Videos.md section 2).

| # | Video | What it covers |
|---|---|---|
| 1 | The overview | Done: above. |
| 2 | A Pi Pico as the programmer | Ready: above. Flashing Wren's PicoLink firmware onto the Pico (drag the .uf2 on), wiring it to each Thing, installing the kernel, putting the factory firmware back. The ST-LINK V2 is mentioned only once it has been tried on a Thing. |
| 3 | BASIC in 1 KB of RAM | Ready: above. Wren BASIC in detail: the shape of a program, the events (`ON START`, `ON EVERY`, `ON CHANGE`, `ON PRESS` and the rest), variables and arrays, `SUB` and `FUNCTION`, drawing and the fonts, the number formats, the pins, and the limits that 1 KB of RAM sets. The Quick Reference (Help menu) on screen as the map. Under it all, briefly: the compiler turns it into the kernel's bytecode. |
| 4 | Prototyping in the emulator | Ready: above. Writing, testing and debugging a program with no board at all, with `Snake.bas` as the example: stepping into `Move` as it weighs each way on, watching `grid` and the body's ring fill up, catching it as it boxes itself in. Then the inputs, mode pins and analog levels the emulator can set. The overview only stops Snake at the food; this goes inside it. |
| 5 | Wear levelling | Ready: above. The flash's ~100 writes: why an interpreter helps, an upload reusing the blocks that did not change, the program table (the FAT) in the tiny EEPROM, the Wear report. |
| 6 | A voltmeter on the OLED Thing | Ready: above. The analog-to-digital converter on PD2: a variable resistor as a voltage divider, `ANALOG` giving 0 to 1023, turned into volts (0 to 3.3 V) with 16-bit integers, both shown; averaging to steady the last digit; a scrolling graph of the last 64 readings. Straight to the real knob, no emulator. |
| 7 | Catch a byte on an edge | Ready: above. An Arduino Mega (`firmware/LatchFeeder`) plays a byte bus: noise on b0-b7 through a straight 8-wire ribbon, then a byte and a rising strobe on PD3; `ON LATCH PD3 RISING` catches each byte and never the noise; ON CHANGE, ON PRESS and ON LATCH compared; setup and hold; rising, falling, both and WHILE. Then how fast: the Mega counts 00-FF at a rising rate (its `w` sweep) and `LatchTest.bas` stops at the first byte wrong or missed; in the emulator it is right to 550 a second and fails at 600, when more than the event queue's 7 arrive while the screen is drawn. |
| 8 | A frequency counter | Ready: above. A slow 555 clock (about 3 to 50 Hz) on PD3, the Thing powered from the clock board's 5 V; a reciprocal counter (timestamped edges over 2 seconds, not a 1-second count); long division in 16-bit integers. |
| 9 | A logic analyzer | Ready: above. b0-b7 sampled as one byte every ms into 64 bytes, triggered by the first change, drawn as eight traces of four pixels, held 3 s; an Arduino Mega (`firmware/LogicSource`) plays a counter, a walking one and eight PWM lines, changing on its own. |
| 10 | A bus sniffer | Ready: above. `ON LATCH PD3 RISING WHILE PD2 LOW`: an Arduino Mega plays a CPU reading and writing memory; the Thing logs only the writes. |
| 11 | A serial decoder | Clock on PD3, data on b0, select on PD2: bytes assembled a bit at a time from an Arduino Mega. |

Open: whether to make a video on how the project was built with Claude.

## The series' look

The house style of How_We_Make_Videos.md section 5 (navy and its grid, the gold headline
with a white second half, Liberation Sans and DejaVu Sans Mono), with these for Wren:

- **The chip**: `WREN BASIC · OVERVIEW · 3/10` on a slide; `WREN BASIC · 1` on a thumbnail.
- **One colour to a meaning**:

  | Colour | Meaning |
  |---|---|
  | Blue `#60a5fa` | Wren BASIC: programs, listings, the editor and compiler |
  | Gold `#fde68a` | the kernel, the debugger, and the point being made |
  | Violet `#c4b5fd` | the boards and their parts: the Things, the Pico, pins |
  | Red `#f87171` | faults and flash wear |
  | Pixel white `#e6f0ff` | an OLED's lit pixels (LEDs are drawn in their own red) |

  Green is left out: the Reggie-16 videos use it a lot.
- **The channel's mark**: the colour wren from `art/wren.svg`, top right on a slide, beside
  "WREN BASIC" on a thumbnail.
- **The recurring motif**: the OLED itself, black glass with lit pixels.
- **The screens on the slides are the emulator's**, never drawn by hand: the program as it
  is in `programs/`, run on Wren's kernel in Wren.Emu, its 64 x 32 panel written as one SVG
  rect per run of lit pixels between `<!--OLED:name-->` and `<!--/OLED-->`. Each HTML's
  opening comment names the run.

## The figures

YouTube Studio's (Analytics, Content), once there are videos; this table overrules the one
in How_We_Make_Videos.md section 2 once it has a few.

| Thumbnail's headline | Views a day | Watched a view |
|---|---:|---|
