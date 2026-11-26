// SerialSource: an Arduino Mega 2560 sending a message one bit at a time, the way SPI does,
// for Wren's programs/OledThing/SerialDecoder.bas on the 8-bit OLED Thing, for the Wren
// BASIC video "A serial decoder".
//
// Each byte: the select goes low, then eight bits, most significant first. A bit goes onto
// the data line while the clock is low, and the clock's rising edge says "take it". Then the
// select goes high again until the next byte. Slow on purpose, 20 bits a second, so the
// bits can be watched arriving. The other seven lines of port F are noise all the time: the
// Thing only listens to b0. No buttons: the Serial Monitor (115200 baud) shows each byte as
// it is sent.
//
// Wiring (the Thing runs from the Mega's 5 V: upload SerialDecoder.bas first, then unplug
// the Pico):
//   A0-A7 (port F, one row of the header) -> the Thing's b0-b7 on J1, with a straight
//                                            8-wire ribbon: A0 to J1 pin 8 (b0) ... A7 to
//                                            J1 pin 1 (b7). Upside down? Set Reversed.
//                                            A0 is the data line; A1-A7 are noise.
//   D2                                    -> the Thing's PD3 (J5 pin 4): the clock
//   D3                                    -> the Thing's PD2 (J5 pin 3): the select
//   5V                                    -> the Thing's VCC (J3 pin 1)
//   GND                                   -> the Thing's GND (J5 pin 1)

const uint8_t ClockPin = 2;
const uint8_t SelectPin = 3;            // low while a byte is being sent
const char Message[] = "HELLO WREN ";
const bool Reversed = false;            // true if the ribbon puts A0 on b7
const unsigned long BitMs = 50;         // 20 bits a second
const unsigned long GapMs = 300;        // between bytes, the select high

uint8_t letter = 0;                     // the next letter of the message

// Bit 0 to bit 7 and back, for a ribbon the other way up.
uint8_t Flip(uint8_t v) {
  uint8_t r = 0;
  for (uint8_t i = 0; i < 8; i++) {
    r = (r << 1) | (v & 1);
    v >>= 1;
  }
  return r;
}

// The data bit on A0, noise on A1-A7: one write to port F.
void Put(uint8_t bit) {
  uint8_t v = (uint8_t)(random(256) & 0xFE) | (bit & 1);
  PORTF = Reversed ? Flip(v) : v;
}

// Waits a while, keeping the noise lines busy but the data bit as it is.
void Hold(uint8_t bit, unsigned long ms) {
  unsigned long start = millis();
  while (millis() - start < ms) {
    Put(bit);
    delayMicroseconds(200);
  }
}

// One byte: select low, eight bits on the clock's rising edges, select high.
void SendByte(uint8_t b) {
  digitalWrite(SelectPin, LOW);
  Hold(0, BitMs / 2);
  for (int8_t i = 7; i >= 0; i--) {
    uint8_t bit = (b >> i) & 1;
    Hold(bit, BitMs / 2);               // the bit, settled, with the clock low
    digitalWrite(ClockPin, HIGH);       // the edge: the Thing takes the bit
    Hold(bit, BitMs / 2);
    digitalWrite(ClockPin, LOW);
  }
  digitalWrite(SelectPin, HIGH);
}

void setup() {
  DDRF = 0xFF;                          // A0-A7: outputs
  pinMode(ClockPin, OUTPUT);
  pinMode(SelectPin, OUTPUT);
  digitalWrite(ClockPin, LOW);
  digitalWrite(SelectPin, HIGH);        // not selected
  Serial.begin(115200);
  Serial.println();
  Serial.println("=== SerialSource ===");
  Serial.print("Sends \"");
  Serial.print(Message);
  Serial.println("\" one bit at a time, 20 bits a second, for SerialDecoder.bas.");
  Serial.println("Wiring: A0-A7 to b0-b7 (ribbon; A0 is the data), D2 to PD3 (clock), D3 to PD2 (select), 5V to VCC, GND to GND.");
  Serial.println("The Thing should show the bits as they arrive, then each byte in hex, matching below.");
  Serial.println();
}

void loop() {
  uint8_t b = (uint8_t)Message[letter];
  Serial.print("Sending '");
  Serial.print((char)b);
  Serial.print("'  hex ");
  if (b < 16) Serial.print('0');
  Serial.print(b, HEX);
  Serial.print("  bits ");
  for (int8_t i = 7; i >= 0; i--) Serial.print((b >> i) & 1);
  Serial.println();
  SendByte(b);
  letter++;
  if (Message[letter] == 0) letter = 0;
  Hold(0, GapMs);
}
