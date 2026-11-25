# The 8-bit OLED Thing v1.0

Digicool Things' board, from its schematic (Rev 1.0). Wren's model is `Wren.Emu.OledThing`.

## Parts

- **U1:** STM8S003F3U (UFQFPN20). 8 KB flash (~100 writes), 128 B EEPROM at
  $4000-$407F (~100k writes), 1 KB RAM. No crystal: it runs on the 16 MHz HSI.
- **U2:** 0.49" 64x32 OLED with an SSD1315, on I2C at address $3C. The module carries
  the I2C pull-ups.
- **C1** 680 nF on VCAP, **C2** 100 nF on NRST, **C3** 100 nF on VDD.
- Power: VCC 2.95-5.5 V. The SWIM header has no power pin.

## Pins

| Signal | MCU pin | Where | Notes |
|---|---|---|---|
| b0 | PC7 (14) | J1 pin 8 | Floating input, ~1 µA |
| b1 | PC6 (13) | J1 pin 7 | |
| b2 | PC5 (12) | J1 pin 6 | |
| b3 | PC4 (11) | J1 pin 5 | AIN2 |
| b4 | PC3 (10) | J1 pin 4 | |
| b5 | PD6 (20) | J1 pin 3 | AIN6 |
| b6 | PD5 (19) | J1 pin 2 | AIN5 |
| b7 | PD4 (18) | J1 pin 1 | |
| HEX | PA1 (2) | J4 pin 1 (pin 2 GND) | Solder jumper JP2 to GND |
| BAR | PA2 (3) | J3 pin 2 (pin 1 VCC) | |
| INV | PA3 (7) | J5 pin 2 | Solder jumper JP1 to GND |
| PD2 | PD2 (16) | J5 pin 3 | AIN3 |
| PD3 | PD3 (17) | J5 pin 4 | AIN4 |
| GND | | J5 pin 1 | |
| SCL | PB4 (9) | OLED pin 3 | True open-drain |
| SDA | PB5 (8) | OLED pin 4 | True open-drain |
| SWIM | PD1 (15) | J2 pin 2 | |
| NRST | NRST (1) | J2 pin 3 | |
| GND | | J2 pin 1 | |

Unused b inputs need an external pull-up or pull-down, or a tie to GND.

## Digicool's firmware

Wren does not carry it: the Firmware window fetches it from the Firmware folder of
Digicool's GitHub repository into `%LocalAppData%\Wren\firmware\digicool`
(`docs/Design.md`, "Digicool's firmware on this machine"). It is compiled C and runs the CPU at 8 MHz. One main loop
reads everything raw, about every 37 µs, with no debounce; the only interrupt is PD3's.

- **Views** (HEX PA1, BAR PA2): both high, two hex digits (about 12 x 18 pixels, cells at
  x 15 and 34) above eight bit cells 7 x 8 at x = 8i, y 24-31, b7 on the left (filled for
  1, outlined for 0); BAR low, two big digits (about 17 x 24, cells at x 6 and 33); HEX
  low, the bit cells 7 x 24 at y 4-27. A change redraws in place; a mode-pin change
  clears first.
- **INV (PA3) low** inverts the panel; the rightmost pixel column stays dark.
- **Inputs**: b0-b7 float in these views (pull-ups only in Dice); HEX, BAR, INV, PD2 and
  PD3 have pull-ups.
- **Dice Roll** (HEX and BAR low): entering shows a 6. Any b input low rolls the die (a
  level, not an edge): a new face every ~115 ms while held, then after release 4 faces
  ~117 ms apart, 3 ~226 ms apart, a blank, and the result ~1.53 s after release. A face
  never repeats twice in a row. The faces are a 32 x 32 rounded box at x 16-47 with 4 x 4
  pips; three runs top right to bottom left. The random source is a 16-bit xorshift
  stepped every loop pass.
- **PD3 Latch**: each falling edge latches b0-b7 (read within a few µs); only a reset
  ends it. Views and INV still redraw with the latched value. Leaving Dice replaces the
  latched value with the inputs read last in Dice.
- **PD2 Hold**: while low, the display keeps its value; release shows the live inputs.
  It has no effect in Dice; a PD3 edge while held still latches.
- No timers, watchdog or EEPROM use. A missing OLED hangs it (its I2C waits never time
  out).

## Programming

ST-LINK V2 to J2: RST to NRST, SWIM to SWIM, GND to GND, and the ST-LINK's 3.3 V or
5 V pin to the Thing's VCC (top-right corner).
The sheet: [Infographics/StLink_Programmer_Wiring.png](Infographics/StLink_Programmer_Wiring.png);
a Pico instead: [Infographics/Pico_Programmer_Wiring.png](Infographics/Pico_Programmer_Wiring.png).
In Wren, both open from **Tools → Firmware…**.
