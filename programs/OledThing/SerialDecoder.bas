' A serial decoder: an Arduino Mega (firmware/SerialSource) sends bytes one bit at a time,
' as SPI does. PD2 is the select, low for the eight bits of a byte; each rising edge on PD3,
' the clock, brings the next bit on b0, most significant first. The bits so far show at the
' top, as they arrive; finished bytes go to a log of the last ten.

CONST KEEP = 10                 ' bytes in the log

DIM shift, have                 ' the byte coming in, and how many of its bits so far
DIM heard(10) AS BYTE           ' finished bytes, a ring
DIM oldest, taken               ' where the next goes, and how many since start
DIM fresh                       ' TRUE when the screen is behind

' The select going low: a new byte begins.
ON LATCH PD2 FALLING
  shift = 0
  have = 0
  fresh = TRUE
END ON

' A clock edge while selected: one more bit, from b0.
ON LATCH PD3 RISING WHILE PD2 LOW
  shift = (shift * 2 + (VALUE AND 1)) AND 255
  have = have + 1
  IF have = 8 THEN
    heard(oldest) = shift
    oldest = (oldest + 1) MOD KEEP
    taken = taken + 1
  END IF
  fresh = TRUE
END ON

ON EVERY 20 MS
  DIM i
  IF NOT fresh THEN EXIT ON
  fresh = FALSE
  CLS
  ' The bits so far, newest on the right; a line; the log, oldest first; the count.
  IF have > 0 THEN BIN 0, 0, shift, have, NORMAL
  BOX 0, 9, 64, 1
  FOR i = 0 TO KEEP - 1
    IF taken > KEEP - 1 - i THEN
      HEX (i MOD 5) * 13, 12 + (i / 5) * 7, heard((oldest + i) MOD KEEP), 2, SMALL
    END IF
  NEXT
  TEXT 0, 26, "BYTES", SMALL
  DEC 24, 26, taken, SMALL
  SHOW
END ON
