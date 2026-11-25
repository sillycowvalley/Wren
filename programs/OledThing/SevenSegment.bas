' The inputs as two hex digits in the style of a seven-segment display, at any of four
' angles. No font is used: each digit is seven bars, and every bar is a rectangle drawn
' through Seg, which turns the upright picture to the angle. Turned 90 or 270 degrees the
' picture is 32 wide and 64 tall, and the digits stand one above the other.
'
' ANGLE sets the angle: 0, 90, 180 or 270 (clockwise). Set it to -1 to choose the angle
' with the mode pins instead:
'
'   PA1 (HEX)  PA2 (BAR)   angle
'     open       open        90
'     low        open       180
'     open       low        270
'     low        low          0
'
' PA3 (INV) low inverts the display.

CONST ANGLE = 90

DIM turn                ' quarter turns clockwise: 0 to 3
DIM shown               ' the byte on screen

ON START
  shown = INPUTS
  Draw
END ON

ON CHANGE
  shown = VALUE
  Draw
END ON

ON PINS
  Draw
END ON

SUB Draw
  IF ANGLE < 0 THEN
    turn = (1 + (1 - PIN(PA1)) + 2 * (1 - PIN(PA2))) AND 3
  ELSE
    turn = (ANGLE / 90) AND 3
  END IF
  CLS
  IF turn = 0 OR turn = 2 THEN
    Digit shown SHR 4, 9, 1                  ' 64 x 32: side by side
    Digit shown AND 15, 37, 1
  ELSE
    Digit shown SHR 4, 7, 1                  ' 32 x 64: one above the other
    Digit shown AND 15, 7, 33
  END IF
  INVERSE NOT PIN(PA3)
  SHOW
END SUB

' Hex digit n, 18 wide and 30 tall with bars 4 thick, its top-left at (x, y) on the
' upright picture. The bars stop short of each other, as on a real display.
SUB Digit(n, x, y)
  DIM bars
  bars = Bars(n)
  IF bars AND 1 THEN Seg x + 4, y, 10, 4                ' a: top
  IF bars AND 2 THEN Seg x + 14, y + 4, 4, 9            ' b: top right
  IF bars AND 4 THEN Seg x + 14, y + 17, 4, 9           ' c: bottom right
  IF bars AND 8 THEN Seg x + 4, y + 26, 10, 4           ' d: bottom
  IF bars AND 16 THEN Seg x, y + 17, 4, 9               ' e: bottom left
  IF bars AND 32 THEN Seg x, y + 4, 4, 9                ' f: top left
  IF bars AND 64 THEN Seg x + 4, y + 13, 10, 4          ' g: middle
END SUB

' The bars lit for hex digit n: bit 0 is a, the top, round to bit 6, g, the middle.
FUNCTION Bars(n)
  IF n = 0 THEN RETURN $3F
  IF n = 1 THEN RETURN $06
  IF n = 2 THEN RETURN $5B
  IF n = 3 THEN RETURN $4F
  IF n = 4 THEN RETURN $66
  IF n = 5 THEN RETURN $6D
  IF n = 6 THEN RETURN $7D
  IF n = 7 THEN RETURN $07
  IF n = 8 THEN RETURN $7F
  IF n = 9 THEN RETURN $6F
  IF n = 10 THEN RETURN $77                   ' A
  IF n = 11 THEN RETURN $7C                   ' b
  IF n = 12 THEN RETURN $39                   ' C
  IF n = 13 THEN RETURN $5E                   ' d
  IF n = 14 THEN RETURN $79                   ' E
  RETURN $71                                  ' F
END FUNCTION

' A bar of the upright picture, filled on the screen turned clockwise by turn quarter
' turns. A turned rectangle is a rectangle.
SUB Seg(x, y, w, h)
  IF turn = 0 THEN
    FILL x, y, w, h
  ELSEIF turn = 1 THEN
    FILL 64 - y - h, x, h, w
  ELSEIF turn = 2 THEN
    FILL 64 - x - w, 32 - y - h, w, h
  ELSE
    FILL y, 32 - x - w, h, w
  END IF
END SUB
