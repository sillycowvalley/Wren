' A scanner in the style of the car in Knight Rider: one bright light sweeping from end to
' end with a fading tail behind it. The LEDs are only on or off, so the tail is dimmed by
' lighting it in some of the kernel's 8 ms scan frames: every other frame for the LED just
' behind the light, one frame in four for the one behind that.

DIM pos             ' the bright LED, 0-7
DIM dir             ' 1 or -1
DIM tail1, tail2    ' the LEDs it was on last, and the one before (-1: none yet)
DIM frame           ' scan frames since start, for the dimming
DIM ticks           ' frames since the light last moved

ON START
  dir = 1
  tail1 = -1
  tail2 = -1
END ON

ON EVERY 8 MS
  DIM shown
  frame = (frame + 1) AND 255
  ticks = ticks + 1
  IF ticks >= 12 THEN                        ' a step every 96 ms
    ticks = 0
    tail2 = tail1
    tail1 = pos
    pos = pos + dir
    IF pos = 7 OR pos = 0 THEN dir = -dir
  END IF
  shown = Bit(pos)
  IF frame MOD 2 = 0 THEN shown = shown OR Bit(tail1)
  IF frame MOD 4 = 0 THEN shown = shown OR Bit(tail2)
  LEDS shown
END ON

' LED n's bit; nothing for an LED off the end.
FUNCTION Bit(n)
  IF n < 0 OR n > 7 THEN RETURN 0
  RETURN 1 SHL n
END FUNCTION
