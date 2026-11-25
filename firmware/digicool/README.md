# Digicool's factory firmware

The factory firmware of Digicool Things' two boards, here with the permission of Greg, Digicool's owner. It is Digicool's work and not covered by Wren's MIT licence.



| File                        | Board                  | Contents                           |
| --------------------------- | ---------------------- | ---------------------------------- |
| `8-bit_oled_thing_v1.1.s19` | 8-bit OLED Thing, v1.1 | 7,806 bytes of flash ($8000-$9E7D) |
| `8-bit_led_thing.s19`       | 8-bit LED Thing        | 4,432 bytes of flash ($8000-$914F) |

Neither file carries option bytes, so a Thing written with one runs its factory behaviour
without read-out protection.

- **Putting it back on an OLED Thing:** in Wren's **Tools → Firmware…**, **Write image…**
  opens this folder: choose `8-bit_oled_thing_v1.1.s19`.
- **Putting it back on an LED Thing:** **Write image…** with `8-bit_led_thing.s19`.

What the firmware does is in `docs/OledThing.md` and `docs/LedThing.md`.



The originals or more recent versions can be found at the locations below:



https://github.com/DigicoolThings/8-bit_OLED_Thing/tree/master/Firmware

https://github.com/DigicoolThings/8-bit_LED_Thing/tree/master/Firmware
