; HexDemo: shows the byte on b0-b7 as two large hex digits.
; INV (PA3) pulled low inverts the display.
;
; A test program for Wren's emulator, not the OLED Thing's firmware design.
; It uses the I2C peripheral, polling, with the OLED at address $3C.

        include "stm8s003.inc"

OLED_WRITE  equ $78         ; $3C shifted left, R/W = 0
CMD_STREAM  equ $00         ; control byte: commands follow
DATA_STREAM equ $40         ; control byte: data follows

; RAM
value       equ $00         ; the byte shown
shown       equ $01         ; the byte on screen now
inverted    equ $02         ; 1 when the display is inverted
column      equ $05         ; drawing counters
page        equ $06

        org $8000
        int start           ; reset
        int start           ; TRAP (unused)

        org $8080
start:
        ldw x,#RAM_END
        ldw sp,x
        clr CLK_CKDIVR      ; 16 MHz HSI, CPU at full speed
        bset PA_CR1,#1      ; pull-ups on the mode pins HEX, BAR, INV
        bset PA_CR1,#2
        bset PA_CR1,#3

        call i2c_init
        call oled_init

        mov inverted,#$FF   ; force the first INV update
        call read_inputs
        ld a,value
        ld shown,a
        call draw           ; first draw

main:
        call read_inputs
        call update_invert
        ld a,value
        cp a,shown
        jreq main
        ld shown,a
        call draw
        jra main

; ---- Inputs ----

; value = b7..b0. b0-b4 are PC7-PC3, b5-b7 are PD6-PD4.
; Bits are rotated out of the port bytes and into value from the top, so the first
; bit taken (b0) ends up in bit 0 after eight rotations.
read_inputs:
        ld a,PC_IDR
        sll a               ; PC7 = b0
        rrc value
        sll a               ; PC6 = b1
        rrc value
        sll a               ; PC5 = b2
        rrc value
        sll a               ; PC4 = b3
        rrc value
        sll a               ; PC3 = b4
        rrc value
        ld a,PD_IDR
        sll a               ; PD7 (not used)
        sll a               ; PD6 = b5
        rrc value
        sll a               ; PD5 = b6
        rrc value
        sll a               ; PD4 = b7
        rrc value
        ret

; INV low inverts the display. Only sends a command when it changes.
update_invert:
        clr a
        btjt PA_IDR,#3,inv_read
        inc a
inv_read:
        cp a,inverted
        jreq inv_done
        ld inverted,a
        call i2c_start_commands
        ld a,#$A6           ; normal
        tnz inverted
        jreq inv_send
        ld a,#$A7           ; inverse
inv_send:
        call i2c_send
        call i2c_stop
inv_done:
        ret

; ---- Drawing ----

; Draws the two digits of value into the 64x32 window, columns 32-95 of the SSD1315's RAM.
; Horizontal addressing: 64 bytes for page 0, then pages 1, 2 and 3.
; Columns 0-15 and 48-63 are blank, the high digit is at 16-31 and the low digit at 32-47.
draw:
        call i2c_start_commands
        ld a,#$21           ; column range 32-95
        call i2c_send
        ld a,#32
        call i2c_send
        ld a,#95
        call i2c_send
        ld a,#$22           ; page range 0-3
        call i2c_send
        clr a
        call i2c_send
        ld a,#3
        call i2c_send
        call i2c_stop

        call i2c_start
        ld a,#DATA_STREAM
        call i2c_send

        clr page
draw_page:
        ; 16 blank columns
        ld a,#16
        call send_blank
        ; high digit
        ld a,value
        swap a
        call draw_digit_row
        ; low digit
        ld a,value
        call draw_digit_row
        ; 16 blank columns
        ld a,#16
        call send_blank
        inc page
        ld a,page
        cp a,#4
        jrne draw_page

        call i2c_stop
        ret

; Sends A zero bytes.
send_blank:
        ld column,a
blank_loop:
        clr a
        call i2c_send
        dec column
        jrne blank_loop
        ret

; Sends one page (16 bytes) of the digit in the low nibble of A.
; Glyph address = BigDigits + digit * 64 + page * 16.
draw_digit_row:
        and a,#$0F
        ldw x,#64
        mul x,a             ; X = digit * 64
        ld a,page
        swap a              ; page * 16 (page is 0-3)
        clrw y
        ld yl,a
        pushw y
        addw x,(1,sp)       ; X = digit * 64 + page * 16
        popw y
        addw x,#BigDigits
        ld a,#16
        ld column,a
digit_loop:
        ld a,(x)
        call i2c_send
        incw x
        dec column
        jrne digit_loop
        ret

; ---- OLED ----

oled_init:
        call i2c_start_commands
        ldw x,#oled_init_table
init_loop:
        ld a,(x)
        cp a,#$FF           ; end marker ($FF is not a command used here)
        jreq init_end
        call i2c_send
        incw x
        jra init_loop
init_end:
        call i2c_stop
        ret

oled_init_table:
        dc.b $AE            ; display off
        dc.b $D5,$80        ; clock
        dc.b $A8,$1F        ; multiplex 32
        dc.b $D3,$00        ; display offset 0
        dc.b $40            ; start line 0
        dc.b $8D,$14        ; charge pump on
        dc.b $20,$00        ; horizontal addressing
        dc.b $A0            ; segment remap off
        dc.b $C0            ; COM scan normal
        dc.b $DA,$12        ; COM pins
        dc.b $81,$CF        ; contrast
        dc.b $D9,$F1        ; pre-charge
        dc.b $DB,$40        ; VCOMH
        dc.b $A4            ; show RAM
        dc.b $A6            ; normal
        dc.b $AF            ; display on
        dc.b $FF            ; end

; ---- I2C (master, polling) ----

i2c_init:
        mov I2C_FREQR,#16   ; 16 MHz peripheral clock
        mov I2C_CCRL,#80    ; 100 kHz standard mode
        clr I2C_CCRH
        mov I2C_TRISER,#17
        mov I2C_OARH,#$40   ; ADDCONF must be set
        bset I2C_CR1,#0     ; enable
        ret

; START, then the OLED's address, then a command-stream control byte.
i2c_start_commands:
        call i2c_start
        ld a,#CMD_STREAM
        call i2c_send
        ret

; START and the OLED's write address.
i2c_start:
        bset I2C_CR2,#0     ; START
wait_sb:
        btjf I2C_SR1,#0,wait_sb
        ld a,#OLED_WRITE
        ld I2C_DR,a
wait_addr:
        btjf I2C_SR1,#1,wait_addr
        ld a,I2C_SR3        ; reading SR1 then SR3 clears ADDR
        ret

; Sends A.
i2c_send:
wait_txe:
        btjf I2C_SR1,#7,wait_txe
        ld I2C_DR,a
        ret

i2c_stop:
wait_btf:
        btjf I2C_SR1,#2,wait_btf
        bset I2C_CR2,#1     ; STOP
        ret

        include "BigDigits.inc"
