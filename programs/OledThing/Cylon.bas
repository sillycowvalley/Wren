' A scanner in the style of a Cylon's eye, on the OLED: a round, bright eye with a glow
' either side, gliding along a visor slit and slowing as it turns, until half of it is
' off the screen at each end. Its path eases in and out (smoothstep): 33 steps from one
' end to the other, 30 ms apart.

DIM p               ' the step along this pass, 0-32
DIM dir             ' 1 out, -1 back

ON START
  dir = 1
  Draw
END ON

ON EVERY 30 MS
  p = p + dir
  IF p = 32 OR p = 0 THEN dir = -dir
  Draw
END ON

SUB Draw
  DIM s, cx
  s = (p * p / 16) * (96 - 2 * p)            ' smoothstep: 0 to 2048 as p goes 0 to 32
  cx = s / 32                                ' the eye's middle, 0 to 64: half of it off the screen at each end
  CLS
  BOX 0, 9, 64, 14                           ' the visor slit
  FILL cx - 2, 11, 4, 1                      ' the eye, 10 x 10 with rounded rows
  FILL cx - 4, 12, 8, 1
  FILL cx - 5, 13, 10, 6
  FILL cx - 4, 19, 8, 1
  FILL cx - 2, 20, 4, 1
  FILL cx - 8, 14, 3, 4                      ' its glow
  FILL cx + 5, 14, 3, 4
  FILL cx - 10, 15, 2, 2
  FILL cx + 8, 15, 2, 2
  SHOW
END SUB
