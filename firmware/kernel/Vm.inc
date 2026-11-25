; Wren VM: runs a handler's bytecode. See docs/Bytecode.md.
;
; The value stack grows down from VSTACK_TOP; vsp points at the top value.
; The call stack grows down from CSTACK_TOP, 4 bytes a frame: return address, frame pointer.
; The loop at vm_next calls each instruction's routine, which returns when it is done.

FAULT_FULL      equ 1
FAULT_EMPTY     equ 2
FAULT_CALLS     equ 3
FAULT_INDEX     equ 4
FAULT_DIVIDE    equ 5
FAULT_OPCODE    equ 6
FAULT_JUMP      equ 7

OP_FIRST        equ $30         ; opcodes from here on are in op_table
        IF BOARD = BOARD_LED
OP_COUNT        equ $47         ; $30-$76: the LED Thing's LEDS is $76
        ELSE
OP_COUNT        equ $46         ; $30-$75
        ENDIF

; Runs the handler at program address X until it returns.
vm_run:
        ldw vpc,x
        ldw x,#VSTACK_TOP
        ldw vsp,x
        ldw x,#VSTACK_TOP-2
        ldw fp,x
        ldw x,#CSTACK_TOP
        ldw csp,x

; Each instruction starts here. Wren's debugger stops on this address.
vm_next:
        call vm_fetch
        cp a,#OP_FIRST
        jrult vm_small
        sub a,#OP_FIRST
        cp a,#OP_COUNT
        jruge vm_bad_opcode
        sll a
        clrw x
        ld xl,a
        ldw x,(op_table,x)
        call (x)
        jra vm_next
vm_small:
        call op_small
        jra vm_next
vm_bad_opcode:
        ld a,#FAULT_OPCODE
        jp vm_error

op_table:
        dc.w op_pushb,op_pushn,op_pushw,op_value                                    ; $30
        dc.w op_loadgb,op_storegb,op_loadl,op_storel                                ; $34
        dc.w op_loadaw,op_storeaw,op_loadab,op_storeab                              ; $38
        dc.w op_add,op_sub,op_mul,op_div,op_mod,op_and,op_or,op_xor                 ; $3C
        dc.w op_not,op_neg,op_shl,op_shr,op_eq,op_ne,op_lt,op_le                    ; $44
        dc.w op_gt,op_ge,op_drop                                                    ; $4C
        dc.w op_jmp,op_jz,op_jnz,op_jmpw,op_jzw,op_jnzw,op_call,op_enter            ; $4F
        dc.w op_ret,op_retv                                                         ; $57
        dc.w sv_cls,sv_show,sv_pixel,sv_line,sv_box,sv_fill,sv_invert,sv_contrast   ; $59
        dc.w sv_inverse,sv_text,sv_hex,sv_dec,sv_bin,sv_bits,sv_bar,sv_wait         ; $61
        dc.w sv_eewrite,sv_inputs,sv_pin,sv_analog,sv_millis,sv_eeread,sv_rnd,sv_abs ; $69
        dc.w sv_min,sv_max,sv_lost,sv_flip,sv_pullups                               ; $71
        IF BOARD = BOARD_LED
        dc.w sv_leds                                                                ; $76
        ENDIF

; ---- Fetching and the value stack ----

; A = the byte at vpc; vpc moves on.
vm_fetch:
        ldw x,vpc
        cpw x,prog_len
        jruge vm_bad_jump
        call read_program
        incw x
        ldw vpc,x
        ret
vm_bad_jump:
        ld a,#FAULT_JUMP
        jp vm_error

; X = the word at vpc (high byte first); vpc moves on.
vm_fetch16:
        call vm_fetch
        push a
        call vm_fetch
        ld xl,a
        pop a
        ld xh,a
        ret

; Pushes X. Uses Y.
vpush:
        ldw y,vsp
        subw y,#2
        cpw y,#VSTACK_LOW
        jrult vm_full
        ldw vsp,y
        ldw (y),x
        ret
vm_full:
        ld a,#FAULT_FULL
        jp vm_error

