# Wren BASIC 1: Snake and Pong in BASIC on an 8 KB chip

## YouTube

**Title**: Snake and Pong in BASIC on an 8 KB chip | Wren

**Description**:

```
Wren is a free, open-source development environment for Digicool Things' 8-bit OLED Thing and 8-bit LED Thing: little boards built round an 8-bit microcontroller with 8 KB of flash and 1 KB of RAM.

You write in Wren BASIC, try it on an emulated Thing in the app, install Wren's kernel once through a Raspberry Pi Pico, and then upload and debug your programs on the real board: breakpoints, stepping and live variables, on the chip itself.

In this video:
- the two Things, and what fits in 8 KB
- the emulator, and the board's own behaviour rewritten in BASIC
- from the emulator to the real board, through a Raspberry Pi Pico
- Snake that plays itself, and the debugger stopping it every time it eats
- Pong, and hex digits in a font of their own at any angle
- the LED Thing: the same kernel, eight LEDs on four pins

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #Microcontroller #RetroComputing #Embedded #STM8 #RaspberryPiPico
```

---

## SNAKE IN BASIC

This is Snake, playing itself on a screen the size of a fingernail, on a chip with one kilobyte of RAM.

It's written in BASIC. And so is everything else you'll see today.

## Two little Things, one chip

These are two boards from Digicool Things. They were made to be learned from: you wire up the inputs and watch them in binary and hex.

Out of the box each does one job well. But underneath is a real microcontroller, and I wanted to program it myself.

The catch is the chip. Eight kilobytes of flash, one kilobyte of RAM. That's less memory than the machines I started on in the eighties.

## Wren: one app for the whole job

So I built Wren. It's one Windows app, written in C#, and it does the whole job.

You never see assembly, and you never see a register. You write BASIC, and you press the same keys you'd press in Visual Studio.

The emulator and the real board sit behind the same keys. The program doesn't know which one it's running on, and neither do you, until you look at the board.

## What fits in 8 KB of flash

The kernel goes on once. It's a small virtual machine, the screen driver, the fonts and the drawing, and it takes just over half the flash.

Your program lives in the rest. Look at the comparison on the right: the factory firmware is compiled C. The same behaviour in Wren BASIC is a fifteenth of the size, because the kernel does the heavy lifting.

Even the biggest program today leaves a lot of the store free.

## The emulator: OledThing.bas

*Open OledThing.bas, F5. Click the inputs, then pull BAR low, then HEX low too, and roll the die.*

This is the Thing's own behaviour, rewritten in BASIC, running on an emulated Thing.

Not a picture of one. The emulator runs the real kernel's machine code, instruction by instruction, and the display chip is modelled too.

I can wire it up, roll the dice and flip the mode pins as often as I like, and nothing wears out. That matters: this chip's flash is only good for about a hundred writes, so I try everything here first.

## Snake that plays itself

Snake plays itself. It heads for the food, but it also counts the ways out of every square, so it doesn't steer itself into a dead end.

The trick for a small chip is the grid. Every cell is one bit, so checking a move is a lookup, not a search through the snake's body.

## Snake on the Thing

*Open Snake.bas. Target → Thing on the programmer, Ctrl+F5.*

Here it is on the board, uploaded through a Raspberry Pi Pico. Every seventy milliseconds it looks around, picks a way, and moves.

When it finally traps itself, it shows its score and starts again.

## The debugger, on the real chip

This is the part I'm proudest of. The debugger works on the real chip, not just the emulator.

Debuggers for chips this small usually need a monitor built into the firmware, eating memory you don't have. This one costs nothing, because the silicon was built for it.

The catch is in the middle: two at a time. For a program this size, two go a long way.

## Debugging Snake on the real chip

*Breakpoint on line 83.*

I'll put a breakpoint on the line where it scores. Now it stops the instant it reaches the food.

These are the live variables, read straight out of the chip's RAM. There's the score, the length, and where the food is.

*F10, F10.*

Step over, and the score goes up. Step again, and it's a cell longer.

*F5.*

Carry on, and it runs to the next meal and stops again. On a chip with one kilobyte of RAM, that still feels like magic to me.

## Pong, with nobody playing

Pong, also playing itself. If both paddles were perfect, it would never end.

So each paddle guesses, and it's a little slow. A steep shot or a bad guess beats it, and that's what makes it fun to watch.

## Pong on the Thing

*Open Pong.bas, Ctrl+F5.*

Watch the paddles. One gets there late, the other gets there wrong, and that's all it takes to make a game of it.

## Hex digits in a font of their own

The kernel has fonts of its own. This program ignores them, and draws every digit from a font it defines itself, one row to a binary number.

Look at the gold pixels. Without them, the curves would be staircases. And because every rectangle goes through one routine that turns it, the other angles cost no second font.

## RotatedHex: any of four angles

*Open RotatedHex.bas on the emulator, F5; change ANGLE to 0, F5; back to 90, Ctrl+F5 on the Thing.*

Angles are cheap in the emulator, so I try them there.

Ninety is the program's own setting, so that's the one that goes on the board.

## The LED Thing: the same kernel

The LED Thing has the same chip, so it gets the same kernel, built for its board. Everything to do with the screen simply drops out.

Its LEDs can only be on or off, yet that eye has a soft glow either side. How it does that is a video of its own.

## The LED Thing, live

*Board → LED Thing. Open LedThing.bas, Ctrl+F5, pull M0 low.*

This is the LED Thing's own behaviour, rewritten in BASIC like the other one. Change the mode pins, and the pattern changes.

*Open Cylon.bas, Ctrl+F5.*

And this is that Cylon eye, glowing on the real board.

## What Wren brings

The link is in the description. If you have a Thing, give it a try. If you don't, the emulator runs every program you've seen.

Next time: turning a Raspberry Pi Pico into the programmer, and wiring it to each Thing. Thanks for watching.
