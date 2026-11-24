# The Pico programmer

A Raspberry Pi Pico, or a Pico-compatible board such as the Waveshare RP2040 Plus, stands in
for an ST-LINK V2. Wren finds it as a serial port. **Tools → Programmer…** sets a Thing up
through it (install the kernel, write images, restore factory settings, read the wear
history, the checks), and on **Debug → Target → Thing on the programmer** the main window
uploads, runs and debugs programs through it. Two firmwares are available. Both use the same wiring, and
Wren recognises either.

- **Wren's PicoLink** (`firmware/PicoLink`): Wren's own sketch, sketch version 16. Its
  SWIM is pico-swimlib's own code (BSD 3-Clause), unchanged but for its interrupt
  defaults; the sketch adds Wren's protocol and NRST. Proven on a real Thing: it installed
  the whole kernel (74 blocks) and the program table, and ran the debug module checks.
  - **To install:** drag `PicoLink.uf2` onto the board, or upload `PicoLink.ino` from the
    Arduino IDE.
- **pico-swimlib** (`firmware/pico-swimlib/pico-espstlink.uf2`): Richard Hulme's
  [pico-swimlib](https://github.com/rhulme/pico-swimlib) (BSD 3-Clause) built unchanged.
  - **Protocol:** it speaks the esp-stlink protocol.
  - **Status:** proven on a real Thing: it restored Digicool's firmware, installed Wren's
    kernel and uploaded `OledThing.bas`.

In both, SWIM is timed in hardware by the RP2040's PIO. SWIM runs at low speed only. A whole
kernel takes a few seconds, a program upload well under one.

## Parts

- **The board:** a Waveshare RP2040 Plus or a Pico, and a USB cable.
- **The pull-up:** one 470 Ω resistor (yellow, violet, brown, gold), from SWIM to 3.3 V.
  - A Thing has about 1.2 nF on SWIM, so the line needs a strong pull-up to rise in time.
  - Don't go below about 410 Ω: the Thing's pin can only sink 8 mA.
  - The board's own pull-up is far too weak.
- **Wires:** four jumper wires to the Thing's J2 (SWIM header) and VCC.

## Wiring

The breadboard cheatsheet: [Infographics/Pico_Programmer_Wiring.png](Infographics/Pico_Programmer_Wiring.png).

| Board pin | Name | To |
|---|---|---|
| 21 | GP16 | Thing J2 pin 2 (SWIM), **and** one leg of the 470 Ω pull-up |
| 22 | GP17 | Thing J2 pin 3 (NRST) |
| 23 | GND | Thing J2 pin 1 (GND) |
| 36 | 3V3(OUT) | the pull-up's other leg, **and** the Thing's VCC (top-right corner) |

**The Thing runs at 3.3 V from the board.** RP2040 pins are not 5 V tolerant, so the Thing
must not be powered from anything else while the board is connected. The OLED is fine at
3.3 V.

## Putting the firmware on the board

1. Hold the board's **BOOTSEL** button while plugging it into USB, then let go. A drive
   called **RPI-RP2** appears.
2. From your Wren folder, drag one of these onto that drive:
   - `firmware\PicoLink\PicoLink.uf2` (Wren's);
   - `firmware\pico-swimlib\pico-espstlink.uf2` (pico-swimlib's).

   The board restarts as a USB serial port under **Ports (COM & LPT)** in Device Manager.
   To switch firmware later, repeat both steps.

## Using it in Wren

1. Wire the board to the Thing as above, and plug it in.
2. In Wren, choose **Tools → Programmer…** and click **Find**. The list shows
   **Pico on COMn**.
3. Choose it and click **Connect**.
   - The log names the firmware and the sync pulse (16 µs is right).
   - It then names the board (an OLED Thing or an LED Thing) and says what the Thing holds:
     empty, Wren's kernel (this build, another build or an older one), or other firmware.
   - While connected, the Thing's program is stopped. **Disconnect**, or closing the
     window, lets it run again.
4. **Install kernel** checks the board and the EEPROM, then writes only the kernel blocks
   that differ, keeping the program.
5. **Restore factory settings** writes Digicool's firmware (`firmware/digicool`) back, two
   clicks. If read-out protection is on, it is removed first, which erases the chip. The
   EEPROM is cleared, and the Thing is left unprotected.
6. **Wear report…**, under Wren's kernel, shows the 54 program-store blocks. It reads the
   writes the Thing itself has recorded for each block and marks the current program's
   blocks. It reads only.
7. Then close the window: programs are uploaded, run and debugged from the main window with
   **Debug → Target → Thing on the programmer** (F5, Ctrl+F5 and the stepping keys).

If Connect says the target did not answer in time, check:
- the 470 Ω pull-up;
- that SWIM goes to GP16 and NRST to GP17;
- that the Thing has 3.3 V on VCC.

## Read-out protection and factory firmware

- **If the Thing refuses memory access** (SWIM_CSR's NO_ACCESS bit stays set for half a
  second after Connect), its read-out protection is likely on. Only **Remove protection**
  is then available. After two clicks it:
  1. writes $00 to the read-out protection byte, which makes the chip erase all its
     flash and EEPROM;
  2. resets the chip and connects again;
  3. writes ST's default option bytes.

  The chip is left blank and unprotected.
- **Write image…** writes any firmware file (.s19 or .hex), such as the Thing's factory
  firmware. It works like Install kernel: only blocks that differ are written, then
  checked. An image without option bytes, like Digicool's, leaves read-out protection off.

## How Wren talks to it

- **The two clients:** `src/Wren.Link/PicoLink.cs` speaks to Wren's sketch;
  `src/Wren.Link/EspStLink.cs` speaks the esp-stlink protocol to pico-swimlib's firmware.
- **Recognising the board:** Wren first asks for an esp-stlink version, then for Wren's
  hello.
- **Connecting:** both connect as pico-swimlib's example does. NRST is held low, then the
  entry sequence and an SRST, then SWIM_CSR is set to $A1.
- **Releasing NRST:** Wren drives NRST **high** rather than letting go. A let-go RP2040 pin
  keeps its default pull-down, which fights the Thing's weak NRST pull-up and leaves the
  chip half in reset.
- **Transfers:** reads and writes go in pieces of up to 64 bytes with Wren's sketch, and
  up to 255 (reads) or 64 (writes) bytes with pico-swimlib.
