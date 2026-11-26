// BusSource: an Arduino Mega 2560 playing a CPU on a memory bus, for Wren's
// programs/OledThing/BusSniffer.bas on the 8-bit OLED Thing, for the Wren BASIC video
// "A bus sniffer".
//
// Every bus cycle puts a byte on the eight data lines and gives the clock a rising edge.
// The write enable is low only for a write. Seven cycles in eight are reads of "memory"
// (random bytes); the eighth writes the next letter of a message. The Thing latches on the
// clock only while the write enable is low, so it sees the writes and never the reads. No
// buttons: the Serial Monitor (115200 baud) counts the cycles once a second.
//
// Wiring (the Thing runs from the Mega's 5 V: upload BusSniffer.bas first, then unplug the
// Pico):
//   A0-A7 (port F, one row of the header) -> the Thing's b0-b7 on J1, with a straight
//                                            8-wire ribbon: A0 to J1 pin 8 (b0) ... A7 to
//                                            J1 pin 1 (b7). Upside down? Set Reversed.
//   D2                                    -> the Thing's PD3 (J5 pin 4): the clock
//   D3                                    -> the Thing's PD2 (J5 pin 3): the write enable
//   5V                                    -> the Thing's VCC (J3 pin 1)
//   GND                                   -> the Thing's GND (J5 pin 1)

const uint8_t ClockPin = 2;
const uint8_t WritePin = 3;             // the write enable: low for a write
const char Message[] = "HELLO FROM THE MEGA ";
const bool Reversed = false;            // true if the ribbon puts A0 on b7
const unsigned long CycleUs = 5000;     // 200 bus cycles a second
const uint8_t WriteEvery = 8;           // one cycle in eight is a write

uint8_t letter = 0;                     // the next letter of the message to write
unsigned long cycle = 0;
unsigned long reads = 0, writes = 0;
unsigned long lastCycle = 0, lastReport = 0;

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

// One bus cycle: the write enable and the byte, then the clock's rising edge, held.
void BusCycle(bool write, uint8_t v) {
  digitalWrite(WritePin, write ? LOW : HIGH);
  Put(v);
  delayMicroseconds(50);                // setup
  digitalWrite(ClockPin, HIGH);         // the edge: the Thing latches if this is a write
  delayMicroseconds(100);               // hold
  digitalWrite(ClockPin, LOW);
  digitalWrite(WritePin, HIGH);
}

void setup() {
  DDRF = 0xFF;                          // A0-A7: outputs
  pinMode(ClockPin, OUTPUT);
  pinMode(WritePin, OUTPUT);
  digitalWrite(ClockPin, LOW);
  digitalWrite(WritePin, HIGH);         // not writing
  Serial.begin(115200);
  Serial.println();
  Serial.println("=== BusSource ===");
  Serial.println("Plays a CPU on a memory bus for BusSniffer.bas: 200 bus cycles a second, one write in eight.");
  Serial.print("The writes spell \"");
  Serial.print(Message);
  Serial.println("\" over and over; the reads are random.");
  Serial.println("Wiring: A0-A7 to b0-b7 (ribbon), D2 to PD3 (clock), D3 to PD2 (write enable), 5V to VCC, GND to GND.");
  Serial.println("The Thing should count the writes (matching the count below) and show them in hex.");
  Serial.println();
  lastCycle = micros();
}

void loop() {
  if (micros() - lastCycle < CycleUs) return;
  lastCycle += CycleUs;
  cycle++;
  if (cycle % WriteEvery == 0) {
    BusCycle(true, (uint8_t)Message[letter]);
    letter++;
    if (Message[letter] == 0) letter = 0;
    writes++;
  } else {
    BusCycle(false, (uint8_t)random(256));
    reads++;
  }
  if (millis() - lastReport >= 1000) {
    lastReport = millis();
    Serial.print(reads + writes);
    Serial.print(" bus cycles so far: ");
    Serial.print(reads);
    Serial.print(" reads, ");
    Serial.print(writes);
    Serial.println(" writes.");
  }
}