; Pops into X.
vpop:
        ldw x,vsp
        cpw x,#VSTACK_TOP
        jruge vm_empty
        incw x
        incw x
        ldw vsp,x
        decw x
        decw x
        ldw x,(x)
        ret
vm_empty:
        ld a,#FAULT_EMPTY
        jp vm_error

; Pops the second operand into tmp2 and the first into X.
vpop2:
        call vpop
        ldw tmp2,x
        jra vpop

; ---- Constants and variables ----

; $00-$2F: PUSHS n, LOADG n, STOREG n (n in the low 4 bits).
op_small:
        cp a,#$10
        jruge small_var
        clrw x                      ; PUSHS
        ld xl,a
        jp vpush
small_var:
        ld tmp,a
        and a,#$0F
        sll a
        clrw x
        ld xl,a                     ; offset of global n
        ld a,tmp
        cp a,#$20
        jruge small_store
        ldw x,(VARS,x)              ; LOADG
        jp vpush
small_store:
        ld a,xl                     ; STOREG
        jra vm_store_var

op_pushb:
        call vm_fetch
        clrw x
        ld xl,a
        jp vpush

op_pushn:
        call vm_fetch
        ld xl,a
        ld a,#$FF
        ld xh,a
        jp vpush

op_pushw:
        call vm_fetch16
        jp vpush

op_value:
        clrw x
        ld a,evt_value
        ld xl,a
        jp vpush

op_loadgb:
        call vm_fetch
        sll a
        clrw x
        ld xl,a
        ldw x,(VARS,x)
        jp vpush

op_storegb:
        call vm_fetch
        sll a
; Pops into the variable at VARS+A.
vm_store_var:
        push a
        call vpop
        pop a
        clrw y
        ld yl,a
        ldw (VARS,y),x
        ret

; X = address of parameter or local (operand byte): fp - 2*n.
vm_local_address:
        call vm_fetch
        sll a
        ld tmp+1,a
        clr tmp
        ldw x,fp
        subw x,tmp
        ret

op_loadl:
        call vm_local_address
        ldw x,(x)
        jp vpush

op_storel:
        call vm_local_address
        pushw x
        call vpop
        popw y
        ldw (y),x
        ret

; X = address of an array element. A = 1 for word arrays, 0 for bytes. Reads the
; operands (base, length), pops the index and checks it against the length.
vm_element:
        push a
        call vm_fetch
        ld tmp+1,a
        clr tmp                     ; base
        call vm_fetch
        ld tmp2+1,a
        clr tmp2                    ; length
        call vpop
        cpw x,tmp2
        jruge vm_bad_index          ; also catches negative indexes
        pop a
        tnz a
        jreq element_byte
        sllw x
element_byte:
        addw x,tmp
        addw x,#VARS
        ret
vm_bad_index:
        ld a,#FAULT_INDEX
        jp vm_error

op_loadaw:
        ld a,#1
        call vm_element
        ldw x,(x)
        jp vpush

op_loadab:
        clr a
        call vm_element
        ld a,(x)
        clrw x
        ld xl,a
        jp vpush

op_storeaw:
        call vpop                   ; the value
        ldw buf+2,x
        ld a,#1
        call vm_element
        ldw y,buf+2
        ldw (x),y
        ret

op_storeab:
        call vpop
        ldw buf+2,x
        clr a
        call vm_element
        ld a,buf+3
        ld (x),a
        ret

; ---- Arithmetic and logic ----

op_add:
        call vpop2
        addw x,tmp2
        jp vpush

op_sub:
        call vpop2
        subw x,tmp2
        jp vpush

op_mul:
        call vpop2
        call mul16
        jp vpush

op_div:
        call vpop2
        call sdivmod
        jp vpush

op_mod:
        call vpop2
        call sdivmod
        ldw x,y
        jp vpush

op_and:
        call vpop2
        ld a,xh
        and a,tmp2
        ld xh,a
        ld a,xl
        and a,tmp2+1
        ld xl,a
        jp vpush

op_or:
        call vpop2
        ld a,xh
        or a,tmp2
        ld xh,a
        ld a,xl
        or a,tmp2+1
        ld xl,a
        jp vpush

