' The event queue, overflowing on purpose. A timer asks for a handler every 2 milliseconds.
' For 5 seconds the handler is light and keeps up; for the next 5 it is heavy, takes longer
' than 2 milliseconds, and the queue fills: the events that find no room are lost, and LOST
' counts them. Then light again, and round. Each second the screen shows which it was, how
' many handlers ran, and how many events were lost.

CONST SPIN = 30                 ' the heavy handler's extra work: loops of nothing much

DIM runs, before, rate          ' handlers run, the count a second ago: per second
DIM gone, lostRate              ' LOST a second ago: lost per second
DIM lastDraw, seconds, heavy, busy

ON EVERY 2 MS
  DIM k
  runs = runs + 1
  IF heavy THEN
    FOR k = 1 TO SPIN
      busy = busy + k
    NEXT
  END IF
  IF MILLIS - lastDraw >= 1000 THEN Draw
END ON

' Once a second: what the last second did, then whether the next one is heavy.
SUB Draw
  lastDraw = MILLIS
  rate = runs - before
  before = runs
  lostRate = LOST - gone
  gone = LOST
  CLS
  IF heavy THEN TEXT 0, 0, "HEAVY", NORMAL ELSE TEXT 0, 0, "LIGHT", NORMAL
  TEXT 0, 12, "RAN", SMALL
  DEC 20, 12, rate, SMALL
  TEXT 44, 12, "/S", SMALL
  TEXT 0, 22, "LOST", SMALL
  DEC 20, 22, lostRate, SMALL
  TEXT 44, 22, "/S", SMALL
  SHOW
  seconds = seconds + 1
  heavy = (seconds / 5) MOD 2
END SUB
