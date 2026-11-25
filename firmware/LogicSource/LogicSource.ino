// LogicSource: an Arduino Mega 2560 making patterns on eight lines for Wren's
// programs/OledThing/LogicAnalyzer.bas on the 8-bit OLED Thing, for the Wren BASIC video
// "An 8-channel logic analyzer". No buttons: it changes pattern on its own every 6 seconds,
// and the Serial Monitor (115200 baud) says which one is playing.
//
//   Counter   a binary count, one step every 2 ms: each line half the speed of the one above
//   Walking   a single 1 walking from b0 to b7, a step every 4 ms
//   PWM       a 16 ms period on every line, b0 high for 2 ms, b1 for 4 ms ... b7 for 16 ms
//
// Wiring (the Thing runs from the Mega's 5 V: upload LogicAnalyzer.bas first, then unplug
// the Pico):
//   A0-A7 (port F, one row of the header) -> the Thing's b0-b7 on J1, with a straight
//                                            8-wire ribbon: A0 to J1 pin 8 (b0) ... A7 to
//                                            J1 pin 1 (b7). Upside down? Set Reversed.
//   5V                                    -> the Thing's VCC (J3 pin 1)
//   GND                                   -> the Thing's GND (J5 pin 1)

const bool Reversed = false;            // true if the ribbon puts A0 on b7
const unsigned long PatternMs = 6000;   // each pattern plays this long

uint8_t pattern = 0;                    // 0 Counter, 1 Walking, 2 PWM
unsigned long patternStart = 0;

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

void Announce() {
  const char *names[] = { "Counter", "Walking", "PWM" };
  Serial.print("Pattern: ");
  Serial.println(names[pattern]);
}

void setup() {
  DDRF = 0xFF;                          // A0-A7: outputs
  Serial.begin(115200);
  patternStart = millis();
  Announce();
}

void loop() {
  unsigned long now = millis();
  if (now - patternStart >= PatternMs) {
    patternStart = now;
    pattern = (pattern + 1) % 3;
    Announce();
  }
  unsigned long t = now - patternStart;
  switch (pattern) {
    case 0:                             // Counter: one step every 2 ms
      Put((uint8_t)(t / 2));
      break;
    case 1:                             // Walking: one place every 4 ms
      Put((uint8_t)(1 << ((t / 4) % 8)));
      break;
    case 2: {                           // PWM: line n high for the first 2 (n + 1) ms of 16
      uint8_t phase = t % 16;
      uint8_t v = 0;
      for (uint8_t n = 0; n < 8; n++)
        if (phase < 2 * (n + 1)) v |= 1 << n;
      Put(v);
      break;
    }
  }
}
