# Wren BASIC 1: Snake and Pong in BASIC on an 8 KB chip

The first video of the Wren BASIC series and its playlist. It shows what Wren is and what
it brings to Digicool Things' two boards, the 8-bit OLED Thing and the 8-bit LED Thing.
Most of it is the OLED Thing, running four programs on the emulator and then on the real
board, with the debugger on Snake. It ends on the LED Thing. It is for people who own a
Thing, people who program small microcontrollers, and anyone who misses typing BASIC into
a machine and watching it go.

- **Length**: about 22 to 24 minutes, 974 spoken words.
- **The viewer leaves knowing**: Wren puts the whole job (write, try, install, debug) for a
  1 KB RAM chip into one Windows app, in BASIC, without wearing out the chip's flash.
- **Slides**: `docs/Videos/Infographics/Wren_01/`, from `Wren_01_00_Thumbnail.html` to
  `Wren_01_10_Closing.html`. Each HTML's opening comment names where its facts come from.
- **Figures**: program sizes are from Wren's compiler on the programs in `programs/`
  (the app's **Build** message says the same); the kernels' sizes from assembling
  `firmware/kernel/Kernel.asm` for each board; the factory firmware sizes from
  `firmware/digicool/README.md`; the chip from `docs/OledThing.md`; the screens on the
  slides are the emulator's, from runs of the programs as they are in `programs/`.

---

## On YouTube

**Title**: Snake and Pong in BASIC on an 8 KB chip | Wren

Alternatives:
- Snake, Pong and a debugger, in BASIC on a 1 KB RAM chip
- I wrote a BASIC for an 8-bit board with 1 KB of RAM

**Description**:

```
Wren is a free, open-source development environment for Digicool Things' 8-bit OLED Thing and 8-bit LED Thing: little boards built round an 8-bit microcontroller with 8 KB of flash and 1 KB of RAM.

You write in Wren BASIC, try it on an emulated Thing in the app, install Wren's kernel once through a Raspberry Pi Pico, and then upload and debug your programs on the real board: breakpoints, stepping and live variables, on the chip itself.

In this video:
- the two Things, and what fits in 8 KB
- the emulator, and the board's own behaviour rewritten in BASIC
- installing the kernel without wearing out the flash
- Snake that plays itself, and the debugger stopping it every time it eats
- Pong, and hex digits in a font of their own at any angle
- the LED Thing: the same kernel, eight LEDs on four pins

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #Microcontroller #RetroComputing #Embedded #STM8 #RaspberryPiPico
```

**Chapters** (take the times from the edit before posting):

```
0:00 Snake and Pong in BASIC
0:30 Two little Things
2:00 What Wren is
3:30 What fits in 8 KB
5:00 The emulator
7:00 A hundred writes
8:30 Installing the kernel
10:30 Snake
12:30 The debugger, on the real chip
15:30 Pong
17:00 Hex in a font of its own
19:00 The LED Thing
21:30 Thanks for watching
```

**Where to post it**:
- Digicool Things' own community and Greg's channels: these are the people who own the boards.
- r/embedded and r/microcontrollers.
- The STM8 communities (the STM8 forum on ST's community site, and STM8 groups on Facebook).
- Retro BASIC groups: r/retrocomputing and the BASIC programming groups on Facebook.
- The Raspberry Pi Pico community (r/raspberrypipico), for the Pico as a programmer.
- A tip to Hackaday (tips@hackaday.com) with the video and the repository.

---

## Before recording

1. **Wren**: the build from `main` with this video's work merged, opened in Visual Studio
   and run (Release). **Board → OLED Thing**, **Debug → Target → Emulator**. Size the main
   window to about 3 : 2 (for example 1500 × 1000) so it fills the console frame.
2. **The programs**: `programs/OledThing/OledThing.bas`, `Snake.bas`, `Pong.bas`,
   `RotatedHex.bas`, and `programs/LedThing/LedThing.bas` and `Cylon.bas`, unchanged.
   `RotatedHex.bas` starts with `CONST ANGLE = 90`.
3. **The OLED Thing**: still on Digicool's factory firmware, so the install scene goes from
   factory to Wren. If it already carries Wren's kernel, skip the **Install kernel** click:
   **Connect** shows the kernel it has. The video uploads four programs to it (OledThing,
   Snake, Pong, RotatedHex), each to the least-written free blocks.
4. **The LED Thing**: carrying Wren's LED kernel already, with nothing to install on camera.
5. **The programmer**: the Pico, wired as in `docs/PicoLink.md`, plugged in, the OLED Thing
   on its lead.
6. **Wires for the OLED Thing**: one jumper from **HEX** (J4 pin 1) to **GND** (J4 pin 2),
   one from **BAR** (J3 pin 2) to **GND** (J5 pin 1), and a loose one to touch any b input
   to GND (J5 pin 1) for the die. Leave them off until scene 4b.
7. **Wires for the LED Thing**: one jumper to pull **M0** (PA3) to GND, for the change of mode.
8. **The slides**: `Wren_01_00_Thumbnail.html` open in Chrome, full screen with F11. A click
   on the right half of the page is the next slide, the left half the one before; F5 plays
   a slide's animation again. Let each slide finish its three seconds before speaking.
9. **OBS**: one scene for the slides (Chrome, full screen) and one for the cut-aways: the
   backdrop PNG, the Wren window in the console frame (48, 152, up to 1114 × 762) and the
   camera in the camera frame (1210, 152, 662 × 372).
10. **The camera**: close on the Thing, square on, the OLED filling most of the frame.
    Manual focus and manual exposure, turned down until the OLED's pixels are crisp, not
    a white glow.
11. **The pointer**: parked at the edge of the screen after each click.
12. **The thumbnail**: `Wren_01_00_Thumbnail.png`.

---

## The script

### 0. Snake and Pong in BASIC (0:00–0:30) — Wren_01_00_Thumbnail.html

Open on the thumbnail, then click into slide 1 at the end.

> This is Snake, playing itself on a screen the size of a fingernail, on a chip with one kilobyte of RAM.

> It's written in BASIC. And so is everything else you'll see today.

### 1. Two little Things (0:30–2:00) — Wren_01_01_TwoThings.html

Click to slide 1.

> These are two boards from Digicool Things. They were made to be learned from: you wire up the inputs and watch them in binary and hex.

> Out of the box each does one job well. But underneath is a real microcontroller, and I wanted to program it myself.

> The catch is the chip. Eight kilobytes of flash, one kilobyte of RAM. That's less memory than the machines I started on in the eighties.

### 2. What Wren is (2:00–3:30) — Wren_01_02_Wren.html

Click to slide 2.

> So I built Wren. It's one Windows app, written in C#, and it does the whole job.

> You never see assembly, and you never see a register. You write BASIC, and you press the same keys you'd press in Visual Studio.

> The emulator and the real board sit behind the same keys. The program doesn't know which one it's running on, and neither do you, until you look at the board.

### 3. What fits in 8 KB (3:30–5:00) — Wren_01_03_WhatFits.html

Click to slide 3.

> The kernel goes on once. It's a small virtual machine, the screen driver, the fonts and the drawing, and it takes just over half the flash.

> Your program lives in the rest. Look at the comparison on the right: the factory firmware is compiled C. The same behaviour in Wren BASIC is a fifteenth of the size, because the kernel does the heavy lifting.

> Even the biggest program today leaves a lot of the store free.

### 3b. The emulator (5:00–7:00) — Wren_01_03b_Emulator.png

Switch to the cut-away scene. In Wren, **File → Open…** `programs/OledThing/OledThing.bas`.
Press **F7**; Messages shows:

```
Built OledThing.bas: 502 bytes in 8 blocks.
```

Press **F5**. Tick and untick boxes in **Inputs b7 – b0**; the OLED shows each value in hex
and as eight bit cells. In **Mode pins**, pull **BAR** low (the big digits), then **HEX**
low as well (Dice Roll); untick an input to roll the die, tick it again to let it land.
Pull **INV** low and back.

> This is the Thing's own behaviour, rewritten in BASIC, running on an emulated Thing.

> Not a picture of one. The emulator runs the real kernel's machine code, instruction by instruction, and the display chip is modelled too.

> I can wire it up, roll the dice and flip the mode pins as often as I like, and nothing wears out. That matters, as you'll see next.

### 4. A hundred writes (7:00–8:30) — Wren_01_04_HundredWrites.html

Click to slide 4.

> Here's the thing nobody tells you about this chip. Its flash is only rated for about a hundred writes.

> Develop the usual way, reflashing every time you change a line, and you could wear a board out in a weekend.

> So Wren keeps the board's flash for last. You try things in the emulator first, the kernel goes on once, and an upload only writes the blocks it needs, spread across the ones used least.

> And if you change your mind, the factory firmware goes straight back on.

### 4b. Installing the kernel (8:30–10:30) — Wren_01_04b_Install.png

Switch to the cut-away scene; the camera shows the OLED Thing still running its factory
firmware. In Wren, **Tools → Firmware…**, choose **Pico programmer**, **Connect**. Then
**Install kernel**; wait for the log to finish (cut the wait in the edit). **Close**.
**Debug → Target → Thing on the programmer**, and **Ctrl+F5**: `OledThing.bas` uploads and
runs on the real board. Fit the HEX and BAR jumpers, then touch the loose wire from a b
input to GND to roll the die.

> The programmer is a Raspberry Pi Pico, a few dollars, talking to the chip over its one-wire programming pin.

> Install kernel looks at what's on the board, works out which Thing it is, and writes that Thing's kernel. Once.

> And there it is: the same program, on the real board, doing exactly what it did on screen a minute ago.

### 5. Snake (10:30–11:30) — Wren_01_05_Snake.html

Click to slide 5.

> Snake plays itself. It heads for the food, but it also counts the ways out of every square, so it doesn't steer itself into a dead end.

> The trick for a small chip is the grid. Every cell is one bit, so checking a move is a lookup, not a search through the snake's body.

### 5b. Snake on the Thing (11:30–12:30) — Wren_01_05b_Snake.png

Switch to the cut-away scene. Take the jumpers off. **File → Open…**
`programs/OledThing/Snake.bas`, then **Ctrl+F5**. Let it play until it eats a few times; if
it boxes itself in, let GAME OVER and the score show.

> Here it is on the board. Every seventy milliseconds it looks around, picks a way, and moves.

> When it finally traps itself, it shows its score and starts again.

### 6. The debugger, on the real chip (12:30–13:30) — Wren_01_06_Debugger.html

Click to slide 6.

> This is the part I'm proudest of. The debugger works on the real chip, not just the emulator.

> Debuggers for chips this small usually need a monitor built into the firmware, eating memory you don't have. This one costs nothing, because the silicon was built for it.

> The catch is in the middle: two at a time. For a program this size, two go a long way.

### 6b. Debugging Snake (13:30–15:30) — Wren_01_06b_DebugSnake.png

Switch to the cut-away scene, Snake still running on the Thing. Click left of line 83's
number for a breakpoint:

```
  IF nx = fx AND ny = fy THEN
    score = score + 1
```

Snake stops the moment it reaches the food. Point to `score`, `length`, `fx` and `fy` in
**Variables**, then press **F10** twice: `score` goes up by one, then `length`.
Press **F5**: the snake runs on to the next food and stops on line 83 again. Click the
breakpoint off, **F5**, and it plays on.

> I'll put a breakpoint on the line where it scores. Now it stops the instant it reaches the food.

> These are the live variables, read straight out of the chip's RAM. There's the score, the length, and where the food is.

> Step over, and the score goes up. Step again, and it's a cell longer.

> Carry on, and it runs to the next meal and stops again. On a chip with one kilobyte of RAM, that still feels like magic to me.

### 7. Pong (15:30–16:15) — Wren_01_07_Pong.html

Click to slide 7.

> Pong, also playing itself. If both paddles were perfect, it would never end.

> So each paddle guesses, and it's a little slow. A steep shot or a bad guess beats it, and that's what makes it fun to watch.

### 7b. Pong on the Thing (16:15–17:00) — Wren_01_07b_Pong.png

Switch to the cut-away scene. **File → Open…** `programs/OledThing/Pong.bas`, **Ctrl+F5**.
Let a few points play.

> Watch the paddles. One gets there late, the other gets there wrong, and that's all it takes to make a game of it.

### 8. Hex in a font of its own (17:00–18:00) — Wren_01_08_RotatedHex.html

Click to slide 8.

> The kernel has fonts of its own. This program ignores them, and draws every digit from a font it defines itself, one row to a binary number.

> Look at the gold pixels. Without them, the curves would be staircases. And because every rectangle goes through one routine that turns it, the other angles cost no second font.

### 8b. RotatedHex in the emulator and on the Thing (18:00–19:00) — Wren_01_08b_RotatedHex.png

Switch to the cut-away scene. **File → Open…** `programs/OledThing/RotatedHex.bas`.
**Debug → Target → Emulator**, **F5**: set some inputs. Stop (**Shift+F5**), change the first
line of code to `CONST ANGLE = 0`, **F5** again: the same digits, upright. Change it back to
`CONST ANGLE = 90`, **Debug → Target → Thing on the programmer**, **Ctrl+F5**, and touch
the loose wire to a b input or two on the board.

> Angles are cheap in the emulator, so I try them there.

> Ninety is the program's own setting, so that's the one that goes on the board.

### 9. The LED Thing (19:00–21:30) — Wren_01_09_LedThing.html

Click to slide 9.

> The LED Thing has the same chip, so it gets the same kernel, built for its board. Everything to do with the screen simply drops out.

> Its eight LEDs hang off just four pins. Only one is ever lit at a time; the kernel scans them faster than your eye can follow.

> That gives a trick for brightness. Light an LED every other scan, and it glows at half strength.

### 9b. The LED Thing live (part of 19:00–21:30) — Wren_01_09b_LedThing.png

Switch to the cut-away scene, the camera on the LED Thing on the programmer. **Board →
LED Thing**. **File → Open…** `programs/LedThing/LedThing.bas`, **Ctrl+F5**. Pull **M0** to
GND and let it go: the pattern changes with the mode. Then **File → Open…**
`programs/LedThing/Cylon.bas`, **Ctrl+F5**.

> This is the LED Thing's own behaviour, rewritten in BASIC like the other one. Change the mode pins, and the pattern changes.

> And this is the half-brightness trick: a Cylon eye, with a soft glow either side.

### 10. Thanks for watching (21:30–22:00) — Wren_01_10_Closing.html

Click to slide 10.

> The link is in the description. If you have a Thing, give it a try. If you don't, the emulator runs every program you've seen.

> Next time, I'll write one from scratch. Thanks for watching.

---

## Shot list

| When | Backdrop | Console frame | Camera frame |
|---|---|---|---|
| 5:00–7:00 | `Wren_01_03b_Emulator.png` | Wren running `OledThing.bas` on the emulator: Inputs, Mode pins, the OLED view | none: the backdrop's card |
| 8:30–10:30 | `Wren_01_04b_Install.png` | Wren's Firmware window: Connect, Install kernel, its log; then the main window, Ctrl+F5 | the OLED Thing: factory display, then Wren's; the jumpers going on, the die rolling |
| 11:30–12:30 | `Wren_01_05b_Snake.png` | Wren with `Snake.bas` open | the OLED Thing playing Snake |
| 13:30–15:30 | `Wren_01_06b_DebugSnake.png` | Wren debugging `Snake.bas`: the breakpoint, Variables, F10, F5 | the OLED Thing, stopped at the food and moving on |
| 16:15–17:00 | `Wren_01_07b_Pong.png` | Wren with `Pong.bas` open | the OLED Thing playing Pong |
| 18:00–19:00 | `Wren_01_08b_RotatedHex.png` | Wren: `RotatedHex.bas` on the emulator at 90° and 0°, then Ctrl+F5 | the OLED Thing showing the inputs at 90° |
| 20:15–21:30 | `Wren_01_09b_LedThing.png` | Wren on **Board → LED Thing**: `LedThing.bas`, then `Cylon.bas` | the LED Thing: the modes, then the Cylon eye |

Nothing else is filmed.
