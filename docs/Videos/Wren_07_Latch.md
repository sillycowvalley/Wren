# Wren BASIC 7: Catch a byte on an edge

## YouTube

**Title**: Catch a byte on an edge: LATCH in BASIC | Wren BASIC 7

**Description**:

```
Eight inputs, one strobe, and the instant it changes: ON LATCH in Wren BASIC catches the inputs of Digicool Things' 8-bit OLED Thing at an edge on PD3, inside the interrupt, and holds them. A whole program in 34 bytes.

In this video:
- what latching is: the byte as it was at the edge, held until the next one
- three ways to listen: ON CHANGE, ON PRESS and ON LATCH
- the program, line by line
- why one press of a button counts seven: contact bounce, caught edge by edge
- rising, falling, both, and a second pin as a gate

The program is programs/OledThing/Latch.bas.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #Latch #Interrupts #Microcontroller #Electronics #Embedded
```

---

## LATCH

> Eight inputs, one strobe, and the instant it changes. Today we catch a byte on an edge.

## Catch it on the edge

> Latching means taking the inputs exactly as they were at one moment, and holding them, however they change afterwards.

> The moment is an edge on another pin: the instant it goes from low to high, or high to low. That's how chips have always handed bytes to each other, with the data on eight wires and a strobe saying "now".

## Three ways to listen

> Wren BASIC can listen to the inputs three ways. ON CHANGE looks every millisecond and tells you when anything has changed.

> ON PRESS is for buttons. It waits until the pin has been steady for twenty milliseconds, so a press counts once.

> And ON LATCH reads the inputs inside the interrupt, a few microseconds after the edge. It's the one to use when timing matters, and it catches every edge.

## The program

> The whole program is a handler and a timer. The handler runs at every rising edge on PD3: it keeps the inputs, and counts.

> The timer draws the screen twenty times a second: the byte in hex, and the count beside it. Even mid-drawing, the inputs are caught at the edge itself; the handler just runs a moment later.

## Latching, live

*On the Thing: open Latch.bas, Ctrl+F5. Set some inputs with the wires on b0–b7. Press the PD3 button and let go. Change the wires; then press and let go again.*

> The button pulls PD3 to ground, so letting go is the rising edge. Set a byte, let go of the button, and there it is.

> Change the inputs, and the screen doesn't move. It holds the byte until the next edge.

> But look at the count. One press, and it went up by more than one.

## Why the count jumps

> A pushbutton doesn't make one clean edge. Its contacts bounce for a few milliseconds, making and breaking, and every bounce is another edge.

> ON LATCH catches every one of them, which is exactly what it's for. In the emulator, three bounces each way turned one press into seven latches.

> For a button, use ON PRESS. For a clean strobe from another circuit, LATCH is the one.

## Rising, falling, both

> An edge can be rising, falling, or both, and falling is what you get if you don't say.

> And a second pin can act as a gate: latch only while it's low, the way a memory chip's write enable works. That's how you'd watch a real bus.

## What LATCH brings

> The inputs at the instant of an edge, caught in the interrupt, held until the next one. In thirty-four bytes.

> Next time: a 555, and a frequency counter. Thanks for watching.
