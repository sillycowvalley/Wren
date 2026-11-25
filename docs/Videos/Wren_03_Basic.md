# Wren BASIC 3: BASIC in 1 KB of RAM

## YouTube

**Title**: BASIC in 1 KB of RAM: the Wren BASIC language | Wren BASIC 3

**Description**:

```
Wren BASIC is the language of Digicool Things' 8-bit OLED Thing and 8-bit LED Thing: a BASIC with no line numbers and no main loop, built on events, that runs in 1 KB of RAM.

In this video, a stopwatch for the OLED Thing, line by line:
- the shape of a program: constants, globals, handlers, SUBs and FUNCTIONs
- events: ON START, ON EVERY, ON CHANGE, ON PRESS, ON LATCH and the rest
- 16-bit integers, and why there's nothing else
- drawing on a 64 x 32 screen, four fonts, and numbers in hex, decimal and binary
- time, pins and the EEPROM: a best time that survives a power cut
- the limits, and what a fault looks like

The stopwatch is programs/OledThing/Stopwatch.bas, and the Quick Reference is in Wren's Help menu.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #RetroComputing #Microcontroller #Embedded #Programming
```

---

## BASIC IN 1 KB OF RAM

> This is a stopwatch, written in BASIC, running on a chip with one kilobyte of RAM. Today we'll go through the language it's written in, line by line.

## No main loop

> If you learned BASIC in the eighties, the first thing you'll notice is what's missing. No line numbers, no GOTO, and no loop going round and round waiting for something to happen.

> Instead, a program is a handful of declarations, and pieces of code that wait for something to call them. Everything else has to live inside one of those.

## Events, not loops

> This is the heart of it. You say what should happen when something happens, and the kernel does the waiting for you.

> It runs one handler at a time, start to finish. Anything that happens meanwhile waits its turn in a queue, so a handler never has to worry about being interrupted halfway through.

> And between events the chip simply sleeps.

## 16-bit integers, and nothing else

> Every number is a sixteen-bit integer. No floating point, and no string variables.

> That sounds limiting, but it's what fits. And there are tricks: Pong keeps its ball in quarter pixels, and the stopwatch counts hundredths of a second, so a whole number is all it ever needs.

> One thing to watch: true is minus one, and AND works on the bits. So it works for tests and for masks alike.

## The stopwatch, on the emulator

*Open Stopwatch.bas, F5. In Spare pins, pull PD3 low and let go to start; again to stop. The same with PD2 to zero it.*

> Here's the whole program running. Two spare pins are its buttons: one starts and stops it, the other sets it back to zero. A real wire bounces when it touches, and the kernel takes care of that for you.

> Stop it, and if that's the best time so far, it goes in the bottom corner.

## SUB and FUNCTION

> The stopwatch draws its digits in pairs, so that's a SUB, called four times, in two fonts.

> And the best time is a FUNCTION with no parameters, so it reads like a variable. That's how the line that keeps the record ends up reading almost like English.

## Drawing on 64 × 32

> Drawing never goes straight to the screen. It goes into a copy in RAM, and nothing appears until you say SHOW.

> That's why a handler usually starts by clearing and ends by showing: the screen changes in one go, with no flicker.

## Four fonts, and numbers

> There are four fonts, from tiny capitals to digits the full height of the screen.

> And numbers have statements of their own. The same byte, in hex, decimal, binary, as eight cells, and as a bar. The hex digits and the eight cells are the board's own factory views.

## Time, pins and the EEPROM

> The first version of the stopwatch counted its ten-millisecond ticks, and it ran five percent slow. Drawing the screen takes longer than ten milliseconds, so ticks went missing.

> The fix is to ask the clock. Each time round, it reads the milliseconds and adds on the difference. It doesn't matter how late the tick is, and it even survives the counter wrapping round.

## The stopwatch, on the Thing

*In Wren: Debug → Target → Thing on the programmer, then Ctrl+F5 to upload and run.*

*Press the PD3 button to start, again to stop. Press the PD2 button to set it back to 0.*

> On the real board, two pushbuttons on a breadboard, one on each pin. And it's the same three hundred and forty-eight bytes, doing exactly what it did on screen.

*Shift+F5, so Wren lets go of the Pico. Unplug the Pico's USB cable, and plug it back in.*

> The Thing gets its power from the Pico, so that's a real power cut. And the best time is still there, because it lives in the EEPROM.

## The limits

> Here's what one kilobyte gets you. The program lives in flash, so it can be quite long, but all the variables share two hundred and fifty-six bytes.

> And if something goes wrong, like dividing by zero, the program stops and the screen says so: the fault number, and where in the program it happened. The kernel keeps running, so you can always upload a fix.

## What Wren BASIC brings

> Events instead of loops, sixteen-bit numbers, four fonts and a screen to draw on. All in one kilobyte of RAM.

> Next time, we'll take Snake apart in the emulator. Thanks for watching.