op_xor:
        call vpop2
        ld a,xh
        xor a,tmp2
        ld xh,a
        ld a,xl
        xor a,tmp2+1
        ld xl,a
        jp vpush

op_neg:
        call vpop
        negw x
        jp vpush

; SHL and SHR: the count is taken modulo 16.
op_shl:
        call vm_shift_count
shl_loop:
        jreq shift_done
        sllw x
        dec a
        jra shl_loop

op_shr:
        call vm_shift_count
shr_loop:
        jreq shift_done
        srlw x
        dec a
        jra shr_loop
shift_done:
        jp vpush

vm_shift_count:
        call vpop2
        ld a,tmp2+1
        and a,#$0F
        ret

op_drop:
        call vpop
        ret

; Comparisons leave -1 (true) or 0 (false). vm_compare sets the flags; RET keeps them.
vm_compare:
        call vpop2
        cpw x,tmp2
        ret

op_not:
        call vpop
        tnzw x
        jreq vm_true
        jra vm_false
op_eq:
        call vm_compare
        jreq vm_true
        jra vm_false
op_ne:
        call vm_compare
        jrne vm_true
        jra vm_false
op_lt:
        call vm_compare
        jrslt vm_true
        jra vm_false
op_le:
        call vm_compare
        jrsle vm_true
        jra vm_false
op_gt:
        call vm_compare
        jrsgt vm_true
        jra vm_false
op_ge:
        call vm_compare
        jrsge vm_true
vm_false:
        clrw x
        jp vpush
vm_true:
        ldw x,#$FFFF
        jp vpush

; X = X * tmp2, the low 16 bits. Uses Y, tmp, buf.
mul16:
        ldw tmp,x
        clrw x
        ld a,tmp+1
        ld xl,a
        ld a,tmp2+1
        mul x,a                     ; low * low
        ldw y,x
        clrw x
        ld a,tmp
        ld xl,a
        ld a,tmp2+1
        mul x,a                     ; high * low
        call mul_add_high
        clrw x
        ld a,tmp+1
        ld xl,a
        ld a,tmp2
        mul x,a                     ; low * high
        call mul_add_high
        ldw x,y
        ret
; Adds XL to YH.
mul_add_high:
        ld a,xl
        ld buf,a
        ld a,yh
        add a,buf
        ld yh,a
        ret

; Signed X / tmp2: X = quotient, Y = remainder, both truncated toward zero.
sdivmod:
        ldw y,tmp2
        tnzw y
        jrne div_ok
        ld a,#FAULT_DIVIDE
        jp vm_error
div_ok:
        clr buf                     ; quotient negative
        clr buf+1                   ; remainder negative
        tnzw x
        jrpl div_a_pos
        negw x
        cpl buf
        cpl buf+1
div_a_pos:
        tnzw y
        jrpl div_b_pos
        negw y
        cpl buf
div_b_pos:
        divw x,y
        tnz buf
        jreq div_q_done
        negw x
div_q_done:
        tnz buf+1
        jreq div_r_done
        negw y
div_r_done:
        ret

; ---- Jumps and calls ----

op_jmp:
        call vm_fetch
        call vm_relative
        ldw vpc,x
        ret

op_jmpw:
        call vm_fetch16
        ldw vpc,x
        ret

; Conditional jumps. A: bit 0 = jump when the value is not zero, bit 1 = 16-bit address.
op_jz:
        clr a
        jra vm_cond
op_jnz:
        ld a,#1
        jra vm_cond
op_jzw:
        ld a,#2
        jra vm_cond
op_jnzw:
        ld a,#3
vm_cond:
        push a
        bcp a,#2
        jrne cond_long
        call vm_fetch
        call vm_relative
        jra cond_target
cond_long:
        call vm_fetch16
cond_target:
        ldw buf+4,x                 ; where to go
        call vpop
        pop a
        tnzw x
        jreq cond_zero
        bcp a,#1
        jreq cond_stay              ; not zero, but JZ
        jra cond_take
cond_zero:
        bcp a,#1
        jrne cond_stay              ; zero, but JNZ
cond_take:
        ldw x,buf+4
        ldw vpc,x
cond_stay:
        ret

