# Wren

<img src="src/Wren/Assets/wren.png" alt="Wren, a small brown bird with its tail cocked up" width="160" align="right" />

A development environment for STM8S003F3-class microcontrollers, in C# / .NET 8.
Its first target is the Digicool Things "8-bit OLED Thing".

Wren is not affiliated with STMicroelectronics or Digicool Things. STM8, ST-LINK and
SWIM are ST's names, used here to describe compatibility.

## What is in it

- **Wren**: the app. You edit a Wren BASIC program in it (File and Edit menus, Notepad
  style) and build, run and debug it with Visual Studio's keys: Ctrl+Shift+B builds, F5
  starts debugging (building first if the program changed), Ctrl+F5 runs without
  debugging, Shift+F5 stops, F9 toggles a breakpoint, F11, F10 and Shift+F11 step into,
  over and out, Ctrl+F10 runs to the cursor. **Debug → Target** chooses where: the
  emulated OLED Thing (the OLED, the inputs and pins, the current line, variables and the
  flash write counts), or a real Thing on the programmer, where Ctrl+F5 uploads the
  program only if it changed and runs it. A program saved from another editor reloads
  by itself.
- **Wren BASIC**: the Thing's language. Programs react to events (`ON CHANGE`,
  `ON LATCH PD3 RISING WHILE PD2 LOW`, `ON EVERY 100` ...) and draw with statements
  such as `HEX`, `BITS`, `TEXT` and `BOX`. See [docs/Design.md](docs/Design.md), and the
  one-page [quick reference](docs/Infographics/Wren_BASIC_Quick_Reference.png).
- **The kernel**: the Thing's firmware in STM8 assembly: the bytecode VM, graphics,
  fonts, events and the program loader. Programs live in a wear-levelled store
  described by a table in EEPROM.
- **Wren.Script**: the BASIC compiler and the debugger's engine.
- **Wren.Asm**: an STM8 assembler using ST's instruction syntax. It writes Intel HEX
  and S-records, which ST Visual Programmer can load.
- **Wren.Emu**: an STM8 emulator with the SSD1315 OLED and the OLED Thing board.
- **programs/**: Wren BASIC programs. `OledThing.bas` is the Thing's own behaviour.
- **art/**: the wren, as hand-written SVG sources: `wren.svg` (the full drawing),
  `wren-icon.svg` (simplified for small icons), `wren-16.svg` (hand-placed 16 x 16 pixels)
  and `wren-oled.svg` (1-bit, as the 64 x 32 OLED would show it). The PNGs and the `.ico`
  the app uses are in `src/Wren/Assets`.

- **Wren.Link**: programming a real Thing through an ST-LINK V2 (Windows, WinUSB) or a
  Raspberry Pi Pico or RP2040 Plus running pico-swimlib ([docs/PicoLink.md](docs/PicoLink.md)): install
  the kernel, upload programs through the FAT, read the wear history. Only blocks that
  change are written. In Wren: **Tools → Programmer…**.

See [docs/ProjectPlan.md](docs/ProjectPlan.md).

## Building

Open `Wren.sln` in Visual Studio 2022 (with the .NET 8 SDK), set **Wren** as the startup
project and run it. The tests are in Test Explorer.

Wren remembers its window positions and sizes, the width of the column beside the program,
the Board choice, the last programmer connected to and the last program opened in
`%AppData%\Wren\Settings.json`. Delete that file to go back to the defaults.

## A taste of Wren BASIC

```
' Two rows: PD3 latches the top one, PD2 the bottom one
DIM top, bottom

ON LATCH PD3
  top = VALUE
  Draw
END ON

ON LATCH PD2
  bottom = VALUE
  Draw
END ON

SUB Draw
  CLS
  HEX 0, 0, top, 2, LARGE
  HEX 0, 16, bottom, 2, LARGE
  BITS 30, 2, 32, 12, top
  BITS 30, 18, 32, 12, bottom
  SHOW
END SUB
```

## Assembler syntax

Instructions follow ST's PM0044 manual: `ld a,#$12`, `ld a,($10,x)`, `ld a,[$80.w]`,
`ldw x,([$80.w],y)`, `btjt $5001,#3,label`. Numbers are `$FF` or `0xFF` (hex), `%1010`
(binary), `255` or `'A'`. Expressions use `+ - * / % & | ^ ~ << >>` and `HIGH()`,
`LOW()`, `PAGE()`.

```
        include "stm8s003.inc"
COUNT   equ 10

        org $8000
        int start           ; reset vector

        org $8080
start:  ldw x,#COUNT
loop:   decw x
        jrne loop
        bset PD_ODR,#3
        jra start

table:  dc.b 1,2,3,"text"
        dc.w $1234
buffer  ds.b 16
```

A label starts in column 1 (the colon is optional there) or ends with a colon.
Directives: `ORG`, `DC.B`/`DB`, `DC.W`/`DW`, `DS.B`/`DS`, `DS.W`, `EQU` or `=`,
`INCLUDE`, `RELAX` (from there on `JP` and `CALL` become 2-byte `JRA`/`CALLR` where the
target is in reach), `END`. Placing two things at the same address is an error.

## Licence

MIT. See [LICENSE](LICENSE). Two folders are others' work under their own terms:
- `firmware/pico-swimlib`: Richard Hulme's pico-swimlib, BSD 3-Clause.
- `firmware/digicool`: Digicool Things' factory firmware, included with Digicool's permission.
