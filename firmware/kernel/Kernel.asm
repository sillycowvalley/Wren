; Wren kernel for the OLED Thing (STM8S003F3) and the STM8S103F3 development board.
;
; Start-up, the program loader (FAT in EEPROM), the event queue and its interrupts,
; and the dispatcher that runs event handlers on the VM. See docs/Design.md and
; docs/Bytecode.md.
;
; Wren's debugger reads these symbols: vm_next, vpc, vsp, fp, csp, evt_id, evt_value,
; vm_fault, VARS.

        include "stm8s003.inc"

        relax                       ; JP and CALL become JRA and CALLR where the target is near

KERNEL_VERSION  equ 1

; ---- Memory ----

STORE       equ $9440       ; program store: 64-byte blocks from here to the end of flash
STORE_BLOCKS equ ($A000-STORE)/64   ; 47

FAT         equ $4000
FAT_VERSION equ FAT+0
FAT_FIRST   equ FAT+1
FAT_LENGTH  equ FAT+2
FAT_CRC     equ FAT+4
FAT_CHAIN   equ FAT+8                       ; next block, one byte per block
FAT_COUNTS  equ FAT_CHAIN+STORE_BLOCKS      ; write counts, one byte per block
SETTINGS    equ FAT_COUNTS+STORE_BLOCKS     ; program settings to the end of the EEPROM
SETTINGS_SIZE equ FAT+128-SETTINGS

VSTACK_LOW  equ $0080       ; VM value stack, grows down from $0100
VSTACK_TOP  equ $0100
FB          equ $0100       ; frame buffer: 4 pages of 64 columns
VARS        equ $0200       ; program variables; the block map sits at the top
VARS_END    equ $0300
MAP_TOP     equ VARS_END-1  ; the block map: entry i at MAP_TOP - i
CSTACK_LOW  equ $0300       ; VM call stack, 4 bytes a frame, grows down from $0340
CSTACK_TOP  equ $0340
CPU_STACK   equ RAM_END

; ---- Kernel variables (short addresses) ----

vpc         equ $00         ; 2  VM program counter (program address)
vsp         equ $02         ; 2  VM value stack pointer: the top value
fp          equ $04         ; 2  VM frame pointer: parameter 0
csp         equ $06         ; 2  VM call stack pointer
prog_len    equ $08         ; 2  program length
map_base    equ $0A         ; 2  address of the block map in RAM
evt_id      equ $0C         ; 1  event being handled
evt_value   equ $0D         ; 1  its value
millis      equ $0E         ; 2  milliseconds since start
lost        equ $10         ; 2  events lost to a full queue
qhead       equ $12         ; 1  queue: next to take
qtail       equ $13         ; 1  queue: next free
inputs_last equ $17         ; 1  b0-b7 at the last check
prev_pins   equ $18         ; 1  PA1,PA2,PA3,PD2,PD3 (bits 0-4) at the last edge
latch_now   equ $19         ; 1  the same, now (in the edge interrupt)
latch_in    equ $1A         ; 1  b0-b7 read at the edge
pins_stable equ $1C         ; 1  debounced PA1,PA2,PA3,PD2,PD3 as bits 0-4
pins_count  equ $1D         ; 5  debounce counters
every_int   equ $22         ; 8  EVERY intervals (4 words; 0 = unused)
every_cnt   equ $2A         ; 8  EVERY countdowns
rnd         equ $32         ; 2  random number state
vm_fault    equ $34         ; 1  0, or the fault number once the program has stopped
blocks      equ $35         ; 1  blocks the program uses
tmp         equ $36         ; 2  scratch
tmp2        equ $38         ; 2  scratch
crc         equ $3A         ; 2  CRC while checking the program
args        equ $3C         ; 12 service arguments a0-a5 (words)
gx          equ $48         ; 1  graphics: pixel x
gy          equ $49         ; 1  graphics: pixel y
gcol        equ $4A         ; 1  graphics: 0 clear, 1 set, 2 invert
gmask       equ $4B         ; 1  graphics: bit mask
cx0         equ $4C         ; 1  clipped rectangle
cy0         equ $4D         ; 1
cx1         equ $4E         ; 1
cy1         equ $4F         ; 1
rx          equ $50         ; 2  rectangle (signed)
ry          equ $52         ; 2
rw          equ $54         ; 2
rh          equ $56         ; 2
tx          equ $58         ; 2  text cursor
ty          equ $5A         ; 2
tfont       equ $5C         ; 1  font
tcols       equ $5D         ; 1  glyph columns in the font table
trows       equ $5E         ; 1  rows drawn per character
tpitch      equ $5F         ; 1  columns drawn per character
tsx         equ $60         ; 1  x scale
tsy         equ $61         ; 1  y scale
ttable      equ $62         ; 2  font table
tglyph      equ $64         ; 2  current glyph
tcol        equ $66         ; 1  loop counters
trow        equ $67         ; 1
tbits       equ $68         ; 1
lx0         equ $69         ; 2  line drawing
ly0         equ $6B         ; 2
ldx         equ $6D         ; 2
ldy         equ $6F         ; 2
lsx         equ $71         ; 1
lsy         equ $72         ; 1
lerr        equ $73         ; 2
le2         equ $75         ; 2
buf         equ $77         ; 7  number text
QUEUE       equ $0340       ; 16 event queue: 8 entries of event, value (below the CPU stack)
QUEUE_SIZE  equ 8
latch_cfg   equ $0350       ; 5  LATCH settings per pin (0 = no handler), see docs/Bytecode.md
box_save    equ $0355       ; 8  rectangle saved while drawing a box
HANDLED     equ $035D       ; 22 one byte per event: non-zero when the program handles it
                            ;    the CPU stack grows down from $03FF to $0373

