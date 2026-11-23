' Shows the levels on PD2 and PD3 as numbers and bars.
' In Wren, tick "Analog" for PD2 or PD3 and move its slider.

ON EVERY 50
  DIM a, b
  a = ANALOG(PD2)
  b = ANALOG(PD3)
  CLS
  TEXT 0, 0, "PD2", SMALL
  DEC 16, 0, a, SMALL
  BAR 0, 7, 64, 8, a, 1023
  TEXT 0, 17, "PD3", SMALL
  DEC 16, 17, b, SMALL
  BAR 0, 24, 64, 8, b, 1023
  SHOW
END ON
