; The LED Thing's eight LEDs, charlieplexed on PD3-PD6 (docs/LedThing.md): each lit when
; its anode pin drives high and its cathode pin drives low, the other two pins floating.
; The tick lights one LED a millisecond, b0 to b7 in turn, so a frame is 8 ms and a lit
; LED is on one eighth of the time, as on Digicool's firmware.

; Anode and cathode bits (PD3 = $08 ... PD6 = $40) for b0 to b7.
led_pins:
        dc.b $08,$10, $08,$20, $10,$08, $10,$20, $20,$08, $20,$10, $08,$40, $40,$08

; From the tick: the LED for this millisecond's slot, if its bit in leds is set.
led_scan:
        ld a,PD_DDR
        and a,#$87                  ; PD3-PD6 inputs ...
        ld PD_DDR,a
        ld a,PD_CR1
        and a,#$87                  ; ... floating
        ld PD_CR1,a
        ld a,millis+1
        and a,#7                    ; this slot's LED
        clrw x
        ld xl,a
        ld a,(bit_masks,x)
        and a,leds
        jreq scan_done
        sllw x
        ld a,PD_ODR
        and a,#$87
        or a,(led_pins,x)           ; the anode high, the cathode low
        ld PD_ODR,a
        ld a,(led_pins,x)
        or a,(led_pins+1,x)
        push a
        or a,PD_CR1                 ; push-pull ...
        ld PD_CR1,a
        pop a
        or a,PD_DDR                 ; ... outputs
        ld PD_DDR,a
scan_done:
        ret

; LEDS value: lights the LEDs whose bits are set (b0 = bit 0).
sv_leds:
        call vpop
        ld a,xl
        ld leds,a
        ret

; The OLED Thing's services have no LED Thing meaning: the compiler refuses them for this
; board, and the kernel treats them as bad opcodes.
sv_cls      equ vm_bad_opcode
sv_show     equ vm_bad_opcode
sv_pixel    equ vm_bad_opcode
sv_line     equ vm_bad_opcode
sv_box      equ vm_bad_opcode
sv_fill     equ vm_bad_opcode
sv_invert   equ vm_bad_opcode
sv_contrast equ vm_bad_opcode
sv_inverse  equ vm_bad_opcode
sv_flip     equ vm_bad_opcode
sv_text     equ vm_bad_opcode
sv_hex      equ vm_bad_opcode
sv_dec      equ vm_bad_opcode
sv_bin      equ vm_bad_opcode
sv_bits     equ vm_bad_opcode
sv_bar      equ vm_bad_opcode
sv_analog   equ vm_bad_opcode