; ---- Events ----

EV_START    equ 0
EV_CHANGE   equ 1
EV_PINS     equ 2
EV_LATCH    equ 3           ; 3-7
EV_PRESS    equ 8           ; 8-12
EV_RELEASE  equ 13          ; 13-17
EV_EVERY    equ 18          ; 18-21

DEBOUNCE_MS equ 20

; ---- Vectors ----

        org $8000
        int start                   ; reset
        int unused_irq              ; TRAP
        int unused_irq              ; 0 TLI
        int unused_irq              ; 1 AWU
        int unused_irq              ; 2 CLK
        int exti_a                  ; 3 port A
        int unused_irq              ; 4 port B
        int unused_irq              ; 5 port C
        int exti_d                  ; 6 port D

; IRQs 7 to 22 are never enabled, and the CPU only reads the vector of an interrupt it
; takes, so their 64 bytes of vector space hold constant tables instead.
        org $8024
bit_masks:  dc.b $01,$02,$04,$08,$10,$20,$40,$80

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

; Messages: x, y, font, then the counted text.
msg_err:    dc.b 0,0,1, 4,"ERR "
msg_at:     dc.b 0,10,1, 3,"AT "
msg_wren:   dc.b 20,4,2, 4,"Wren"

        org $8064
        int tim4_irq                ; 23 TIM4

; IRQs 24 to 29 are never enabled either: 24 more bytes.
msg_noprog: dc.b 2,22,1, 10,"No program"

; ---- Option bytes ----
; UBC (OPT1) write-protects the kernel's 81 blocks, $8000-$943F, against firmware
; writes, so a program upload can never damage it. NOPT1 is its complement.
        org $4801
        dc.b (STORE-$8000)/64, ~((STORE-$8000)/64) & $FF

        org $8080
unused_irq:
        iret

; ---- Start-up ----

start:
        ldw x,#CPU_STACK
        ldw sp,x
        clr CLK_CKDIVR              ; 16 MHz

        ; clear RAM below the CPU stack
        clrw x
clear_ram:
        clr (x)
        incw x
        cpw x,#CSTACK_TOP+QUEUE_SIZE*2
        jrne clear_ram

        ; pull-ups on the mode and spare pins: PA1-PA3, PD2, PD3
        ld a,PA_CR1
        or a,#$0E
        ld PA_CR1,a
        ld a,PD_CR1
        or a,#$0C
        ld PD_CR1,a

        ldw x,#$ACE1
        ldw rnd,x
        ldw x,#VSTACK_TOP
        ldw vsp,x
        ldw x,#CSTACK_TOP
        ldw csp,x

        call i2c_init
        call oled_init
        call gfx_cls

        btjt PD_IDR,#2,start_program
        jp loader                   ; PD2 held low at start-up: upload a program
