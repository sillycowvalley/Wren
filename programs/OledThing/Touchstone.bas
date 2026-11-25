' One of the Touchstone-86 chip tester's two displays (docs/Touchstone.md). The tester
' latches a byte onto b0-b7 for each Thing, with nothing to say which display is which:
' set ROLE before uploading, 2 for DS2 (HI on the silk) and 1 for DS1 (LO).
'
'   DS2  the state and the test as words, e.g. RAM over FAIL; a byte outside the
'        tester's code as its two hex digits. Until the byte first changes after
'        power-up it shows ?xx: the latch powers up holding a random byte.
'   DS1  its byte as two big hex digits, nothing else (a case number, a count or a
'        result code: only DS2's state says which).
'
' BAR, HEX, INV and PD2/PD3 float on the tester and are never read.

CONST ROLE = 2

DIM shown       ' the settled byte on screen
DIM written     ' TRUE once the byte has changed since power-up
DIM started     ' TRUE after the first ON CHANGE (once after ON START)

ON CHANGE
  IF INPUTS = VALUE THEN                     ' settled: two reads agree
    IF started AND VALUE <> shown THEN written = TRUE
    started = TRUE
    shown = VALUE
    CLS
    IF ROLE = 1 THEN
      Digits
    ELSEIF NOT written THEN
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
END ON

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
