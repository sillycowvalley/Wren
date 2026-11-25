' A stopwatch, written as a tour of Wren BASIC: events, SUBs, a FUNCTION, the fonts, a
' bar and the mode pins. Pull HEX (PA1) low to start or stop it, BAR (PA2) low to set it
' back to 0. The best time stopped so far is kept in the EEPROM, so it survives a power
' cut.

CONST SLOT = 0                  ' the two setting bytes that hold the best time

DIM hundredths, seconds         ' the time so far, up to 99.99 s
DIM running                     ' TRUE while it counts
DIM last, spare                 ' MILLIS at the last look, and milliseconds not yet counted

ON START
  Draw
END ON

' The time comes from MILLIS, not from counting these ticks: drawing takes a few ms, so
' a tick can come late, but the clock never does.
ON EVERY 20 MS
  Tick
  IF running THEN Draw
END ON

ON PRESS PA1
  Tick
  running = NOT running
  IF NOT running THEN Remember
  Draw
END ON

ON PRESS PA2
  running = FALSE
  hundredths = 0
  seconds = 0
  spare = 0
  Draw
END ON

' Adds the time since the last look, if it is running.
SUB Tick
  DIM now
  now = MILLIS
  IF running THEN
    spare = spare + (now - last)             ' right across MILLIS wrapping round
    WHILE spare >= 10
      spare = spare - 10
      hundredths = hundredths + 1
      IF hundredths = 100 THEN
        hundredths = 0
        seconds = (seconds + 1) MOD 100
      END IF
    WEND
  END IF
  last = now
END SUB

' The time in large digits, the hundredths as a bar, and the best time in small ones.
SUB Draw
  DIM b
  CLS
  Two 6, 0, seconds, 12, LARGE
  FILL 31, 12, 3, 3                          ' the decimal point
  Two 36, 0, hundredths, 12, LARGE
  BAR 0, 19, 64, 4, hundredths, 99
  TEXT 0, 26, "BEST", SMALL
  b = Best
  IF b > 0 THEN
    Two 20, 26, b / 100, 4, SMALL
    PIXEL 28, 30
    Two 30, 26, b MOD 100, 4, SMALL
  ELSE
    TEXT 20, 26, "--", SMALL
  END IF
  SHOW
END SUB

' v as two digits, 00 to 99, each w pixels apart.
SUB Two(x, y, v, w, font)
  DEC x, y, v / 10, font
  DEC x + w, y, v MOD 10, font
END SUB

' The best time in hundredths of a second, from the EEPROM; 0 for none yet.
FUNCTION Best
  DIM t
  t = EEREAD(SLOT) * 256 + EEREAD(SLOT + 1)
  IF t < 1 OR t > 9999 THEN RETURN 0
  RETURN t
END FUNCTION

' Keeps this time if it is the best so far.
SUB Remember
  DIM t
  t = seconds * 100 + hundredths
  IF t > 0 AND (Best = 0 OR t < Best) THEN
    EEWRITE SLOT, t SHR 8
    EEWRITE SLOT + 1, t AND 255
  END IF
END SUB
