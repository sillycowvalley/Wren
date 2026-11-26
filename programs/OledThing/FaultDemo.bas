' A fault on purpose. It counts down from 5, then divides by zero. The kernel stops the
' program and shows ERR with its code (5, division by zero) and where in the program it
' happened, and goes on running itself, so a new upload always works.

DIM n, zero                     ' zero stays 0: the compiler can't fold it away

ON START
  n = 5
END ON

ON EVERY 1000 MS
  CLS
  IF n > 0 THEN
    TEXT 0, 12, "FAULT IN", SMALL
    DEC 44, 0, n, BIG
    n = n - 1
  ELSE
    n = 10 / zero               ' ERR 5
  END IF
  SHOW
END ON
