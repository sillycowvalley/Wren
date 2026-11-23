; Wren VM: runs a handler's bytecode. See docs/Bytecode.md.
;
; The value stack grows down from VSTACK_TOP; vsp points at the top value.
; The call stack grows down from CSTACK_TOP, 4 bytes a frame: return address, frame pointer.

FAULT_FULL      equ 1
FAULT_EMPTY     equ 2
FAULT_CALLS     equ 3
FAULT_INDEX     equ 4
FAULT_DIVIDE    equ 5
FAULT_OPCODE    equ 6
FAULT_JUMP      equ 7

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
        cp a,#$10
        jrult vm_go_pushs
        cp a,#$14
        jrult vm_group_push
        cp a,#$20
        jrult vm_bad_opcode
        cp a,#$30
        jrult vm_go_loadg
        cp a,#$40
        jrult vm_go_storeg
        cp a,#$48
        jrult vm_group_vars
        cp a,#$50
        jrult vm_bad_opcode
        cp a,#$63
        jrult vm_group_math
        cp a,#$70
        jrult vm_bad_opcode
        cp a,#$7A
        jrult vm_group_flow
        cp a,#$80
        jrult vm_bad_opcode
        cp a,#$9C
        jrult vm_group_sys
vm_bad_opcode:
        ld a,#FAULT_OPCODE
        jp vm_error
vm_go_pushs:
        jp op_pushs
vm_go_loadg:
        jp op_loadg
vm_go_storeg:
        jp op_storeg

vm_group_push:
        sub a,#$10
        ldw y,#table_push
        jra vm_dispatch
vm_group_vars:
        sub a,#$40
        ldw y,#table_vars
        jra vm_dispatch
vm_group_math:
        sub a,#$50
        ldw y,#table_math
        jra vm_dispatch
vm_group_flow:
        sub a,#$70
        ldw y,#table_flow
        jra vm_dispatch
vm_group_sys:
        sub a,#$80
        ldw y,#table_sys
; Jumps to entry A of the table at Y.
vm_dispatch:
        sll a
        clrw x
        ld xl,a
        ldw tmp,y
        addw x,tmp
        ldw x,(x)
        jp (x)

table_push: dc.w op_pushb,op_pushn,op_pushw,op_value
table_vars: dc.w op_loadgb,op_storegb,op_loadl,op_storel,op_loadaw,op_storeaw,op_loadab,op_storeab
table_math: dc.w op_add,op_sub,op_mul,op_div,op_mod,op_and,op_or,op_xor,op_not,op_neg,op_shl,op_shr
            dc.w op_eq,op_ne,op_lt,op_le,op_gt,op_ge,op_drop
table_flow: dc.w op_jmp,op_jz,op_jnz,op_jmpw,op_jzw,op_jnzw,op_call,op_enter,op_ret,op_retv
table_sys:  dc.w sv_cls,sv_show,sv_pixel,sv_line,sv_box,sv_fill,sv_invert,sv_contrast
            dc.w sv_inverse,sv_text,sv_hex,sv_dec,sv_bin,sv_bits,sv_bar,sv_wait
            dc.w sv_eewrite,sv_inputs,sv_pin,sv_analog,sv_millis,sv_eeread,sv_rnd,sv_abs
            dc.w sv_min,sv_max,sv_lost,sv_flip

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

; Pushes X and goes on to the next instruction.
vm_push_next:
        call vpush
        jp vm_next

; Pushes -1 if the zero flag is clear... (see the compare ops) or 0.
vm_true:
        ldw x,#$FFFF
        jra vm_push_next
vm_false:
        clrw x
        jra vm_push_next

; ---- Constants and variables ----

op_pushs:
        clrw x
        ld xl,a
        jra vm_push_next

op_pushb:
        call vm_fetch
        clrw x
        ld xl,a
        jra vm_push_next

op_pushn:
        call vm_fetch
        ld xl,a
        ld a,#$FF
        ld xh,a
        jra vm_push_next

