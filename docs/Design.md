# Wren firmware design: kernel, language and program storage

**Status: approved and built: the kernel, the compiler and the debugger run on the emulated Thing.**

Decided so far: programs are **bytecode** run by a virtual machine (VM); the language is
**BASIC-flavoured**; programs react to the outside world through **event handlers**; a
Thing holds **one program**; the EEPROM is used as below.

## The idea in one paragraph

Each Thing gets a **kernel** once, over SWIM: the VM, timers, the program loading and its
board's own parts (the OLED Thing's display driver, graphics and fonts; the LED Thing's
LED scan). The kernel sits in the first part of flash, locked by the
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
- The 128-byte data EEPROM is 2 blocks of 64 bytes, rated ~100k writes. Some STM8S003s
  have the S103's 640 bytes ($4000-$427F); Wren checks each Thing as it installs the
  kernel: it writes and reads back one block at $4080, and records the answer in the
  kernel's info block. EEPROM is non-volatile, so no power cycle is needed.

| Blocks | Address | Size | Use |
|---|---|---|---|
| 0-1 | $8000-$807F | 128 B | Interrupt vectors (locked) |
| 2-73 | $8080-$927F | 4.6 KB | Kernel, locked by the UBC option byte; 19 bytes free before the info block |
| 73 | $9270-$927F | 16 B | The kernel info block (Bytecode.md), the end of the kernel's last block |
| 74-127 | $9280-$9FFF | 3.4 KB | Program store: 54 blocks (3,456 bytes) |
| EEPROM | $4000-$4073 | 116 B | The FAT: header, block chain and write counts |
| EEPROM | $4074-$407F | 12 B | Program settings (`EEREAD`/`EEWRITE`) on a 128-byte EEPROM |
| EEPROM | $4080-$427F | 512 B | Program settings on a 640-byte EEPROM instead |

## Wear levelling: the FAT in EEPROM

A program may sit in any blocks of the store, in any order. The EEPROM describes them:

| EEPROM | Bytes | Contents |
|---|---|---|
| $4000 | 1 | Format version: 2 |
| $4001 | 1 | First block of the program (0-53), or $FF for no program |
| $4002 | 2 | Length of the bytecode in bytes |
| $4004 | 2 | CRC-16 of the bytecode |
| $4006 | 2 | Upload count |
| $4008 | 54 | Chain: for each block, the next block of the program (0-53), $FE for the last one, $FF for free |
| $403E | 54 | Write counts: how many times each block has been written |
| $4074 | 12 | Program settings, on a 128-byte EEPROM |

Writing a program (Wren does this over SWIM and reads everything back):

1. Read the FAT and the whole store. A block of the new program that some store block
   already holds, byte for byte (the current program's or one left by an earlier
   program), is chained as it is and not written. The other blocks are taken from those
   left, least-written first. The old program is replaced, so all its blocks are free for
   the new one: a program can use the whole store (3,456 bytes).
2. Write the program's new blocks; each is one block write, one wear cycle. On
   `OledThing.bas` (8 blocks), changing a number or adding a statement near the end
   writes 1 block, and going back to a program uploaded earlier writes none; an edit near
   the start moves all the code after it, so every block is written.
3. Write the new FAT: chain, header and the raised write counts.
4. Read back the blocks and the FAT and compare. If anything differs, do it again.

At start-up the kernel checks the CRC, follows the chain once and builds a RAM table
from program address to physical block, so the VM finds any byte with one lookup.

How far this stretches the ~100 writes (54-block store), for programs that differ in
every block (reused blocks cost nothing, so real use goes further). Choosing the
least-written blocks keeps all 54 within one write of each other:

| Program size | Blocks | Uploads before any block reaches 100 writes |
|---|---|---|
| 128 B | 2 | about 2,700 |
| 256 B | 4 | about 1,350 |
| 1 KB | 16 | about 335 |
| 3,456 B (largest) | 54 | 100 |

Wren shows the write counts after every upload. The EEPROM itself is rated ~100k
writes, so the FAT outlasts the flash many times over.

Uploads go over SWIM: Wren.Link reads the FAT and the store through the programmer,
writes the new blocks, then the FAT, and verifies. It uploads only to a kernel whose info block says this
layout, and skips the upload when the Thing already holds the program.

