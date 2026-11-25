# Wren BASIC 2: A Raspberry Pi Pico as the programmer

## YouTube

**Title**: Pi Pico as a programmer for 8-bit Things | Wren BASIC 2

**Description**:

```
A Raspberry Pi Pico, one resistor and four wires: everything you need to program, and debug, Digicool Things' 8-bit OLED Thing and 8-bit LED Thing from Wren.

In this video:
- the one-wire programming pin, and why the Pico is good at it
- putting Wren's PicoLink firmware on the Pico: one file, no tools
- wiring it to the OLED Thing and to the LED Thing
- installing Wren's kernel, once
- putting the factory firmware back, as it shipped
- what to check when Connect can't reach the Thing

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren
The wiring sheet: docs/Infographics/Pico_Programmer_Wiring.png in the repository.

#Wren #BASIC #RaspberryPiPico #RP2040 #Microcontroller #Embedded #STM8
```

---

## PI PICO PROGRAMMER

> Last time, Wren put BASIC on two little boards from Digicool Things. Today: how the programs get onto them, with a Raspberry Pi Pico.

## One wire to program a chip

> Every chip like this has a programming pin. One wire carries the whole conversation: write the flash, read the RAM, stop the processor, start it again.

> That wire's timing is strict, and the Pico has a trick for exactly that: little state machines that run beside its processor and keep perfect time.

> So Wren talks to the Pico over USB, and the Pico talks to the Thing.

## A Pico, a resistor and four wires

> That's the whole shopping list. An original Pico will do, or any RP2040 board with the same pinout.

> The resistor matters more than it looks. The Thing's programming line is slow to rise on its own, so it needs a strong pull up to three point three volts between pulses. Too weak, and the edges arrive late. Too strong, and the Thing can't pull it back down.

## PicoLink on the Pico

> Before it can program anything, the Pico needs Wren's firmware. It's a single file, and putting it on needs no tools at all.

## Flashing the Pico

*Hold BOOTSEL, plug in the USB cable, let go. The console shows the RPI-RP2 drive's window once it opens. Drag PicoLink.uf2 onto it.*

> Drag the file across, and that's it. The drive disappears, and the Pico comes back as a serial port. That's how Wren finds it.

## Wiring the OLED Thing

> Four wires. One carries the conversation, one is reset, one is ground, and one powers the Thing from the Pico.

> That last one comes with the only rule that really matters. While the Pico's connected, nothing else may power the board: the Pico's pins aren't built for five volts.

## Connecting to the OLED Thing

*In Wren: Tools → Firmware…; the console moves to the Firmware window. Choose the Pico, Connect.*

> Connect, and Wren asks the Thing what it is. This one's an OLED Thing, still running its factory firmware.

> While it's connected, the Thing's own program is stopped. Let go, and it carries on.

## Install kernel, once

> This is the one big write, and normally you only do it once. Wren works out which Thing it's talking to from what's already on it, and checks how much EEPROM this particular chip really has.

> Then it locks the kernel in place. From then on, nothing a program does can overwrite it. Only a programmer like the Pico can.

## Installing the kernel

*In the Firmware window: Install kernel, then Install kernel again to go ahead.*

> It asks twice, because each install uses up one of the chip's limited flash writes.

*Wait for the log; cut the wait in the edit.*

> A few seconds, and it's done.

*Close; the console moves back to Wren. Debug → Target → Thing on the programmer. Open OledThing.bas, Ctrl+F5.*

> From here on, it's the main window, and the same keys as the emulator.

## Wiring the LED Thing

> The LED Thing is even simpler. Its programming header carries power as well, so all four wires go to one place.

## The LED Thing, live

*Move the wires to the LED Thing. In the Firmware window: Connect, Install kernel twice, Close; the console moves back to Wren. Board → LED Thing, open LedThing.bas, Ctrl+F5.*

> The same steps. Wren recognises the board, and gives it the LED Thing's own kernel.

## Back to the factory

> Wren isn't a one-way door. Greg at Digicool kindly lets Wren carry the boards' own firmware, so putting one back the way it came is two clicks.

> Even a chip that's been locked against reading can be brought back. It just has to be erased first.

## Factory firmware, back

*Move the wires back to the OLED Thing. In the Firmware window: Connect, Write image…, choose 8-bit_oled_thing_v1.1.s19, Write image again to go ahead. Disconnect.*

> And there's the factory display, exactly as it shipped.

## When Connect says no

> If Connect can't reach the Thing, it's almost always one of three causes, and they're all on screen.

> And if the Pico's port won't come back after you unplug it, something is still holding on to it. A Wren paused in a debugger will do that.

## What the Pico brings

> A few dollars and one resistor, and it installs, uploads and debugs, all from one window.

> Next time, the language itself: everything Wren BASIC can do in one kilobyte of RAM. Thanks for watching.
