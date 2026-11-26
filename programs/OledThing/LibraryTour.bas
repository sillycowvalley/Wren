' A tour of the kernel's drawing library, one thing every 3 seconds: pixels, lines, boxes
' and fills, inverting, the four fonts, the numbers in hex, decimal and binary, and the bit
' cells and bar. Each is one statement in BASIC and one routine in the kernel.

CONST PAUSE = 3000              ' ms on each

DIM page, shown

ON START
  Draw
END ON

ON EVERY 100 MS
  DIM i
  shown = shown + 100
  IF shown >= PAUSE THEN
    shown = 0
    page = (page + 1) MOD 8
    Draw
  END IF
  IF page = 6 THEN Numbers      ' the numbers keep counting
END ON

SUB Draw
  DIM i
  CLS
  IF page = 0 THEN
    TEXT 0, 0, "PIXEL", SMALL
    FOR i = 0 TO 120
      PIXEL RND(64), 7 + RND(25)
    NEXT
  ELSEIF page = 1 THEN
    TEXT 0, 0, "LINE", SMALL
    FOR i = 0 TO 63 STEP 8
      LINE 0, 31, i, 7
    NEXT
  ELSEIF page = 2 THEN
    TEXT 0, 0, "BOX FILL", SMALL
    BOX 2, 9, 26, 20
    FILL 36, 9, 26, 20
  ELSEIF page = 3 THEN
    TEXT 0, 0, "INVERT", SMALL
    FILL 0, 9, 32, 22
    TEXT 4, 14, "WREN", NORMAL
    INVERT 16, 7, 48, 25
  ELSEIF page = 4 THEN
    TEXT 0, 0, "SMALL", SMALL
    TEXT 0, 7, "NORMAL", NORMAL
    TEXT 0, 16, "LARGE", LARGE
  ELSEIF page = 5 THEN
    TEXT 0, 0, "BIG", SMALL
    TEXT 14, 0, "42", BIG
  ELSEIF page = 6 THEN
    Numbers
  ELSE
    TEXT 0, 0, "BITS BAR", SMALL
    BITS 0, 9, 64, 10, $A5
    BAR 0, 23, 64, 7, 2, 3
  END IF
  SHOW
END SUB

' Hex, decimal and binary of a count, live.
SUB Numbers
  DIM v
  v = (MILLIS / 100) AND 255
  CLS
  TEXT 0, 0, "HEX", SMALL
  HEX 20, 0, v, 2, SMALL
  TEXT 0, 8, "DEC", SMALL
  DEC 20, 8, v, SMALL
  TEXT 0, 16, "BIN", SMALL
  BIN 0, 23, v, 8, SMALL
  SHOW
END SUB