Kernel updates go over SWIM and need the UBC lock cleared and set again. Each one
costs a wear cycle on the kernel blocks, so the kernel is developed on the emulator and
the development board and installed on a Thing only a handful of times. Before writing,
Install kernel checks the board (Digicool's firmware, a Wren kernel's info block, or an
OLED answering on I2C) and installs that board's kernel, refusing a board it cannot tell;
it checks the EEPROM's
size and records it in the kernel's info block. A Thing whose FAT is version 1 (the
47-block store at $9440) keeps its program: old block n is new block n + 7 at the same
address, so the table is rewritten with the program and its write counts in place.

## The VM

- A stack machine with 16-bit values. Each instruction is one byte of opcode, plus one
  or two bytes of operand where needed. Roughly 60 opcodes: constants, load and store of
  globals, locals and array elements, arithmetic and logic, comparisons, jumps, calls
  and returns, and `SYS n`, which calls kernel service n (drawing, timers, pins).
- Speed: about 100-200 thousand operations a second at 16 MHz, far more than a 64x32
  display needs.
- Safety: stack depth, array indexes and jump targets are checked. A fault stops the
  program and shows `ERR code @ address` on the OLED (on the LED Thing, b7 lit with the
  code in b0-b6). The kernel itself keeps running,
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
below comes to about 100 bytes: 2 blocks, so about 2,700 uploads before a block
reaches its rating.

RAM budget (1 KB):

| Bytes | Use |
|---|---|
| 256 | Frame buffer (64x32, 1 bit per pixel) |
| 256 | Program variables and arrays, sharing with the block map: the map takes one byte per block the program uses (1-54) and the variables get the rest (255-202) |
| 128 | VM value stack |
| 64 | VM call stack |
| 16 | Event queue (8 slots of event and value; 7 can wait) |
| 96 | Kernel variables (timers, latches, debounce) |
| 160 | CPU stack and RAM routine for flash writes |

## Events

Interrupts do the time-critical part and put an event in a queue; the VM runs the
matching handler, one at a time, to completion. A handler that is not defined costs
nothing.

