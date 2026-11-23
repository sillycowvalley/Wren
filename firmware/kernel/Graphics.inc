; Wren graphics: the 64x32 frame buffer, shapes, text and number views.
;
; FB holds 4 pages of 64 columns, as the SSD1315 does: byte FB + page*64 + x,
; bit y&7. Coordinates are signed 16-bit and everything is clipped to the panel.
; gcol: 0 clear, 1 set, 2 invert.

PANEL_W     equ 64
PANEL_H     equ 32

; Clears the frame buffer.
gfx_cls:
        ldw x,#FB
cls_loop:
        clr (x)
        incw x
        cpw x,#FB+256
        jrne cls_loop
        ret

; Plots gx, gy (already on the panel) in gcol.
plot8:
        ld a,gy
        srl a
        srl a
        srl a                       ; page
        clrw x
        ld xl,a
        ld a,#64
        mul x,a
        ld a,xl
        or a,gx
        ld xl,a                     ; page*64 + x
        ld a,gy
        and a,#7
        clrw y
        ld yl,a
        ld a,(bit_masks,y)
        ld gmask,a
        ld a,gcol
        jreq plot_clear
        cp a,#1
        jreq plot_set
        ld a,(FB,x)
        xor a,gmask
        ld (FB,x),a
        ret
plot_set:
        ld a,(FB,x)
        or a,gmask
        ld (FB,x),a
        ret
plot_clear:
        ld a,gmask
        cpl a
        and a,(FB,x)
        ld (FB,x),a
        ret

; Plots X, Y (signed) in gcol, if on the panel.
plot16:
        cpw x,#PANEL_W
        jruge plot16_off            ; also catches negative
        cpw y,#PANEL_H
        jruge plot16_off
        ld a,xl
        ld gx,a
        ld a,yl
        ld gy,a
        call plot8
plot16_off:
        ret

; Clips rx, ry, rw, rh to the panel: cx0..cx1-1, cy0..cy1-1. Carry set when nothing is left.
clip_rect:
        ldw x,rx
        tnzw x
        jrpl clip_x0
        clrw x
clip_x0:
        cpw x,#PANEL_W
        jrsge clip_empty
        ld a,xl
        ld cx0,a
        ldw x,rx
        addw x,rw
        cpw x,#PANEL_W
        jrsle clip_x1
        ldw x,#PANEL_W
clip_x1:
        cpw x,#1
        jrslt clip_empty
        ld a,xl
        ld cx1,a
        cp a,cx0
        jrule clip_empty
        ldw x,ry
        tnzw x
        jrpl clip_y0
        clrw x
clip_y0:
        cpw x,#PANEL_H
        jrsge clip_empty
        ld a,xl
        ld cy0,a
        ldw x,ry
        addw x,rh
        cpw x,#PANEL_H
        jrsle clip_y1
        ldw x,#PANEL_H
clip_y1:
        cpw x,#1
        jrslt clip_empty
        ld a,xl
        ld cy1,a
        cp a,cy0
        jrule clip_empty
        rcf
        ret
clip_empty:
        scf
        ret

; Fills rx, ry, rw, rh in gcol.
fill16:
        call clip_rect
        jrc fill16_done
        ld a,cy0
        ld gy,a
fill_row:
        ld a,cx0
        ld gx,a
fill_col:
        call plot8
        inc gx
        ld a,gx
        cp a,cx1
        jrne fill_col
        inc gy
        ld a,gy
        cp a,cy1
        jrne fill_row
fill16_done:
        ret

; Draws the outline of rx, ry, rw, rh in gcol. Keeps rx, ry, rw, rh.
box16:
        ldw x,rw
        cpw x,#1
        jrslt box_done
        ldw x,rh
        cpw x,#1
        jrslt box_done
        ; keep the rectangle
        ldw x,rx
        ldw box_save,x
        ldw x,ry
        ldw box_save+2,x
        ldw x,rw
        ldw box_save+4,x
        ldw x,rh
        ldw box_save+6,x
        ; top
        ldw x,#1
        ldw rh,x
        call fill16
        ; bottom
        ldw x,box_save+2
        addw x,box_save+6
        decw x
        ldw ry,x
        call fill16
        ; left
        ldw x,box_save+2
        ldw ry,x
        ldw x,box_save+6
        ldw rh,x
        ldw x,#1
        ldw rw,x
        call fill16
        ; right
        ldw x,box_save
        addw x,box_save+4
        decw x
        ldw rx,x
        call fill16
        ; put the rectangle back
        ldw x,box_save
        ldw rx,x
        ldw x,box_save+4
        ldw rw,x
box_done:
        ret

