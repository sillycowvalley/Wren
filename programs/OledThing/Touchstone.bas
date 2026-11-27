' One of the Touchstone-86 chip tester's two displays (docs/Touchstone.md). The tester
' latches a byte onto b0-b7 for each Thing, with nothing to say which display is which:
' set ROLE before uploading, 2 for DS2 (HI on the silk) and 1 for DS1 (LO).
'
'   DS2  the state and the test as words, e.g. RAM over FAIL; a byte outside the
'        tester's code as its two hex digits. Until the byte first changes after
'        power-up it shows ?xx: the latch powers up holding a random byte.
'   DS1  its byte as two big hex digits (a case number, a count or a result code:
'        only DS2's state says which), drawn once the byte has held for 20 ms.
'        Before a failure's case the tester sends a frame, a byte every 5 ms, its
'        high digit the byte's place and its low digit the frame's: DS1 then shows
'        the failure itself, the case, and for a RAM or table failure the address
'        or input, the value wanted and the value got.
'
' BAR, HEX, INV and PD2/PD3 float on the tester and are never read.

CONST ROLE = 2

DIM shown       ' the settled byte
DIM written     ' TRUE once the byte has changed since power-up (DS2)
DIM started     ' TRUE after the first ON CHANGE (once after ON START)
DIM changed     ' MILLIS when DS1's byte last changed
DIM pending     ' DS1: a byte not yet drawn
DIM place       ' DS1: the frame's next place, 0-15; 99 between frames
DIM last        ' DS1: MILLIS of the frame's last byte
DIM framed      ' DS1: 1 a frame read, 2 and its case after it; 0 none
DIM digit(16) AS BYTE

ON CHANGE
  IF INPUTS = VALUE THEN                     ' settled: two reads agree
    IF ROLE = 1 THEN
      Take VALUE
    ELSE
      IF started AND VALUE <> shown THEN written = TRUE
      started = TRUE
      shown = VALUE
      CLS
      IF NOT written THEN
        TEXT 6, 2, "?", BIG
        HEX 24, 2, shown, 2, BIG
      ELSEIF InCode THEN
        Test shown AND 15
        State shown SHR 4
      ELSE
        Digits
      END IF
      SHOW
    END IF
  END IF
END ON

' DS1 draws once its byte has held for 20 ms: never in the middle of a frame.
ON EVERY 5
  IF pending AND MILLIS - changed >= 20 THEN
    pending = FALSE
    CLS
    IF framed THEN
      Detail
    ELSE
      Digits
    END IF
    SHOW
  END IF
END ON

' DS1: a new byte, the next of a frame or not.
SUB Take(v)
  DIM p
  p = v SHR 4
  IF framed = 1 AND v = Case THEN            ' Ft nn after its frame
    framed = 2
  ELSE
    framed = 0
    IF p = 0 THEN place = 0
    IF p = place AND (p = 0 OR MILLIS - last < 20) THEN
      digit(p) = v AND 15
      place = place + 1
      last = MILLIS
      IF place = 16 OR (place = 4 AND digit(3) = 0) THEN
        framed = 1
        place = 99
      END IF
    ELSE
      place = 99
    END IF
  END IF
  shown = v
  changed = MILLIS
  pending = TRUE
END SUB

' The frame's case, its places 1 and 2.
FUNCTION Case
  RETURN (digit(1) SHL 4) OR digit(2)
END FUNCTION

' A word of the frame, four places from at, high digit first.
FUNCTION Word(at)
  RETURN (digit(at) SHL 12) OR (digit(at + 1) SHL 8) OR (digit(at + 2) SHL 4) OR digit(at + 3)
END FUNCTION

