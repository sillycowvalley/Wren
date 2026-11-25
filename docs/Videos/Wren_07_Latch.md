# Wren BASIC 7: Catch a byte on an edge

## YouTube

**Title**: Catch a byte on an edge: LATCH in BASIC | Wren BASIC 7

**Description**:

```
An Arduino Mega plays a computer's bus: eight data lines that never sit still, and a strobe that says "now". Digicool Things' 8-bit OLED Thing, running 34 bytes of Wren BASIC, catches each byte at the strobe's rising edge, and never the noise in between.

In this video:
- what latching is: the byte as it was at the edge, held until the next one
- three ways to listen: ON CHANGE, ON PRESS and ON LATCH
- the program, line by line
- a Mega as the bus: noise, then a byte, setup and hold, and the strobe
- a straight 8-wire ribbon, and the Thing powered from the Mega
- rising, falling, both, and a second pin as a gate

The Thing's program is programs/OledThing/Latch.bas; the Mega's sketch is firmware/LatchFeeder.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #Latch #Arduino #Interrupts #Microcontroller #Electronics
```

---

## LATCH

> Eight wires full of noise, and one that says "now". Today we catch a byte on an edge.

## Catch it on the edge

> Latching means taking the inputs exactly as they were at one moment, and holding them, however they change afterwards.

> The moment is an edge on another pin: the instant it goes from low to high. That's how chips have always handed bytes to each other, with the data on eight wires and a strobe saying "now".

## Three ways to listen

> Wren BASIC can listen to the inputs three ways. ON CHANGE looks every millisecond and tells you when anything has changed.

> ON PRESS is for buttons. It waits until the pin has been steady for twenty milliseconds, because a button bounces.

> And ON LATCH reads the inputs inside the interrupt, a few microseconds after the edge. It's the one for a clean strobe from another circuit.

## The program

> The whole program is a handler and a timer. The handler runs at every rising edge on PD3: it keeps the inputs, and counts.

> The timer draws the screen twenty times a second: the byte in hex, and the count beside it. Even mid-drawing, the inputs are caught at the edge itself; the handler just runs a moment later.

## A bus that never sits still

> To give it something worth catching, an Arduino Mega plays the part of a computer's bus.

> Most of the time, its eight data lines are noise: a new random byte every few microseconds. Then it puts a real byte on, waits a moment so the lines settle, and raises the strobe. A moment later, the noise starts again.

> All eight lines change in a single write, because they're one port on the Mega: one row of its header.

## Wiring it up

> One row of the Mega's header to one row of the Thing's, with a straight eight-wire ribbon. One more wire for the strobe.

> And the Thing runs from the Mega's five volts, so once the program is on it, the Pico comes off.

## Latching, live

*Upload Latch.bas with the Pico, then unplug the Pico. Wire the ribbon, D2 to PD3, 5 V and GND. Power the Mega; open its Serial Monitor beside Wren.*

> Once a second, a byte. Fifty-seven, fifty-two, forty-five, four E: W, R, E, N.

> The Mega's own log agrees, byte for byte. And the count goes up by exactly one each time: no bounces here, just one clean edge.

> In between, those eight wires are a blur of noise. The Thing never shows any of it.

## Rising, falling, both

> An edge can be rising, falling, or both, and falling is what you get if you don't say.

> And a second pin can act as a gate: latch only while it's low, the way a memory chip's write enable works. That's how you'd watch a real bus.

## What LATCH brings

> The inputs at the instant of an edge, caught in the interrupt, held until the next one. In thirty-four bytes.

> Next time: a 555, and a frequency counter. Thanks for watching.
