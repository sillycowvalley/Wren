' Latches b0-b7 on a rising edge on PD3 and shows the byte in hex.
' LATCH has no debounce: a button on PD3 bounces, and count shows it.
DIM captured, count

ON LATCH PD3 RISING
  captured = VALUE              ' b0-b7 as they were at the edge
  count = count + 1
END ON

ON EVERY 50 MS
  CLS
  HEX 0, 0, captured, 2, LARGE
  DEC 40, 0, count, SMALL
  SHOW
END ON
