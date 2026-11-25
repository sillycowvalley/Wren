' How fast can the Thing latch? An Arduino Mega (firmware/LatchFeeder, in its count mode)
' strobes 00, 01, 02 ... FF, 00 ... onto b0-b7 with rising edges on PD3, faster and faster.
' This locks onto the count and checks every byte: a wrong one, or one missing, stops it,
' showing what came, what was due, and the rate it failed at. PD2 (a button to GND) starts
' it again.

DIM expected, locked            ' the next byte due, once the first one has set it
DIM failed, got, want           ' TRUE after an error: what came, and what was due
DIM strobes, before, rate       ' strobes seen, and the count a second ago: per second
DIM failRate                    ' the rate when it failed

ON LATCH PD3 RISING
  strobes = strobes + 1
  IF failed THEN EXIT ON
  IF locked AND VALUE <> expected THEN
    failed = TRUE
    got = VALUE
    want = expected
    failRate = rate
  END IF
  expected = (VALUE + 1) AND 255
  locked = TRUE
END ON

ON PRESS PD2
  failed = FALSE
  locked = FALSE
END ON

' Once a second: the rate, and the screen.
ON EVERY 1000 MS
  rate = strobes - before
  before = strobes
  CLS
  IF failed THEN
    TEXT 0, 0, "FAILED", NORMAL
    TEXT 0, 10, "GOT", SMALL
    HEX 16, 10, got, 2, SMALL
    TEXT 28, 10, "WANT", SMALL
    HEX 48, 10, want, 2, SMALL
    TEXT 0, 18, "AT", SMALL
    DEC 12, 18, failRate, SMALL
    TEXT 32, 18, "/S", SMALL
    TEXT 0, 26, "LOST", SMALL
    DEC 20, 26, LOST, SMALL                  ' strobes dropped by a full event queue
  ELSE
    DEC 0, 0, rate, LARGE
    TEXT 0, 18, "STROBES A SECOND", SMALL
    IF locked THEN TEXT 0, 26, "ALL GOOD", SMALL ELSE TEXT 0, 26, "WAITING", SMALL
  END IF
  SHOW
END ON