; X = vpc + the signed byte A.
vm_relative:
        clrw x
        ld xl,a
        tnz a
        jrpl rel_pos
        ld a,#$FF
        ld xh,a
rel_pos:
        addw x,vpc
        ret

op_call:
        call vm_fetch16
        ldw tmp2,x                  ; target
        ldw y,csp
        subw y,#4
        cpw y,#CSTACK_LOW
        jrult vm_calls_full
        ldw csp,y
        ldw x,vpc
        ldw (y),x                   ; return address
        ldw x,fp
        ldw (2,y),x                 ; caller's frame
        ldw x,tmp2
        ldw vpc,x
        ret
vm_calls_full:
        ld a,#FAULT_CALLS
        jp vm_error

; ENTER params, locals: fp = address of parameter 0, then the locals are pushed as 0.
op_enter:
        call vm_fetch               ; parameters
        ld tmp+1,a
        call vm_fetch               ; locals
        ld tmp2+1,a
        ; fp = vsp + 2*(params-1)
        ld a,tmp+1
        sll a
        clrw x
        ld xl,a
        addw x,vsp
        subw x,#2
        ldw fp,x
enter_locals:
        tnz tmp2+1
        jreq enter_done
        clrw x
        call vpush
        dec tmp2+1
        jra enter_locals
enter_done:
        ret

op_retv:
        call vpop
        ldw buf+4,x
        call vm_leave
        ldw x,buf+4
        jp vpush

op_ret:
        call vm_leave
        ret

; Removes the current frame and returns to the caller's code. With no caller the
; handler is finished: the stack is unwound to return from vm_run.
vm_leave:
        ldw x,csp
        cpw x,#CSTACK_TOP
        jrne leave_frame
        addw sp,#4                  ; vm_leave's and the instruction's return addresses
        ret
leave_frame:
        ldw x,fp
        addw x,#2
        ldw vsp,x                   ; drop parameters and locals
        ldw x,csp
        pushw x
        ldw x,(x)
        ldw vpc,x                   ; return address
        popw x
        ldw x,(2,x)
        ldw fp,x                    ; caller's frame
        ldw x,csp
        addw x,#4
        ldw csp,x
        ret

; ---- Services ----

; Pops A arguments into args: the first argument into a0.
pop_args:
        sll a
pop_args_loop:
        tnz a
        jreq pop_args_done
        sub a,#2
        push a
        call vpop
        pop a
        clrw y
        ld yl,a
        ldw (args,y),x
        jra pop_args_loop
pop_args_done:
        ret

        IF BOARD = BOARD_OLED
; Copies args a0-a3 to the rectangle rx, ry, rw, rh.
args_to_rect:
        ldw x,args
        ldw rx,x
        ldw x,args+2
        ldw ry,x
        ldw x,args+4
        ldw rw,x
        ldw x,args+6
        ldw rh,x
        ret

sv_cls:
        call gfx_cls
        ret

sv_show:
        call oled_show
        ret

sv_pixel:
        ld a,#3
        call pop_args
        ld a,args+5
        ld gcol,a
        ldw x,args
        ldw y,args+2
        call plot16
        ret

sv_line:
        ld a,#5
        call pop_args
        ld a,args+9
        ld gcol,a
        call line16
        ret

sv_box:
        ld a,#5
        call pop_args
        ld a,args+9
        ld gcol,a
        call args_to_rect
        call box16
        ret

sv_fill:
        ld a,#5
        call pop_args
        ld a,args+9
        ld gcol,a
        call args_to_rect
        call fill16
        ret

sv_invert:
        ld a,#4
        call pop_args
        mov gcol,#2
        call args_to_rect
        call fill16
        ret

sv_contrast:
        call vpop
        ld a,xl
        push a
        ld a,#$81
        call oled_cmd_start
        pop a
        call i2c_send
        call i2c_stop
        ret

sv_inverse:
        call vpop
        ld a,#$A6
        tnzw x
        jreq inverse_send
        ld a,#$A7
inverse_send:
        call oled_cmd
        ret

