; Exercises calls, the stack, TRAP/IRET, pointers and far addressing.
; Results go to RAM $100-$1FF. Compared byte for byte with ucsim in CpuMixTests.

OUT     equ $100            ; results, 1 byte at a time via OUTP
OUTP    equ $80             ; pointer to the next free result byte
PTR     equ $82             ; a 16-bit pointer used by the pointer modes
FPTR    equ $84             ; a 24-bit pointer for LDF/CALLF
STACK   equ $17FF           ; ucsim stops if SP goes below $1500, so the stack is high

        org $8000
        int start           ; reset
        int trap_handler    ; TRAP

        org $8080
start:
        ldw x,#STACK
        ldw sp,x
        ; clear $80-$1FF
        ldw x,#$80
clear:  clr (x)
        incw x
        cpw x,#$200
        jrne clear
        ldw x,#OUT
        ldw OUTP,x

        ; --- calls and the stack ---
        ld a,#$11
        call save_a
        ldw x,sp
        call save_x         ; SP before
        call nested
        ldw x,sp
        call save_x         ; SP after (same as before)
        ld a,#$5A
        push a
        ldw x,#$1234
        pushw x
        ld a,(1,sp)         ; $12
        call save_a
        ld a,(2,sp)         ; $34
        call save_a
        ld a,(3,sp)         ; $5A
        call save_a
        popw y
        pop a
        call save_a
        ldw x,y
        call save_x
        push #$C3
        pop $90
        ld a,$90
        call save_a
        ldw x,#$ABCD
        ldw (1,sp),x        ; overwrite the return-address area below SP? no: (1,sp) is free stack
        ldw x,(1,sp)
        call save_x
        sub sp,#4
        ld a,#$66
        ld (4,sp),a
        addw sp,#4
        ldw x,sp
        call save_x

        ; --- TRAP and IRET ---
        ld a,#$A5
        ldw x,#$1111
        ldw y,#$2222
        rcf
        trap
        call save_a         ; IRET restored A
        call save_x
        ldw x,y
        call save_x
        push cc
        pop a
        call save_a         ; CC after IRET
        ld a,$91            ; values the handler saw
        call save_a
        ld a,$92
        call save_a
        ld a,$93
        call save_a

        ; --- pointer modes ---
        ldw x,#table
        ldw PTR,x
        ld a,[PTR.w]
        call save_a
        ldw x,#2
        ld a,([PTR.w],x)
        call save_a
        ldw y,#3
        ld a,([PTR.w],y)
        call save_a
        ldw x,[PTR.w]
        call save_x
        ldw x,#jumped
        ldw PTR,x
        jp [PTR.w]
        ld a,#$EE           ; skipped
        call save_a
jumped:
        ldw x,#sub_by_x
        call (x)
        ldw x,#$10
        ldw y,#vectors-$10
        ldw x,(table2-$10,x) ; X-indexed long offset
        call save_x

        ; --- far ---
        clr FPTR
        ldw x,#table
        ldw FPTR+1,x
        ldf a,[FPTR.e]
        call save_a
        ldw x,#1
        ldf a,(table,x)
        call save_a
        ldw y,#4
        ldf a,(table,y)
        call save_a
        callf far_sub
        call save_a

        ; --- exchanges, moves, bits ---
        mov $94,#$3C
        ld a,#$C3
        exg a,$94
        call save_a
        ld a,$94
        call save_a
        mov $95,$94
        mov $0196,$0094
        bset $95,#0
        bres $95,#7
        bcpl $95,#3
        ld a,$95
        call save_a
        ld a,$0196
        call save_a
        ld a,#$20
        btjt $95,#0,bit_set
        ld a,#$30
bit_set:
        call save_a

        ; --- counting loop with JRxx ---
        clr a
        ldw x,#10
count:  add a,#3
        decw x
        jrne count
        call save_a

        ; --- signed compares ---
        ld a,#$F0           ; -16
        cp a,#$10           ; 16
        jrslt is_less
        ld a,#0
        jra cmp_done
is_less:
        ld a,#1
cmp_done:
        call save_a

        ldw x,sp
        call save_x
        break
done:   jra done

; Store A at OUTP and move OUTP on. Keeps X and Y.
save_a:
        pushw x
        ldw x,OUTP
        ld (x),a
        incw x
        ldw OUTP,x
        popw x
        ret

; Store X (high byte first). Keeps A and Y.
save_x:
        push a
        pushw y
        ldw y,x
        ldw x,OUTP
        ldw (x),y
        incw x
        incw x
        ldw OUTP,x
        ldw x,y
        popw y
        pop a
        ret

nested:
        call nested2
        ret
nested2:
        ldw x,sp
        call save_x         ; two return addresses deep
        ld a,(1,sp)         ; high byte of nested's return address
        call save_a
        ret

trap_handler:
        ld a,(1,sp)         ; CC pushed by TRAP
        ld $91,a
        ld a,(2,sp)         ; A pushed by TRAP
        ld $92,a
        push cc
        pop a
        ld $93,a            ; CC inside the handler
        ld a,#$77
        ld (2,sp),a         ; IRET will restore A=$77
        iret

sub_by_x:
        ld a,#$42
        call save_a
        ret

far_sub:
        ld a,#$99
        retf

table:  dc.b $10,$20,$30,$40,$50
table2: dc.w $BEEF
vectors:
