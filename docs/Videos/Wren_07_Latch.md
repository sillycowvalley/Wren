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
- how fast can it go? A counting sweep, and where it breaks
- rising, falling, both, and a second pin as a gate

The Thing's programs are programs/OledThing/Latch.bas and LatchTest.bas; the Mega's sketch is firmware/LatchFeeder.

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

> The timer draws the screen twenty times a second: the byte in hex, and the count beside it. Even while it's drawing, the inputs are caught right at the edge. The handler just runs a moment later.

## A bus that never sits still

> To give it something worth catching, an Arduino Mega plays the part of a computer's bus.

> Most of the time, its eight data lines are noise: a new random byte every few microseconds. Then it puts a real byte on, waits a moment so the lines settle, and raises the strobe. A moment later, the noise starts again.

> All eight lines change in a single write, because they're one port on the Mega: one row of its header.

## Wiring it up

> One row of the Mega's header to one row of the Thing's, with a straight eight-wire ribbon. One more wire for the strobe.

> And the Thing runs from the Mega's five volts, so once the program is on it, the Pico comes off.

## Latching, live

*Upload Latch.bas with the Pico, then unplug the Pico. Wire the ribbon, D2 to PD3, 5 V and GND. Power the Mega and open its Serial Monitor; the console moves from Wren to the Arduino IDE (LatchFeeder).*

> Once a second, a byte. Five seven, five two, four five, four E: W, R, E, N.

> The Mega's own log agrees, byte for byte. And the count goes up by exactly one each time: no bounces here, just one clean edge.

> In between, those eight wires are a blur of noise. The Thing never shows any of it.

## How fast can it latch?

> One byte a second is easy. So how fast can it go before it misses one?

> For that, the Mega counts: nought, one, two, all the way to two hundred and fifty-five and round again. The Thing knows exactly which byte should come next, so the first one that's wrong, or missing, stops it cold.

> It shows what came, what it wanted, and how fast the bytes were arriving in the second before.

## The sweep, live

*Swap the Thing back onto the Pico, upload LatchTest.bas in Wren, then back onto the Mega's 5 V. The console moves to the Arduino IDE (LatchFeeder): in the Serial Monitor type w and press Enter: the Mega starts at 100 strobes a second and adds 50 every 3 seconds. Wait for FAILED; cut the wait in the edit. Press the PD2 button to start the check again.*

> Every strobe checked, and the rate climbing. All good.

> And there it is: it's stopped. What came, what it wanted, and the rate in the second before.

## Where it breaks

> The latch itself never misses. The interrupt catches every edge, and puts the byte in a queue for its handler.

> But the queue has room for seven, and once a second the test program stops to draw the screen. If more than seven bytes arrive while it's drawing, the next ones have nowhere to go. LOST counts them.

> So the limit isn't the latch. It's seven bytes, in the time it takes to draw one screen.

## Rising, falling, both

> An edge can be rising, falling, or both, and falling is what you get if you don't say.

> And a second pin can act as a gate: latch only while it's low, the way a memory chip's write enable works. That's how you'd watch a real bus.

## What LATCH brings

> The inputs at the instant of an edge, caught in the interrupt, held until the next one. In thirty-four bytes.

> Next time: a 555, and a frequency counter. Thanks for watching.
