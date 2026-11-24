# The Pico programmer

A Raspberry Pi Pico (RP2040) or Pico 2 (RP2350) running Wren's sketch,
`firmware/PicoLink/PicoLink.ino`, stands in for an ST-LINK V2. Wren finds it as a serial
port, and **Tools → Programmer…** does the same jobs through it: install the kernel, upload
programs, read the wear history, run.

It speaks SWIM at low speed only (about 30 µs a byte). A whole kernel takes a few seconds,
and a program upload well under one.

## Parts

- A Pico or Pico 2 (a Pico W or Pico 2 W works too) and a USB cable.
- One 1 kΩ resistor: the SWIM line's pull-up. The Pico's own pull-up is too weak for
  SWIM's 250 ns pulses.
- Four jumper wires to the Thing's J2 (SWIM header) and VCC.

## Wiring

| Pico pin | Pico name | To |
|---|---|---|
| 4 | GP2 | Thing J2 pin 2 (SWIM), **and** the 1 kΩ resistor's first leg |
| 36 | 3V3(OUT) | the 1 kΩ resistor's second leg, **and** the Thing's VCC (top-right corner) |
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
   Thing holds.
4. **Install kernel**, **Upload program** and **Run** work as they do with an ST-LINK.

If Connect says the target did not answer, check the 1 kΩ pull-up, that SWIM goes to GP2
and NRST to GP3, and that the Thing has 3.3 V on VCC.

## How it works

The sketch drives SWIM from GP2 as an open-drain line, timed by the CPU's SysTick, from
RAM, with interrupts off while a transfer is on the wire. A 1 is low for 250 ns, a 0 for
2.5 µs, in a 2.75 µs bit. It reads the target's bits by timing their low pulses, checks
parity, and answers ACK or NACK. Bad frames are resent, up to 16 times. NRST is driven
from GP3, also open drain.

Wren talks to it in short requests (hello, reset, entry, read, write, SRST, release),
described in `src/Wren.Link/PicoLink.cs`. Reads and writes go in pieces of up to 64 bytes:
one flash block.

The sketch has been checked against a simulated chip on the SWIM line, but not yet
against a real one:
- CPU clocks of 133, 150 and 200 MHz;
- the target's clock 3 % fast and slow;
- NACKed and damaged frames.

## Open issues

1. **First run on real hardware.** Try it on the S103 development board first. If
   Connect fails, paste the log, and say which Pico and which Arduino core version.
