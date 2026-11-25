' Snake that plays itself, on the OLED. The snake heads for the blinking food, steering
' round the walls and its own body and away from dead ends; it grows a cell each time it
' eats, and when it is boxed in it shows GAME OVER and its score, then starts again.
'
' The board is 31 x 15 cells of 2 x 2 pixels inside a border. Which cells the snake
' fills is kept as bits in grid, so a move is checked without searching the body; the
' body itself is a ring of cells, head at sx(head), sy(head).

CONST W = 31                    ' cells across
CONST H = 15                    ' and down
CONST RING = 64                 ' the body's ring: the longest snake is RING - 1

DIM sx(RING) AS BYTE, sy(RING) AS BYTE
DIM grid(60) AS BYTE            ' one bit a cell: 465 cells
DIM head, length, dir           ' dir: 0 right, 1 down, 2 left, 3 up
DIM fx, fy                      ' the food
DIM score, over                 ' frames left of GAME OVER, else 0

ON START
  NewGame
END ON

ON EVERY 70 MS
  IF over > 0 THEN
    over = over - 1
    IF over = 0 THEN NewGame
    EXIT ON
  END IF
  Move
  SHOW
END ON

SUB NewGame
  DIM i
  CLS
  BOX 0, 0, 64, 32
  FOR i = 0 TO 59
    grid(i) = 0
  NEXT
  length = 3
  head = 2
  FOR i = 0 TO 2
    sx(i) = 5 + i
    sy(i) = 7
    Mark sx(i), sy(i), 1
  NEXT
  dir = 0
  score = 0
  PlaceFood
  SHOW
END SUB

' One step: the best way on, then the body follows, eating or not.
SUB Move
  DIM d, best, bestScore, s, nx, ny, hx, hy, tail
  hx = sx(head)
  hy = sy(head)
  best = -1
  bestScore = 32767
  FOR d = 0 TO 3
    IF d <> ((dir + 2) AND 3) THEN           ' never straight back
      nx = hx + StepX(d)
      ny = hy + StepY(d)
      IF NOT Taken(nx, ny) THEN
        s = (ABS(nx - fx) + ABS(ny - fy)) * 2 - Exits(nx, ny) * 3
        IF Exits(nx, ny) = 0 AND NOT (nx = fx AND ny = fy) THEN s = s + 200   ' a dead end
        IF d = dir THEN s = s - 1            ' straight on, other things equal
        IF s < bestScore THEN
          bestScore = s
          best = d
        END IF
      END IF
    END IF
  NEXT
  IF best < 0 THEN
    GameOver
    EXIT SUB
  END IF
  dir = best
  nx = hx + StepX(dir)
  ny = hy + StepY(dir)
  IF nx = fx AND ny = fy THEN
    score = score + 1
    IF length < RING - 1 THEN length = length + 1 ELSE Unmark
  ELSE
    Unmark
  END IF
  head = (head + 1) AND (RING - 1)
  sx(head) = nx
  sy(head) = ny
  Mark nx, ny, 1
  IF nx = fx AND ny = fy THEN PlaceFood ELSE FILL 1 + 2 * fx, 1 + 2 * fy, 2, 2, 2   ' the food blinks
END SUB

' The tail's last cell goes.
SUB Unmark
  DIM t
  t = (head - length + 1) AND (RING - 1)
  Mark sx(t), sy(t), 0
END SUB

' Fills (1) or empties (0) a cell, on the screen and in grid.
SUB Mark(x, y, v)
  DIM c, bit
  c = y * W + x
  bit = 1 SHL (c AND 7)
  IF v THEN grid(c SHR 3) = grid(c SHR 3) OR bit ELSE grid(c SHR 3) = grid(c SHR 3) AND (255 XOR bit)
  FILL 1 + 2 * x, 1 + 2 * y, 2, 2, v
END SUB

' TRUE for a wall or a cell of the snake.
FUNCTION Taken(x, y)
  DIM c
  IF x < 0 OR x >= W OR y < 0 OR y >= H THEN RETURN TRUE
  c = y * W + x
  RETURN ((grid(c SHR 3) SHR (c AND 7)) AND 1) <> 0
END FUNCTION

' How many ways on there are from a cell.
FUNCTION Exits(x, y)
  DIM n
  IF NOT Taken(x + 1, y) THEN n = n + 1
  IF NOT Taken(x - 1, y) THEN n = n + 1
  IF NOT Taken(x, y + 1) THEN n = n + 1
  IF NOT Taken(x, y - 1) THEN n = n + 1
  RETURN n
END FUNCTION

FUNCTION StepX(d)
  IF d = 0 THEN RETURN 1
  IF d = 2 THEN RETURN -1
  RETURN 0
END FUNCTION

FUNCTION StepY(d)
  IF d = 1 THEN RETURN 1
  IF d = 3 THEN RETURN -1
  RETURN 0
END FUNCTION

' The food, on a free cell.
SUB PlaceFood
  fx = RND(W)
  fy = RND(H)
  WHILE Taken(fx, fy)
    fx = RND(W)
    fy = RND(H)
  WEND
  FILL 1 + 2 * fx, 1 + 2 * fy, 2, 2
END SUB

SUB GameOver
  CLS
  BOX 0, 0, 64, 32
  TEXT 5, 6, "GAME OVER"
  TEXT 14, 18, "SCORE", SMALL
  DEC 38, 18, score, SMALL
  over = 40                     ' about 3 seconds
END SUB