op_pushw:
        call vm_fetch16
        jra vm_push_next

op_value:
        clrw x
        ld a,evt_value
        ld xl,a
        jra vm_push_next

op_loadg:
        and a,#$0F
        sll a
        clrw x
        ld xl,a
        ldw x,(VARS,x)
        jra vm_push_next

op_storeg:
        and a,#$0F
        sll a
        jra vm_store_var

op_loadgb:
        call vm_fetch
        sll a
        clrw x
        ld xl,a
        ldw x,(VARS,x)
        jra vm_push_next

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
        jp vm_next

; Address of parameter or local A in X: fp - 2*A.
vm_local_address:
        sll a
        ld tmp+1,a
        clr tmp
        ldw x,fp
        subw x,tmp
        ret

op_loadl:
        call vm_fetch
        call vm_local_address
        ldw x,(x)
        jra vm_push_next

op_storel:
        call vm_fetch
        call vm_local_address
        pushw x
        call vpop
        popw y
        ldw (y),x
        jp vm_next

; Reads the array operands (base, length) and pops the index: X = element number,
; checked against the length; tmp = base.
vm_array_index:
        call vm_fetch
        ld tmp+1,a
        clr tmp
        call vm_fetch
        ld tmp2+1,a
        clr tmp2
        pushw x
        ldw x,tmp
        ldw buf,x                   ; keep the base
        popw x
        call vpop
        cpw x,tmp2
        jruge vm_bad_index          ; also catches negative indexes
        ldw y,buf
        ldw tmp,y
        ret
vm_bad_index:
        ld a,#FAULT_INDEX
        jp vm_error

op_loadaw:
        call vm_array_index
        sllw x
        addw x,tmp
        addw x,#VARS
        ldw x,(x)
        jp vm_push_next

op_storeaw:
        call vpop                   ; the value
        ldw buf+2,x
        call vm_array_index
        sllw x
        addw x,tmp
        addw x,#VARS
        ldw y,buf+2
        ldw (x),y
        jp vm_next

op_loadab:
        call vm_array_index
        addw x,tmp
        addw x,#VARS
        ld a,(x)
        clrw x
        ld xl,a
        jp vm_push_next

op_storeab:
        call vpop
        ldw buf+2,x
        call vm_array_index
        addw x,tmp
        addw x,#VARS
        ld a,buf+3
        ld (x),a
        jp vm_next

; ---- Arithmetic and logic ----

op_add:
        call vpop2
        addw x,tmp2
        jp vm_push_next

op_sub:
        call vpop2
        subw x,tmp2
        jp vm_push_next

op_mul:
        call vpop2
        call mul16
        jp vm_push_next

op_div:
        call vpop2
        call sdivmod
        jp vm_push_next

op_mod:
        call vpop2
        call sdivmod
        ldw x,y
        jp vm_push_next

op_and:
        call vpop2
        ld a,xh
        and a,tmp2
        ld xh,a
        ld a,xl
        and a,tmp2+1
        ld xl,a
        jp vm_push_next

op_or:
        call vpop2
        ld a,xh
        or a,tmp2
        ld xh,a
        ld a,xl
        or a,tmp2+1
        ld xl,a
        jp vm_push_next

op_xor:
        call vpop2
        ld a,xh
        xor a,tmp2
        ld xh,a
        ld a,xl
        xor a,tmp2+1
        ld xl,a
        jp vm_push_next

op_not:
        call vpop
        tnzw x
        jreq vm_true_j
        jp vm_false
vm_true_j:
        jp vm_true

op_neg:
        call vpop
        negw x
        jp vm_push_next

op_shl:
        call vpop2
        ld a,tmp2+1
        and a,#$0F
shl_loop:
        jreq shl_done
        sllw x
        dec a
        jra shl_loop
shl_done:
        jp vm_push_next

op_shr:
        call vpop2
        ld a,tmp2+1
        and a,#$0F
shr_loop:
        jreq shr_done
        srlw x
        dec a
        jra shr_loop
shr_done:
        jp vm_push_next

