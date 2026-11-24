# pico-swimlib programmer firmware

`pico-espstlink.uf2` turns a Raspberry Pi Pico, or a Pico-compatible board such as the
Waveshare RP2040 Plus, into a SWIM programmer for STM8 chips. It is Richard Hulme's
[pico-swimlib](https://github.com/rhulme/pico-swimlib) (BSD 3-Clause, see `LICENSE`
here), built unchanged from its `pico-espstlink` example.

- **Bit timing:** done in hardware, by an RP2040 PIO state machine and DMA.
- **Protocol:** it speaks the esp-stlink serial protocol over USB serial.
- **Pins:** SWIM on **GP16**, NRST on **GP17**.

The wiring is in `docs/Infographics/Pico_Programmer_Wiring.png`.

## Putting it on the board

1. Hold the board's **BOOTSEL** button while plugging it into USB, then let go. A drive
   called **RPI-RP2** appears.
2. Drag `pico-espstlink.uf2` onto that drive. The board restarts as a USB serial port
   named "Pico ESPSTLink".

## How it was built

- **Source:** pico-swimlib commit `3cf28f99bf8d1645145a48d878b822de11a85739`.
- **SDK and target:** Pico SDK 2.1.0, `PICO_BOARD=pico`, Release.
- **Target built:** `pico-espstlink`.
- **Result:** the `.uf2` file's SHA-256 starts `3296a80f63d293c9`.
