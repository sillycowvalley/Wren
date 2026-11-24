# The Pico programmer

A Raspberry Pi Pico (RP2040) or Pico 2 (RP2350) running Wren's sketch,
`firmware/PicoLink/PicoLink.ino`, stands in for an ST-LINK V2. Wren finds it as a serial
port, and **Tools → Programmer…** does the same jobs through it: install the kernel, upload
programs, read the wear history, run.

It speaks SWIM at low speed only (about 30 µs a byte). A whole kernel takes a few seconds,
and a program upload well under one.

## Parts

- A Pico or Pico 2 (a Pico W or Pico 2 W works too) and a USB cable.
- Two 1 kΩ resistors, side by side as the SWIM line's pull-up (500 Ω). A Thing has
  about 1.2 nF on SWIM, so the line needs a strong pull-up to rise in time; don't go
  below about 410 Ω, as the Thing's pin can only sink 8 mA. The Pico's own pull-up is far
  too weak.
- Four jumper wires to the Thing's J2 (SWIM header) and VCC.

## Wiring

The breadboard cheatsheet: [Infographics/Pico_Programmer_Wiring.png](Infographics/Pico_Programmer_Wiring.png).

| Pico pin | Pico name | To |
|---|---|---|
| 4 | GP2 | Thing J2 pin 2 (SWIM), **and** one leg of each pull-up resistor |
| 36 | 3V3(OUT) | the other leg of each pull-up resistor, **and** the Thing's VCC (top-right corner) |
| 5 | GP3 | Thing J2 pin 3 (NRST) |
| 3 | GND | Thing J2 pin 1 (GND) |

**The Thing runs at 3.3 V from the Pico.** The Pico's pins are not 5 V tolerant, so the
Thing must not be powered from anything else while the Pico is connected. The OLED is
fine at 3.3 V. The Pico's onboard LED lights while it is working.

## Putting the sketch on the Pico (once)

1. Install the **Arduino IDE** (2.x) from arduino.cc.
2. **File → Preferences**. In **Additional boards manager URLs**, add
   `https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json`
   and click **OK**.
3. **Tools → Board → Boards Manager**. Search for **pico** and install
   **Raspberry Pi Pico/RP2040/RP2350** by Earle F. Philhower, III.
4. **File → Open** and choose `firmware\PicoLink\PicoLink.ino` in your Wren folder.
5. **Tools → Board → Raspberry Pi Pico/RP2040/RP2350**, then **Raspberry Pi Pico** (or
   **Raspberry Pi Pico 2** for an RP2350).
6. Hold the Pico's **BOOTSEL** button while plugging it into USB, then let go. It appears
   as a drive. In **Tools → Port**, choose the **UF2 Board** entry.
7. Click **Upload** (the right-arrow button). When it says "Done", the Pico restarts as a
   serial port. Device Manager shows it under **Ports (COM & LPT)**.
8. Close the Arduino IDE, or at least its Serial Monitor: only one program can use the port.

## Using it in Wren

1. Wire the Pico to the Thing (or the development board) as above, and plug the Pico in.
2. In Wren, choose **Tools → Programmer…** and click **Find**. The list shows
   **Pico on COMn**.
3. Choose it and click **Connect**. The log names the sketch's version, and then what the
   Thing holds: empty, Wren's kernel (this build), or other firmware.
4. **Back up…** saves the whole chip (flash, EEPROM, option bytes) as an `.s19` file.
   Connect and Back up only read. Keep the backup of Digicool's firmware on your own
   machine: it must never go into the repository.
5. **Install kernel**, **Upload program** and **Run** work as they do with an ST-LINK.

If Connect says the target did not answer, check the pull-up (two 1 kΩ side by side), that SWIM goes to GP2
and NRST to GP3, and that the Thing has 3.3 V on VCC.

## How it works

The sketch follows ST's SWIM manual, UM0470 (not in this repository: it is ST's).
- **Lines:** SWIM is driven from GP2 as an open-drain line. NRST is driven from GP3, also open drain.
- **Timing:** taken from the CPU's SysTick, with the code running from RAM and interrupts off while a transfer is on the wire.
- **Connecting:**
  1. NRST is held low and the entry sequence sent. The Thing answers with a sync pulse, sometimes starting before the Pico lets go.
  2. SWIM_CSR is written, then NRST released.
  3. A communication reset follows: the line held low for 25 µs (UM0470 3.2 step 9 and 3.6). The Thing's clock is trimmed by now, and its fresh sync pulse calibrates how the sketch reads the Thing's bits.
- **The Pico's bits:** a 1 is low for 220 ns, a 0 for 2.0 µs, in a 2.75 µs bit, inside the manual's table 3. Each low ends with a brief push-pull high. The Thing decodes a bit by counting low samples on its own clock, so the shape matters, not the period.
- **The Thing's bits:** read at 11 of its SWIM clocks, midway between a 1's 2 low clocks and a 0's 20. An edge hidden by a slow rise is predicted from the bit period.
- **Errors:** parity is checked, and the Thing gets an ACK or a NACK. A bad frame is resent up to 16 times.

Wren talks to it in short requests (hello, reset, entry, communication reset, read, write, SRST, release, line check), described in `src/Wren.Link/PicoLink.cs`. Reads and writes go in pieces of up to 64 bytes: one flash block. When Connect fails, Wren runs the line check by itself and explains each reading.

The sketch has been checked against a simulated chip that decodes bits as UM0470 describes, but not yet against a real one. The simulation covers:
- CPU clocks of 133, 150 and 200 MHz;
- the Thing's clock from 0.6 to 1.5 times its trimmed rate before the reset, and 3 % either side after it;
- line rises up to 435 ns;
- replies with no gap after an acknowledge;
- NACKed and damaged frames.

## Open issues

1. **First run on real hardware.** Try it on the S103 development board first. If
   Connect fails, paste the log, and say which Pico and which Arduino core version.
