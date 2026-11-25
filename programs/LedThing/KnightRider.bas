' A scanner in the style of the car in Knight Rider: one bright light sweeping from end to
' end with a long tail fading behind it, four LEDs long.
'
' The LEDs are only on or off, so the tail is dimmed by lighting each tail LED in some of
' the kernel's 8 ms scan frames: 6, 4, 2 and 1 frames in every 8, going back along the
' tail. The frames are spread out (frame order 0 4 2 6 1 5 3 7), so the flicker is as
' fast as it can be: 62 Hz at half brightness, 31 Hz at a quarter or three quarters.

DIM pos             ' the bright LED, 0-7
DIM dir             ' 1 or -1
DIM tail(4)         ' the LEDs it was on, newest first (-1: none yet)
DIM frame           ' scan frames since start, for the dimming
DIM ticks           ' frames since the light last moved

ON START
  DIM i
  dir = 1
  FOR i = 0 TO 3
    tail(i) = -1
  NEXT
END ON

ON EVERY 8 MS
  DIM shown, i, slot
  frame = (frame + 1) AND 255
  ticks = ticks + 1
  IF ticks >= 9 THEN                         ' a step every 72 ms
    ticks = 0
    FOR i = 3 TO 1 STEP -1
      tail(i) = tail(i - 1)
    NEXT
    tail(0) = pos
    pos = pos + dir
    IF pos = 7 OR pos = 0 THEN dir = -dir
  END IF
  slot = Spread(frame AND 7)
  shown = Bit(pos)
  IF slot < 6 THEN shown = shown OR Bit(tail(0))
  IF slot < 4 THEN shown = shown OR Bit(tail(1))
  IF slot < 2 THEN shown = shown OR Bit(tail(2))
  IF slot < 1 THEN shown = shown OR Bit(tail(3))
  LEDS shown
END ON

' Frame f's place in the order 0 4 2 6 1 5 3 7 (its three bits reversed), so a tail LED
' lit for n frames in 8 has them spread evenly.
FUNCTION Spread(f)
  RETURN ((f AND 1) * 4) OR (f AND 2) OR ((f AND 4) / 4)
END FUNCTION

' LED n's bit; nothing for an LED off the end.
FUNCTION Bit(n)
  IF n < 0 OR n > 7 THEN RETURN 0
  RETURN 1 SHL n
END FUNCTION
