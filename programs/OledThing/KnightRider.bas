' A scanner in the style of the car in Knight Rider, on the OLED: a row of eight lamps,
' one lit sweeping from end to end, with a long tail fading behind it, four lamps long.
' The OLED's pixels are on or off, so the tail fades by size: the full lamp, then bars 8,
' 6 and 4 pixels tall, then a dot.

DIM pos             ' the bright lamp, 0-7
DIM dir             ' 1 or -1
DIM tail(4)         ' the lamps it was on, newest first (-1: none yet)

ON START
  DIM i
  dir = 1
  FOR i = 0 TO 3
    tail(i) = -1
  NEXT
  Draw
END ON

ON EVERY 70 MS
  DIM i
  FOR i = 3 TO 1 STEP -1
    tail(i) = tail(i - 1)
  NEXT
  tail(0) = pos
  pos = pos + dir
  IF pos = 7 OR pos = 0 THEN dir = -dir
  Draw
END ON

' Eight lamps 8 pixels apart, each 6 x 12, across the middle of the screen. The tail is
' drawn oldest first, so a newer, brighter lamp covers an older one in the same place.
SUB Draw
  DIM i
  CLS
  FOR i = 0 TO 7
    BOX 8 * i + 1, 10, 6, 12
  NEXT
  Lamp tail(3), 3, 15, 2, 2                  ' a dot
  Lamp tail(2), 3, 14, 2, 4                  ' a short bar
  Lamp tail(1), 3, 13, 2, 6                  ' a taller one
  Lamp tail(0), 3, 12, 2, 8                  ' the tallest
  FILL 8 * pos + 1, 10, 6, 12                ' the light
  SHOW
END SUB

' Fills part of lamp n: dx and y from its left edge and the screen's top, w x h.
SUB Lamp(n, dx, y, w, h)
  IF n >= 0 THEN FILL 8 * n + dx, y, w, h
END SUB