start_program:
        call load_program
        jrc no_program

        call setup_events

        ; START, then PINS and CHANGE once so the program can draw its first screen
        ld a,#EV_START
        clrw x
        call queue_event
        call read_inputs
        ld inputs_last,a
        ld xl,a
        ld a,#EV_PINS
        call queue_event
        ld a,inputs_last
        ld xl,a
        ld a,#EV_CHANGE
        call queue_event
        call tim4_init
        rim

; ---- The dispatcher ----

; Takes events from the queue and runs their handlers, one at a time.
main_loop:
        tnz vm_fault
        jrne idle
        sim
        ld a,qhead
        cp a,qtail
        jrne main_take
        rim
        wfi
        jra main_loop
main_take:
        clrw x
        ld xl,a
        sllw x
        ld a,(QUEUE,x)
        ld evt_id,a
        ld a,(QUEUE+1,x)
        ld evt_value,a
        ld a,qhead
        inc a
        and a,#QUEUE_SIZE-1
        ld qhead,a
        rim
        call find_handler           ; X = code address, carry clear when found
        jrc main_loop
        call vm_run
        jra main_loop

idle:
        rim
        wfi
        jra idle

no_program:
        call gfx_cls
        ldw x,#msg_wren
        call draw_message
        ldw x,#msg_noprog
        call draw_message
        call oled_show
        jra idle

; Draws the message record at X: x, y, font, then the counted text.
draw_message:
        clr tx
        ld a,(x)
        ld tx+1,a
        clr ty
        ld a,(1,x)
        ld ty+1,a
        ld a,(2,x)
        addw x,#3
        ldw tmp2,x
        jp kernel_text

; ---- Loading the program ----

; Follows the FAT chain into the block map, then checks the length and CRC.
; Carry set when there is no valid program.
load_program:
        ld a,FAT_VERSION
        cp a,#1
        jrne load_bad_near
        clr blocks
        ldw y,#MAP_TOP
        ld a,FAT_FIRST
map_loop:
        cp a,#STORE_BLOCKS
        jruge load_bad_near
        ld (y),a
        decw y
        inc blocks
        clrw x
        ld xl,a
        ld a,(FAT_CHAIN,x)          ; next block
        cp a,#$FE
        jreq map_done
        cpw y,#MAP_TOP-STORE_BLOCKS
        jrne map_loop               ; longer than the store: a loop in the chain
load_bad_near:
        jp load_bad
map_done:
        incw y
        ldw map_base,y              ; the lowest byte of the map
        ; length: 1 to blocks*64
        ldw x,FAT_LENGTH
        ldw prog_len,x
        tnzw x
        jreq load_bad
        ; tmp2 = blocks * 64
        ld a,blocks
        clrw x
        ld xl,a
        ld a,#64
        mul x,a
        ldw tmp2,x
        ldw x,prog_len
        cpw x,tmp2
        jrugt load_bad
        ; header: version 1, variables fit beside the map
        clrw x
        call read_program
        cp a,#1
        jrne load_bad
        ldw x,#1
        call read_program           ; variable bytes
        clrw x
        ld xl,a
        addw x,#VARS
        cpw x,map_base
        jrugt load_bad
        call check_crc
        ret                         ; carry from check_crc
load_bad:
        scf
        ret

; CRC-16/CCITT of the program against the FAT. Carry set when it differs.
check_crc:
        ldw x,#$FFFF
        ldw crc,x
        clrw x
crc_byte:
        call read_program
        call crc_update
        incw x
        cpw x,prog_len
        jrult crc_byte
        ldw x,crc
        cpw x,FAT_CRC
        jrne crc_bad
        rcf
        ret
crc_bad:
        scf
        ret

; Adds byte A to crc (CRC-16/CCITT, polynomial $1021). Keeps A and X.
crc_update:
        pushw x
        push a
        xor a,crc
        ld crc,a
        ld a,#8
