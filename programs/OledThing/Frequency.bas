' A frequency counter for a clock on PD3, from about 3 to 50 Hz. Upload it, unplug the
' Pico, and power the Thing from the clock board (the Thing runs on 2.95 to 5.5 V): its 5 V
' to the Thing's VCC, GND to GND (J5 pin 1), and the clock straight to PD3 (J5 pin 4).
'
' At these speeds, counting edges for one second would be coarse: only whole hertz, so 3.5 Hz
' reads 3 or 4.
' So this times them instead: every 2 seconds or so it divides the periods counted by the
' time they took, to a tenth of a hertz.

CONST WINDOW = 2000             ' ms: each reading spans at least this long

DIM first, periods              ' the window: MILLIS at its first edge, and periods since
DIM tenths, ptenths             ' the last reading: tenths of a hertz, tenths of a ms a period
DIM edges, last                 ' all the rising edges, and MILLIS at the latest
DIM running                     ' TRUE while edges keep coming

ON LATCH PD3 RISING
  DIM now, span
  now = MILLIS
  edges = edges + 1
  IF NOT running THEN
    running = TRUE
    first = now
    periods = 0
  ELSE
    periods = periods + 1
    span = now - first
    IF span >= WINDOW THEN
      tenths = Divide(periods * 100, span)   ' periods x 10000 / span, without overflowing
      ptenths = span * 10 / periods
      first = now
      periods = 0
    END IF
  END IF
  last = now
END ON

ON EVERY 200 MS
  IF running AND MILLIS - last > 3000 THEN running = FALSE   ' the clock has stopped
  Draw
END ON

' a x 100 / b, rounded, one digit at a time, so no step goes past 32767 (b up to 3276).
FUNCTION Divide(a, b)
  DIM q, r, d1, d2
  q = a / b
  r = a MOD b
  d1 = r * 10 / b
  r = r * 10 MOD b
  d2 = r * 10 / b
  r = r * 10 MOD b
  IF r * 2 >= b THEN d2 = d2 + 1             ' round the last digit
  RETURN q * 100 + d1 * 10 + d2
END FUNCTION

SUB Draw
  DIM w
  CLS
  IF NOT running THEN
    TEXT 0, 0, "No clock"
  ELSEIF tenths = 0 THEN
    TEXT 0, 0, "Timing..."
  ELSE
    w = 12
    IF tenths >= 100 THEN w = 24
    DEC 0, 0, tenths / 10, LARGE
    FILL w + 1, 12, 3, 3                     ' the decimal point
    DEC w + 6, 0, tenths MOD 10, LARGE
    TEXT w + 22, 0, "Hz", LARGE
    TEXT 0, 18, "PERIOD", SMALL
    w = 4                                    ' the whole ms, right-aligned to x = 40
    IF ptenths >= 100 THEN w = 8
    IF ptenths >= 1000 THEN w = 12
    DEC 40 - w, 18, ptenths / 10, SMALL
    PIXEL 40, 22
    DEC 42, 18, ptenths MOD 10, SMALL
    TEXT 48, 18, "MS", SMALL
  END IF
  TEXT 0, 26, "EDGES", SMALL
  DEC 24, 26, edges, SMALL
  SHOW
END SUB