| Handler | When it runs |
|---|---|
| `ON START` | Once, after reset |
| `ON LATCH pin [FALLING\|RISING\|BOTH] [WHILE pin LOW\|HIGH]` | An edge on PA1-PA3, PD2 or PD3 (falling unless stated), optionally only while another of those pins is at a level, e.g. `ON LATCH PD3 RISING WHILE PD2 LOW` for a clock and /WE. The interrupt reads b0-b7 first; the handler gets them as `VALUE`. The host must hold b0-b7 for 3 µs after the edge (2.7 µs worst case from PM0044's cycle counts: an instruction finishing and the interrupt entry), and about 3 µs more when the program has `EVERY`, `CHANGE`, `PINS`, `PRESS` or `RELEASE` handlers: the 1 ms tick queues their events with interrupts off (4.6 µs in the emulator, which counts cycles high). A LATCH alone on port D (PD2 or PD3, one edge, no `WHILE`) is caught from a strobe of any length, since port D's interrupt is set to that edge and is the edge itself. Any other LATCH reads the pins back to find which pin moved, and which way, so its strobe must last about 5 µs. The port interrupts outrank the 1 ms tick, so a LATCH never waits for it; edges closer together than one interrupt's run (about 7 µs) can merge into one |
| `ON PRESS pin` / `ON RELEASE pin` | A button on PA1-PA3, PD2 or PD3, debounced (20 ms) |
| `ON PINS` | Any of PA1-PA3, PD2 or PD3 changes, debounced (20 ms), and once after `ON START` |
| `ON CHANGE` | b0-b7 differ from their last value (checked every millisecond), and once after `ON START`; `VALUE` holds the byte |
| `ON EVERY n` | Every n milliseconds (up to 4 timers) |

If events arrive faster than the handlers finish they wait in the queue (up to 7; a new
`CHANGE` or `PINS` replaces one of its kind still waiting); any lost when it is full are
counted and readable as `LOST`.

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
| Fonts | `SMALL` (3x5: 16 columns x 5 rows; capitals only: lowercase is drawn as capitals), `NORMAL` (5x7: 10 x 4), `LARGE` (5x7 drawn 2x: 10x14, 5 x 2), `BIG` (5x7 drawn 3x across and 4x down: 15x28 in an 18x32 cell, 3 characters on one line) |
| Inputs | `INPUTS` (b0-b7 as a byte), `PIN(PA1)` and so on (0 or 1), `ANALOG(PD2)` and `ANALOG(PD3)` (0-1023; this turns the pin's pull-up off, so that pin is then an analog input, not a button) |
| Time | `MILLIS` (wraps every 65.5 s), `WAIT ms` |
| Storage | `EEREAD(n)`, `EEWRITE n, v` for the setting bytes: 12 on a 128-byte EEPROM (n = 0-11), 512 on a 640-byte one (only writes when the value changes; beyond the last, EEWRITE does nothing and EEREAD gives 0) |
| Pins | `PULLUPS mask`: bit n pulls bn up (1) or leaves it floating (0); all are pulled up at start (`PULLUPS &HFF`). The chip has no pull-downs |
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

`programs/OledThing/OledThing.bas` recreates the OLED Thing's firmware in Wren BASIC: the HEX,
BAR and combined views, INV, Dice Roll, and v1.1's PD3 Latch and PD2 Hold, laid out and
timed as Digicool's firmware does them; only the hex digits are in Wren's font. It is 502
bytes (8 blocks). Its tests compare its pixels with what Digicool's firmware showed in
the emulator: its bit cells drawn by the same rule, and its die faces as hashes (the faces
are Digicool's artwork, so only their hashes are in the tests).

## Digicool's firmware on this machine

Wren carries none of Digicool's firmware. Each time Tools → Firmware opens, `DigicoolFirmware`
(Wren.Link) lists the Firmware folders of Digicool's two GitHub repositories
(`DigicoolThings/8-bit_OLED_Thing`, `DigicoolThings/8-bit_LED_Thing`) through GitHub's public
API and fetches every `.s19` file that is missing from `%LocalAppData%\Wren\firmware\digicool`
or differs from Digicool's, compared by git's blob hash, which the listing gives. A file is
kept only if its hash matches the listing, it came from `raw.githubusercontent.com/DigicoolThings`,
and its S-records lie wholly in the S003's flash. Offline, the check says so in the log and
what was fetched before stays. **Write image…** opens that folder, or the folder an image
was last chosen from.

The board detection knows Digicool's images by their first 8 bytes (the reset and TRAP
vectors): the three published (OLED Thing v1.0 and v1.1, LED Thing) are built in, and each
image fetched or found in the folder is added, its board taken from Digicool's file name.

## How it is built and tested

- **One kernel source, two boards**: `Kernel.asm` is assembled with `BOARD` set to 1 (the
  OLED Thing and the development board) or 2 (the LED Thing, `docs/LedThing.md`), and
  `IF BOARD = BOARD_OLED ... ELSE ... ENDIF` keeps each board's parts. Adding the LED
  build left the OLED kernel's bytes unchanged, so it costs the OLED Things no flash writes.

- **Kernel** in STM8 assembly under `firmware/kernel/`, assembled by Wren.Asm when Wren
  starts. It is about 4.6 KB and must end below its info block at $9270 (the assembler
refuses an overlap; a test checks the store at $9280).
  Kept small by: the VM calling each instruction's routine (which ends in a 1-byte
  `RET`), one dispatch table for opcodes $30-$75, the assembler's `RELAX` directive
  (`JP`/`CALL` become 2-byte `JRA`/`CALLR` where the target is near), and the vector
  slots of interrupts that are never enabled holding constant tables.
- **Wren.Script** (C#): the BASIC compiler, the FAT planner (`ProgramStore`), a bytecode
  disassembler, and `ThingSession`, which runs a program on the emulated Thing for the
  debugger: stepping by statement, line breakpoints, globals and locals.
- **Wren** shows the program with the current line, breakpoints, variables, the running
  handler and its `VALUE`, and the store's write counts. The assembly level stays hidden.
- **Wren.Link**'s `ThingDebugger` does the same on a real Thing over SWIM, with the chip's
  debug module (UM0470): data-read breakpoints on a line's first bytecode, an instruction
  breakpoint on `vm_next` for stepping, and RAM read while the CPU is stalled. `ProgramView`
  reads a program's state from memory for both.
- **Wren.Emu** models block programming and the UBC lock, so uploads and kernel installs
  through the simulated programmers are tested end to end, including an old Thing's
  program surviving the kernel update.
- **Tests** compile BASIC programs, run them on the emulated Thing with the kernel, and
  compare the OLED's pixels; FAT tests upload thousands of programs and check the
  wear spread, and that a failed upload is caught by the read-back.

## Open questions

- **Kernel space.** 19 bytes are free below the info block at $9270. A larger kernel
  feature needs more trimming or the store moved up another block (64 bytes of program
  space each).