crc_bit:
        ldw x,crc
        sllw x
        jrnc crc_nox
        push a
        ld a,xh
        xor a,#$10
        ld xh,a
        ld a,xl
        xor a,#$21
        ld xl,a
        pop a
crc_nox:
        ldw crc,x
        dec a
        jrne crc_bit
        pop a
        popw x
        ret

; A = program byte at program address X. Keeps X and Y.
read_program:
        pushw x
        pushw y
        ld a,xl
        and a,#$3F
        ld tmp2+1,a
        srlw x
        srlw x
        srlw x
        srlw x
        srlw x
        srlw x
        negw x
        addw x,#MAP_TOP
        ld a,(x)                    ; physical block
        clrw x
        ld xl,a
        ld a,#64
        mul x,a
        addw x,#STORE
        ld a,xl
        or a,tmp2+1
        ld xl,a
        ld a,(x)
        popw y
        popw x
        ret

; ---- Handlers ----

; Reads the handler table: which events have handlers, EVERY intervals, LATCH pins.
setup_events:
        ldw x,#2
        call read_program           ; number of handlers
        ld tmp,a
        ldw x,#3
setup_loop:
        tnz tmp
        jreq setup_done
        call read_program           ; event number
        incw x
        incw x
        incw x                      ; past event and address
        clrw y
        ld yl,a
        inc a
        ld (HANDLED,y),a            ; event + 1: never 0
        dec a
        cp a,#EV_EVERY
        jrult setup_not_every
        cp a,#EV_EVERY+4
        jruge setup_not_every
        ; interval
        sub a,#EV_EVERY
        sll a
        clrw y
        ld yl,a
        call read_program
        ld (every_int,y),a
        ld (every_cnt,y),a
        incw x
        call read_program
        ld (every_int+1,y),a
        ld (every_cnt+1,y),a
        incw x
        jra setup_next
setup_not_every:
        cp a,#EV_LATCH
        jrult setup_next
        cp a,#EV_LATCH+5
        jruge setup_next
        sub a,#EV_LATCH
        clrw y
        ld yl,a
        call read_program           ; settings byte
        incw x
        ld (latch_cfg,y),a
        ld a,yl
        call latch_enable
setup_next:
        dec tmp
        jra setup_loop
setup_done:
        ; both edges on ports A and D; the interrupt picks the ones each LATCH wants
        mov EXTI_CR1,#$C3
        call read_pins
        ld prev_pins,a
        ld pins_stable,a
        ret

; A = LATCH pin (0-4: PA1, PA2, PA3, PD2, PD3): interrupts on that pin.
latch_enable:
        cp a,#3
        jruge latch_pd_pin
        inc a                       ; PA1-PA3: bits 1-3
        clrw y
        ld yl,a
        ld a,(bit_masks,y)
        or a,PA_CR2
        ld PA_CR2,a
        ret
latch_pd_pin:
        dec a                       ; PD2, PD3: bits 2-3
        clrw y
        ld yl,a
        ld a,(bit_masks,y)
        or a,PD_CR2
        ld PD_CR2,a
        ret

; Finds the handler for evt_id. X = its address, carry clear; carry set when none.
find_handler:
        ldw x,#2
        call read_program
        ld tmp,a
        ldw x,#3
find_loop:
        tnz tmp
        jreq find_none
        call read_program
        cp a,evt_id
        jreq find_found
        ; next entry: 3 bytes, 4 for LATCH, 5 for EVERY
        cp a,#EV_EVERY
        jrult find_not_every
        incw x
        incw x
        jra find_skip3
find_not_every:
        cp a,#EV_LATCH
        jrult find_skip3
        cp a,#EV_LATCH+5
        jruge find_skip3
        incw x
find_skip3:
        incw x
        incw x
        incw x
        dec tmp
        jra find_loop
find_found:
        incw x
        call read_program
        ld tmp2,a
        incw x
        call read_program
        ld tmp2+1,a
        ldw x,tmp2
        rcf
        ret
find_none:
        scf
        ret

; ---- The event queue ----

; Adds event A with value XL. Called with interrupts disabled or from an interrupt.
; Events without a handler are not queued. CHANGE and PINS replace a waiting one of
; the same kind instead of adding another.
queue_event:
        clrw y
        ld yl,a
        tnz (HANDLED,y)
        jreq queue_done
        ld tmp2,a
        cp a,#EV_CHANGE
        jreq queue_merge
        cp a,#EV_PINS
        jrne queue_add
