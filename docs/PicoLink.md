# The Pico programmer

A Raspberry Pi Pico, or a Pico-compatible board such as the Waveshare RP2040 Plus, running
Richard Hulme's [pico-swimlib](https://github.com/rhulme/pico-swimlib) stands in for an
ST-LINK V2. Wren finds it as a serial port, and **Tools → Programmer…** does the same jobs
through it: install the kernel, upload programs, read the wear history, back up, run.

- **The firmware:** `firmware/pico-swimlib/pico-espstlink.uf2`, pico-swimlib (BSD 3-Clause)
  built unchanged from its `pico-espstlink` example. Its licence and build notes sit
  beside it.
- **The timing:** SWIM is timed in hardware by the RP2040's PIO and DMA.
- **The protocol:** the esp-stlink serial protocol, over USB.

SWIM runs at low speed only. A whole kernel takes a few seconds, a program upload well
under one.

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

## Putting the firmware on the board (once)

1. Hold the board's **BOOTSEL** button while plugging it into USB, then let go. A drive
   called **RPI-RP2** appears.
2. Drag `firmware\pico-swimlib\pico-espstlink.uf2` from your Wren folder onto that drive.
   The board restarts as a USB serial port, "Pico ESPSTLink", under
   **Ports (COM & LPT)** in Device Manager.

## Using it in Wren

1. Wire the board to the Thing as above, and plug it in.
2. In Wren, choose **Tools → Programmer…** and click **Find**. The list shows
   **Pico on COMn**.
3. Choose it and click **Connect**.
   - The log names the firmware and the sync pulse (16 µs is right).
   - It then says what the Thing holds: empty, Wren's kernel (this build), or other
     firmware.
   - The **Connect** button greys out, and the other buttons become clickable.
4. **Back up…** saves the whole chip (flash, EEPROM, option bytes) as an `.s19` file.
   Connect and Back up only read. Keep the backup of Digicool's firmware on your own
   machine: it must never go into the repository.
5. **Install kernel**, **Upload program** and **Run** work as they do with an ST-LINK.

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

`src/Wren.Link/EspStLink.cs` speaks the esp-stlink protocol (reset, entry, SRST, read,
write, version).

- **Connecting:** it follows pico-swimlib's own example. NRST is held low, then the entry
  sequence and an SRST, then SWIM_CSR is set to $A1.
- **Releasing NRST:** Wren drives NRST **high** rather than letting go, unlike the example.
  A let-go RP2040 pin keeps its default pull-down, which fights the Thing's weak NRST
  pull-up and leaves the chip half in reset.
- **Transfers:** reads go in pieces of up to 255 bytes. Writes go a flash block (64 bytes)
  at a time.
- **Recognising the board:** Wren asks the board for its esp-stlink version when it opens
  the port. Anything else on the port is tried as Wren's older PicoLink sketch
  (`firmware/PicoLink`).

## Open issues

1. **First run on a real Thing.** This has been tested against a simulated programmer
   only. If Connect fails, paste the log.
2. **Wren's PicoLink sketch** (`firmware/PicoLink`, `src/Wren.Link/PicoLink.cs`) is
   superseded by pico-swimlib, and never read a byte reliably from a Thing. It goes once
   pico-swimlib is shown working.
