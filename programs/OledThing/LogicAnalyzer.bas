' An 8-channel logic analyzer: b0-b7 as a timing diagram, eight traces of four pixels, b0 at
' the top. It waits for any input to change, takes 64 samples, one every TICK ms, draws
' them, holds the picture for HOLD seconds, and waits for the next change.

CONST TICK = 1                  ' ms between samples: the screen is 64 x TICK ms wide
CONST HOLD = 3                  ' seconds the picture stays before it looks again

DIM samples(64) AS BYTE         ' one byte a sample: b0-b7 at once
DIM taken                       ' how many samples so far: 0 waiting, 1-63 taking, 64 done
DIM resting                     ' ticks left before it looks again
DIM ready                       ' FALSE until ON CHANGE's first call, which comes at start

ON START
  CLS
  TEXT 0, 12, "Waiting..."
  SHOW
END ON

' The trigger: the first change starts a capture. ON CHANGE
' also runs once at start: that call only gets it ready.
ON CHANGE
  IF NOT ready THEN
    ready = TRUE
  ELSEIF taken = 0 AND resting = 0 THEN
    samples(0) = VALUE
    taken = 1
  END IF
END ON

ON EVERY TICK MS
  IF taken > 0 AND taken < 64 THEN
    samples(taken) = INPUTS
    taken = taken + 1
    IF taken = 64 THEN
      Draw
      resting = HOLD * 1000 / TICK
    END IF
  ELSEIF resting > 0 THEN
    resting = resting - 1
    IF resting = 0 THEN taken = 0
  END IF
END ON

' Each channel: high on its top row, low two rows down, an edge joining them.
SUB Draw
  DIM ch, x, y, bit, was, mask
  CLS
  mask = 1
  FOR ch = 0 TO 7
    y = ch * 4
    was = samples(0) AND mask
    FOR x = 0 TO 63
      bit = samples(x) AND mask
      IF bit THEN PIXEL x, y ELSE PIXEL x, y + 2
      IF bit <> was THEN LINE x, y, x, y + 2
      was = bit
    NEXT
    mask = mask * 2
  NEXT
  SHOW
END SUB