; Line from (a0, a1) to (a2, a3) in gcol (Bresenham).
line16:
        ldw x,args
        ldw lx0,x
        ldw x,args+2
        ldw ly0,x
        ; ldx = |x2 - x1|, lsx = direction
        mov lsx,#1
        ldw x,args+4
        subw x,args
        jrpl line_dx
        negw x
        mov lsx,#$FF
line_dx:
        ldw ldx,x
        ; ldy = -|y2 - y1|, lsy = direction
        mov lsy,#1
        ldw x,args+6
        subw x,args+2
        jrpl line_dy
        negw x
        mov lsy,#$FF
line_dy:
        negw x
        ldw ldy,x
        ; err = dx + dy
        ldw x,ldx
        addw x,ldy
        ldw lerr,x
line_loop:
        ldw x,lx0
        ldw y,ly0
        call plot16
        ldw x,lx0
        cpw x,args+4
        jrne line_step
        ldw x,ly0
        cpw x,args+6
        jreq line_done
line_step:
        ldw x,lerr
        sllw x
        ldw le2,x
        ; if e2 >= dy: err += dy, x += sx
        cpw x,ldy
        jrslt line_no_x
        ldw x,lerr
        addw x,ldy
        ldw lerr,x
        ldw x,lx0
        tnz lsx
        jrmi line_x_back
        incw x
        jra line_x_set
line_x_back:
        decw x
line_x_set:
        ldw lx0,x
line_no_x:
        ; if e2 <= dx: err += dx, y += sy
        ldw x,le2
        cpw x,ldx
        jrsgt line_loop
        ldw x,lerr
        addw x,ldx
        ldw lerr,x
        ldw x,ly0
        tnz lsy
        jrmi line_y_back
        incw x
        jra line_y_set
line_y_back:
        decw x
line_y_set:
        ldw ly0,x
        jra line_loop
line_done:
        ret

; ---- Text ----

; Font A: 0 SMALL, 1 NORMAL, 2 LARGE (NORMAL x2), 3 BIG (NORMAL 3x across, 4x down).
set_font:
        and a,#3
        ld tfont,a
        jrne font_normal
        ldw x,#FontSmall
        ldw ttable,x
        mov tcols,#3
        mov trows,#6
        mov tpitch,#4
        mov tsx,#1
        mov tsy,#1
        ret
font_normal:
        ldw x,#FontNormal
        ldw ttable,x
        mov tcols,#5
        mov trows,#8
        mov tpitch,#6
        cp a,#2
        jreq font_large
        jrugt font_big
        mov tsx,#1
        mov tsy,#1
        ret
font_large:
        mov tsx,#2
        mov tsy,#2
        ret
font_big:
        mov tsx,#3
        mov tsy,#4
        ret

; Draws character A at tx, ty in the current font, clearing its cell, and moves tx on.
text_char:
        cp a,#32
        jrult char_unknown
        cp a,#127
        jrult char_known
char_unknown:
        ld a,#'?'
char_known:
        tnz tfont
        jrne char_table
        cp a,#96
        jrult char_table
        sub a,#32                   ; SMALL has no lowercase: a-z as A-Z
char_table:
        sub a,#32
        clrw x
        ld xl,a
        ld a,tcols
        mul x,a
        addw x,ttable
        ldw tglyph,x
        clr tcol
char_col:
        ; the column's bits, or 0 past the glyph
        clr tbits
        ld a,tcol
        cp a,tcols
        jruge char_bits
        clrw x
        ld xl,a
        addw x,tglyph
        ld a,(x)
        ld tbits,a
char_bits:
        ; rx = tx + col*sx, rw = sx
        clrw x
        ld a,tcol
        ld xl,a
        ld a,tsx
        mul x,a
        addw x,tx
        ldw rx,x
        clrw x
        ld a,tsx
        ld xl,a
        ldw rw,x
        clrw x
        ld a,tsy
        ld xl,a
        ldw rh,x
        clr trow
char_row:
        ; ry = ty + row*sy
        clrw x
        ld a,trow
        ld xl,a
        ld a,tsy
        mul x,a
        addw x,ty
        ldw ry,x
        clr gcol
        srl tbits
        jrnc char_pixel
        inc gcol
char_pixel:
        call fill16
        inc trow
        ld a,trow
        cp a,trows
        jrne char_row
        inc tcol
        ld a,tcol
        cp a,tpitch
        jrne char_col
        ; tx += pitch * sx
        clrw x
        ld a,tpitch
        ld xl,a
        ld a,tsx
        mul x,a
        addw x,tx
        ldw tx,x
        ret

; Draws the counted string at tmp2 (length, then characters) at tx, ty in font A.
kernel_text:
        call set_font
        ldw x,tmp2
        ld a,(x)
        ld buf+6,a
kernel_text_loop:
        tnz buf+6
        jreq kernel_text_done
        ldw x,tmp2
        incw x
        ldw tmp2,x
        ld a,(x)
        call text_char
        dec buf+6
        jra kernel_text_loop
