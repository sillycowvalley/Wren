# Testbenches

A testbench is another circuit's signals, played into the emulated Thing's pins as the
program runs: what a computer's bus, a sensor or a test ROM would do to b0-b7 and the spare
pins, and when. It lets a program be tried against the real sequence, at its real speed,
before it is uploaded to a Thing.

## In the app

- **Debug, Open Testbench…** chooses a `.bench` file. With the program running, the
  emulated Thing is powered on again, so the testbench plays from its start; otherwise F5
  starts both.
- The testbench runs on the Thing's own clock, from power-on: **Restart** and **Power cycle**
  play it again from the start. Breakpoints, stepping and Start Without Debugging all work
  as usual; each change is made at its time, however the program is stepped.
- When it ends, Messages says so. The pins stay as it left them, and the inputs panel can
  change them by hand again.
- **Debug, Close Testbench** stops it. Changing the board closes it too: a testbench is for
  one board.
- It plays into the emulator only, not into a Thing on the programmer.

## The file

Plain text, a line at a time; `#` starts a comment.

```
# The Touchstone-86's DS1, a RAM failure (written by TTLSim)
THING OledThing
0.000 B=5A
250.024 B=00
1257.406 B=FF
1262.708 B=02 PD3=0
END 1848
```

| Line | Meaning |
|---|---|
| `THING OledThing` or `THING LedThing` | The board it is for; OLED Thing if left out |
| `ms PIN=value …` | At `ms` milliseconds after power-on (to the microsecond: three decimals), set each pin given. Several on one line change together. Times must not go back |
| `END ms` | When it ends; if left out, at its last change |

| Pin | Board | Value |
|---|---|---|
| `B` | both | b0-b7 as a hex byte, `00` to `FF` (`$` allowed) |
| `HEX`, `BAR`, `INV`, `PD2`, `PD3` | OLED Thing | `0` pulled to GND, `1` let go |
| `M0`, `M1`, `M2` | LED Thing | `0` pulled to GND, `1` let go |

A mistake is named by its line in Messages, and the testbench is not opened.

## In the tests

`Wren.Script`'s `Testbench` reads a file and `TestbenchPlayer` plays it into a
`ThingSession`: `player.Run(session, seconds, session.RunFree)` runs for that long, split at
each change. `TestbenchTests` holds the format and the timing; `TouchstoneTests` plays the
Touchstone-86's testbenches (`docs/Touchstone.md`).
