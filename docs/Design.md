# Wren firmware design: kernel, language and program storage

**Status: approved and built: the kernel, the compiler and the debugger run on the emulated Thing.**

Decided so far: programs are **bytecode** run by a virtual machine (VM); the language is
**BASIC-flavoured**; programs react to the outside world through **event handlers**; a
Thing holds **one program**; the EEPROM is used as below (the S103's extra EEPROM is not).

## The idea in one paragraph

Each Thing gets a **kernel** once, over SWIM: the VM, the OLED driver, graphics, fonts,
timers and the loader. The kernel sits in the first part of flash, locked by the
user boot code (UBC) option byte so nothing can erase it except a SWIM programmer.
Programs are written in Wren BASIC, compiled by Wren on the PC, and uploaded into the
rest of flash, the **program store**, as many times as needed. A **file allocation
table (FAT)** in the EEPROM says which blocks hold the program and how often each block
has been written, so every upload goes to the least-worn free blocks and the ~100-write
limit is spread over the whole store.

## Flash and EEPROM

Facts from RM0016 for the STM8S003 (low density):

- Flash is 8 KB: 128 blocks of 64 bytes. A block is the unit of erase and of fast
  programming. The ~100-write rating is per block.
- Erased flash reads $00. Writing one byte erases and rewrites its 4-byte word; writing
  a whole block erases and rewrites the block in one operation, one wear cycle.
- Whole-block writes to program flash must run from RAM, and the CPU stops while flash
  is written.
- The UBC option byte locks blocks 0 to N-1 against all writes from firmware.
- The 128-byte data EEPROM is 2 blocks of 64 bytes, rated ~100k writes.

Proposed layout (the split is settled once the kernel's real size is known):

| Blocks | Address | Size | Use |
|---|---|---|---|
| 0-1 | $8000-$807F | 128 B | Interrupt vectors (locked) |
| 2-79 | $8080-$93FF | 4.9 KB | Kernel, locked by the UBC option byte; it fills this to within about 20 bytes |
| 80-127 | $9400-$9FFF | 3 KB | Program store: 48 blocks |
| EEPROM | $4000-$4067 | 104 B | The FAT: header, block chain and write counts |
| EEPROM | $4068-$407F | 24 B | Program settings (`EEREAD`/`EEWRITE`) |

## Wear levelling: the FAT in EEPROM

A program may sit in any blocks of the store, in any order. The EEPROM describes them:

| EEPROM | Bytes | Contents |
|---|---|---|
| $4000 | 1 | Format version |
| $4001 | 1 | First block of the program (0-47), or $FF for no program |
| $4002 | 2 | Length of the bytecode in bytes |
| $4004 | 2 | CRC-16 of the bytecode |
| $4006 | 2 | Upload count |
| $4008 | 48 | Chain: for each block, the next block of the program (0-47), $FE for the last one, $FF for free |
| $4038 | 48 | Write counts: how many times each block has been written |
| $4068 | 24 | Free for program settings |

Writing a program (Wren does this over SWIM and reads everything back):

1. Read the FAT. The blocks the new program needs are taken from all 48, least-written
   first. The old program is replaced, so all its blocks are free for the new one: a
   program can use the whole store (3 KB).
2. Write the program's blocks; each is one block write, one wear cycle.
3. Write the new FAT: chain, header and the raised write counts.
4. Read back the blocks and the FAT and compare. If anything differs, do it again.

At start-up the kernel checks the CRC, follows the chain once and builds a RAM table
from program address to physical block, so the VM finds any byte with one lookup.

How far this stretches the ~100 writes (48-block store). Choosing the least-written
blocks keeps all 48 within one write of each other:

| Program size | Blocks | Uploads before any block reaches 100 writes |
|---|---|---|
| 128 B | 2 | about 2,400 |
| 256 B | 4 | about 1,200 |
| 1 KB | 16 | about 300 |
| 3 KB (largest) | 48 | 100 |

Wren shows the write counts after every upload. The EEPROM itself is rated ~100k
writes, so the FAT outlasts the flash many times over.

Uploads arrive two ways, with the same FAT:

- **SWIM (first):** Wren.Link reads the FAT through the ST-LINK, writes the blocks,
  then the FAT, and verifies.
- **From a host:** hold PD2 low while the Thing starts; it shows "Upload". The Reggie
  or a Teensy then sends "WREN", the length, the program and its CRC on b0-b7, strobed
  by PD3 (see "Uploading from a host" in Bytecode.md). The kernel writes each block
  from a routine copied to RAM, to blocks the current program does not use, writes the
  FAT once the CRC checks out and starts the new program. There is no wire back to the
  host: a bad upload shows "Failed" on the OLED and the old program stays.

Kernel updates go over SWIM only and need the UBC lock cleared and set again. Each one
costs a wear cycle on the kernel blocks, so the kernel is developed on the emulator and
the development board and installed on a Thing only a handful of times.

## The VM

- A stack machine with 16-bit values. Each instruction is one byte of opcode, plus one
  or two bytes of operand where needed. Roughly 60 opcodes: constants, load and store of
  globals, locals and array elements, arithmetic and logic, comparisons, jumps, calls
  and returns, and `SYS n`, which calls kernel service n (drawing, timers, pins).
- Speed: about 100-200 thousand operations a second at 16 MHz, far more than a 64x32
  display needs.
- Safety: stack depth, array indexes and jump targets are checked. A fault stops the
  program and shows `ERR code @ address` on the OLED. The kernel itself keeps running,
  so a new program can always be uploaded.

Code density. BASIC lets one statement do a lot, and the bytecode is shaped so that it
costs few bytes:

- Every built-in statement (`HEX`, `BITS`, `TEXT`, `BOX` ...) is one kernel service:
  its arguments, then a 1-byte opcode. The work is in the kernel, written once.
- Constants 0-15, `VALUE`, the pin names and the font names are 1-byte opcodes; other
  constants up to 255 take 2 bytes. The first 16 variables are loaded and stored with
  1-byte opcodes.
- Optional arguments that are left out are filled in by the compiler with a 1-byte
  constant (`PIXEL x,y` means colour 1).
- `IF` and loops use short relative jumps (2 bytes) wherever they reach.

For example, `HEX 21, 2, shown, 2, LARGE` is 7 bytes. The whole "usual views" program
below comes to about 100 bytes: 2 blocks, so about 2,400 uploads before a block
reaches its rating.

RAM budget (1 KB):

| Bytes | Use |
|---|---|
| 256 | Frame buffer (64x32, 1 bit per pixel) |
| 256 | Program variables and arrays, sharing with the block map: the map takes one byte per block the program uses (1-48) and the variables get the rest (255-208) |
| 128 | VM value stack |
| 64 | VM call stack |
| 32 | Event queue |
| 96 | Kernel variables (timers, latches, debounce) |
| 160 | CPU stack and RAM routine for flash writes |

## Events

Interrupts do the time-critical part and put an event in a queue; the VM runs the
matching handler, one at a time, to completion. A handler that is not defined costs
nothing.

| Handler | When it runs |
|---|---|
| `ON START` | Once, after reset |
| `ON LATCH pin [FALLING\|RISING\|BOTH] [WHILE pin LOW\|HIGH]` | An edge on PA1-PA3, PD2 or PD3 (falling unless stated), optionally only while another of those pins is at a level, e.g. `ON LATCH PD3 RISING WHILE PD2 LOW` for a clock and /WE. The interrupt reads b0-b7 and the pins at once; the handler gets b0-b7 as `VALUE`. The host must hold them steady for the 1-2 µs this takes |
| `ON PRESS pin` / `ON RELEASE pin` | A button on PA1-PA3, PD2 or PD3, debounced (20 ms) |
| `ON PINS` | Any of PA1-PA3, PD2 or PD3 changes, debounced (20 ms), and once after `ON START` |
| `ON CHANGE` | b0-b7 differ from their last value (checked every millisecond), and once after `ON START`; `VALUE` holds the byte |
| `ON EVERY n` | Every n milliseconds (up to 4 timers) |

If events arrive faster than the handlers finish they wait in the queue (16 deep);
any lost when it is full are counted and readable as `LOST`.

## Wren BASIC

Structured BASIC: no line numbers, one statement per line, keywords in any case,
`'` starts a comment.

- **Values:** 16-bit signed integers. `DIM name` for a variable, `DIM name(n)` for an
  array of n 16-bit values, `name(0)` to `name(n-1)`, `DIM name(n) AS BYTE` for bytes.
  Arrays are global; `DIM` inside a SUB or handler makes locals. `CONST name = value`.
  Strings appear only as literals in `TEXT`. Numbers: `123`, `&H1F` or `$1F`, `&B101`.
- **Truth:** comparisons give -1 (TRUE) or 0 (FALSE). `NOT` is logical (0 becomes -1,
  anything else 0); `AND`, `OR`, `XOR` work on bits. `ON`/`OFF` are 1/0.
- **Statements:** `name = expr`, `IF ... THEN ... ELSEIF ... ELSE ... END IF` (or on one
  line: `IF c THEN statement [ELSE statement]`),
  `WHILE ... WEND`, `FOR i = a TO b [STEP s] ... NEXT`, `SUB name(params) ... END SUB`,
  `FUNCTION name(params) ... END FUNCTION`, `CALL name(args)` (or just `name args`),
  `RETURN [expr]`, `EXIT FOR/WHILE/SUB/FUNCTION/ON`. `:` separates statements on a line;
  `'` or `REM` starts a comment. `FOR ... STEP` needs a constant step.
- **Operators:** `+ - * / MOD`, `AND OR XOR NOT`, `SHL SHR`, `= <> < <= > >=`.

Built-in services:

| Area | Statements and functions |
|---|---|
| Display | `CLS`, `SHOW` (send the frame buffer to the OLED), `PIXEL x,y[,c]`, `LINE x1,y1,x2,y2[,c]`, `BOX x,y,w,h[,c]`, `FILL x,y,w,h[,c]`, `INVERT x,y,w,h`, `CONTRAST n`, `INVERSE on`, `FLIP on` (upside down, for a Thing mounted the other way up) |
| Text and numbers | `TEXT x,y,"text"[,font]`, `HEX x,y,v,digits[,font]`, `DEC x,y,v[,font]`, `BIN x,y,v,bits[,font]`, `BITS x,y,w,h,v` (eight cells, b7 on the left: filled for a 1 bit, outlined for a 0, as the Thing's own BAR view), `BAR x,y,w,h,v,max` |
| Fonts | `SMALL` (3x5: 16 columns x 5 rows; capitals only: lowercase is drawn as capitals), `NORMAL` (5x7: 10 x 4), `LARGE` (5x7 drawn 2x: 10x14, 5 x 2), `BIG` (5x7 drawn 3x across and 4x down: 4 characters on one line) |
| Inputs | `INPUTS` (b0-b7 as a byte), `PIN(PA1)` and so on (0 or 1), `ANALOG(PD2)` and `ANALOG(PD3)` (0-1023; this turns the pin's pull-up off, so that pin is then an analog input, not a button) |
| Time | `MILLIS` (wraps every 65.5 s), `WAIT ms` |
| Storage | `EEREAD(n)`, `EEWRITE n, v` for the 24 setting bytes, n = 0-23 (only writes when the value changes) |
| Other | `RND(n)`, `ABS(v)`, `MIN(a,b)`, `MAX(a,b)`, `LOST` |

`LARGE` and `BIG` are drawn by scaling the 5x7 font at run time, which saves the 1 KB a
separate 16x32 table would take.

### Examples

The Thing's usual views. As in its own firmware, pulling HEX (PA1) or BAR (PA2) low
turns that part off, and INV (PA3) low inverts the display:

```
' Hex digits and bit cells for b0-b7, chosen by the mode pins
DIM shown

ON CHANGE
  shown = VALUE
  Draw
END ON

ON PINS
  Draw
END ON

SUB Draw
  CLS
  IF PIN(PA1) AND PIN(PA2) THEN       ' both shown
    HEX 21, 2, shown, 2, LARGE
    BITS 4, 21, 56, 9, shown
  ELSEIF PIN(PA1) THEN                ' BAR pulled low: big digits only
    HEX 16, 2, shown, 2, BIG
  ELSEIF PIN(PA2) THEN                ' HEX pulled low: bit cells only
    BITS 0, 8, 64, 16, shown
  END IF
  INVERSE NOT PIN(PA3)
  SHOW
END SUB
```

Michael's two latched rows:

```
' PD3 latches the top row, PD2 the bottom row
DIM top, bottom

ON START
  CLS
  TEXT 0, 0, "PD3 --", NORMAL
  TEXT 0, 16, "PD2 --", NORMAL
  SHOW
END ON

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
  TEXT 0, 0, "PD3", NORMAL
  HEX 24, 0, top, 2, NORMAL
  BITS 0, 9, 64, 6, top
  TEXT 0, 16, "PD2", NORMAL
  HEX 24, 16, bottom, 2, NORMAL
  BITS 0, 25, 64, 6, bottom
  SHOW
END SUB
```

## The Thing's own behaviour

`programs/OledThing.bas` recreates the OLED Thing's firmware in Wren BASIC: the HEX,
BAR and combined views, INV, Dice Roll, and v1.1's PD3 Latch and PD2 Hold. It is about
310 bytes (5 blocks). Its tests check every mode's pixels on the emulated Thing.

## How it is built and tested

- **Kernel** in STM8 assembly under `firmware/kernel/`, assembled by Wren.Asm when Wren
  starts. It is about 4.5 KB and must end below the store at $9400 (a test checks).
  Kept small by: the VM calling each instruction's routine (which ends in a 1-byte
  `RET`), one dispatch table for opcodes $30-$74, the assembler's `RELAX` directive
  (`JP`/`CALL` become 2-byte `JRA`/`CALLR` where the target is near), and the vector
  slots of interrupts that are never enabled holding constant tables.
- **Wren.Script** (C#): the BASIC compiler, the FAT planner (`ProgramStore`), a bytecode
  disassembler, and `ThingSession`, which runs a program on the emulated Thing for the
  debugger: stepping by statement, line breakpoints, globals and locals.
- **Wren** shows the program with the current line, breakpoints, variables, the running
  handler and its `VALUE`, and the store's write counts. The assembly level stays hidden.
- **Wren.Emu** models block programming (which must run from RAM) and the UBC lock, so
  the host upload is tested end to end: tests send programs over the pins, check the
  FAT, the wear spread over many uploads, a bad CRC, and that the kernel is never
  written.
- **Tests** compile BASIC programs, run them on the emulated Thing with the kernel, and
  compare the OLED's pixels; FAT tests upload thousands of programs and check the
  wear spread, and that a failed upload is caught by the read-back.

## Open questions

- **Kernel space.** With the loader and `ANALOG` in, the kernel is about 20 bytes short
  of the store. A further kernel feature needs either more trimming or the store moved
  up by a block or two (each block is 64 bytes of program space, out of 3 KB).

1. **Host upload protocol.** Built as described above; the Reggie side still has to be
   written and tried against it.
