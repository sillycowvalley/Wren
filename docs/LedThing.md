# The 8-bit LED Thing v1.0

Digicool Things' second board, from its schematic (Rev 1.0, 2019-12-12) and its factory
firmware (`firmware/digicool/8-bit_led_thing.s19`). Wren does not support it yet: the plan
is item 7 of `docs/ProjectPlan.md`.

## Parts

- **U2:** STM8S003F3U6TR (UFQFPN20), the same chip as the OLED Thing. No crystal: it runs on
  the 16 MHz HSI.
- **U1:** an 8-LED DIL bar: pins 1-8 the anodes, 9-16 the cathodes.
- **R1-R4:** 180 Ω, one in each LED drive line (PD3, PD4, PD5, PD6 in that order).
- **C1** 680 nF on VCAP, **C2** 100 nF on NRST, **C3** 100 nF on VDD.
- Power: VDD 2.95-5.5 V (typically 3.3 V or 5 V). No pull-up resistors anywhere.

## Pins

| Signal | MCU pin | Notes |
|---|---|---|
| b0 | PD2 | Floating input, ~1 µA |
| b1 | PC7 | |
| b2 | PC6 | |
| b3 | PC5 | |
| b4 | PC4 | |
| b5 | PC3 | |
| b6 | PB4 | True open-drain pin: it has no pull-up, so it can only float |
| b7 | PB5 | True open-drain pin, as b6 |
| M2 | PA1 | Mode input, internal pull-up (~55 kΩ) |
| M1 | PA2 | |
| M0 | PA3 | |
| LEDs | PD3-PD6 | Charlieplexed, below |
| SWIM | PD1 | Programming header with VDD and NRST |

Unused b inputs need an external pull-up or pull-down, or a tie to GND. The STM8S has no
internal pull-downs: an input floats or is pulled up.

## The LEDs

The eight LEDs are charlieplexed on PD3-PD6: each sits between two of the four pins, lit
when its anode pin drives high and its cathode pin drives low, with the other two pins
left as inputs. Each LED's current passes through two of the 180 Ω resistors (about 8 mA
at 5 V, 4 mA at 3.3 V for a red bar). Read from the factory firmware running in Wren's
emulator, one b input high at a time:

| LED | Anode (high) | Cathode (low) |
|---|---|---|
| b0 | PD3 | PD4 |
| b1 | PD3 | PD5 |
| b2 | PD4 | PD3 |
| b3 | PD4 | PD5 |
| b4 | PD5 | PD3 |
| b5 | PD5 | PD4 |
| b6 | PD3 | PD6 |
| b7 | PD6 | PD3 |

Only one LED is lit at a time; the firmware scans them, each lit about one eighth of the
time.

## Digicool's firmware

The factory firmware is in `firmware/digicool` (with Digicool's permission; not MIT). It
leaves b0-b7 floating and pulls M0-M2 up. The mode pins choose what the bar shows:

| M2 M1 M0 | Mode |
|---|---|
| 0 0 0 | In-out middle bounce scanner (curtain close and open) |
| 0 0 1 | In-out middle bounce scanner (2 LEDs) |
| 0 1 0 | Binary counter loop ($00 to $FF) |
| 0 1 1 | Left-right single LED scanner |
| 1 0 0 | Left and right to middle sweep scanner |
| 1 0 1 | Left-right sweep scanner (0 to 8 LEDs) |
| 1 1 0 | Dice roll (for example M0 to GND through a push-to-break switch) |
| 1 1 1 | Each LED shows its b input (the default, through the pull-ups) |
