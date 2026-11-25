# Wren BASIC 4: Snake, inside the emulator

## YouTube

**Title**: Snake, taken apart in an emulator | Wren BASIC 4

**Description**:

```
No board, no wear: Wren's emulator runs the real kernel of Digicool Things' 8-bit OLED Thing, instruction by instruction, with the debugger on top. Here it takes apart Snake, a game that plays itself in 834 bytes of BASIC.

In this video:
- what's inside the emulated Thing, and everything you can poke at
- how Snake chooses its next move, stopped in the middle of choosing
- a grid of 465 cells in 60 bytes: one bit a cell
- a ring of 64 for the snake's body
- catching the exact moment it boxes itself in

Snake is programs/OledThing/Snake.bas.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #Emulator #Debugging #Snake #Microcontroller #RetroComputing
```

---

## SNAKE IN THE EMULATOR

> Snake plays itself, in eight hundred and thirty-four bytes of BASIC. Today we take it apart, without a board in sight.

## A Thing in software

> The emulator isn't a picture of the board. It runs the very same kernel bytes the real chip runs, one instruction at a time, and models the display, the timers and the pins around it.

> The processor was checked against another emulator, instruction by instruction, so when something works here, it works on the chip.

> And nothing here wears out. Run it a thousand times; the real board never knows.

## Everything you can touch

> This is the whole window. The program on the left, the board in the middle, and on the right, the line it's on and what its variables hold.

> Every pin a program can read, you can set from here, and the debugging keys are the ones you already know.

## Snake on the emulator

*Open Snake.bas, F5.*

> Here it is, running exactly as it would on the board.

## How Snake chooses

> Every seventy milliseconds the snake asks the same question: which way next? It never turns straight back, and it never moves into a wall or into itself.

> Every way that's left gets a score. Being close to the food brings the score down, and so does having lots of room around you. A dead end adds a big penalty, and going straight on breaks a tie. The lowest score wins.

> Here it's heading right, and the food is just above. Up has the lowest score, so up wins.

## Inside Move

*Click left of line 68 for a breakpoint, F5.*

> I'll stop it right where it compares the scores. These are Move's own variables: which way it's trying, where that would take it, and the score.

*F5, F5.*

> Carry on, and the next way comes round, then the next. You can watch it change its mind.

*Click line 68's breakpoint off and put one on line 64. F5, then F11, then F10 twice.*

> And step into Taken, the function that asks: is that cell free? There's the cell it works out, as a single number.

## One bit a cell

> The board is thirty-one cells by fifteen. Four hundred and sixty-five cells, and the snake needs to know, for each one, whether it's taken.

> So every cell is one bit. Sixty bytes for the whole board. Checking a cell is a multiply, a shift and a mask; it never has to look through the snake.

> Here's the snake from a moment ago: six cells, which land in just two bytes.

## A ring for the body

> The body is a ring of sixty-four places. The head moves on one place each step, and the tail follows on behind it.

> Nothing is ever shifted along. When the snake moves, one cell is written at the head, and one is cleared at the tail. That's why a long snake costs no more time than a short one.

## Catching the end

> Sooner or later the snake traps itself. In the code, that's the moment no direction is left: the best direction is still minus one, which means none was found.

> Put a breakpoint on that line, and you catch the very last move of the game.

## The last move

*Click left of line 76 for a breakpoint, F5. Wait; cut the wait in the edit.*

> And there it is. Every way out is taken, and the screen still shows the trap it built for itself.

*F11.*

> One step more, and we're into GameOver, the code that puts up GAME OVER and the score.

## No board needed

> Everything today happened with no board at all, and every pin the program reads can be set from the window.

> When the program's right, the board gets it. One upload, and no wasted writes.

## What the emulator brings

> A Thing in software, with the debugger on top: stop it anywhere, look at anything, and wear nothing out.

> Next time: how Wren makes those uploads count, on a flash that only takes about a hundred writes. Thanks for watching.
