# Wren

A development environment for STM8S003F3-class microcontrollers, in C# / .NET 8.
Its first target is the Digicool Things "8-bit OLED Thing".

Wren is not affiliated with STMicroelectronics or Digicool Things. STM8, ST-LINK and
SWIM are ST's names, used here to describe compatibility.

## What is in it

- **Wren**: the app. It compiles a Wren BASIC program, uploads it to an emulated OLED
  Thing and runs it: the OLED, the inputs and pins, the program with the current line,
  stepping by statement, breakpoints, variables, and the flash write counts. It
  reloads when the program file is saved.
- **Wren BASIC**: the Thing's language. Programs react to events (`ON CHANGE`,
  `ON LATCH PD3 RISING WHILE PD2 LOW`, `ON EVERY 100` ...) and draw with statements
  such as `HEX`, `BITS`, `TEXT` and `BOX`. See [docs/Design.md](docs/Design.md).
- **The kernel**: the Thing's firmware in STM8 assembly: the bytecode VM, graphics,
  fonts, events and the program loader. Programs live in a wear-levelled store
  described by a table in EEPROM.
- **Wren.Script**: the BASIC compiler and the debugger's engine.
- **Wren.Asm**: an STM8 assembler using ST's instruction syntax. It writes Intel HEX
  and S-records, which ST Visual Programmer can load.
- **Wren.Emu**: an STM8 emulator with the SSD1315 OLED and the OLED Thing board.
- **programs/**: Wren BASIC programs. `OledThing.bas` is the Thing's own behaviour.

Planned: **Wren.Link** (programming over an ST-LINK V2). See
[docs/ProjectPlan.md](docs/ProjectPlan.md).

## Building

Open `Wren.sln` in Visual Studio 2022 (with the .NET 8 SDK), set **Wren** as the startup
project and run it. The tests are in Test Explorer.

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

MIT. See [LICENSE](LICENSE).
