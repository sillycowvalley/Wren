# The Touchstone-86's displays

The Touchstone-86 is an 8086-family chip tester (in the TTLSim repository). It reports on
two OLED Things, DS2 and DS1 side by side, read DS2 then DS1. Each Thing gets one byte from
a 74HCT574 latch on b0-b7: DS2 the high byte, DS1 the low. `programs/OledThing/Touchstone.bas`
shows that byte as words on DS2 and as two big hex digits on DS1.

The display code is the tester's. Its source of truth is TTLSim's
`Data/Circuits/Altoid-86/Documentation/Touchstone86_Display_Code.md` and its test ROM,
`tstrom.asm`. A change to the code is made there first, then here.

## What reaches a Thing

- b7-b0 from the latch, at 5 V HCT levels. There is no strobe and no select. All eight bits
  change together on one latch edge, and the latch holds its byte until the next write.
- VCC and GND. BAR, HEX, INV, PD2, PD3 and the OLED header are not connected.
- The latches are not cleared at power-on: until the CPU's first write they hold a random
  byte, and with a dead CPU they keep it.
- The two Things are not joined, and nothing tells a Thing which display it is.

## The code

DS2 is `st`: the state s and the test t, 1 to E. DS1 is a number that DS2's state says how
to read.

| DS2 | State | DS2 shows | DS1 |
|---|---|---|---|
| 0t | Test t ready | name, READY | 00 |
| At | Test t running | name, RUN | 00; AC: a count every 100 ms; AD: the soak's passes |
| Ct | Test t passed | name, PASS | 00 |
| Ft | Test t failed | name, FAIL | the first failing case, 01-FF |
| Bt | Test t's result | name, RESULT | the result code: BA E1, BB 86 / 30 / EE, BE bb |

| t | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | A | B | C | D | E |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Name | REGS | RAM | STACK | ALU | SHIFT | MULDIV | STRING | BCD | ADDR | TRAP | ID | STATIC | SOAK | CORE |

Any other DS2 byte (a low digit of 0 or F, or a high digit other than 0, A, B, C or F) is
not in the code.

## The program

`CONST ROLE` at its top is set before uploading: 2 for DS2 (HI on the tester's silk), 1 for
DS1 (LO). It is 498 bytes, 8 blocks.

- **DS2:** the test's name on the top half and the state on the bottom, centred. Words of up
  to 5 letters are LARGE; MULDIV, STRING, STATIC and RESULT are NORMAL. A byte not in the
  code shows as two big hex digits.
- **DS1:** two big hex digits (as a stock Thing with BAR low), nothing else.
- **Before the first change:** until DS2's byte changes after power-up, it shows the byte as
  `?` and two big hex digits.

## The requirements, and how they are met

| | Requirement | How |
|---|---|---|
| R1 | The stock footprint, 5 V, HCT levels on b0-b7 | It is a stock OLED Thing. |
| R2 | No configuration pins | Nothing reads BAR, HEX, INV, PD2 or PD3. |
| R3 | A role per Thing | `CONST ROLE`, set when each Thing's program is uploaded. |
| R4 | Show only a settled byte | `ON CHANGE` (b0-b7 checked every millisecond) redraws only when a second read agrees with `VALUE`. A read across the latch edge differs from the settled byte, so `ON CHANGE` comes again a millisecond later. |
| R5 | DS2 in words; a byte outside the code as hex | As above. |
| R6 | No words for the power-up byte | Words only once the byte has changed since power-up; until then `?xx`. A power-up byte equal to the first write keeps the mark till the next change. A Thing that is reset, not powered off, also shows `?xx` until the next change. |
| R7 | DS1 as two hex digits only | As above. |
| R8 | Each new byte shown within 50 ms | The tests check each step 50 ms after the byte changes. |
| R9 | The stock behaviour stays a choice | Upload `OledThing.bas`, or write Digicool's firmware with **Write image…**. |

`TouchstoneTests` runs the tester's example sequence and every test name on DS2, bytes
outside the code, the power-up mark, and DS1 following the static test's count. Open: try
it on the tester.
