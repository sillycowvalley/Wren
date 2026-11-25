' The OLED Thing's own behaviour, written in Wren BASIC, as Digicool's firmware v1.1
' does it (docs/OledThing.md); only the font is Wren's own.
'
' b0-b7 show as hex digits above eight bit cells. Pull a mode pin low to change that:
'   HEX (PA1) low   the hex digits are off: bit cells only, full height
'   BAR (PA2) low   the bit cells are off: big hex digits only
'   both low        Dice Roll: holding any b input low rolls a die
'   INV (PA3) low   the display is inverted
'   PD3 falling     latches b0-b7: the display keeps that value until reset
'   PD2 low         holds the display while it is low

DIM shown       ' the byte on screen
DIM latched     ' TRUE once PD3 has latched a value
DIM die         ' the die's face, 1 to 6
DIM wasDice     ' Dice Roll was showing at the last pin change
DIM inv         ' INV at the last pin change

ON CHANGE
  IF DiceMode THEN
    IF VALUE <> 255 THEN Roll                    ' an input is held low
  ELSEIF NOT latched AND PIN(PD2) THEN
    shown = VALUE
    Draw
  END IF
END ON

ON LATCH PD3
  latched = TRUE
  shown = VALUE
  Draw
END ON

' A mode pin, PD2 or PD3 changed (and once at start).
ON PINS
  IF DiceMode THEN
    IF NOT wasDice OR inv <> PIN(PA3) THEN die = 6   ' entering Dice shows a six
  ELSEIF NOT latched AND PIN(PD2) THEN
    shown = INPUTS                               ' live again, after Dice or Hold
  END IF
  wasDice = DiceMode
  inv = PIN(PA3)
  Draw
  IF DiceMode AND INPUTS <> 255 THEN Roll
END ON

FUNCTION DiceMode
  RETURN NOT PIN(PA1) AND NOT PIN(PA2)
END FUNCTION

SUB Draw
  CLS
  IF DiceMode THEN
    Face die
  ELSEIF PIN(PA1) AND PIN(PA2) THEN      ' HEX and BAR
    HEX 21, 2, shown, 2, LARGE
    BITS 0, 24, 64, 8, shown
  ELSEIF PIN(PA1) THEN                   ' BAR off: big digits centred where Digicool's are
    HEX 10, 0, shown SHR 4, 1, BIG
    HEX 37, 0, shown AND 15, 1, BIG
  ELSE                                   ' HEX off
    BITS 0, 4, 64, 24, shown
  END IF
  INVERSE NOT PIN(PA3)
  SHOW
END SUB

' The roll, as Digicool's: a step every 117 ms while an input is held low (a new face
' each time), then slowing after release: 4 faces 117 ms apart, 3 at 234 ms, a blank,
' and the last face again. A face never comes twice in a row. A mode change ends it.
SUB Roll
  DIM stepn, tick, f
  WHILE stepn < 10
    WAIT 117
    tick = tick + 1
    IF NOT DiceMode OR PIN(PA3) <> inv THEN EXIT SUB
    IF tick MOD (1 SHL (stepn / 4)) = 0 THEN
      IF INPUTS <> 255 THEN stepn = 0 ELSE stepn = stepn + 1
      CLS
      IF stepn < 8 THEN
        f = die
        WHILE f = die
          f = RND(6) + 1
        WEND
        die = f
      END IF
      IF stepn <> 8 THEN Face die
      SHOW
    END IF
  WEND
END SUB

' A die face, 1 to 6: Digicool's rounded box and pips
SUB Face(n)
  FILL 20, 0, 24, 1 : FILL 20, 31, 24, 1
  FILL 16, 4, 1, 24 : FILL 47, 4, 1, 24
  Corner 1, 2 : Corner 30, 28
  IF n MOD 2 = 1 THEN Pip 30, 14
  IF n = 2 OR n > 3 THEN
    Pip 21, 5 : Pip 39, 23
  END IF
  IF n > 2 THEN
    Pip 39, 5 : Pip 21, 23
  END IF
  IF n = 6 THEN
    Pip 21, 14 : Pip 39, 14
  END IF
END SUB

' The box's rounded corners on one side: the 2-pixel step on row y, the 2-pixel side at y2
SUB Corner(y, y2)
  FILL 18, y, 2, 1 : FILL 44, y, 2, 1
  FILL 17, y2, 1, 2 : FILL 46, y2, 1, 2
END SUB

SUB Pip(x, y)
  FILL x + 1, y, 2, 4
  FILL x, y + 1, 4, 2
END SUB