; FLIP: the Thing's module is mounted rotated, so upright is $A1 (segment remap) with $C8
; (reversed COM scan), as oled_init sets it; FLIP turns the picture 180 degrees from that.
sv_flip:
        call vpop
        tnzw x
        jreq flip_off
        ld a,#$A0                   ; upside down: no remap,
        call oled_cmd
        ld a,#$C0                   ; normal COM scan
        call oled_cmd
        ret
flip_off:
        ld a,#$A1                   ; upright
        call oled_cmd
        ld a,#$C8
        call oled_cmd
        ret

; TEXT x, y, font with the string inline: length, then the characters.
sv_text:
        ld a,#3
        call pop_args
        ldw x,args
        ldw tx,x
        ldw x,args+2
        ldw ty,x
        ld a,args+5
        call set_font
        call vm_fetch
        ld buf+6,a                  ; characters left
text_loop:
        tnz buf+6
        jreq text_done
        call vm_fetch
        call text_char
        dec buf+6
        jra text_loop
text_done:
        ret

; Sets the text position from a0, a1 and the font from argument A (its offset in args).
text_start:
        clrw y
        ld yl,a
        ld a,(args+1,y)
        call set_font
        ldw x,args
        ldw tx,x
        ldw x,args+2
        ldw ty,x
        ret

sv_hex:
        ld a,#5
        call pop_args
        ld a,#8
        call text_start
        ld a,args+7                 ; digits
        call text_hex
        ret

sv_dec:
        ld a,#4
        call pop_args
        ld a,#6
        call text_start
        call text_dec
        ret

sv_bin:
        ld a,#5
        call pop_args
        ld a,#8
        call text_start
        ld a,args+7                 ; bits
        call text_bin
        ret

sv_bits:
        ld a,#5
        call pop_args
        call bits_view
        ret

sv_bar:
        ld a,#6
        call pop_args
        call bar_view
        ret
        ENDIF

sv_wait:
        call vpop
        ldw tmp2,x                  ; milliseconds
        ldw y,millis
        ldw buf+4,y                 ; start
wait_loop:
        ldw x,millis
        subw x,buf+4
        cpw x,tmp2
        jruge wait_done
        nop                         ; not WFI, so the debugger can still reach the chip
        jra wait_loop
wait_done:
        ret

sv_eewrite:
        ld a,#2
        call pop_args
        ldw x,args
        cpw x,INFO_SETSIZE
        jruge eewrite_done          ; outside the settings: ignored
        addw x,INFO_SETTINGS
        mov FLASH_DUKR,#$AE
        mov FLASH_DUKR,#$56
        ld a,args+3
        call ee_write               ; only writes when the value changes
        bres FLASH_IAPSR,#3         ; lock again
eewrite_done:
        ret

; Writes A to the EEPROM byte at X (unlocked) unless it already holds it.
ee_write:
        cp a,(x)
        jreq ee_same
        ld (x),a
ee_wait:
        btjf FLASH_IAPSR,#2,ee_wait
ee_same:
        ret

        IF BOARD = BOARD_OLED
; PULLUPS mask: bit n pulls bn up (1) or leaves it floating (0). b0-b4 are PC7-PC3,
; b5-b7 PD6-PD4.
sv_pullups:
        call vpop
        ld a,xl
        ld tmp,a
        ld a,#8
pull_loop:
        srl tmp
        rlcw x                      ; XL becomes the mask reversed: b0 in bit 7 ... b7 in bit 0
        dec a
        jrne pull_loop
        ld a,xl
        and a,#$F8                  ; b0-b4 to bits 7-3: PC7-PC3
        ld tmp+1,a
        ld a,PC_CR1
        and a,#$07
        or a,tmp+1
        ld PC_CR1,a
        ld a,xl
        swap a
        and a,#$70                  ; b5-b7 to bits 6-4: PD6-PD4
        ld tmp+1,a
        ld a,PD_CR1
        and a,#$8F
        or a,tmp+1
        ld PD_CR1,a
        ret
        ELSE
; LED Thing: PULLUPS mask. b0 is PD2, b1-b5 are PC7-PC3; b6 and b7 (PB4, PB5) are true
; open-drain pins with no pull-up, so their bits are ignored.
sv_pullups:
        call vpop
        ld a,xl
        ld tmp,a
        ld a,PD_CR1
        and a,#$FB
        btjf tmp,#0,pull_d
        or a,#$04                   ; b0: PD2
