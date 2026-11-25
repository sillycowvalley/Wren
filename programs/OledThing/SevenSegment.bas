' The inputs as two hex digits in the style of a seven-segment display, with b0-b7 as bit
' cells below them, like Digicool's own readout, at any of four angles. No font is used:
' each digit is seven bars, and every bar and cell is a rectangle drawn through Area,
' which turns the upright picture to the angle. Turned 90 or 270 degrees the picture is
' 32 wide and 64 tall, and the digits stand one above the other.
'
'   PA1 (HEX)  PA2 (BAR)   angle
'     open       open        90   (the Thing standing on its side)
'     low        open       180
'     open       low        270
'     low        low          0
'   PA3 (INV) low inverts the display.

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
  DIM i
  turn = (1 + (1 - PIN(PA1)) + 2 * (1 - PIN(PA2))) AND 3
  CLS
  IF turn = 0 OR turn = 2 THEN
    ' 64 x 32: the digits side by side, the bit cells across the bottom
    Digit shown SHR 4, 11, 1, 18, 22, 3
    Digit shown AND 15, 35, 1, 18, 22, 3
    FOR i = 0 TO 7
      Area 8 * i + 1, 26, 6, 5, (shown SHR (7 - i)) AND 1
    NEXT
  ELSE
    ' 32 x 64: the digits one above the other, the bit cells across the bottom
    Digit shown SHR 4, 5, 1, 22, 24, 4
    Digit shown AND 15, 5, 29, 22, 24, 4
    FOR i = 0 TO 7
      Area 4 * i, 57, 3, 6, (shown SHR (7 - i)) AND 1
    NEXT
  END IF
  INVERSE NOT PIN(PA3)
  SHOW
END SUB

' Hex digit n with its top-left at (x, y) on the upright picture, w x h, bars t thick.
SUB Digit(n, x, y, w, h, t)
  DIM bars, mid
  bars = Bars(n)
  mid = (h - t) / 2                           ' the middle bar's top
  IF bars AND 1 THEN Area x + t, y, w - 2 * t, t, 1                         ' a: top
  IF bars AND 2 THEN Area x + w - t, y + t, t, mid - t, 1                   ' b: top right
  IF bars AND 4 THEN Area x + w - t, y + mid + t, t, h - mid - 2 * t, 1     ' c: bottom right
  IF bars AND 8 THEN Area x + t, y + h - t, w - 2 * t, t, 1                 ' d: bottom
  IF bars AND 16 THEN Area x, y + mid + t, t, h - mid - 2 * t, 1            ' e: bottom left
  IF bars AND 32 THEN Area x, y + t, t, mid - t, 1                          ' f: top left
  IF bars AND 64 THEN Area x + t, y + mid, w - 2 * t, t, 1                  ' g: middle
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

' A rectangle of the upright picture, filled (full = 1) or outlined (full = 0), drawn on
' the screen turned clockwise by turn quarter turns. A turned rectangle is a rectangle.
SUB Area(x, y, w, h, full)
  DIM sx, sy, sw, sh
  IF turn = 0 THEN
    sx = x : sy = y : sw = w : sh = h
  ELSEIF turn = 1 THEN
    sx = 64 - y - h : sy = x : sw = h : sh = w
  ELSEIF turn = 2 THEN
    sx = 64 - x - w : sy = 32 - y - h : sw = w : sh = h
  ELSE
    sx = y : sy = 32 - x - w : sw = h : sh = w
  END IF
  IF full THEN FILL sx, sy, sw, sh ELSE BOX sx, sy, sw, sh
END SUB
