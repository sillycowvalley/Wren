' A scanner in the style of a Cylon's eye: a wide, soft-edged glow gliding from end to end,
' slowing as it turns. The eye is three LEDs: the middle one bright, the two beside it at
' half brightness (lit in every other 8 ms scan frame, since the LEDs are only on or off).

DIM pos             ' the middle of the eye, 0-7
DIM dir             ' 1 or -1
DIM frame           ' scan frames since start, for the dimming
DIM delay            ' frames until the eye moves on

ON START
  dir = 1
END ON

ON EVERY 8 MS
  frame = (frame + 1) AND 255
  delay = delay - 1
  IF delay <= 0 THEN
    pos = pos + dir
    IF pos = 7 OR pos = 0 THEN dir = -dir
    delay = 9 + Slow(pos)
  END IF
  IF frame MOD 2 = 0 THEN
    LEDS Bit(pos - 1) OR Bit(pos) OR Bit(pos + 1)
  ELSE
    LEDS Bit(pos)
  END IF
END ON

' Extra frames at LED n: none in the middle, most at the ends (0, 2, 6 and 12 frames).
FUNCTION Slow(n)
  DIM d
  d = ABS(2 * n - 7)                         ' 1 in the middle to 7 at the ends
  RETURN (d * d - 1) / 4
END FUNCTION

' LED n's bit; nothing for an LED off the end.
FUNCTION Bit(n)
  IF n < 0 OR n > 7 THEN RETURN 0
  RETURN 1 SHL n
END FUNCTION
