' A voltmeter on PD2. Wire a variable resistor's two ends to 3.3 V and GND and its wiper
' to PD2 (J5 pin 3): turning it moves PD2 anywhere from 0 to 3.3 V. ANALOG(PD2) reads
' that as 0 to 1023, against the chip's own supply (3.3 V from the Pico), and this shows
' it as volts and as the raw number. A button from PD3 (J5 pin 4) to GND switches between
' the meter and a graph of the last 64 readings.

CONST SAMPLES = 8               ' conversions averaged into each reading
CONST TALL = 23                 ' the graph's height in pixels, less one

DIM raw, mv                     ' the last reading: 0-1023, and in millivolts
DIM history(64) AS BYTE         ' the last 64 readings as graph heights, a ring
DIM oldest                      ' where the next one goes: the oldest
DIM graph                       ' TRUE for the graph view

ON EVERY 50 MS
  Measure
  IF graph THEN DrawGraph ELSE DrawMeter
END ON

ON PRESS PD3
  graph = NOT graph
END ON

' One reading: the average of SAMPLES conversions, in raw steps and in millivolts.
SUB Measure
  DIM i, sum
  FOR i = 1 TO SAMPLES
    sum = sum + ANALOG(PD2)
  NEXT
  raw = sum / SAMPLES
  mv = raw * 3 + raw * 29 / 128              ' 3300 / 1023 is 3.2258; 3 + 29 / 128 is 3.2266
  history(oldest) = raw * TALL / 1023
  oldest = (oldest + 1) AND 63
END SUB

' The voltage in large digits, a bar, and the raw number.
SUB DrawMeter
  CLS
  DEC 4, 0, mv / 1000, LARGE
  FILL 17, 12, 3, 3                          ' the decimal point
  DEC 22, 0, (mv / 100) MOD 10, LARGE
  DEC 34, 0, (mv / 10) MOD 10, LARGE
  TEXT 48, 0, "V", LARGE
  BAR 0, 18, 64, 5, raw, 1023
  TEXT 0, 26, "RAW", SMALL
  DEC 16, 26, raw, SMALL
  SHOW
END SUB

' The last 64 readings, oldest on the left, under the voltage and the raw number.
SUB DrawGraph
  DIM x, y, last
  CLS
  DEC 0, 0, mv / 1000, SMALL
  PIXEL 4, 4
  DEC 6, 0, (mv / 100) MOD 10, SMALL
  DEC 10, 0, (mv / 10) MOD 10, SMALL
  TEXT 15, 0, "V", SMALL
  DEC 44, 0, raw, SMALL
  FOR x = 0 TO 63
    y = 31 - history((oldest + x) AND 63)
    IF x = 0 THEN last = y
    LINE x - 1, last, x, y
    last = y
  NEXT
  SHOW
END SUB
