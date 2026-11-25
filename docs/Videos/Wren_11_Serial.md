# Wren BASIC 11: A serial decoder

## YouTube

**Title**: One wire, one bit at a time: a serial decoder in BASIC | Wren BASIC 11

**Description**:

```
An Arduino Mega sends a message one bit at a time, the way SPI does, and Digicool Things' 8-bit OLED Thing puts the bytes back together: a serial decoder in 170 bytes of Wren BASIC, with the bits shown as they arrive.

In this video:
- serial: a select, a clock, and one data line
- a falling edge to start a byte, a rising edge for each bit
- shifting a bit in: double it, add the new one
- the bits so far in binary, the finished bytes in hex
- the other seven wires full of noise, and none of it getting in

The program is programs/OledThing/SerialDecoder.bas; the Mega's sketch is firmware/SerialSource.

Wren (MIT licence, C#, Windows): https://github.com/sillycowvalley/Wren

#Wren #BASIC #SPI #Serial #Arduino #Microcontroller #Electronics
```

---

## SERIAL DECODER

> Last time, eight wires carried a byte at once. Today it's one wire, a bit at a time, and the OLED Thing puts the bytes back together.

## One wire, a bit at a time

> Serial means sending a byte down a single wire, one bit after another. Two more wires keep it in step.

> The select goes low: a byte is coming. Then for each bit, the data goes on the wire, and the clock's rising edge says "take it". Eight edges, eight bits, most significant first. That's how SPI works.

## Shift it in

> Two handlers. When the select falls, a new byte starts from nothing.

> At each clock edge while the select is low, the byte so far is doubled, which moves every bit one place left, and the new bit from b0 goes on the end. After the eighth, the byte is done, and it goes into a ring of the last ten.

> The Mega fills the other seven wires with noise, all the time. AND 1 keeps only b0, so none of it gets in.

## The screen

> At the top, the bits that have arrived so far, in binary, newest on the right. Below, the last ten bytes in hex, oldest first, and a count.

> Here it's five bits into a W: zero, one, zero, one, zero.

## The decoder, live

*Upload SerialDecoder.bas with the Pico, then unplug the Pico. Ribbon from the Mega's A0–A7 to J1, D2 to PD3, D3 to PD2, 5 V and GND. Power the Mega with SerialSource on it; open its Serial Monitor beside the camera.*

> The Mega is sending twenty bits a second, slow enough to watch. There they come, one at a time.

> Eight, and it's a byte. The Mega prints each one as it sends it, and the Thing agrees.

> And there's the message: four eight, four five, four C, four C, four F. H, E, L, L, O.

## What the decoder brings

> A select, a clock, one data wire, and a byte built a bit at a time. A serial decoder, in a hundred and seventy bytes.

> Thanks for watching.
