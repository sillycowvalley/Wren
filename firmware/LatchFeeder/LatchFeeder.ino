// LatchFeeder: an Arduino Mega 2560 feeding the 8-bit OLED Thing a byte bus, for the Wren
// BASIC video "Catch a byte on an edge". It plays a computer putting bytes on a bus: the byte
// goes on the eight data lines, then a rising edge on the strobe says "now". Between strobes
// the lines are noise.
//
// Two modes, chosen by typing in the Serial Monitor (115200 baud, "Newline"):
//   m        message mode (the default): "WREN BASIC " one byte a second, for
//            programs/OledThing/Latch.bas.
//   c        count mode: 00, 01, 02 ... FF, 00 ... at the rate set, for
//            programs/OledThing/LatchTest.bas, which stops at the first byte wrong or missed.
//            A line once a second shows the rate and how many it has strobed.
//   r 500    count mode at 500 strobes a second.
//   w        sweep: count mode from 100 strobes a second, 50 more every 3 seconds, to find
//            where the Thing starts to miss.
//   s 10     setup: the byte steady this many microseconds before the edge.
//   h 10     hold: and this many after it.
//   ?        what it is doing now.
//
// Wiring (the Thing runs from the Mega's 5 V: upload the Thing's program first, then unplug
// the Pico):
//   A0-A7 (port F, one row of the header) -> the Thing's b0-b7 on J1, with a straight
//                                            8-wire ribbon: A0 to J1 pin 8 (b0) ... A7 to
//                                            J1 pin 1 (b7). Upside down? Set Reversed.
//   D2                                    -> the Thing's PD3 (J5 pin 4): the strobe
//   5V                                    -> the Thing's VCC (J3 pin 1)
//   GND                                   -> the Thing's GND (J5 pin 1)
// For LatchTest.bas, a button from the Thing's PD2 (J5 pin 3) to GND starts its check again.

const uint8_t StrobePin = 2;
const char Message[] = "WREN BASIC ";
const bool Reversed = false;            // true if the ribbon puts A0 on b7

bool counting = false;                  // count mode, else message mode
bool sweeping = false;
long rate = 100;                        // strobes a second in count mode
unsigned int setupUs = 100;             // the byte steady this long before the edge...
unsigned int holdUs = 100;              // ...and this long after it
uint8_t nextByte = 0;                   // the next byte to strobe
unsigned long lastStrobe = 0;           // micros() at the last strobe
unsigned long lastStep = 0;             // millis() at the last sweep step
unsigned long sent = 0;                 // strobes in count mode since it started
unsigned long lastReport = 0;           // millis() at the last once-a-second line
uint16_t noise = 1;                     // a fast pseudo-random source for the noise

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

// A new noise byte, quickly: a 16-bit xorshift.
uint8_t Noise() {
  noise ^= noise << 7;
  noise ^= noise >> 9;
  noise ^= noise << 8;
  return (uint8_t)noise;
}

// The byte, steady before and after the rising edge.
void Strobe(uint8_t b) {
  Put(b);
  delayMicroseconds(setupUs);
  digitalWrite(StrobePin, HIGH);        // the Thing latches b0-b7 now
  delayMicroseconds(holdUs);
  digitalWrite(StrobePin, LOW);
}

void Report() {
  if (counting) {
    Serial.print(sweeping ? "Sweep: " : "Count: ");
    Serial.print(rate);
    Serial.print(" strobes a second");
  } else {
    Serial.print("Message: one byte a second");
  }
  Serial.print(", setup ");
  Serial.print(setupUs);
  Serial.print(" us, hold ");
  Serial.print(holdUs);
  Serial.println(" us");
}

void StartCounting(long perSecond) {
  counting = true;
  rate = perSecond;
  nextByte = 0;
  sent = 0;
  lastStrobe = micros();
  lastReport = millis();
  Report();
}

// One command from the Serial Monitor.
void Command() {
  char c = Serial.read();
  switch (c) {
    case 'm': counting = false; sweeping = false; nextByte = 0; Report(); break;
    case 'c': sweeping = false; StartCounting(rate); break;
    case 'r': sweeping = false; StartCounting(max(1L, Serial.parseInt())); break;
    case 'w': sweeping = true; lastStep = millis(); StartCounting(100); break;
    case 's': setupUs = Serial.parseInt(); Report(); break;
    case 'h': holdUs = Serial.parseInt(); Report(); break;
    case '?': Report(); break;
  }
}

void setup() {
  DDRF = 0xFF;                          // A0-A7: outputs
  pinMode(StrobePin, OUTPUT);
  digitalWrite(StrobePin, LOW);         // the strobe rests low; its rising edge latches
  Serial.begin(115200);
  Serial.println("LatchFeeder: m message, c count, r <rate>, w sweep, s <us>, h <us>, ?");
  Report();
}

void loop() {
  if (Serial.available()) Command();

  if (!counting) {
    // Message mode: noise for a second, then the next byte of the message.
    unsigned long start = millis();
    while (millis() - start < 1000 && !Serial.available()) Put(Noise());
    if (Serial.available()) return;
    uint8_t b = (uint8_t)Message[nextByte];
    Strobe(b);
    Serial.print("Strobed $");
    if (b < 0x10) Serial.print('0');
    Serial.print(b, HEX);
    Serial.print("  '");
    Serial.print((char)b);
    Serial.println("'");
    nextByte++;
    if (Message[nextByte] == 0) nextByte = 0;
    return;
  }

  // Count mode: noise until the next strobe is due, then the next byte.
  if (sweeping && millis() - lastStep >= 3000) {
    lastStep = millis();
    rate += 50;
    Report();
  }
  unsigned long interval = 1000000UL / rate;
  while (micros() - lastStrobe < interval) Put(Noise());
  lastStrobe += interval;
  Strobe(nextByte++);
  sent++;

  // Once a second, a line to show it's running: the rate, and strobes so far.
  if (millis() - lastReport >= 1000) {
    lastReport = millis();
    Serial.print(sweeping ? "Sweep: " : "Count: ");
    Serial.print(rate);
    Serial.print(" a second, ");
    Serial.print(sent);
    Serial.println(" strobed");
  }
}
