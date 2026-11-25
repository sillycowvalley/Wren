# Wren BASIC videos

The Wren BASIC series and its YouTube playlist: videos about Wren, made as
[How_We_Make_Videos.md](../How_We_Make_Videos.md) says. Each video is a script here,
`Wren_NN_Name.md`, and a folder of slides, `Infographics/Wren_NN`.

| Video | Script | Slides | State |
|---|---|---|---|
| 1. Snake and Pong in BASIC on an 8 KB chip (the overview) | [Wren_01_Overview.md](Wren_01_Overview.md) | `Infographics/Wren_01` | Script and slides ready; not yet recorded |
| 2. A Raspberry Pi Pico as the programmer | [Wren_02_Pico.md](Wren_02_Pico.md) | `Infographics/Wren_02` | Script and slides ready; the LED Thing's header pin order to confirm; not yet recorded |
| 3. BASIC in 1 KB of RAM | [Wren_03_Basic.md](Wren_03_Basic.md) | `Infographics/Wren_03` | Script and slides ready, built round `programs/OledThing/Stopwatch.bas`; not yet recorded |
| 4. Snake, inside the emulator | [Wren_04_Emulator.md](Wren_04_Emulator.md) | `Infographics/Wren_04` | Script and slides ready; not yet recorded |

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
| 5 | Wear levelling | The flash's ~100 writes: why an interpreter helps, an upload reusing the blocks that did not change, the program table (the FAT) in the tiny EEPROM, the Wear report. |
| 6 | KNIGHT RIDER on the LED Thing | Eight LEDs charlieplexed on four pins, brightness from scan frames, `KnightRider.bas` and `Cylon.bas`. |
| 7 | Analog inputs | A knob (a potentiometer) on PD2 or PD3, read with `ANALOG` (0 to 1023): first the emulator's slider, then the real knob on the board, shown as a number and a bar (`Analog.bas`) and then as a history graph. |

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