queue_merge:
        ; look for the same event still waiting
        ld a,qhead
queue_scan:
        cp a,qtail
        jreq queue_add
        clrw y
        ld yl,a
        sllw y
        ld a,(QUEUE,y)
        cp a,tmp2
        jrne queue_scan_next
        ld a,xl
        ld (QUEUE+1,y),a
        ret
queue_scan_next:
        ld a,yl
        srl a
        inc a
        and a,#QUEUE_SIZE-1
        jra queue_scan
queue_add:
        ld a,qtail
        inc a
        and a,#QUEUE_SIZE-1
        cp a,qhead
        jreq queue_full
        ld a,qtail
        clrw y
        ld yl,a
        sllw y
        ld a,tmp2
        ld (QUEUE,y),a
        ld a,xl
        ld (QUEUE+1,y),a
        ld a,qtail
        inc a
        and a,#QUEUE_SIZE-1
        ld qtail,a
queue_done:
        ret
queue_full:
        ldw y,lost
        incw y
        ldw lost,y
        ret

; ---- Inputs ----

; A = b7..b0. b0-b4 are PC7-PC3, b5-b7 are PD6-PD4. Bits are rotated into a byte on
; the CPU stack, so interrupts can use this too.
read_inputs:
        push a
        ld a,PC_IDR
        sll a                       ; PC7 = b0
        rrc (1,sp)
        sll a                       ; PC6 = b1
        rrc (1,sp)
        sll a                       ; PC5 = b2
        rrc (1,sp)
        sll a                       ; PC4 = b3
        rrc (1,sp)
        sll a                       ; PC3 = b4
        rrc (1,sp)
        ld a,PD_IDR
        sll a                       ; PD7 (not an input)
        sll a                       ; PD6 = b5
        rrc (1,sp)
        sll a                       ; PD5 = b6
        rrc (1,sp)
        sll a                       ; PD4 = b7
        rrc (1,sp)
        pop a
        ret

; A = PA1,PA2,PA3,PD2,PD3 as bits 0-4.
read_pins:
        ld a,PD_IDR
        and a,#$0C
        sll a                       ; PD2, PD3 to bits 3, 4
        push a
        ld a,PA_IDR
        srl a
        and a,#$07                  ; PA1-PA3 to bits 0-2
        or a,(1,sp)
        addw sp,#1
        ret

; ---- Interrupts ----

tim4_init:
        mov TIM4_PSCR,#7            ; 16 MHz / 128
        mov TIM4_ARR,#124           ; / 125 = 1 kHz
        bset TIM4_IER,#0
        bset TIM4_CR1,#0
        ret

; Every millisecond: the clock, EVERY timers, input changes and button debouncing.
tim4_irq:
        bres TIM4_SR,#0
        call isr_save
        ldw x,millis
        incw x
        ldw millis,x

        ; EVERY timers
        clr tmp+1
every_loop:
        ld a,tmp+1
        sll a
        clrw x
        ld xl,a
        ldw tmp2,x                  ; offset of this timer's words
        ldw x,(every_int,x)
        tnzw x
        jreq every_next
        ldw x,tmp2
        ldw x,(every_cnt,x)
        decw x
        jrne every_store
        ldw x,tmp2
        ldw x,(every_int,x)         ; reload
        ldw y,tmp2
        ldw (every_cnt,y),x
        ld a,tmp+1
        add a,#EV_EVERY
        clrw x
        call queue_event
        jra every_next
every_store:
        ldw y,tmp2
        ldw (every_cnt,y),x
every_next:
        inc tmp+1
        ld a,tmp+1
        cp a,#4
        jrne every_loop

        ; b0-b7 changed?
        call read_inputs
        cp a,inputs_last
        jreq tim4_pins
        ld inputs_last,a
        ld xl,a
        ld a,#EV_CHANGE
        call queue_event

tim4_pins:
        ; debounce PA1-PA3, PD2, PD3
        call read_pins
        ld tmp2+1,a                 ; raw
        clr tmp+1                   ; pin
