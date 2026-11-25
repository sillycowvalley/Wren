// LatchFeeder: an Arduino Mega 2560 feeding Wren's programs/OledThing/Latch.bas on the
// 8-bit OLED Thing, for the Wren BASIC video "Catch a byte on an edge".
//
// It spells out a message one byte a second, as a computer puts bytes on a bus: the byte
// goes on the eight data lines, then a rising edge on the strobe says "now". Between
// strobes the lines are anything but still: a new random byte every few microseconds. The
// Thing latches b0-b7 at the rising edge, so it shows the message, never the noise.
//
// Wiring (the Thing runs from the Mega's 5 V: upload Latch.bas first, then unplug the Pico):
//   A0-A7 (port F, one row of the header) -> the Thing's b0-b7 on J1, with a straight
//                                            8-wire ribbon: A0 to J1 pin 8 (b0) ... A7 to
//                                            J1 pin 1 (b7). Upside down? Set Reversed.
//   D2                                    -> the Thing's PD3 (J5 pin 4): the strobe
//   5V                                    -> the Thing's VCC (J3 pin 1)
//   GND                                   -> the Thing's GND (J5 pin 1)
//
// The Serial Monitor (115200 baud) shows each byte as it is strobed, to compare with the
// Thing's screen.

const uint8_t StrobePin = 2;
const char Message[] = "WREN BASIC ";
const bool Reversed = false;            // true if the ribbon puts A0 on b7
const unsigned long ByteMs = 1000;      // one byte a second
const unsigned int SetupUs = 100;       // the byte steady this long before the edge...
const unsigned int HoldUs = 100;        // ...and this long after it

// Bit 0 to bit 7 and back, for a ribbon the other way up.
uint8_t Flip(uint8_t v) {
  uint8_t r = 0;
  for (uint8_t i = 0; i < 8; i++) {
    r = (r << 1) | (v & 1);
    v >>= 1;
  }
  return r;
}

// All eight lines change at once: one write to port F.
void Put(uint8_t v) {
  PORTF = Reversed ? Flip(v) : v;
}

void setup() {
  DDRF = 0xFF;                          // A0-A7: outputs
  pinMode(StrobePin, OUTPUT);
  digitalWrite(StrobePin, LOW);         // the strobe rests low; its rising edge latches
  Serial.begin(115200);
  randomSeed(analogRead(A8));
}

void loop() {
  for (uint8_t i = 0; Message[i] != 0; i++) {
    uint8_t b = (uint8_t)Message[i];

    // A busy bus: noise until it is time for the next byte.
    unsigned long start = millis();
    while (millis() - start < ByteMs) Put((uint8_t)random(256));

    // The byte, steady before and after the rising edge.
    Put(b);
    delayMicroseconds(SetupUs);
    digitalWrite(StrobePin, HIGH);      // the Thing latches b0-b7 now
    delayMicroseconds(HoldUs);
    digitalWrite(StrobePin, LOW);

    Serial.print("Strobed $");
    if (b < 0x10) Serial.print('0');
    Serial.print(b, HEX);
    Serial.print("  '");
    Serial.print((char)b);
    Serial.println("'");
  }
}