pull_d:
        ld PD_CR1,a
        srl tmp                     ; b0 done
        clr tmp+1
        ld a,#5
pull_c:
        srl tmp                     ; b1 first ...
        rlc tmp+1                   ; ... ending in bit 4, b5 in bit 0
        dec a
        jrne pull_c
        ld a,tmp+1
        sll a
        sll a
        sll a                       ; b1 in bit 7 (PC7) ... b5 in bit 3 (PC3)
        ld tmp+1,a
        ld a,PC_CR1
        and a,#$07
        or a,tmp+1
        ld PC_CR1,a
        ret
        ENDIF

sv_inputs:
        call read_inputs
        clrw x
        ld xl,a
        jp vpush

sv_pin:
        call vpop
        ld a,xl
        cp a,#5
        jruge pin_zero
        clrw y
        ld yl,a
        call read_pins
        and a,(bit_masks,y)
        jreq pin_zero
        ldw x,#1
        jp vpush
pin_zero:
        clrw x
        jp vpush

        IF BOARD = BOARD_OLED
; ANALOG p: 0-1023 from PD2 (AIN3) or PD3 (AIN4); other pins give 0. The pin's pull-up
; is turned off, since it would pull the voltage up: the pin is then an analog input.
sv_analog:
        call vpop
        ld a,xl
        cp a,#3                     ; PD2 is pin 3 and AIN3, PD3 is pin 4 and AIN4
        jrult analog_zero
        cp a,#5
        jruge analog_zero
        ld ADC_CSR,a                ; channel; clears EOC
        dec a                       ; PD2 -> bit 2, PD3 -> bit 3
        clrw x
        ld xl,a
        ld a,(bit_masks,x)
        cpl a
        and a,PD_CR1
        ld PD_CR1,a                 ; pull-up off
        mov ADC_CR2,#$08            ; right-aligned
        bset ADC_CR1,#0             ; wake up (or start, if already awake)
        ld a,#40
analog_settle:
        dec a                       ; let it settle: one conversion time, ~2 us
        jrne analog_settle
        bset ADC_CR1,#0             ; start
analog_wait:
        btjf ADC_CSR,#7,analog_wait
        ld a,ADC_DRL                ; low byte first when right-aligned
        ld xl,a
        ld a,ADC_DRH
        ld xh,a
        bres ADC_CR1,#0             ; power down
        jp vpush
analog_zero:
        clrw x
        jp vpush
        ENDIF

sv_millis:
        ldw x,millis
        jp vpush

sv_eeread:
        call vpop
        cpw x,INFO_SETSIZE
        jruge eeread_zero           ; outside the settings: 0
        addw x,INFO_SETTINGS
        ld a,(x)
        clrw x
        ld xl,a
        jp vpush
eeread_zero:
        clrw x
        jp vpush

; RND n: 0 to n-1 from a 16-bit Galois LFSR, stirred with the clock.
sv_rnd:
        call vpop
        ldw tmp2,x                  ; n
        ldw x,rnd
        ld a,xl
        xor a,millis+1
        ld xl,a
        srlw x
        jrnc rnd_keep
        ld a,xh
        xor a,#$B4
        ld xh,a
rnd_keep:
        tnzw x
        jrne rnd_store
        incw x                      ; never let the state stick at 0
rnd_store:
        ldw rnd,x
        ldw y,tmp2
        cpw y,#1
        jrslt rnd_zero
        divw x,y
        ldw x,y
        jp vpush
rnd_zero:
        clrw x
        jp vpush

sv_abs:
        call vpop
        tnzw x
        jrpl abs_done
        negw x
abs_done:
        jp vpush

sv_min:
        call vpop2
        cpw x,tmp2
        jrsle min_done
        ldw x,tmp2
min_done:
        jp vpush

sv_max:
        call vpop2
        cpw x,tmp2
        jrsge max_done
        ldw x,tmp2
max_done:
        jp vpush

sv_lost:
        ldw x,lost
        jp vpush
