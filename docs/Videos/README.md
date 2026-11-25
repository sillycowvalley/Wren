# Wren BASIC videos

The Wren BASIC series and its YouTube playlist: videos about Wren, made as
[How_We_Make_Videos.md](../How_We_Make_Videos.md) says. Each video is a script here,
`Wren_NN_Name.md`, and a folder of slides, `Infographics/Wren_NN`.

| Video | Script | Slides | State |
|---|---|---|---|
| 1. Snake and Pong in BASIC on an 8 KB chip (the overview) | [Wren_01_Overview.md](Wren_01_Overview.md) | `Infographics/Wren_01` | Script and slides ready; not yet recorded |

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
