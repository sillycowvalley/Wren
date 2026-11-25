' A scanner in the style of a Cylon's eye, on the OLED: a bright eye with a glow either
' side, gliding along a visor slit and slowing as it turns. Its path eases in and out
' (smoothstep): 33 steps from one end to the other, 30 ms apart.

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
  DIM s, x
  s = (p * p / 16) * (96 - 2 * p)            ' smoothstep: 0 to 2048 as p goes 0 to 32
  x = 2 + (s / 32) * 44 / 64                 ' the eye's left edge, 2 to 46
  CLS
  BOX 0, 9, 64, 14                           ' the visor slit
  FILL x, 11, 12, 10                         ' the eye
  FILL x - 4, 13, 4, 6                       ' its glow
  FILL x + 12, 13, 4, 6
  FILL x - 7, 15, 3, 2
  FILL x + 16, 15, 3, 2
  SHOW
END SUB
