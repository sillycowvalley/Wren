; Wren kernel: program upload from a host over b0-b7, strobed by PD3.
; See "Uploading from a host" in docs/Bytecode.md.
;
; Entered when PD2 is held low at start-up. The host sends "WREN", the length (high byte
; first), the program and its CRC-16, one byte per falling edge on PD3. Blocks go to the
; least-written blocks the current program does not use, so a failed upload leaves the
; old program in place. The FAT is written only once the CRC checks out; then the new
; program starts. A failure shows "Failed" until the Thing is reset.

UP_RAMCODE  equ $0080       ; the block writer, copied here: block writes must run from RAM
UP_USED     equ $00C0       ; 48: 0 free, 1 the current program's, 2 the new program's
UP_BUF      equ VARS        ; 64: the block being received
UP_LIST     equ VARS+64     ; 48: the new program's blocks, in order
UP_HEADER   equ VARS+112    ; 8: the new FAT header, written last

up_count    equ vpc         ; 2  bytes received (the VM is not running)
up_level    equ args        ; 1  allocation pass: 1 free blocks only, 2 also the current program's
up_best     equ args+1      ; 1  best block so far ($FF none)
up_bestc    equ args+2      ; 1  its write count

FAT_UPLOADS equ FAT+6

loader:
        clrw x
        ldw up_count,x
        call up_show                ; "Upload" and 0

        ; the block writer must run from RAM
        clrw x
up_copy:
        ld a,(up_writer,x)
        ld (UP_RAMCODE,x),a
        incw x
        cpw x,#up_writer_end-up_writer
        jrne up_copy

        ; mark the current program's blocks (RAM was cleared at start-up)
        ld a,FAT_VERSION
        cp a,#1
        jrne up_sync
        ld a,FAT_FIRST
up_mark:
        cp a,#STORE_BLOCKS
        jruge up_sync
        clrw x
        ld xl,a
        tnz (UP_USED,x)
        jrne up_sync                ; a loop in the chain
        inc (UP_USED,x)
        ld a,(FAT_CHAIN,x)
        jra up_mark

        ; wait for "WREN": the last four bytes are kept in tmp and tmp2
up_sync:
        call up_get
        mov tmp,tmp+1
        mov tmp+1,tmp2
        mov tmp2,tmp2+1
        ld tmp2+1,a
        ldw x,tmp
        cpw x,#$5752                ; "WR"
        jrne up_sync
        ldw x,tmp2
        cpw x,#$454E                ; "EN"
        jrne up_sync

        ; length: 1 to the size of the store
        call up_get
        ld prog_len,a
        call up_get
        ld prog_len+1,a
        ldw x,prog_len
        jreq up_failed
        cpw x,#STORE_BLOCKS*64+1
        jruge up_failed

        ldw x,#$FFFF
        ldw crc,x
        clrw x
        ldw up_count,x
        clr blocks

        ; the program, a block at a time
up_data:
        call up_get
        call crc_update
        push a
        ld a,up_count+1
        and a,#63
        clrw x
        ld xl,a
        pop a
        ld (UP_BUF,x),a
        ldw x,up_count
        incw x
        ldw up_count,x
        ld a,xl
        and a,#63
        jreq up_block               ; a full block
        cpw x,prog_len
        jrne up_data
        ; the last, partial block: the rest is zero
        clrw x
        ld xl,a
up_pad:
        clr (UP_BUF,x)
        incw x
        cpw x,#64
        jrne up_pad
up_block:
        call up_write_block
        ldw x,up_count
        cpw x,prog_len
        jrne up_data

        ; the CRC
        call up_get
        ld tmp,a
        call up_get
        ld tmp+1,a
        ldw x,tmp
        cpw x,crc
        jrne up_failed

        call up_write_fat
        jp start                    ; run the new program

up_failed:
        call gfx_cls
        ldw x,#msg_upload
        call draw_message
        ldw x,#msg_failed
        call draw_message
        call oled_show
        jp idle                     ; until the Thing is reset

; A = b0-b7 at the next falling edge of PD3.
up_get:
        btjf PD_IDR,#3,up_get       ; wait for PD3 high
up_get_low:
        btjt PD_IDR,#3,up_get_low   ; then for it to fall
        jp read_inputs

; Writes UP_BUF to a newly chosen block and shows the progress.
up_write_block:
        call up_alloc               ; X = the block
        jrc up_failed
        ld a,#2
        ld (UP_USED,x),a
        ld a,xl
        clrw y
        ld yl,a                     ; Y = block, for later
        ld a,blocks
        clrw x
        ld xl,a
        ld a,yl
        ld (UP_LIST,x),a
        inc blocks
        ldw x,y
        ld a,#64
        mul x,a
        addw x,#STORE
        ldw y,#UP_BUF
        mov FLASH_PUKR,#$56
        mov FLASH_PUKR,#$AE
        ld a,FLASH_IAPSR            ; clears a stale EOP
        call UP_RAMCODE
        bres FLASH_IAPSR,#1         ; lock the flash again