op_eq:
        call vpop2
        cpw x,tmp2
        jreq vm_true_j
        jp vm_false
op_ne:
        call vpop2
        cpw x,tmp2
        jrne vm_true_j
        jp vm_false
op_lt:
        call vpop2
        cpw x,tmp2
        jrslt vm_true_j
        jp vm_false
op_le:
        call vpop2
        cpw x,tmp2
        jrsle vm_true_j
        jp vm_false
op_gt:
        call vpop2
        cpw x,tmp2
        jrsgt vm_true_j
        jp vm_false
op_ge:
        call vpop2
        cpw x,tmp2
        jrsge vm_true_j
        jp vm_false

op_drop:
        call vpop
        jp vm_next

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
        ld a,xl
        ld buf,a
        ld a,yh
        add a,buf
        ld yh,a
        clrw x
        ld a,tmp+1
        ld xl,a
        ld a,tmp2
        mul x,a                     ; low * high
        ld a,xl
        ld buf,a
        ld a,yh
        add a,buf
        ld yh,a
        ldw x,y
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

op_jmp:
        call vm_fetch
        call vm_relative
        ldw vpc,x
        jp vm_next

op_jz:
        call vm_fetch
        push a
        call vpop
        pop a
        tnzw x
        jrne jz_skip
        call vm_relative
        ldw vpc,x
jz_skip:
        jp vm_next

op_jnz:
        call vm_fetch
        push a
        call vpop
        pop a
        tnzw x
        jreq jnz_skip
        call vm_relative
        ldw vpc,x
jnz_skip:
        jp vm_next

op_jmpw:
        call vm_fetch16
        ldw vpc,x
        jp vm_next

op_jzw:
        call vm_fetch16
        pushw x
        call vpop
        popw y
        tnzw x
        jrne jzw_skip
        ldw vpc,y
jzw_skip:
        jp vm_next

op_jnzw:
        call vm_fetch16
        pushw x
        call vpop
        popw y
        tnzw x
        jreq jnzw_skip
        ldw vpc,y
jnzw_skip:
        jp vm_next

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
        jp vm_next
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
        jp vm_next

op_ret:
        ldw x,csp
        cpw x,#CSTACK_TOP
        jrne ret_frame
        ret                         ; end of the handler: back to the dispatcher
ret_frame:
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
        jp vm_next

op_retv:
        call vpop
        ldw buf+4,x
        ldw x,csp
        cpw x,#CSTACK_TOP
        jrne retv_frame
        ret
retv_frame:
        ldw x,fp
        addw x,#2
        ldw vsp,x
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
        ldw x,buf+4
        jp vm_push_next

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
        jp vm_next

sv_show:
        call oled_show
        jp vm_next

sv_pixel:
        ld a,#3
        call pop_args
        ld a,args+5
        ld gcol,a
        ldw x,args
        ldw y,args+2
        call plot16
        jp vm_next

sv_line:
        ld a,#5
        call pop_args
        ld a,args+9
        ld gcol,a
        call line16
        jp vm_next

sv_box:
        ld a,#5
        call pop_args
        ld a,args+9
        ld gcol,a
        call args_to_rect
        call box16
        jp vm_next

sv_fill:
        ld a,#5
        call pop_args
        ld a,args+9
        ld gcol,a
        call args_to_rect
        call fill16
        jp vm_next

sv_invert:
        ld a,#4
        call pop_args
        mov gcol,#2
        call args_to_rect
        call fill16
        jp vm_next

sv_contrast:
        call vpop
        ld a,xl
        push a
        ld a,#$81
        call oled_cmd_start
        pop a
        call i2c_send
        call i2c_stop
        jp vm_next

sv_inverse:
        call vpop
        ld a,#$A6
        tnzw x
        jreq inverse_send
        ld a,#$A7
inverse_send:
        call oled_cmd
        jp vm_next

sv_flip:
        call vpop
        tnzw x
        jreq flip_off
        ld a,#$A1                   ; columns mirrored
        call oled_cmd
        ld a,#$C8                   ; rows scanned the other way
        call oled_cmd
        jp vm_next
