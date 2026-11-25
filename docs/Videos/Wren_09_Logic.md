# Wren BASIC 9: An 8-channel logic analyzer

## YouTube

**Title**: An 8-channel logic analyzer on a 64x32 screen | Wren BASIC 9

**Description**:

```
Eight inputs, sampled together, drawn as eight traces: Digicool Things' 8-bit OLED Thing as a logic analyzer, in 236 bytes of Wren BASIC, watching an Arduino Mega's patterns.

In this video:
- what a logic analyzer shows: eight lines against time
- sampling all eight at once, into 64 bytes
- a trigger: start on the first change, then hold the picture
- eight traces of four pixels: a 64 x 32 screen exactly
- a counter, a walking one and eight PWM lines, live

The program is programs/OledThing/LogicAnalyzer.bas; the Mega's sketch is firmware/LogicSource.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #LogicAnalyzer #Arduino #Microcontroller #Electronics #Embedded
```

---

## LOGIC ANALYZER

> Eight wires, and what each of them did over the last sixty-four milliseconds. Today the OLED Thing becomes a logic analyzer.

## Eight lines against time

> A logic analyzer doesn't care about voltages, only about high and low. It samples every line at once, over and over, and draws them one above the other, so you can see what happened when.

> Sixty-four samples across, eight lines down, four pixels each. That's exactly this screen.

## One byte a sample

> The eight inputs are one byte, so a single sample is a single number. Sixty-four of them fit in a sixty-four byte array.

> A sample is taken every millisecond, by a timer. Nothing else happens while it's taking them: no drawing, nothing to make a tick late.

## A trigger

> Sampling all the time would just show a blur. So it waits: the moment any input changes, it starts. That's the trigger.

> Once the sixty-four are in, it draws them, holds the picture for three seconds, and waits for the next change. No buttons needed.

## Drawing the traces

> Each line is drawn on its own. A mask picks out one bit from every sample: a pixel on the top row when it's high, two rows down when it's low, and an edge where it changes.

> The mask doubles each time round, so the same few lines of code draw all eight channels.

## The analyzer, live

*Upload LogicAnalyzer.bas with the Pico, then unplug the Pico. Ribbon from the Mega's A0–A7 to J1, 5 V and GND. Power the Mega with LogicSource on it; it changes pattern every 6 seconds.*

> First, a counter. The top line toggles every two milliseconds, and every line below it at half the speed: the picture in every digital electronics textbook.

> Now a single one, walking from line to line.

> And eight PWM signals, all on the same period, each high a little longer than the one above.

## What the analyzer brings

> Eight lines sampled as one byte, a trigger, and sixty-four bytes of memory: a logic analyzer in two hundred and thirty-six bytes.

> Next time: watching a computer write to memory. Thanks for watching.
