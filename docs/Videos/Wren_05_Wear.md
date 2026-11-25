# Wren BASIC 5: A hundred writes

## YouTube

**Title**: A flash that takes 100 writes, made to last | Wren BASIC 5

**Description**:

```
The flash in Digicool Things' 8-bit boards is rated for about a hundred writes. Reflash the whole chip every time you change a line, and a board could be worn out in a weekend. Here's how Wren makes those writes last.

In this video:
- why a hundred writes is a real limit, and what a write is
- why an interpreter helps: the kernel goes on once, the program is just data
- an upload writes only the blocks that changed, and going back costs nothing
- the program table: 116 bytes of EEPROM that keep the whole store in order
- least-written first: 675 uploads before any block reaches its hundred
- the Wear report, read from a real board

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #Flash #WearLeveling #Microcontroller #Embedded #STM8
```

---

## 100 WRITES

> The flash on this chip is rated for about a hundred writes. Not a hundred thousand. A hundred.

## A hundred writes

> Flash is written in blocks of sixty-four bytes, and every time a block is rewritten, it wears a little.

> Firmware is usually built as one image. Change a single line, and the whole chip is written again. A hundred changes, and you're past what the chip is rated for.

## Why an interpreter helps

> Wren splits the job in two. The kernel goes on once, and is locked there. Your program is just data: bytecode that the kernel reads.

> And because the kernel looks up where every block of the program lives, the blocks can be anywhere in the store, in any order. That freedom is what makes the rest of this possible.

## Only what changed

> Before an upload, Wren reads what's already on the board. Any block of the new program that's already there, byte for byte, just stays where it is.

> Change something near the end, and one block is written. Upload the same program again, and nothing is written at all.

> An edit near the start moves everything after it, so that edit costs every block. But undo it, and the old blocks are still sitting there. Going back costs nothing.

## Uploading, block by block

*On the Thing: open OledThing.bas, Ctrl+F5. Ctrl+F5 again. In SUB Pip, change the 4 in the first FILL to 5, Ctrl+F5. Change it back, Ctrl+F5. Read each line in Messages.*

> Watch the messages. The first upload writes whatever the board doesn't already hold. Run it again, and nothing is uploaded at all.

> One small change: one block. And changing it back: none.

## The table in the EEPROM

> Wren keeps track of all this in the chip's other memory, the EEPROM, which takes about a hundred thousand writes instead of a hundred.

> One hundred and sixteen bytes: where the program starts, how long it is, a checksum, which block follows which, and how many times every block has been written.

## Least-written first

> When a block does have to be written, Wren picks the one that's been written least.

> So the wear spreads across all fifty-four blocks, never more than one write apart. Take a program the size of the board's own. That's six hundred and seventy-five completely different uploads before any block reaches a hundred writes.

## The Wear report

*On the Thing: in Wren, Tools → Firmware…; the console moves to the Firmware window. Connect, then Wear report…; the console moves to the wear window.*

> And the board keeps its own count. This is it, read straight from the table: every block, and how many times it's been written. The heavy borders are the program it holds now.

## How far it stretches

> The smaller the program, the further it goes. And real editing goes further still, because most uploads reuse most of their blocks.

## What wear levelling brings

> The kernel once, the program as data, only the blocks that changed, and the least-written block every time. A hundred writes turns into years of tinkering.

> Next time: a knob, one pin, and a voltmeter in BASIC. Thanks for watching.
