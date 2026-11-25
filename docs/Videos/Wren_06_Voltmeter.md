# Wren BASIC 6: A voltmeter in BASIC

## YouTube

**Title**: A voltmeter in BASIC on an 8-bit chip | Wren BASIC 6

**Description**:

```
A variable resistor, one pin and the analog-to-digital converter inside Digicool Things' 8-bit OLED Thing: a voltmeter in 282 bytes of Wren BASIC, showing the voltage and the raw reading, with a scrolling graph.

In this video:
- a variable resistor as a voltage divider, and how it's wired
- ANALOG: 0 to 1023, against the chip's own 3.3 V
- turning that into volts with nothing but 16-bit integers
- averaging, to steady the last digit
- a meter view and a graph of the last 64 readings, switched with a button

The program is programs/OledThing/Voltmeter.bas.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #ADC #Voltmeter #Microcontroller #Electronics #Embedded
```

---

## VOLTMETER

> A knob, one pin, and the converter that's been sitting inside this chip all along. Today we turn the OLED Thing into a voltmeter.

## A knob is a voltage divider

> A variable resistor has three legs. Put three point three volts across the outer two, and the middle one, the wiper, sits somewhere in between.

> Turn the knob, and the wiper moves from nought all the way to three point three volts. That's the voltage we're going to measure.

## Wiring it up

> The wiper goes to PD2. The two ends go to three point three volts and to ground, the same supply the Pico gives the board.

> And the stopwatch's PD3 button from video three stays where it is. Its PD2 button comes off, because PD2 is now the input. Here, PD3 switches between two views.

## 0 to 1023

> Inside the chip is a ten-bit converter. It compares the pin with the chip's own supply and gives back a number from nought to a thousand and twenty-three.

> So every step is about three millivolts. And because its yardstick is the supply itself, three point three volts on the pin reads as the very top.

> One detail: reading a pin with ANALOG turns its pull-up off. Left on, the pull-up would drag the voltage upwards.

## Volts in 16-bit integers

> Turning steps into millivolts means multiplying by three point two two five eight. With no floating point, the obvious way is to multiply by thirty-three hundred first, then divide.

> But a thousand and twenty-three times thirty-three hundred is over three million, and a sixteen-bit number tops out at thirty-two thousand.

> So it's done in two parts: three times the reading, plus the reading times twenty-nine, divided by a hundred and twenty-eight. Nothing overflows, and it's never out by more than a millivolt.

## Steadier digits

> A single conversion jitters by a step or two, and that makes the last digit flicker.

> So each reading is eight conversions, added up and divided by eight. The total never passes eight thousand two hundred, so it fits easily.

## Drawing it

> The meter view draws each digit on its own, so one point oh five never comes out as one point five.

> The graph view keeps the last sixty-four readings in a ring, just like the snake's body. Each new reading replaces the oldest, and the screen draws them oldest on the left.

## The voltmeter, live

*On the Thing: open Voltmeter.bas, Ctrl+F5. Turn the knob end to end. Press the PD3 button for the graph, turn the knob back and forth, press it again.*

> There's the voltage, and the raw number beneath it. All the way down, all the way up.

> And the graph, drawing the knob as I turn it.

## What the converter brings

> A knob, one pin, ten bits, and a little integer arithmetic: a voltmeter in two hundred and eighty-two bytes.

> Next time: catching a byte on an edge. Thanks for watching.