; Shows "Upload" and the bytes received so far.
up_show:
        call gfx_cls
        ldw x,#msg_upload
        call draw_message
        ldw x,#12
        ldw ty,x
        clrw x
        ldw tx,x
        ldw x,up_count
        ldw args+4,x
        ld a,#2
        call set_font
        call text_dec
        jp oled_show

; Copied to UP_RAMCODE and run from there: programs the 64 bytes at Y into the block
; at flash address X (standard block programming: the block is erased first).
up_writer:
        mov FLASH_CR2,#$01
        mov FLASH_NCR2,#$FE
        ld a,#64
up_writer_loop:
        push a
        ld a,(y)
        ld (x),a
        pop a
        incw x
        incw y
        dec a
        jrne up_writer_loop
up_writer_wait:
        btjf FLASH_IAPSR,#2,up_writer_wait  ; EOP
        ret
up_writer_end:

; X = the least-written block for the new program, from the blocks no program uses;
; failing that, also from the current program's. Carry set when none is left.
up_alloc:
        mov up_level,#1
up_alloc_pass:
        mov up_best,#$FF
        clrw x
up_alloc_scan:
        ld a,(UP_USED,x)
        cp a,up_level
        jruge up_alloc_next
        ld a,(FAT_COUNTS,x)
        tnz up_best
        jrmi up_alloc_take          ; $FF: the first candidate
        cp a,up_bestc
        jruge up_alloc_next
up_alloc_take:
        ld up_bestc,a
        ld a,xl
        ld up_best,a
up_alloc_next:
        incw x
        cpw x,#STORE_BLOCKS
        jrne up_alloc_scan
        clrw x
        ld a,up_best
        ld xl,a
        tnz a
        jrpl up_alloc_found
        inc up_level
        ld a,up_level
        cp a,#3
        jrne up_alloc_pass
        scf
        ret
up_alloc_found:
        rcf
        ret

; Writes the new program's chain and write counts, then the header; FAT_FIRST last,
; which makes the new program the current one.
up_write_fat:
        mov FLASH_DUKR,#$AE
        mov FLASH_DUKR,#$56
        clr tmp+1                   ; index in UP_LIST
up_fat_loop:
        clrw x
        ld a,tmp+1
        ld xl,a
        ld a,(UP_LIST,x)
        ld tmp2,a                   ; this block
        ld a,(UP_LIST+1,x)
        ld tmp2+1,a                 ; the next
        ld a,tmp+1
        inc a
        cp a,blocks
        jrne up_fat_chain
        mov tmp2+1,#$FE             ; the last block
up_fat_chain:
        clrw x
        ld a,tmp2
        ld xl,a
        ld a,tmp2+1
        addw x,#FAT_CHAIN
        call ee_write
        clrw x
        ld a,tmp2
        ld xl,a
        ld a,(FAT_COUNTS,x)
        inc a
        jrne up_fat_count
        dec a                       ; stays at 255
up_fat_count:
        addw x,#FAT_COUNTS
        call ee_write
        inc tmp+1
        ld a,tmp+1
        cp a,blocks
        jrne up_fat_loop
        ; header: built in RAM, written from the end, so FAT_FIRST goes in next to last
        ld a,#1
        ld UP_HEADER,a
        ld a,UP_LIST
        ld UP_HEADER+1,a
        ldw x,prog_len
        ldw UP_HEADER+2,x
        ldw x,crc
        ldw UP_HEADER+4,x
        ldw x,FAT_UPLOADS
        incw x
        ldw UP_HEADER+6,x
        ldw y,#8
up_fat_header:
        decw y
        ld a,(UP_HEADER,y)
        ldw x,y
        addw x,#FAT
        call ee_write
        tnzw y
        jrne up_fat_header
        bres FLASH_IAPSR,#3         ; lock the EEPROM again
        ret

; Writes A to the EEPROM byte at X, unless it already holds A. Keeps X.
; The EEPROM must be unlocked.
ee_write:
        cp a,(x)
        jreq ee_same
        ld (x),a
ee_wait:
        btjf FLASH_IAPSR,#2,ee_wait
ee_same:
        ret

msg_upload:     dc.b 0,0,1, 6,"Upload"
msg_failed:     dc.b 0,12,1, 6,"Failed"
