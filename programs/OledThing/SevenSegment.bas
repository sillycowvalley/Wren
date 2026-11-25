' The inputs as two hex digits in the style of a seven-segment display, side by side, at
' any of four angles. No font is used: each digit is seven bars, and every bar is a
' rectangle drawn through Seg, which turns the upright picture to the angle. Turned 90 or
' 270 degrees the picture is 32 wide and 64 tall, so the digits are smaller there.
'
' Where the bars meet, a digit with a loop (0, 6, 8, 9, A, b, d) keeps the empty corners of
' a real display; the others fill each corner where two or more of their bars meet, so 7
' and F get square corners and 1 one unbroken bar.

CONST ANGLE = 90                ' 0, 90, 180 or 270 degrees, clockwise
CONST LOOPS = $2F41             ' bit n set: digit n has a loop (0, 6, 8, 9, A, b, d)

DIM turn                        ' quarter turns clockwise: 0 to 3
DIM shown                       ' the byte on screen

ON START
  turn = (ANGLE / 90) AND 3
  shown = INPUTS
  Draw
END ON

ON CHANGE
  shown = VALUE
  Draw
END ON

SUB Draw
  CLS
  IF turn = 0 OR turn = 2 THEN
    Digit shown SHR 4, 9, 1, 18, 30, 4       ' 64 x 32: digits 18 x 30, bars 4 thick
    Digit shown AND 15, 37, 1, 18, 30, 4
  ELSE
    Digit shown SHR 4, 1, 19, 13, 25, 3      ' 32 x 64: digits 13 x 25, bars 3 thick
    Digit shown AND 15, 17, 19, 13, 25, 3
  END IF
  SHOW
END SUB

' Hex digit n, w wide and h tall with bars t thick, its top-left at (x, y) on the upright
' picture. The bars stop short of each other, as on a real display.
SUB Digit(n, x, y, w, h, t)
  DIM bars, mid
  bars = Bars(n)
  mid = (h - t) / 2                           ' the middle bar's top
  IF bars AND 1 THEN Seg x + t, y, w - 2 * t, t                         ' a: top
  IF bars AND 2 THEN Seg x + w - t, y + t, t, mid - t                   ' b: top right
  IF bars AND 4 THEN Seg x + w - t, y + mid + t, t, h - mid - 2 * t     ' c: bottom right
  IF bars AND 8 THEN Seg x + t, y + h - t, w - 2 * t, t                 ' d: bottom
  IF bars AND 16 THEN Seg x, y + mid + t, t, h - mid - 2 * t            ' e: bottom left
  IF bars AND 32 THEN Seg x, y + t, t, mid - t                          ' f: top left
  IF bars AND 64 THEN Seg x + t, y + mid, w - 2 * t, t                  ' g: middle
  IF ((LOOPS SHR n) AND 1) = 0 THEN
    Corner bars, 33, x, y, t                  ' top left: a, f
    Corner bars, 3, x + w - t, y, t           ' top right: a, b
    Corner bars, 112, x, y + mid, t           ' middle left: f, e, g
    Corner bars, 70, x + w - t, y + mid, t    ' middle right: b, c, g
    Corner bars, 24, x, y + h - t, t          ' bottom left: e, d
    Corner bars, 12, x + w - t, y + h - t, t  ' bottom right: c, d
  END IF
END SUB

' Fills the t x t corner at (x, y) when two or more of the bars that meet there (mask) are lit.
SUB Corner(bars, mask, x, y, t)
  DIM lit
  lit = bars AND mask
  IF (lit AND (lit - 1)) <> 0 THEN Seg x, y, t, t
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
