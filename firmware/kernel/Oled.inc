; Wren OLED driver: the I2C peripheral (master, polling) and the SSD1315.
; The panel shows RAM columns 32-95 and pages 0-3.

OLED_WRITE  equ $78         ; address $3C, write
OLED_CMDS   equ $00         ; control byte: commands follow
OLED_DATA   equ $40         ; control byte: data follows

; 400 kHz fast mode from the 16 MHz peripheral clock.
i2c_init:
        mov I2C_FREQR,#16
        mov I2C_CCRL,#13
        mov I2C_CCRH,#$80           ; fast mode
        mov I2C_TRISER,#6
        mov I2C_OARH,#$40           ; ADDCONF must be set
        bset I2C_CR1,#0
        ret

; START and the OLED's write address.
i2c_start:
        bset I2C_CR2,#0
i2c_wait_sb:
        btjf I2C_SR1,#0,i2c_wait_sb
        ld a,#OLED_WRITE
        ld I2C_DR,a
i2c_wait_addr:
        btjf I2C_SR1,#1,i2c_wait_addr
        ld a,I2C_SR3                ; reading SR1 then SR3 clears ADDR
        ret

; Sends A.
i2c_send:
        btjf I2C_SR1,#7,i2c_send
        ld I2C_DR,a
        ret

i2c_stop:
        btjf I2C_SR1,#2,i2c_stop
        bset I2C_CR2,#1
        ret

; Starts a command transfer and sends command A.
oled_cmd_start:
        push a
        call i2c_start
        ld a,#OLED_CMDS
        call i2c_send
        pop a
        jra i2c_send

; Sends the single command A.
oled_cmd:
        call oled_cmd_start
        jra i2c_stop

oled_init:
        call i2c_start
        ld a,#OLED_CMDS
        call i2c_send
        ldw x,#oled_init_table
oled_init_loop:
        ld a,(x)
        cp a,#$FF
        jreq oled_init_end
        call i2c_send
        incw x
        jra oled_init_loop
oled_init_end:
        jra i2c_stop

oled_init_table:
        dc.b $AE                    ; display off
        dc.b $D5,$80                ; clock
        dc.b $A8,$1F                ; multiplex 32
        dc.b $D3,$00                ; display offset 0
        dc.b $40                    ; start line 0
        dc.b $8D,$14                ; charge pump on
        dc.b $20,$00                ; horizontal addressing
        dc.b $A0                    ; columns not mirrored
        dc.b $C0                    ; rows not mirrored
        dc.b $DA,$12                ; COM pins
        dc.b $81,$CF                ; contrast
        dc.b $D9,$F1                ; pre-charge
        dc.b $DB,$40                ; VCOMH
        dc.b $A4                    ; show RAM
        dc.b $A6                    ; not inverted
        dc.b $AF                    ; display on
        dc.b $FF                    ; end of table

; Sends the frame buffer to the panel's window: columns 32-95, pages 0-3.
oled_show:
        call i2c_start
        ld a,#OLED_CMDS
        call i2c_send
        ld a,#$21
        call i2c_send
        ld a,#32
        call i2c_send
        ld a,#95
        call i2c_send
        ld a,#$22
        call i2c_send
        clr a
        call i2c_send
        ld a,#3
        call i2c_send
        call i2c_stop
        call i2c_start
        ld a,#OLED_DATA
        call i2c_send
        ldw x,#FB
show_loop:
        ld a,(x)
        call i2c_send
        incw x
        cpw x,#FB+256
        jrne show_loop
        jp i2c_stop
