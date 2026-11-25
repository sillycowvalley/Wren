' The inputs as two hex digits in a clean font of its own, side by side, at any of four
' angles. The font is defined here: each digit is 5 x 9 pixels, drawn three times the
' size (15 x 27), and smoothed as it is: where two lit pixels meet only at a corner, the
' corner between them is filled, so rounded corners and diagonals are not stairs. Every
' rectangle goes through Seg, which turns the upright picture to the angle.

CONST ANGLE = 90                ' 0, 90, 180 or 270 degrees, clockwise

DIM font(144) AS BYTE           ' nine rows a digit, a byte a row, bit 4 the leftmost
DIM rows(9) AS BYTE             ' the digit being drawn
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
    rows(r) = font(n * 9 + r)
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

SUB Glyph(n, r0, r1, r2, r3, r4, r5, r6, r7, r8)
  DIM i
  i = n * 9
  font(i) = r0
  font(i + 1) = r1
  font(i + 2) = r2
  font(i + 3) = r3
  font(i + 4) = r4
  font(i + 5) = r5
  font(i + 6) = r6
  font(i + 7) = r7
  font(i + 8) = r8
END SUB

' The font, 0 to F: each digit's nine rows, top to bottom, in binary (1 a lit pixel).
SUB Fonts
  Glyph  0, &B01110, &B10001, &B10001, &B10001, &B10001, &B10001, &B10001, &B10001, &B01110     ' 0
  Glyph  1, &B00100, &B01100, &B10100, &B00100, &B00100, &B00100, &B00100, &B00100, &B11111     ' 1
  Glyph  2, &B01110, &B10001, &B00001, &B00001, &B00010, &B00100, &B01000, &B10000, &B11111     ' 2
  Glyph  3, &B01110, &B10001, &B00001, &B00001, &B00110, &B00001, &B00001, &B10001, &B01110     ' 3
  Glyph  4, &B00010, &B00110, &B01010, &B10010, &B10010, &B11111, &B00010, &B00010, &B00010     ' 4
  Glyph  5, &B11111, &B10000, &B10000, &B11110, &B00001, &B00001, &B00001, &B10001, &B01110     ' 5
  Glyph  6, &B00110, &B01000, &B10000, &B10000, &B11110, &B10001, &B10001, &B10001, &B01110     ' 6
  Glyph  7, &B11111, &B00001, &B00001, &B00010, &B00010, &B00100, &B00100, &B00100, &B00100     ' 7
  Glyph  8, &B01110, &B10001, &B10001, &B10001, &B01110, &B10001, &B10001, &B10001, &B01110     ' 8
  Glyph  9, &B01110, &B10001, &B10001, &B10001, &B01111, &B00001, &B00001, &B00010, &B01100     ' 9
  Glyph 10, &B01110, &B10001, &B10001, &B10001, &B11111, &B10001, &B10001, &B10001, &B10001     ' A
  Glyph 11, &B11110, &B10001, &B10001, &B10001, &B11110, &B10001, &B10001, &B10001, &B11110     ' B
  Glyph 12, &B01110, &B10001, &B10000, &B10000, &B10000, &B10000, &B10000, &B10001, &B01110     ' C
  Glyph 13, &B11110, &B10001, &B10001, &B10001, &B10001, &B10001, &B10001, &B10001, &B11110     ' D
  Glyph 14, &B11111, &B10000, &B10000, &B10000, &B11110, &B10000, &B10000, &B10000, &B11111     ' E
  Glyph 15, &B11111, &B10000, &B10000, &B10000, &B11110, &B10000, &B10000, &B10000, &B10000     ' F
END SUB
