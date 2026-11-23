# Wren

A development environment for STM8S003F3-class microcontrollers, in C# / .NET 8.
Its first target is the Digicool Things "8-bit OLED Thing".

Wren is not affiliated with STMicroelectronics or Digicool Things. STM8, ST-LINK and
SWIM are ST's names, used here to describe compatibility.

## What is in it

- **Wren**: the app. It assembles a program, runs it on an emulated OLED Thing, and
  shows the OLED, the inputs, the CPU and how many times the firmware wrote to flash
  and EEPROM. It reloads when the source file is saved.
- **Wren.Asm**: an STM8 assembler using ST's instruction syntax. It writes Intel HEX
  and S-records, which ST Visual Programmer can load.
- **Wren.Emu**: an STM8 emulator with the SSD1315 OLED and the OLED Thing board.
- **firmware/**: test programs in STM8 assembly. `demo/HexDemo.asm` shows the inputs
  as two large hex digits.

Planned: **Wren.Link** (programming over an ST-LINK V2) and **Wren.Script** (a token
language for the 128-byte EEPROM). See [docs/ProjectPlan.md](docs/ProjectPlan.md).

## Building

Open `Wren.sln` in Visual Studio 2022 (with the .NET 8 SDK), set **Wren** as the startup
project and run it. The tests are in Test Explorer.

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
`INCLUDE`, `END`.

## Licence

MIT.