kernel_text_done:
        ret

; Draws the value in a2 (args+4) as A hex digits (1-4).
text_hex:
        cp a,#1
        jrsge hex_min
        ld a,#1
hex_min:
        cp a,#4
        jrsle hex_max
        ld a,#4
hex_max:
        ld buf+6,a
        ldw x,args+4
        ldw tmp2,x
        ; fill buf from the right
        ld a,buf+6
        ld buf+5,a
hex_fill:
        ld a,tmp2+1
        and a,#$0F
        cp a,#10
        jrult hex_digit
        add a,#'A'-10
        jra hex_put
hex_digit:
        add a,#'0'
hex_put:
        dec buf+5
        clrw x
        push a
        ld a,buf+5
        ld xl,a
        pop a
        ld (buf,x),a
        ldw x,tmp2
        srlw x
        srlw x
        srlw x
        srlw x
        ldw tmp2,x
        tnz buf+5
        jrne hex_fill
        ; draw buf[0..digits-1]
        clr buf+5
hex_draw:
        clrw x
        ld a,buf+5
        ld xl,a
        ld a,(buf,x)
        call text_char
        inc buf+5
        ld a,buf+5
        cp a,buf+6
        jrne hex_draw
        ret

; Draws the value in a2 (args+4) in signed decimal.
text_dec:
        ldw x,args+4
        tnzw x
        jrpl dec_positive
        negw x
        pushw x
        ld a,#'-'
        call text_char
        popw x
dec_positive:
        clr buf+6                   ; digits
dec_fill:
        ldw y,#10
        divw x,y
        ld a,yl
        add a,#'0'
        push a
        inc buf+6
        tnzw x
        jrne dec_fill
dec_draw:
        pop a
        call text_char
        dec buf+6
        jrne dec_draw
        ret

; Draws the low A bits (1-16) of a2 (args+4) in binary.
text_bin:
        cp a,#1
        jrsge bin_min
        ld a,#1
bin_min:
        cp a,#16
        jrsle bin_max
        ld a,#16
bin_max:
        ld buf+6,a
        ; shift the value left so the first bit to draw is bit 15
        ldw x,args+4
        ld a,#16
        sub a,buf+6
bin_align:
        jreq bin_start
        sllw x
        dec a
        jra bin_align
bin_start:
        ldw tmp2,x
bin_loop:
        ldw x,tmp2
        sllw x                      ; next bit into carry
        ldw tmp2,x                  ; (LDW and LD keep the carry)
        ld a,#'0'
        jrnc bin_put
        inc a
bin_put:
        call text_char
        dec buf+6
        jrne bin_loop
        ret

; ---- Views ----

; BITS x, y, w, h, v (a0-a4): eight cells, b7 on the left, filled for 1, outlined for 0.
bits_view:
        ; cell width = w / 8
        ldw x,args+4
        sraw x
        sraw x
        sraw x
        ldw buf,x                   ; cell pitch
        decw x
        ldw rw,x                    ; cell width (1 pixel gap)
        cpw x,#1
        jrslt bits_done
        ldw x,args+6
        ldw rh,x
        ldw x,args+2
        ldw ry,x
        ldw x,args
        ldw rx,x
        ld a,#$80
        ld buf+6,a                  ; bit mask
bits_loop:
        mov gcol,#0                 ; clear the cell first
        call fill16
        mov gcol,#1
        ld a,args+9
        and a,buf+6
        jreq bits_empty
        call fill16
        jra bits_next
bits_empty:
        call box16
bits_next:
        ldw x,rx
        addw x,buf
        ldw rx,x
        srl buf+6
        jrne bits_loop
bits_done:
        ret

; BAR x, y, w, h, v, max (a0-a5): an outline filled in proportion to v / max.
bar_view:
        call args_to_rect
        mov gcol,#1
        call box16
        ; clear the inside
        ldw x,rx
        incw x
        ldw rx,x
        ldw x,ry
        incw x
        ldw ry,x
        ldw x,rw
        subw x,#2
        jrsle bar_done
        ldw rw,x
        ldw buf+2,x                 ; inside width
        ldw x,rh
        subw x,#2
        jrsle bar_done
        ldw rh,x
        mov gcol,#0
        call fill16
        ; filled width = inside * v / max, v clamped to 0..max
        ldw x,args+10
        cpw x,#1
        jrslt bar_done              ; max <= 0
        ldw x,args+8
        cpw x,#1
        jrslt bar_done              ; v <= 0
        cpw x,args+10
        jrsle bar_v_ok
        ldw x,args+10
bar_v_ok:
        ldw tmp2,x
        ldw x,buf+2
        call mul16
        ldw y,args+10
        divw x,y                    ; both positive
        ldw rw,x
        mov gcol,#1
        call fill16
bar_done:
        ret
