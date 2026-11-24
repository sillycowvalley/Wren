# Digicool's factory firmware

`8-bit_oled_thing_v1.1.s19` is the 8-bit OLED Thing's own firmware, v1.1, by Digicool
Things. It is here with the permission of Greg, Digicool's owner. It is Digicool's work
and not covered by Wren's MIT licence.

- **Contents:** 7,806 bytes of flash ($8000–$9E7D) and no option bytes.
- **Putting it back on a Thing:** in Wren's **Tools → Programmer…**, use **Write image…**.
  Because the file carries no option bytes, the Thing ends up running its factory
  behaviour without read-out protection.

What the firmware does is in `docs/OledThing.md`.
