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

## Digicool's firmware (for reference; not part of this repository)

- Shows the byte on b0-b7 as two hex digits and a bar.
- HEX or BAR pulled low turns that part off; INV low inverts; HEX and BAR both low give
  "Dice Roll" mode, rolled by pulling any b input low.
- v1.1 adds PD3 "Latch" (each falling edge latches b0-b7 until reset) and PD2 "Hold"
  (while low, the display freezes).

## Programming

ST-LINK V2 to J2: RST to NRST, SWIM to SWIM, GND to GND, and the ST-LINK's 3.3 V or
5 V pin to the Thing's VCC (top-right corner).
