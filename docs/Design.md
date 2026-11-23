# Wren firmware design: kernel, language and program storage

**Status: draft for Michael's approval. No firmware code until it is approved.**

Decided so far: programs are **bytecode** run by a virtual machine (VM); the language is
**BASIC-flavoured**; programs react to the outside world through **event handlers**.

## The idea in one paragraph

Each Thing gets a **kernel** once, over SWIM: the VM, the OLED driver, graphics, fonts,
timers and the loader. The kernel sits in the first part of flash, locked by the
user boot code (UBC) option byte so nothing can erase it except a SWIM programmer.
Programs are written in Wren BASIC, compiled by Wren on the PC, and uploaded into the
rest of flash, the **program store**, as many times as needed. Uploads go round the
store like a ring, so the ~100-write limit is spread over all its blocks instead of
spent on one.

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
| 2-71 | $8080-$91FF | 4.4 KB | Kernel (locked) |
| 72-127 | $9200-$9FFF | 3.5 KB | Program store: 56 blocks |
| EEPROM 0-1 | $4000-$407F | 128 B | Settings and program data (`EEREAD`/`EEWRITE`) |

## Wear levelling: the program ring

Every upload is a **program image**: a 64-byte-aligned run of blocks. Its first 8
bytes are a header:

| Bytes | Field |
|---|---|
| 2 | Magic "WB" |
| 2 | Sequence number (one more than the newest program in the store) |
| 2 | Length of the bytecode in bytes |
| 2 | CRC-16 of the bytecode |

Writing a program:

1. Find the newest valid program (highest sequence with a good CRC). The new one goes
   in the first block after its end. If it does not fit before the end of the store, it
   goes at the start of the store instead.
2. Write the program's blocks in order, the **header block last**. Each block is one
   block write: one wear cycle.
3. If power fails part way, the new header is missing or its CRC is wrong, so the
   previous program still runs. The new program never overlaps the current one.

At start-up the kernel scans the store's block starts for headers and runs the newest
valid program. Older programs are simply overwritten as the ring comes round.

How far this stretches the ~100 writes (56-block store):

| Program size | Blocks | Uploads before any block reaches 100 writes |
|---|---|---|
| 256 B | 4 | about 1,400 |
| 512 B | 8 | about 700 |
| 1 KB | 16 | about 350 |
| 1.75 KB (largest) | 28 | about 200 |

A program can be at most half the store, so a new one always fits beside the current
one. The sequence number also tells Wren how worn the store is: Wren shows an estimate
after every upload.

Uploads arrive two ways, into the same ring:

- **SWIM (first):** Wren.Link reads the store's headers through the ST-LINK, picks the
  place as above and writes the blocks.
- **From a host (later):** the Reggie or a Teensy sends the program on b0-b7 with PD3
  as the strobe; the kernel's loader writes it (from a routine copied to RAM).

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

RAM budget (1 KB):

| Bytes | Use |
|---|---|
| 256 | Frame buffer (64x32, 1 bit per pixel) |
| 256 | Program variables and arrays |
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
| `ON LATCH pin` | A falling edge on PA1-PA3 or PD2-PD3. The interrupt copies b0-b7 at once; the handler gets it as `VALUE` |
| `ON PRESS pin` / `ON RELEASE pin` | A button on PA1-PA3, PD2 or PD3, debounced (20 ms) |
| `ON CHANGE` | b0-b7 differ from their last value (checked every millisecond), and once after `ON START`; `VALUE` holds the byte |
| `ON EVERY n` | Every n milliseconds (up to 4 timers) |

If events arrive faster than the handlers finish they wait in the queue (16 deep);
any lost when it is full are counted and readable as `LOST`.

## Wren BASIC

Structured BASIC: no line numbers, one statement per line, keywords in any case,
`'` starts a comment.

- **Values:** 16-bit signed integers. `DIM name` for a variable, `DIM name(n)` for an
  array of 16-bit values, `DIM name(n) AS BYTE` for bytes. `CONST name = value`.
  Strings appear only as literals in `TEXT`.
- **Statements:** `name = expr`, `IF ... THEN ... ELSEIF ... ELSE ... END IF`,
  `WHILE ... WEND`, `FOR i = a TO b [STEP s] ... NEXT`, `SUB name(params) ... END SUB`,
  `FUNCTION name(params) ... END FUNCTION`, `CALL name(args)` (or just `name args`),
  `RETURN [expr]`, `EXIT FOR/WHILE/SUB`.
- **Operators:** `+ - * / MOD`, `AND OR XOR NOT`, `SHL SHR`, `= <> < <= > >=`.

Built-in services:

| Area | Statements and functions |
|---|---|
| Display | `CLS`, `SHOW` (send the frame buffer to the OLED), `PIXEL x,y[,c]`, `LINE x1,y1,x2,y2[,c]`, `BOX x,y,w,h[,c]`, `FILL x,y,w,h[,c]`, `INVERT x,y,w,h`, `CONTRAST n`, `INVERSE on` |
| Text and numbers | `TEXT x,y,"text"[,font]`, `HEX x,y,v,digits[,font]`, `DEC x,y,v[,font]`, `BIN x,y,v,bits[,font]`, `BITS x,y,w,h,v` (eight cells, b7 on the left: filled for a 1 bit, outlined for a 0, as the Thing's own BAR view), `BAR x,y,w,h,v,max` |
| Fonts | `SMALL` (3x5: 16 columns x 5 rows), `NORMAL` (5x7: 10 x 4), `LARGE` (5x7 drawn 2x: 10x14, 5 x 2), `BIG` (5x7 drawn 3x across and 4x down: 4 characters on one line) |
| Inputs | `INPUTS` (b0-b7 as a byte), `PIN(PA1)` and so on (0 or 1), `ANALOG(PD2)` and `ANALOG(PD3)` (0-1023) |
| Time | `MILLIS` (wraps every 65.5 s), `WAIT ms` |
| Storage | `EEREAD(addr)`, `EEWRITE addr, v` (only writes when the value changes) |
| Other | `RND(n)`, `ABS(v)`, `MIN(a,b)`, `MAX(a,b)`, `LOST` |

`LARGE` and `BIG` are drawn by scaling the 5x7 font at run time, which saves the 1 KB a
separate 16x32 table would take.

### Examples

The Thing's usual job, laid out like its own firmware with HEX and BAR on:

```
' Two hex digits for b0-b7, with the bits underneath
ON CHANGE
  CLS
  HEX 21, 2, VALUE, 2, LARGE
  BITS 4, 21, 56, 9, VALUE
  SHOW
END ON
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

## How it is built and tested

- **Kernel** in STM8 assembly under `firmware/kernel/`, assembled by Wren.Asm.
- **Wren.Script** (C#): the BASIC compiler and a bytecode disassembler, with its own
  tests.
- **Wren.Emu** gains block programming, the UBC lock and wear counted per 64-byte
  block, so the ring can be tested for thousands of uploads in the emulator.
- **Tests** compile BASIC programs, run them on the emulated Thing with the kernel, and
  compare the OLED's pixels; ring tests upload thousands of programs and check the
  wear spread and power-failure recovery.

## Open questions

1. **Keyword names and spelling** of the services above: any to rename before the
   compiler is written?
2. **Several programs at once:** keep only the newest, or let the store hold a few
   named programs chosen from a menu on the mode buttons?
3. **Host upload protocol** over b0-b7 and PD3: settle when the Reggie side is built.
4. **The S103's extra EEPROM** (and possibly the S003's): if the test shows 640 bytes,
   use the extra for program data?
