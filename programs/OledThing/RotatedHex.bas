' The inputs as two hex digits in a clean font of its own, side by side, at any of four
' angles. The font is defined here: each digit is 5 x 9 pixels, drawn three times the
' size (15 x 27), and smoothed as it is: where two lit pixels meet only at a corner, the
' corner between them is filled, so rounded corners and diagonals are not stairs. Every
' rectangle goes through Seg, which turns the upright picture to the angle.

CONST ANGLE = 90                ' 0, 90, 180 or 270 degrees, clockwise

DIM font(48)                    ' three words a digit: rows 0-2, 3-5, 6-8, five bits a row
DIM rows(9) AS BYTE             ' the digit being drawn, a byte a row, bit 4 the leftmost
DIM turn                        ' quarter turns clockwise: 0 to 3
DIM shown                       ' the byte on screen

ON START
  turn = (ANGLE / 90) AND 3
  Fonts
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
    Digit shown SHR 4, 15, 2                 ' 64 x 32
    Digit shown AND 15, 34, 2
  ELSE
    Digit shown SHR 4, 0, 18                 ' 32 x 64
    Digit shown AND 15, 17, 18
  END IF
  SHOW
END SUB

' Hex digit n with its top-left at (x, y) on the upright picture.
SUB Digit(n, x, y)
  DIM r, c, run
  FOR r = 0 TO 8
    rows(r) = (font(n * 3 + r / 3) SHR ((2 - r MOD 3) * 5)) AND 31
  NEXT
  FOR r = 0 TO 8
    run = 0                                  ' the row's runs of lit pixels
    FOR c = 0 TO 5
      IF c < 5 AND ((rows(r) SHR (4 - c)) AND 1) <> 0 THEN
        run = run + 1
      ELSEIF run > 0 THEN
        Seg x + 3 * (c - run), y + 3 * r, 3 * run, 3
        run = 0
      END IF
    NEXT
    Smooth r, x, y + 3 * r
  NEXT
END SUB

' The smoothing for row r, a whole row at a time: an unlit pixel whose lit neighbours
' above (or below) and to one side meet only at its corner, with the other two sides unlit,
' gets that corner, one screen pixel. Rows are shifted one place left and right so that
' each pixel lines up with its neighbours; bits 1-5 are the row's five pixels.
SUB Smooth(r, x, y)
  DIM here, up, down, left, right, dark, c, bit
  here = rows(r) * 2
  IF r > 0 THEN up = rows(r - 1) * 2
  IF r < 8 THEN down = rows(r + 1) * 2
  left = here SHR 1                          ' the pixel to the left of each one
  right = here SHL 1                         ' and to the right
  dark = 62 AND (here XOR 127)
  FOR c = 0 TO 4
    bit = 1 SHL (5 - c)
    IF (dark AND bit) <> 0 THEN
      IF (up AND left AND bit) <> 0 AND ((down OR right) AND bit) = 0 THEN Seg x + 3 * c, y, 1, 1
      IF (up AND right AND bit) <> 0 AND ((down OR left) AND bit) = 0 THEN Seg x + 3 * c + 2, y, 1, 1
      IF (down AND left AND bit) <> 0 AND ((up OR right) AND bit) = 0 THEN Seg x + 3 * c, y + 2, 1, 1
      IF (down AND right AND bit) <> 0 AND ((up OR left) AND bit) = 0 THEN Seg x + 3 * c + 2, y + 2, 1, 1
    END IF
  NEXT
END SUB

' A rectangle of the upright picture, filled on the screen turned clockwise by turn quarter
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

SUB Glyph(n, a, b, c)
  font(n * 3) = a
  font(n * 3 + 1) = b
  font(n * 3 + 2) = c
END SUB

' The font, 0 to F. Each row is 5 bits, bit 4 the leftmost; a word holds three rows, the
' first in bits 14-10. Zero, for one, is
'   .###.  #...#  #...#  #...#  #...#  #...#  #...#  #...#  .###.
SUB Fonts
  Glyph 0, 14897, 17969, 17966
  Glyph 1, 4500, 4228, 4255
  Glyph 2, 14881, 1092, 8735
  Glyph 3, 14881, 1217, 1582
  Glyph 4, 2250, 19039, 2114
  Glyph 5, 32272, 30753, 1582
  Glyph 6, 6416, 17361, 17966
  Glyph 7, 31777, 2116, 4228
  Glyph 8, 14897, 17873, 17966
  Glyph 9, 14897, 17889, 1100
  Glyph 10, 14897, 18417, 17969
  Glyph 11, 31281, 18385, 17982
  Glyph 12, 14896, 16912, 16942
  Glyph 13, 31281, 17969, 17982
  Glyph 14, 32272, 17360, 16927
  Glyph 15, 32272, 17360, 16912
END SUB
