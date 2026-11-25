# Wren BASIC 8: A frequency counter in BASIC

## YouTube

**Title**: A frequency counter in BASIC on an 8-bit chip | Wren BASIC 8

**Description**:

```
A 555 clock from another project, and Digicool Things' 8-bit OLED Thing as the instrument: a frequency counter in 376 bytes of Wren BASIC, from about 3 to 50 Hz, to a tenth of a hertz.

In this video:
- powering the Thing from the circuit it's measuring
- why counting edges for a second is too coarse for a slow clock
- timing them instead: a reciprocal counter
- long division in 16-bit integers, one digit at a time
- frequency, period and an edge count, read live off a 555

The program is programs/OledThing/Frequency.bas.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #FrequencyCounter #555 #Microcontroller #Electronics #Embedded
```

---

## FREQUENCY COUNTER

> A slow 555 clock from another project, and the OLED Thing as the instrument. Today: a frequency counter.

## Powered by the circuit

> Until now the Pico has powered the board. But the Thing runs on anything from three to five and a half volts.

> So once the program is on it, the Pico comes off, and the Thing takes its power from the clock board. Same supply, same ground, and the clock goes straight into PD3. No level shifting needed.

## Counting is too coarse

> The obvious way to measure frequency is to count edges for one second. At a megahertz that's wonderfully precise.

> But this clock ticks a few times a second. Count for one second, and three hertz could read two, three or four, depending on where the second happens to start and end.

## Timing instead

> So turn it around: time the edges. Every rising edge gets a timestamp, and every couple of seconds the program divides the periods it counted by the time they took.

> That's called a reciprocal counter, and it gives a tenth of a hertz across the whole range.

## Long division, 16 bits at a time

> The sum is periods times ten thousand, over the time. At fifty hertz that's a million, and a sixteen-bit number stops at thirty-two thousand.

> So the program does it the way we learned at school: one digit at a time, carrying the remainder. No step ever gets big enough to overflow. And the last digit is rounded, not chopped.

## The counter, live

*Upload Frequency.bas with the Pico, then unplug the Pico. Wire the clock board's 5 V to VCC (J3 pin 1), GND to J5 pin 1, the clock to J5 pin 4. Turn the 555's pot slowly from one end to the other.*

> There's the clock: the frequency, its period, and every edge counted.

> Turn the pot, and it follows. All the way down, and all the way up.

## What the counter brings

> A clock, one pin, a timestamp on every edge, and long division done by hand. The OLED Thing as a frequency counter, in three hundred and seventy-six bytes.

> Thanks for watching.
