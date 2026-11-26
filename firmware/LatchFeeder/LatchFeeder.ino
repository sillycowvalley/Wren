// LatchFeeder: an Arduino Mega 2560 feeding the 8-bit OLED Thing a byte bus, for the Wren
// BASIC video "Catch a byte on an edge". It plays a computer putting bytes on a bus: the byte
// goes on the eight data lines, then a rising edge on the strobe says "now". Between strobes
// the lines are noise.
//
// Open the Serial Monitor at 115200 baud. It shows a menu: type a number and press Enter.
//   1  The message: "WREN BASIC ", one letter a second. For Latch.bas. (It starts in this.)
//   2  The speed test: 00, 01, 02 ... FF and round, from 100 a second, 50 a second faster
//      every 3 seconds. For LatchTest.bas, which stops at the first byte wrong or missed.
//   3  A steady count at one speed: type 3, a space and the speed, e.g. "3 400".
// Every second it prints what it's doing, so it's plain that it's running.
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
const unsigned int SetupUs = 100;       // the byte steady this long before the edge...
const unsigned int HoldUs = 100;        // ...and this long after it

enum Mode { MessageMode, SpeedTest, SteadyCount };
Mode mode = MessageMode;
long rate = 100;                        // bytes a second when counting
uint8_t nextByte = 0;                   // the next byte to strobe
unsigned long sent = 0;                 // bytes strobed since the mode started
unsigned long lastStrobe = 0;           // micros() at the last strobe
unsigned long lastFaster = 0;           // millis() when the speed test last sped up
unsigned long lastStatus = 0;           // millis() at the last status line
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
  delayMicroseconds(SetupUs);
  digitalWrite(StrobePin, HIGH);        // the Thing latches b0-b7 now
  delayMicroseconds(HoldUs);
  digitalWrite(StrobePin, LOW);
}

void Menu() {
  Serial.println();
  Serial.println("LatchFeeder. Type a number and press Enter:");
  Serial.println("  1  The message, one letter a second (for Latch.bas)");
  Serial.println("  2  The speed test, faster every 3 seconds (for LatchTest.bas)");
  Serial.println("  3  A steady count at one speed, e.g. 3 400");
  Serial.println();
}

void Start(Mode m, long perSecond) {
  mode = m;
  rate = perSecond;
  nextByte = 0;
  sent = 0;
  lastStrobe = micros();
  lastFaster = millis();
  lastStatus = millis();
  if (m == MessageMode) Serial.println("Now: the message, one letter a second.");
  if (m == SpeedTest) Serial.println("Now: the speed test, from 100 bytes a second, 50 faster every 3 seconds.");
  if (m == SteadyCount) {
    Serial.print("Now: counting at ");
    Serial.print(rate);
    Serial.println(" bytes a second.");
  }
}

// A line typed in the Serial Monitor.
void Command() {
  long choice = Serial.parseInt();
  if (choice == 1) Start(MessageMode, 1);
  else if (choice == 2) Start(SpeedTest, 100);
  else if (choice == 3) {
    long speed = Serial.parseInt();
    if (speed < 1 || speed > 4000) Serial.println("Give a speed from 1 to 4000, e.g. 3 400");
    else Start(SteadyCount, speed);
  }
  else Menu();
  while (Serial.available()) Serial.read();   // the rest of the line
}

void setup() {
  DDRF = 0xFF;                          // A0-A7: outputs
  pinMode(StrobePin, OUTPUT);
  digitalWrite(StrobePin, LOW);         // the strobe rests low; its rising edge latches
  Serial.begin(115200);
  Serial.setTimeout(50);
  Menu();
  Start(MessageMode, 1);
}

void loop() {
  if (Serial.available()) Command();

  if (mode == MessageMode) {
    // Noise for a second, then the next letter of the message.
    unsigned long start = millis();
    while (millis() - start < 1000 && !Serial.available()) Put(Noise());
    if (Serial.available()) return;
    uint8_t b = (uint8_t)Message[nextByte];
    Strobe(b);
    Serial.print("Sent '");
    Serial.print((char)b);
    Serial.print("'  hex ");
    if (b < 0x10) Serial.print('0');
    Serial.println(b, HEX);
    nextByte++;
    if (Message[nextByte] == 0) nextByte = 0;
    return;
  }

  // Counting: noise until the next byte is due, then the next byte.
  if (mode == SpeedTest && millis() - lastFaster >= 3000) {
    lastFaster = millis();
    rate += 50;
  }
  unsigned long interval = 1000000UL / rate;
  while (micros() - lastStrobe < interval) Put(Noise());
  lastStrobe += interval;
  Strobe(nextByte++);
  sent++;

  if (millis() - lastStatus >= 1000) {
    lastStatus = millis();
    Serial.print(mode == SpeedTest ? "Speed test: " : "Steady count: ");
    Serial.print(rate);
    Serial.print(" bytes a second, ");
    Serial.print(sent);
    Serial.println(" sent so far.");
  }
}
