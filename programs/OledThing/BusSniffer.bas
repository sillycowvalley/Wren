' A bus sniffer: an Arduino Mega (firmware/BusSource) plays a CPU reading and writing memory.
' Every bus cycle puts a byte on b0-b7 and a rising edge on PD3, the clock; PD2 is the write
' enable, low only for a write. Latching on the clock WHILE PD2 is low catches the writes
' and nothing else: the reads never reach the program at all. It logs the last 20 bytes
' written, five to a row, newest last.

CONST KEEP = 20                 ' bytes in the log

DIM written(20) AS BYTE         ' the log, a ring
DIM oldest                      ' where the next byte goes: the oldest one
DIM writes                      ' bytes written since start
DIM fresh                       ' TRUE when the screen is behind the log

ON LATCH PD3 RISING WHILE PD2 LOW
  written(oldest) = VALUE
  oldest = (oldest + 1) MOD KEEP
  writes = writes + 1
  fresh = TRUE
END ON

ON EVERY 100 MS
  DIM i, row, col
  IF NOT fresh THEN EXIT ON
  fresh = FALSE
  CLS
  TEXT 0, 0, "WRITES", SMALL
  DEC 28, 0, writes, SMALL
  FOR i = 0 TO KEEP - 1
    row = i / 5
    col = i MOD 5
    IF writes > KEEP - 1 - i THEN HEX col * 13, 7 + row * 6, written((oldest + i) MOD KEEP), 2, SMALL
  NEXT
  SHOW
END ON
