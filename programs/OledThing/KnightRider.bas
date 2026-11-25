' A scanner in the style of the car in Knight Rider, on the OLED: a row of eight lamps,
' one lit sweeping from end to end, with a fading tail behind it. The OLED's pixels are
' on or off, so the tail fades by size: a full lamp, a narrow bar, then a dot.

DIM pos             ' the bright lamp, 0-7
DIM dir             ' 1 or -1
DIM tail1, tail2    ' the lamps it was on last, and the one before (-1: none yet)

ON START
  dir = 1
  tail1 = -1
  tail2 = -1
  Draw
END ON

ON EVERY 90 MS
  tail2 = tail1
  tail1 = pos
  pos = pos + dir
  IF pos = 7 OR pos = 0 THEN dir = -dir
  Draw
END ON

' Eight lamps 8 pixels apart, each 6 x 12, across the middle of the screen.
SUB Draw
  DIM i
  CLS
  FOR i = 0 TO 7
    BOX 8 * i + 1, 10, 6, 12
  NEXT
  IF tail2 >= 0 THEN FILL 8 * tail2 + 3, 15, 2, 2
  IF tail1 >= 0 THEN FILL 8 * tail1 + 3, 12, 2, 8
  FILL 8 * pos + 1, 10, 6, 12
  SHOW
END SUB