flip_off:
        ld a,#$A0
        call oled_cmd
        ld a,#$C0
        call oled_cmd
        jp vm_next

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
        jp vm_next

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
        jp vm_next

sv_dec:
        ld a,#4
        call pop_args
        ld a,#6
        call text_start
        call text_dec
        jp vm_next

sv_bin:
        ld a,#5
        call pop_args
        ld a,#8
        call text_start
        ld a,args+7                 ; bits
        call text_bin
        jp vm_next

sv_bits:
        ld a,#5
        call pop_args
        call bits_view
        jp vm_next

sv_bar:
        ld a,#6
        call pop_args
        call bar_view
        jp vm_next

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
        wfi
        jra wait_loop
wait_done:
        jp vm_next

sv_eewrite:
        ld a,#2
        call pop_args
        ldw x,args
        cpw x,#SETTINGS_SIZE
        jruge eewrite_done          ; outside the settings: ignored
        ld a,args+1
        clrw x
        ld xl,a
        ld a,args+3                 ; value
        cp a,(SETTINGS,x)
        jreq eewrite_done           ; unchanged: no write
        mov FLASH_DUKR,#$AE
        mov FLASH_DUKR,#$56
        ld (SETTINGS,x),a
eewrite_wait:
        btjf FLASH_IAPSR,#2,eewrite_wait
        bres FLASH_IAPSR,#3         ; lock again
eewrite_done:
        jp vm_next

sv_inputs:
        call read_inputs
        clrw x
        ld xl,a
        jp vm_push_next

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
        jp vm_push_next
pin_zero:
        clrw x
        jp vm_push_next

; ANALOG is not done yet: the emulator has no ADC. It returns 0.
sv_analog:
        call vpop
        clrw x
        jp vm_push_next

sv_millis:
        ldw x,millis
        jp vm_push_next

sv_eeread:
        call vpop
        cpw x,#SETTINGS_SIZE
        jruge eeread_zero           ; outside the settings: 0
        ld a,(SETTINGS,x)
        clrw x
        ld xl,a
        jp vm_push_next
eeread_zero:
        clrw x
        jp vm_push_next

; RND n: 0 to n-1 from a 16-bit xorshift, stirred with the clock.
sv_rnd:
        call vpop
        ldw tmp2,x                  ; n
        ldw x,rnd
        ld a,xl
        xor a,millis+1
        ld xl,a
        ; x ^= x << 7
        ldw y,x
        ld a,#7
rnd_shl7:
        sllw y
        dec a
        jrne rnd_shl7
        ldw buf,y
        ld a,xh
        xor a,buf
        ld xh,a
        ld a,xl
        xor a,buf+1
        ld xl,a
        ; x ^= x >> 9
        ldw y,x
        ld a,#9
rnd_shr9:
        srlw y
        dec a
        jrne rnd_shr9
        ldw buf,y
        ld a,xh
        xor a,buf
        ld xh,a
        ld a,xl
        xor a,buf+1
        ld xl,a
        ; x ^= x << 8
        ld a,xl
        push a
        ld a,xh
        xor a,(1,sp)
        ld xh,a
        pop a
        ldw rnd,x
        ; result: x mod n (n <= 0 gives 0)
        ldw y,tmp2
        tnzw y
        jrsle rnd_zero
        divw x,y
        ldw x,y
        jp vm_push_next
rnd_zero:
        clrw x
        jp vm_push_next

sv_abs:
        call vpop
        tnzw x
        jrpl abs_done
        negw x
abs_done:
        jp vm_push_next

sv_min:
        call vpop2
        cpw x,tmp2
        jrsle min_done
        ldw x,tmp2
min_done:
        jp vm_push_next

sv_max:
        call vpop2
        cpw x,tmp2
        jrsge max_done
        ldw x,tmp2
max_done:
        jp vm_push_next

sv_lost:
        ldw x,lost
        jp vm_push_next
