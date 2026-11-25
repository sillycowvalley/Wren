' The LED Thing's own behaviour, written in Wren BASIC, as Digicool's factory firmware
' does it (docs/LedThing.md). The mode pins choose what the eight LEDs show:
'
'   M2 M1 M0   mode
'    0  0  0    0   the curtain closing and opening
'    0  0  1    1   two LEDs in and out
'    0  1  0    2   a binary counter
'    0  1  1    3   one LED left and right
'    1  0  0    4   filling from the ends, emptying from the ends
'    1  0  1    5   a sweep
'    1  1  0    6   Dice Roll: going from mode 7 to 6 (releasing a switch on M0) rolls
'    1  1  1    7   the b inputs, each LED showing its input (the default: the pins float high)
'
' A pattern steps every 91 ms, the counter every 183 ms. A mode change starts the new
' mode's pattern from its first step.

DIM mode        ' the mode now, 0-7
DIM stepn       ' the pattern's step; in Dice Roll, the ticks since the roll started
DIM face        ' the die's face, 1-6 (0 before the first roll)
DIM rolling     ' TRUE while a roll runs

ON START
  mode = ModeNow
  IF mode = 6 THEN StartRoll ELSE Light       ' at power-up in mode 6 it rolls
END ON

' A mode pin changed.
ON PINS
  DIM m
  m = ModeNow
  IF m = mode THEN EXIT ON
  IF m = 6 AND mode = 7 THEN
    mode = m
    StartRoll
  ELSE
    mode = m
    stepn = 0
    rolling = FALSE
    Light                                     ' coming to mode 6 from 0-5 shows the last face
  END IF
END ON

ON CHANGE
  IF mode = 7 THEN LEDS VALUE
END ON

ON EVERY 91 MS
  stepn = (stepn + 1) AND 511                ' 512 steps: a whole number of every pattern
  IF mode < 6 THEN
    Light
  ELSEIF mode = 6 AND rolling THEN
    RollStep
  END IF
END ON

FUNCTION ModeNow
  RETURN PIN(M0) + 2 * PIN(M1) + 4 * PIN(M2)
END FUNCTION

' The LEDs for this mode and step.
SUB Light
  DIM s
  IF mode = 0 THEN                           ' 81 C3 E7 FF E7 C3 81 00
    s = stepn MOD 8
    IF s < 4 THEN
      LEDS Ends(s)
    ELSEIF s < 7 THEN
      LEDS Ends(6 - s)
    ELSE
      LEDS 0
    END IF
  ELSEIF mode = 1 THEN                       ' 81 42 24 18 24 42 81 00
    s = stepn MOD 8
    IF s < 4 THEN
      LEDS Pair(s)
    ELSEIF s < 7 THEN
      LEDS Pair(6 - s)
    ELSE
      LEDS 0
    END IF
  ELSEIF mode = 2 THEN                       ' 00, 01 ... FF
    LEDS (stepn / 2) AND 255
  ELSEIF mode = 3 THEN                       ' 01 02 ... 80 80 40 ... 01
    s = stepn MOD 16
    IF s < 8 THEN LEDS 1 SHL s ELSE LEDS 1 SHL (15 - s)
  ELSEIF mode = 4 THEN                       ' 81 C3 E7 FF 7E 3C 18 00
    s = stepn MOD 8
    IF s < 4 THEN LEDS Ends(s) ELSE LEDS Ends(s - 4) XOR 255
  ELSEIF mode = 5 THEN                       ' 01 03 ... FF FE ... 00 80 C0 ... FF 7F ... 00
    s = stepn MOD 32
    IF s < 8 THEN
      LEDS (2 SHL s) - 1
    ELSEIF s < 16 THEN
      LEDS (255 SHL (s - 7)) AND 255
    ELSEIF s < 24 THEN
      LEDS (255 SHL (23 - s)) AND 255
    ELSE
      LEDS 255 SHR (s - 23)
    END IF
  ELSEIF mode = 6 THEN
    LEDS Pips(face)
  ELSE
    LEDS INPUTS
  END IF
END SUB

' k + 1 LEDs lit at each end: 81, C3, E7, FF for k = 0 to 3.
FUNCTION Ends(k)
  RETURN ((255 SHL (7 - k)) AND 255) OR ((2 SHL k) - 1)
END FUNCTION

' The kth LED from each end: 81, 42, 24, 18 for k = 0 to 3.
FUNCTION Pair(k)
  RETURN (128 SHR k) OR (1 SHL k)
END FUNCTION

SUB StartRoll
  rolling = TRUE
  stepn = 0
  NewFace
END SUB

' The roll, as Digicool's: faces at 0, 183, 367, 550, 734, 1101 and 1468 ms (every two
' ticks, then every four), a blank at 1835 ms, then the last face again at 2202 ms, held.
SUB RollStep
  IF stepn = 2 OR stepn = 4 OR stepn = 6 OR stepn = 8 OR stepn = 12 OR stepn = 16 THEN
    NewFace
  ELSEIF stepn = 20 THEN
    LEDS 0
  ELSEIF stepn = 24 THEN
    LEDS Pips(face)
    rolling = FALSE
  END IF
END SUB

' A new face, never the one before.
SUB NewFace
  DIM f
  f = face
  WHILE f = face
    f = RND(6) + 1
  WEND
  face = f
  LEDS Pips(face)
END SUB

' A die face on the LEDs: 1 to 6 pips.
FUNCTION Pips(n)
  IF n = 1 THEN RETURN $10
  IF n = 2 THEN RETURN $18
  IF n = 3 THEN RETURN $34
  IF n = 4 THEN RETURN $3C
  IF n = 5 THEN RETURN $7A
  IF n = 6 THEN RETURN $7E
  RETURN 0
END FUNCTION