' The failure, by the frame's kind (place 3): 0 the case alone; 1 a RAM word and 2 a
' RAM byte, at an address; 3, 4 and 5 a table row whose AX, DX or flags were wrong.
SUB Detail
  DIM kind, want, got, wrong, digits
  kind = digit(3)
  IF kind = 0 THEN
    TEXT 0, 12, "CASE"
    HEX 28, 0, Case, 2, BIG
    EXIT SUB
  END IF
  want = Word(8)
  got = Word(12)
  wrong = want XOR got
  IF kind <= 2 THEN
    digits = 4
    IF kind = 2 THEN digits = 2
    TEXT 0, 0, "@"
    HEX 12, 0, Word(4), 4
    IF kind = 2 THEN
      IF Word(4) AND 1 THEN
        TEXT 42, 0, "ODD"
      ELSE
        TEXT 42, 0, "EVN"
      END IF
    ELSEIF (wrong AND &HFF00) = 0 THEN
      TEXT 42, 0, "EVN"
    ELSEIF (wrong AND &HFF) = 0 THEN
      TEXT 42, 0, "ODD"
    ELSE
      TEXT 42, 0, "BTH"
    END IF
    TEXT 0, 8, "WANT"
    HEX 30, 8, want, digits
    TEXT 0, 16, "READ"
    HEX 30, 16, got, digits
    TEXT 0, 24, "XOR"
    HEX 30, 24, wrong, digits
  ELSE
    TEXT 0, 0, "ROW"
    HEX 24, 0, Case, 2
    IF kind = 3 THEN TEXT 48, 0, "AX"
    IF kind = 4 THEN TEXT 48, 0, "DX"
    IF kind = 5 THEN TEXT 48, 0, "FL"
    TEXT 0, 8, "IN"
    HEX 30, 8, Word(4), 4
    TEXT 0, 16, "WANT"
    HEX 30, 16, want, 4
    TEXT 0, 24, "GOT"
    HEX 30, 24, got, 4
  END IF
END SUB

' The byte is in the tester's code: a test 1 to E, a state 0, A, B, C or F.
FUNCTION InCode
  DIM s, t
  s = shown SHR 4
  t = shown AND 15
  IF t < 1 OR t > 14 THEN RETURN FALSE
  RETURN s = 0 OR s = 10 OR s = 11 OR s = 12 OR s = 15
END FUNCTION

' Two big hex digits, as the stock Thing shows them with BAR low.
SUB Digits
  HEX 10, 0, shown SHR 4, 1, BIG
  HEX 37, 0, shown AND 15, 1, BIG
END SUB

' The test's name, centred on the top half: LARGE up to 5 letters, NORMAL for 6.
SUB Test(t)
  IF t = 1 THEN TEXT 9, 0, "REGS", LARGE
  IF t = 2 THEN TEXT 15, 0, "RAM", LARGE
  IF t = 3 THEN TEXT 3, 0, "STACK", LARGE
  IF t = 4 THEN TEXT 15, 0, "ALU", LARGE
  IF t = 5 THEN TEXT 3, 0, "SHIFT", LARGE
  IF t = 6 THEN TEXT 14, 3, "MULDIV"
  IF t = 7 THEN TEXT 14, 3, "STRING"
  IF t = 8 THEN TEXT 15, 0, "BCD", LARGE
  IF t = 9 THEN TEXT 9, 0, "ADDR", LARGE
  IF t = 10 THEN TEXT 9, 0, "TRAP", LARGE
  IF t = 11 THEN TEXT 21, 0, "ID", LARGE
  IF t = 12 THEN TEXT 14, 3, "STATIC"
  IF t = 13 THEN TEXT 9, 0, "SOAK", LARGE
  IF t = 14 THEN TEXT 9, 0, "CORE", LARGE
END SUB

' The state, centred on the bottom half. RESULT: DS1 holds a result code.
SUB State(s)
  IF s = 0 THEN TEXT 3, 17, "READY", LARGE
  IF s = 10 THEN TEXT 15, 17, "RUN", LARGE
  IF s = 11 THEN TEXT 14, 20, "RESULT"
  IF s = 12 THEN TEXT 9, 17, "PASS", LARGE
  IF s = 15 THEN TEXT 9, 17, "FAIL", LARGE
END SUB
