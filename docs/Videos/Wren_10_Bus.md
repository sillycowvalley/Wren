# Wren BASIC 10: A bus sniffer

## YouTube

**Title**: Sniffing a memory bus in BASIC | Wren BASIC 10

**Description**:

```
An Arduino Mega plays a CPU, reading and writing memory, and Digicool Things' 8-bit OLED Thing listens in: a bus sniffer in 127 bytes of Wren BASIC that catches every write and never sees a read.

In this video:
- what a bus cycle is: a byte, a clock, and a write enable
- ON LATCH ... WHILE: an edge that only counts while another pin is low
- the reads filtered in the interrupt, before the program ever sees them
- a log of the last 20 bytes written, in a ring
- the Mega's message, spelled out in hex

The program is programs/OledThing/BusSniffer.bas; the Mega's sketch is firmware/BusSource.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #BusSniffer #Arduino #RetroComputing #Microcontroller #Electronics
```

---

## BUS SNIFFER

> A computer talks to its memory millions of times a second. Today an Arduino does it slowly, and the OLED Thing listens in, but only to the writes.

## A bus cycle

> Every time a processor touches memory, it's a bus cycle. A byte on the data lines, and a clock that says "now".

> One more wire says which way it's going. The write enable goes low when the processor is writing, and stays high when it's reading.

## Latch, while

> Back in episode seven, the latch caught every edge, and I said a second pin could act as a gate. Here it is: catch the clock edge on PD3, but only while the write pin, PD2, is low.

> That check happens in the interrupt, the instant the edge arrives. A read never even reaches the program. There's nothing to filter out, because it was never let in.

## The log

> Each write goes into a ring of twenty bytes, the same trick as the snake's body and the voltmeter's graph. The oldest is overwritten by the newest.

> Ten times a second, if anything's new, the screen shows the bytes in hex, five to a row, with a count of every write so far.

## The sniffer, live

*Upload BusSniffer.bas with the Pico, then unplug the Pico. Ribbon from the Mega's A0–A7 to J1, D2 to PD3, D3 to PD2, 5 V and GND. Power the Mega with BusSource on it and open its Serial Monitor; the console moves from Wren to the Arduino IDE (BusSource).*

> The Mega is doing two hundred bus cycles a second. Seven in eight are reads.

> The Thing counts only the writes, and the Mega's own count agrees, exactly.

> And there's the message, in hex: four eight... four five... four C... four C... four F. H, E, L, L, O.

## What the sniffer brings

> An edge, a condition, and a ring of twenty bytes: every write on the bus, and none of the reads. All in a program of a hundred and twenty-seven bytes.

> Next time: one wire instead of eight. Thanks for watching.
