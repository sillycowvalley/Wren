' The OLED Thing's own behaviour, written in Wren BASIC.
'
' b0-b7 show as hex digits above eight bit cells. Pull a mode pin low to change that:
'   HEX (PA1) low   the hex digits are off: bit cells only, full size
'   BAR (PA2) low   the bit cells are off: big hex digits only
'   both low        Dice Roll: pulling any b input low rolls a die
'   INV (PA3) low   the display is inverted
' As in Digicool's firmware v1.1:
'   PD3 falling     latches b0-b7: the display keeps that value until reset
'   PD2 low         holds the display while it is low

DIM shown       ' the byte on screen
DIM latched     ' TRUE once PD3 has latched a value
DIM last        ' b0-b7 when last seen, to spot an input pulled low
DIM die         ' the die's face in Dice Roll

ON START
  die = 1
END ON

ON CHANGE
  IF DiceMode THEN
    IF (last AND (VALUE XOR 255)) <> 0 THEN Roll   ' an input went low
  ELSEIF NOT latched AND PIN(PD2) THEN
    shown = VALUE
    Draw
  END IF
  last = VALUE
END ON

ON LATCH PD3
  latched = TRUE
  shown = VALUE
  Draw
END ON

ON RELEASE PD2
  IF NOT latched THEN shown = INPUTS
  Draw
END ON

ON PINS
  Draw
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
    BITS 4, 21, 56, 9, shown
  ELSEIF PIN(PA1) THEN                   ' BAR off
    HEX 15, 0, shown, 2, BIG
  ELSE                                   ' HEX off
    BITS 0, 8, 64, 16, shown
  END IF
  INVERSE NOT PIN(PA3)
  SHOW
END SUB

SUB Roll
  DIM i
  FOR i = 1 TO 8
    die = RND(6) + 1
    CLS
    Face die
    SHOW
    WAIT 60
  NEXT
END SUB

' A die face, 1 to 6
SUB Face(n)
  BOX 17, 0, 31, 32
  IF n MOD 2 = 1 THEN Pip 32, 16
  IF n > 1 THEN
    Pip 24, 8 : Pip 40, 24
  END IF
  IF n > 3 THEN
    Pip 40, 8 : Pip 24, 24
  END IF
  IF n = 6 THEN
    Pip 24, 16 : Pip 40, 16
  END IF
END SUB

SUB Pip(x, y)
  FILL x - 2, y - 2, 5, 5
END SUB
