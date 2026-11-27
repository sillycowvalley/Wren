# The Touchstone-86's displays

The Touchstone-86 is an 8086-family chip tester (in the TTLSim repository). It reports on
two OLED Things, DS2 and DS1 side by side, read DS2 then DS1. Each Thing gets one byte from
a 74HCT574 latch on b0-b7: DS2 the high byte, DS1 the low. `programs/OledThing/Touchstone.bas`
shows that byte as words on DS2, and on DS1 as two big hex digits or, after a failure, the
failure itself.

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

**A failure's frame.** Before a failure's `Ft nn`, the ROM sends DS1 a frame, with `Ft` on
DS2 throughout: `FF`, then a byte every 5 ms whose high digit is its place in the frame, 0
to F, and whose low digit is the frame's digit there. The places count up, so each byte is a
change. The digits:

| Place | Digit |
|---|---|
| 0 | the test t |
| 1, 2 | the case nn |
| 3 | the kind: 0 no detail, and the frame ends here; 1 a RAM word, 2 a RAM byte; 3, 4 or 5 a table row whose AX, DX or checked flags were wrong |
| 4-7 | the RAM address, or the row's AX in |
| 8-B | the value wanted |
| C-F | the value got (read again at the address, for RAM; the flags masked to those checked) |

Then `Ft nn` as ever, so a stock Thing ends on the same two digits.
## The program

`CONST ROLE` at its top is set before uploading: 2 for DS2 (HI on the tester's silk), 1 for
DS1 (LO). It is 1,053 bytes, 17 blocks.

- **DS2:** the test's name on the top half and the state on the bottom, centred. Words of up
  to 5 letters are LARGE; MULDIV, STRING, STATIC and RESULT are NORMAL. A byte not in the
  code shows as two big hex digits.
- **DS1:** two big hex digits (as a stock Thing with BAR low), drawn once the byte has held
  for 20 ms, so a frame is never drawn as it arrives. After a frame and its `nn`, the
  failure, until the byte next changes:
  - a RAM word: `@` and the address, the bank the wrong bits are in (`EVN`, `ODD` or `BTH`),
    `WANT`, `READ` and their `XOR`; a RAM byte the same, two digits, the bank by the address;
  - a table row: `ROW` and the row, `AX`, `DX` or `FL`, then `IN` (the row's AX in), `WANT`
    and `GOT`;
  - a case with no detail: `CASE` and the case in big digits.
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
| R7 | DS1 as two hex digits, and a failure's frame as the failure | As above. A frame's place 0 starts it; each byte after must come within 20 ms with the next place, or the frame is dropped and the bytes are only hex. The static test's count, every 100 ms, can never make one. |
| R8 | Each new byte shown within 50 ms | DS2 draws on the change; DS1 20-25 ms after it. The tests check each step 50 ms after the byte changes, and the failure within 50 ms of `Ft nn`. |
| R9 | The stock behaviour stays a choice | Upload `OledThing.bas`, or write Digicool's firmware with **Write image…**. |

## The testbenches

`programs/OledThing/Testbench` holds the tester's testbenches (`docs/Testbench.md`),
`Touchstone_<scenario>_DS1.bench` and `_DS2.bench`: what each Thing sees, from power-on,
as TTLSim's test ROM drives the latches on its 8086 core at 4 MHz. TTLSim writes them and
holds them to the ROM (`TouchstoneRomTests`, `Data/Circuits/Altoid-86/Firmware/Testbench`);
they are copied here unchanged. Open one with **Debug, Open Testbench…**, with `ROLE` set
for the Thing it names, and run.

| Scenario | What happens |
|---|---|
| `tour` | The menu, test 01 passing, identity (`BB 86`), the static test counting for a second, the core's questions (`BE 07`) |
| `ram_fault` | D5 of the odd RAM bank stuck low: `F2 02`, a RAM word at 0000h, FFFFh wanted, DFFFh read |
| `alu_fault` | ADC decoded as ADD: `F4 0C`, row 0C's AX, 0001h wanted, 0000h got |
| `mul_fault` | MUL decoded as IMUL: `F6 07`, row 07's DX, FFFEh wanted, 0000h got |
| `case_fault` | CS compared with F100h: `F1 08`, a case with no detail |
| `soak_fault` | Run until failure: a pass, then the stuck bit stops the soak at `F2 02` |
| `no_cpu` | Nothing written: each latch keeps its power-up byte |

`TouchstoneTests` runs the tester's example sequence and every test name on DS2, bytes
outside the code, the power-up mark, DS1 following the static test's count, and every
testbench through both roles: DS2's last words, DS1's failure screens, and DS1 drawing
nothing in the middle of a frame. Open: try it on the tester.
