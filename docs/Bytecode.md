# Wren bytecode

The format shared by the compiler (Wren.Script) and the kernel's VM
(`firmware/kernel/Vm.inc`). Values are 16-bit; words in the code are high byte first.
Truth: comparisons give -1 (true) or 0 (false); `IF` and `WHILE` treat any non-zero
value as true. `NOT` is logical (0 becomes -1, anything else 0).

## Program image

The program's bytes, at program address 0 upwards. The FAT maps each 64 bytes to a
flash block.

| Offset | Bytes | Contents |
|---|---|---|
| 0 | 1 | Format version: 1 |
| 1 | 1 | Bytes of variable RAM the program uses (globals and arrays). Variables and the block map share 256 bytes, so this is at most 256 minus the program's blocks |
| 2 | 1 | Number of handlers, N |
| 3 | 3 each | N handlers: event number, then code address. `LATCH` handlers have 1 more byte, the LATCH settings; `EVERY` handlers have 2 more: the interval in ms |
| ... | | Code |

LATCH settings: bit 0 falling edge, bit 1 rising edge, bit 2 a qualifier is used,
bit 3 the qualifier's level (1 high), bits 4-6 the qualifier pin.

Event numbers: 0 START, 1 CHANGE, 2 PINS, 3-7 LATCH, 8-12 PRESS, 13-17 RELEASE
(pins PA1, PA2, PA3, PD2, PD3 in that order), 18-21 EVERY (first to fourth).

## The FAT (EEPROM)

The program store is the last 48 blocks of flash, $9400-$9FFF. Block n is at
$9400 + 64 n.

| Address | Bytes | Contents |
|---|---|---|
| $4000 | 1 | FAT version: 1 |
| $4001 | 1 | First block of the program (0-47), $FF for none |
| $4002 | 2 | Program length in bytes |
| $4004 | 2 | CRC-16/CCITT (start $FFFF, polynomial $1021) of the program |
| $4006 | 2 | Upload count |
| $4008 | 48 | Next block for each block (0-47), $FE last, $FF free |
| $4038 | 48 | Times each block has been written |
| $4068 | 24 | Program settings (`EEREAD`, `EEWRITE`, n = 0-23) |

## Opcodes

Stack effects are written before -- after; the top of the stack is on the right.

| Code | Name | Operands | Effect |
|---|---|---|---|
| $00-$0F | PUSHS n | | -- n |
| $10-$1F | LOADG n | | -- global n (n < 16) |
| $20-$2F | STOREG n | | v -- |
| $30 | PUSHB | b | -- b (0-255) |
| $31 | PUSHN | b | -- b-256 (-256 to -1) |
| $32 | PUSHW | w | -- w |
| $33 | VALUE | | -- the event's value |
| $34 | LOADGB | i | -- global i |
| $35 | STOREGB | i | v -- |
| $36 | LOADL | i | -- parameter or local i |
| $37 | STOREL | i | v -- |
| $38 | LOADAW | base, n | i -- word array element (i < n) |
| $39 | STOREAW | base, n | i v -- |
| $3A | LOADAB | base, n | i -- byte array element |
| $3B | STOREAB | base, n | i v -- |
| $3C-$47 | ADD SUB MUL DIV MOD AND OR XOR NOT NEG SHL SHR | | a b -- r (NOT, NEG: a -- r). DIV and MOD truncate toward 0; SHR is logical |
| $48-$4D | EQ NE LT LE GT GE | | a b -- -1 or 0 (signed) |
| $4E | DROP | | v -- |
| $4F-$51 | JMP JZ JNZ | rel8 | jump relative to the next instruction; JZ/JNZ pop |
| $52-$54 | JMPW JZW JNZW | addr16 | jump to a program address |
| $55 | CALL | addr16 | call a SUB or FUNCTION |
| $56 | ENTER | params, locals | start of a SUB or FUNCTION |
| $57 | RET | | end of a SUB or handler |
| $58 | RETV | | v -- : return from a FUNCTION with v |
| $59 | CLS | | -- |
| $5A | SHOW | | -- |
| $5B | PIXEL | | x y c -- |
| $5C | LINE | | x1 y1 x2 y2 c -- |
| $5D | BOX | | x y w h c -- |
| $5E | FILL | | x y w h c -- |
| $5F | INVERT | | x y w h -- |
| $60 | CONTRAST | | n -- |
| $61 | INVERSE | | f -- |
| $62 | TEXT | length, bytes | x y font -- |
| $63 | HEX | | x y v digits font -- |
| $64 | DEC | | x y v font -- |
| $65 | BIN | | x y v bits font -- |
| $66 | BITS | | x y w h v -- |
| $67 | BAR | | x y w h v max -- |
| $68 | WAIT | | ms -- |
| $69 | EEWRITE | | n v -- |
| $6A | INPUTS | | -- b0-b7 |
| $6B | PIN | | p -- 1 or 0 |
| $6C | ANALOG | | p -- 0-1023 |
| $6D | MILLIS | | -- ms |
| $6E | EEREAD | | n -- v |
| $6F | RND | | n -- 0 to n-1 |
| $70 | ABS | | v -- r |
| $71 | MIN | | a b -- r |
| $72 | MAX | | a b -- r |
| $73 | LOST | | -- events lost |
| $74 | FLIP | | f -- (non-zero: the picture upside down) |

Opcodes from $30 are dispatched through one table in the kernel, so they stay
contiguous: a new opcode goes on the end.

Colours: 0 clear, 1 set, 2 invert. Pins: PA1 0, PA2 1, PA3 2, PD2 3, PD3 4.
Fonts: SMALL 0, NORMAL 1, LARGE 2, BIG 3.

## Calls

`CALL` saves the return address and the frame pointer on the VM's call stack.
`ENTER p, l` makes the frame: the p arguments already pushed become parameters 0 to p-1
and l locals (set to 0) follow. `RET` and `RETV` remove the frame. A `RET` with an empty
call stack ends the event handler.

## Faults

A fault stops the program and shows `ERR n` and the program address on the OLED:
1 value stack full, 2 value stack empty, 3 call stack full, 4 array index,
5 division by zero, 6 bad opcode, 7 jump outside the program.