pins_loop:
        ld a,tmp+1
        clrw y
        ld yl,a
        ld a,(bit_masks,y)
        ld tmp,a
        ld a,tmp2+1
        xor a,pins_stable
        and a,tmp
        jreq pins_same
        ld a,(pins_count,y)
        inc a
        ld (pins_count,y),a
        cp a,#DEBOUNCE_MS
        jrult pins_next
        ; changed for long enough
        clr a
        ld (pins_count,y),a
        ld a,pins_stable
        xor a,tmp
        ld pins_stable,a
        and a,tmp
        jreq pins_pressed
        ld a,tmp+1
        add a,#EV_RELEASE
        jra pins_queue
pins_pressed:
        ld a,tmp+1
        add a,#EV_PRESS
pins_queue:
        push a
        ld a,inputs_last
        ld xl,a
        pop a
        call queue_event
        ld a,inputs_last
        ld xl,a
        ld a,#EV_PINS
        call queue_event
        jra pins_next
pins_same:
        clr a
        ld (pins_count,y),a
pins_next:
        inc tmp+1
        ld a,tmp+1
        cp a,#5
        jrne pins_loop
        jp isr_restore

; Port A and port D edges: LATCH on PA1-PA3, PD2 and PD3. b0-b7 and the pins are
; read first, as close to the edge as possible. Each LATCH pin's settings choose the
; edge and an optional qualifier pin that must be at a given level.
exti_a:
exti_d:
        call isr_save
        call read_inputs
        ld latch_in,a
        call read_pins
        ld latch_now,a
        clr tmp+1                   ; pin
latch_loop:
        clrw y
        ld a,tmp+1
        ld yl,a
        ld a,(latch_cfg,y)
        jreq latch_next             ; no handler
        ld tmp,a                    ; settings
        ld a,(bit_masks,y)
        ld tmp2,a                   ; this pin's bit
        and a,prev_pins
        ld tmp2+1,a                 ; was high
        ld a,latch_now
        and a,tmp2                  ; is high
        cp a,tmp2+1
        jreq latch_next             ; no edge on this pin
        tnz a
        jrne latch_rose
        btjf tmp,#0,latch_next      ; fell: wanted?
        jra latch_qualify
latch_rose:
        btjf tmp,#1,latch_next      ; rose: wanted?
latch_qualify:
        btjf tmp,#2,latch_fire      ; no qualifier
        ld a,tmp
        swap a
        and a,#$07                  ; qualifier pin
        clrw x
        ld xl,a
        ld a,(bit_masks,x)
        and a,latch_now
        jreq latch_q_low
        btjf tmp,#3,latch_next      ; high, but low wanted
        jra latch_fire
latch_q_low:
        btjt tmp,#3,latch_next      ; low, but high wanted
latch_fire:
        clrw x
        ld a,latch_in
        ld xl,a
        ld a,tmp+1
        add a,#EV_LATCH
        call queue_event
latch_next:
        inc tmp+1
        ld a,tmp+1
        cp a,#5
        jrne latch_loop
        ld a,latch_now
        ld prev_pins,a
        jp isr_restore

; Interrupts use tmp and tmp2 too. isr_save puts the main code's values on the stack
; under the return address; isr_restore takes them back and returns from the interrupt.
isr_save:
        popw y                      ; return address
        push tmp
        push tmp+1
        push tmp2
        push tmp2+1
        pushw y
        ret

isr_restore:
        pop tmp2+1
        pop tmp2
        pop tmp+1
        pop tmp
        iret

; ---- Faults ----

; Stops the program with fault A and shows it.
vm_error:
        ld vm_fault,a
        ldw x,#CPU_STACK
        ldw sp,x
        rim
        call gfx_cls
        ldw x,#msg_err
        call draw_message
        ; fault number
        ld a,vm_fault
        add a,#'0'
        call text_char
        ; program address
        ldw x,#msg_at
        call draw_message
        ldw x,vpc
        ldw args+4,x
        ld a,#4
        call text_hex
        call oled_show
        jp idle


        include "Vm.inc"
        include "Graphics.inc"
        include "Oled.inc"
        include "Loader.inc"
        include "Fonts.inc"

kernel_end:
