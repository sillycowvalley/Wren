' Pong that plays itself, on the OLED. Each paddle follows the ball while it comes its
' way, aiming a little off: each time the ball heads for it, a paddle picks an error of
' up to 7 pixels either way, and it moves no faster than 1.25 pixels a frame, so a bad
' guess or a steep shot beats it. Where the ball meets the paddle sets its angle, and
' every hit is a little faster. First to 9 wins; then a new game.
'
' Positions and speeds are in quarter pixels, so the ball can move at shallow angles.

CONST PH = 8                    ' paddle height in pixels
CONST TOP = 96                  ' the lowest a paddle's top can be: (32 - PH) * 4

DIM bx, by, vx, vy              ' the ball's top-left corner and its speed a frame
DIM ly, ry                      ' the paddles' tops
DIM lerr, rerr                  ' where each paddle aims, off the ball's middle
DIM lscore, rscore
DIM pause                       ' frames before the ball moves again

ON START
  NewGame
END ON

ON EVERY 30 MS
  IF pause > 0 THEN
    pause = pause - 1
    IF pause = 0 AND (lscore = 9 OR rscore = 9) THEN NewGame
  ELSE
    MoveBall
  END IF
  ly = Follow(ly, vx < 0, lerr)
  ry = Follow(ry, vx > 0, rerr)
  Draw
END ON

SUB NewGame
  lscore = 0
  rscore = 0
  ly = TOP / 2
  ry = TOP / 2
  Serve 1
END SUB

' The ball from the middle, toward the left (-1) or the right (1), at a random angle.
SUB Serve(toward)
  bx = 31 * 4
  by = 15 * 4
  vx = 6 * toward
  vy = RND(9) - 4
  Aim
  pause = 25
END SUB

SUB MoveBall
  bx = bx + vx
  by = by + vy
  IF by < 0 THEN                             ' off the top and bottom edges
    by = -by
    vy = -vy
  ELSEIF by > 120 THEN
    by = 240 - by
    vy = -vy
  END IF
  ' The left paddle's face is at x = 3, the right one's at 61 (the ball's right edge at 236 / 4 + 2).
  IF vx < 0 AND bx <= 12 AND bx - vx > 12 THEN
    IF by + 8 >= ly AND by <= ly + PH * 4 THEN Bounce 12, ly
  ELSEIF vx > 0 AND bx >= 236 AND bx - vx < 236 THEN
    IF by + 8 >= ry AND by <= ry + PH * 4 THEN Bounce 236, ry
  END IF
  IF bx < -8 THEN Point FALSE
  IF bx > 256 THEN Point TRUE
END SUB

' Off a paddle whose top is at py: the farther from its middle, the steeper.
SUB Bounce(x, py)
  bx = x
  vy = (by + 4 - py - PH * 2) / 3
  IF vx < 0 THEN vx = MAX(vx - 1, -10) ELSE vx = MIN(vx + 1, 10)
  vx = -vx
  Aim
END SUB

' The paddle the ball now heads for picks its error: -7 to 7 pixels, in quarter pixels.
SUB Aim
  IF vx < 0 THEN lerr = (RND(15) - 7) * 4 ELSE rerr = (RND(15) - 7) * 4
END SUB

SUB Point(left)
  IF left THEN
    lscore = lscore + 1
    Serve -1
  ELSE
    rscore = rscore + 1
    Serve 1
  END IF
  IF lscore = 9 OR rscore = 9 THEN pause = 100         ' the final score for 3 seconds
END SUB

' A paddle's new top: toward the ball, off by err, while it comes this way; back to the
' middle otherwise.
FUNCTION Follow(py, coming, err)
  DIM target
  IF coming THEN target = by + 4 - PH * 2 + err ELSE target = TOP / 2
  IF target > py + 5 THEN
    py = py + 5
  ELSEIF target < py - 5 THEN
    py = py - 5
  ELSE
    py = target
  END IF
  RETURN MAX(0, MIN(py, TOP))
END FUNCTION

SUB Draw
  DIM y
  CLS
  FOR y = 0 TO 30 STEP 4
    FILL 31, y, 2, 2                         ' the net
  NEXT
  DEC 22, 1, lscore, SMALL
  DEC 38, 1, rscore, SMALL
  FILL 1, ly / 4, 2, PH
  FILL 61, ry / 4, 2, PH
  FILL bx / 4, by / 4, 2, 2
  SHOW
END SUB
