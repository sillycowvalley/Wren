# Wren BASIC videos

The Wren BASIC series and its YouTube playlist: videos about Wren, made as
[How_We_Make_Videos.md](../How_We_Make_Videos.md) says. Each video is a script here,
`Wren_NN_Name.md`, and a folder of slides, `Infographics/Wren_NN`.

| Video | Script | Slides | State |
|---|---|---|---|
| 1. Snake and Pong in BASIC on an 8 KB chip (the overview) | [Wren_01_Overview.md](Wren_01_Overview.md) | `Infographics/Wren_01` | Script and slides ready; not yet recorded |

## The playlist, planned

Hands-on videos first, so a viewer can get a board running and follow along; the deep
dives after, for those who stay. Each headline leads with a name or a claim, never "how"
(How_We_Make_Videos.md section 2).

| # | Video | What it covers |
|---|---|---|
| 1 | The overview | Done: above. |
| 2 | A Pi Pico as the programmer | Flashing the Pico's firmware (PicoLink or pico-swimlib), wiring it to each Thing, installing the kernel, putting the factory firmware back. The ST-LINK V2 is mentioned only once it has been tried on a Thing. |
| 3 | Prototyping in the emulator | Writing, testing and debugging a program with no board at all: the inputs, mode pins and analog levels, breakpoints, stepping and variables. |
| 4 | A game from scratch | A tour of Wren BASIC (events, drawing, fonts) by writing a new game on camera. The headline is the game's genre, one everyone knows (a brick-breaker, say). |
| 5 | Wear levelling | The flash's ~100 writes: why an interpreter helps, an upload reusing the blocks that did not change, the program table (the FAT) in the tiny EEPROM, the Wear report. |
| 6 | KNIGHT RIDER on the LED Thing | Eight LEDs charlieplexed on four pins, brightness from scan frames, `KnightRider.bas` and `Cylon.bas`. |
| 7 | Analog inputs | `ANALOG` on PD2 and PD3: a knob or a light sensor, shown as a number, a bar and a history graph. |
| 8 | BASIC in 1 KB of RAM | The kernel's virtual machine: the bytecode, the event handlers, its speed, and why an interpreter suits a chip this small. |
| 9 | The debugger, under the hood | Line breakpoints as data-read breakpoints on bytecode, stepping on `vm_next`, variables read with the CPU stalled, two breakpoints, the timers frozen. |
| 10 | Reverse-engineering the factory firmware | Digicool's compiled C, disassembled and annotated, rebuilt in 502 bytes of BASIC, and both run side by side in the emulator's tests. |
| 11 | Building the emulator and the assembler | The CPU checked against ucsim, the display modelled, every instruction form checked against sdasstm8. One video or two. |

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
- **The recurring motif**: the OLED itself, black glass with lit pixels, and on a thumbnail
  a row of OLED pixels at the bottom right.
- **The screens on the slides are the emulator's**, never drawn by hand: the program as it
  is in `programs/`, run on Wren's kernel in Wren.Emu, its 64 x 32 panel written as one SVG
  rect per run of lit pixels between `<!--OLED:name-->` and `<!--/OLED-->`. Each HTML's
  opening comment names the run.

## The figures

YouTube Studio's (Analytics, Content), once there are videos; this table overrules the one
in How_We_Make_Videos.md section 2 once it has a few.

| Thumbnail's headline | Views a day | Watched a view |
|---|---:|---|
