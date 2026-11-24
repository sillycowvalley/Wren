// PicoLink: a quick SWIM programmer for Wren on a Raspberry Pi Pico (RP2040) or Pico 2 (RP2350).
//
// Build it in the Arduino IDE with Earle Philhower's "Raspberry Pi Pico/RP2040/RP2350" core
// (Boards Manager). The steps and the wiring are in docs/PicoLink.md:
//   GP2      -> the Thing's SWIM, with a 1 kOhm pull-up from GP2 to 3V3(OUT)
//   GP3      -> the Thing's NRST
//   GND      -> GND
//   3V3(OUT) -> the Thing's VCC (the Thing must run at 3.3 V: the Pico's pins are not 5 V tolerant)
//
// It speaks SWIM at low speed (8 MHz SWIM clock: a bit is 22 clocks, 2.75 us; a 1 is low for
// 2 clocks, a 0 for 20), timed with the core's SysTick, from RAM, with interrupts off while a
// transfer is on the wire. After NRST is released, a SWIM communication reset brings a sync
// pulse at the target's trimmed clock, which calibrates the reading of the target's bits
// (UM0470 3.2 step 9 and 3.6). The host's own pulses end with a brief push-pull high, so they
// stay sharp on a slow line. The target's bits are read 1.7 us in, and a bit edge hidden by
// a slow rise is predicted from the bit period. Wren (src/Wren.Link/PicoLink.cs) sends the
// requests; the protocol is described there. SWIM itself is described in ST's UM0470.

#include <hardware/clocks.h>
#include <hardware/gpio.h>
#include <hardware/structs/sio.h>
#include <hardware/structs/systick.h>
#include <hardware/sync.h>

const uint SwimPin = 2;
const uint ResetPin = 3;
const uint32_t SwimMask = 1u << SwimPin;
const uint32_t ResetMask = 1u << ResetPin;
const uint8_t SketchVersion = 11;
const int MaxChunk = 64;

// Reply statuses (as in PicoLink.cs).
enum : uint8_t { StatusOk = 0, StatusNoAnswer = 1, StatusNack = 2, StatusParity = 3,
                 StatusBadFrame = 4, StatusNoSync = 5, StatusBadCommand = 6 };

// SWIM commands: a 0 start bit then three bits.
const uint32_t SwimSrst = 0, SwimRotf = 1, SwimWotf = 2;

// What went wrong last, reported after an error status: which frame of the request
// (counting from 1) and how long the line was last seen low, in CPU cycles.
static uint8_t failedFrame;
static uint32_t lowCycles;

// Times in CPU cycles, worked out from the clock in setup().
// The host's bits have a fixed shape well inside UM0470's table 3 (low speed: a 0 low for
// 1.6-2.4 us, a 1 for 150-250 ns): the target decodes a bit by counting low samples of its
// SWIM clock (8 or fewer is a 1, 9 or more a 0), so the shape matters, not its own clock.
// The target's bits are timed by its clock: targetPeriod (22 SWIM clocks) and sampleAt
// (11, midway between a 1's 2 low clocks and a 0's 20), calibrated from a sync pulse
// (128 SWIM clocks) when one is measured cleanly.
static uint32_t hostPeriod, shortLow, longLow, targetPeriod, sampleAt;
static uint32_t answerTimeout, bitTimeout, riseTimeout, edgeWindow, interFrameGap, commResetLow;
static uint32_t entryLow, entrySlow, entryFast, syncTimeout, afterSync, syncShortest, syncLongest, stuckTimeout, settleTimeout;
static uint32_t cpuHz;

static uint32_t cycles(uint32_t ns) { return (uint32_t)((uint64_t)cpuHz * ns / 1000000000ull); }

// SysTick counts down from 0xFFFFFF at the CPU clock; now() counts up, modulo 2^24.
static __force_inline uint32_t now() { return ~systick_hw->cvr & 0xFFFFFF; }
static __force_inline uint32_t since(uint32_t t) { return (now() - t) & 0xFFFFFF; }
// Waits until c cycles after t; t may be in the past or (a little) in the future.
static __force_inline void waitUntil(uint32_t t, uint32_t c) {
  uint32_t end = t + c;
  for (;;) {
    uint32_t ahead = (end - now()) & 0xFFFFFF;
    if (ahead == 0 || ahead >= 0x800000) return;
  }
}

// Open drain: the output value is 0, so enabling the output pulls low and disabling it lets go.
static __force_inline void driveLow() { sio_hw->gpio_oe_set = SwimMask; }
static __force_inline void letGo() { sio_hw->gpio_oe_clr = SwimMask; }

// Ends one of the host's own low pulses by driving the line high for a moment before
// letting go, so the edge is sharp however weak the pull-up (the target never drives
// while the host is sending).
static uint32_t boostHigh;
static __force_inline void letGoSharply() {
  sio_hw->gpio_set = SwimMask;
  uint32_t t = now();
  while (since(t) < boostHigh) { }
  sio_hw->gpio_oe_clr = SwimMask;
  sio_hw->gpio_clr = SwimMask;
}
static __force_inline bool lineHigh() { return (sio_hw->gpio_in & SwimMask) != 0; }

// Waits for the line to fall; false on timeout. *at is when it was seen low.
static bool __not_in_flash_func(waitFall)(uint32_t timeout, uint32_t *at) {
  uint32_t start = now();
  for (;;) {
    if (!lineHigh() || !lineHigh() || !lineHigh() || !lineHigh()) break;
    if (since(start) > timeout) return false;
  }
  *at = now();
  return true;
}

// Waits for the line to rise; false on timeout.
static bool __not_in_flash_func(waitRise)(uint32_t timeout) {
  uint32_t start = now();
  for (;;) {
    if (lineHigh() || lineHigh() || lineHigh() || lineHigh()) return true;
    if (since(start) > timeout) return false;
  }
}

// Sends one bit that starts at time t (now, or a moment ago).
static void __not_in_flash_func(sendBit)(uint32_t t, int bit) {
  driveLow();
  waitUntil(t, bit ? shortLow : longLow);
  letGoSharply();
  waitUntil(t, hostPeriod);
}

// Sends a frame's last bit and returns as soon as its low is over: the target may answer
// as soon as the line has been high for a moment (UM0470 table 3: inter-bit time > 0), well
// before the host's bit period would end.
static void __not_in_flash_func(sendLastBit)(uint32_t t, int bit) {
  driveLow();
  waitUntil(t, bit ? shortLow : longLow);
  letGoSharply();
}

// Reads one bit from the target: 1 or 0, or -1 if the line never fell. *at is its start.
static int __not_in_flash_func(readBit)(uint32_t timeout, uint32_t *at) {
  if (!waitFall(timeout, at)) return -1;
  waitUntil(*at, sampleAt);
  return lineHigh() ? 1 : 0;
}

// Sends a frame (its bits, most significant first, starting with the 0 start bit) and its
// parity, then reads the target's ACK; a NACK sends the frame again.
// The target may start its next frame the moment an ACK bit ends, so after an ACK the
// host goes straight back to listening, and only a frame the host sends waits for the
// ACK bit's period to finish (lineBusyFrom + lineBusyFor).
static uint32_t lineBusyFrom, lineBusyFor;

static uint8_t __not_in_flash_func(sendFrame)(uint32_t value, int bits) {
  for (int attempt = 0; attempt < 16; attempt++) {
    waitUntil(lineBusyFrom, lineBusyFor);
    uint32_t t = now();
    int parity = 0;
    for (int i = bits - 1; i >= 0; i--) {
      int bit = (value >> i) & 1;
      parity ^= bit;
      sendBit(t, bit);
      t += hostPeriod;
    }
    sendLastBit(t, parity);
    uint32_t at;
    int ack = readBit(answerTimeout, &at);
    if (ack < 0) return StatusNoAnswer;
    lineBusyFrom = at;
    lineBusyFor = targetPeriod + interFrameGap;
    if (ack) return StatusOk; // the ACK's low is over (it was sampled high): listen at once
    waitUntil(at, targetPeriod);
    if (!waitRise(riseTimeout)) {
      waitRise(stuckTimeout); // measure how long the target held the line
      lowCycles = since(at);
      return StatusBadFrame;
    }
  }
  return StatusNack;
}

// Reads a frame from the target (a 1 start bit, eight data bits, parity) and answers ACK,
// or NACK so that it is sent again.
static uint8_t __not_in_flash_func(receiveFrame)(uint8_t *out) {
  for (int attempt = 0; attempt < 16; attempt++) {
    uint32_t at;
    int bit = readBit(answerTimeout, &at);
    if (bit < 0) return StatusNoAnswer;
    if (bit != 1) return StatusBadFrame;
    // Each later bit starts one of the target's periods after the last. Its falling edge
    // shows only if the line rose in the 2-clock gap after the previous bit, which a slow
    // pull-up does not manage after a 0; so take the edge when it is seen near where it is
    // due, and otherwise count on the period.
    uint32_t value = 0;
    uint32_t bitStart = at;
    for (int i = 0; i < 9; i++) {
      waitUntil(bitStart, targetPeriod - edgeWindow);
      uint32_t fell;
      if (lineHigh()) {
        if (!waitFall(2 * edgeWindow, &fell)) return StatusBadFrame;
        bitStart = fell;
      } else {
        bitStart += targetPeriod;
      }
      waitUntil(bitStart, sampleAt);
      value = (value << 1) | (lineHigh() ? 1u : 0u);
    }
    at = bitStart;
    int parity = 0;
    for (int i = 1; i <= 8; i++) parity ^= (value >> i) & 1;
    bool good = parity == (int)(value & 1);
    // The host's ACK (or NACK) bit, one period after the parity bit began. After an ACK,
    // return as soon as the line is let go: the next frame may follow the bit at once.
    waitUntil(at, targetPeriod);
    uint32_t t = now();
    driveLow();
    waitUntil(t, good ? shortLow : longLow);
    letGoSharply();
    lineBusyFrom = t;
    lineBusyFor = hostPeriod + interFrameGap;
    // After a NACK the target sends the frame again, perhaps as soon as the NACK's low is
    // over: go straight back to listening for its start bit.
    if (good) {
      *out = (uint8_t)(value >> 1);
      return StatusOk;
    }
  }
  return StatusParity;
}

// A command with its count and 24-bit address (ROTF and WOTF): frames 1 to 5.
static uint8_t __not_in_flash_func(sendHeader)(uint32_t command, int count, uint32_t address) {
  failedFrame = 1;
  uint8_t status = sendFrame(command, 4);
  if (status == StatusOk) { failedFrame = 2; status = sendFrame((uint32_t)count, 9); }
  if (status == StatusOk) { failedFrame = 3; status = sendFrame((address >> 16) & 0xFF, 9); }
  if (status == StatusOk) { failedFrame = 4; status = sendFrame((address >> 8) & 0xFF, 9); }
  if (status == StatusOk) { failedFrame = 5; status = sendFrame(address & 0xFF, 9); }
  return status;
}

static uint8_t __not_in_flash_func(readMemory)(int count, uint32_t address, uint8_t *data) {
  uint32_t saved = save_and_disable_interrupts();
  uint8_t status = sendHeader(SwimRotf, count, address);
  for (int i = 0; i < count && status == StatusOk; i++) { failedFrame = (uint8_t)(6 + i); status = receiveFrame(&data[i]); }
  restore_interrupts(saved);
  return status;
}

static uint8_t __not_in_flash_func(writeMemory)(int count, uint32_t address, const uint8_t *data) {
  uint32_t saved = save_and_disable_interrupts();
  uint8_t status = sendHeader(SwimWotf, count, address);
  for (int i = 0; i < count && status == StatusOk; i++) { failedFrame = (uint8_t)(6 + i); status = sendFrame(data[i], 9); }
  restore_interrupts(saved);
  return status;
}

static uint8_t __not_in_flash_func(softReset)() {
  uint32_t saved = save_and_disable_interrupts();
  failedFrame = 1;
  uint8_t status = sendFrame(SwimSrst, 4);
  restore_interrupts(saved);
  return status;
}

// The entry sequence: low for 16 us, four pulses at 1 kHz, four at 2 kHz; then the target
// answers with a sync pulse 128 SWIM clocks (16 us) long. *syncCycles is how long it was.
// The target may start its pulse before the host lets go (seen on a Thing: the line stays
// low about 16 us after release). So: if the line rises within a couple of microseconds
// (its own rise time), the pulse is still to come; if it stays low, the pulse has begun
// and its length is counted from the release. A pulse far from 16 us is not the sync.
// When an entry fails, failedFrame says where: EntryNoRise (the line did not rise once let
// go; lowCycles is how long it stayed low), EntryNoPulse (no pulse came), EntryBadPulse
// (a pulse of the wrong length; lowCycles is its length).
const uint8_t EntryNoRise = 0xF1, EntryNoPulse = 0xF2, EntryBadPulse = 0xF3;

// The entry pulses, ending with the line let go. Interrupts must be off. Records how long
// the line took to rise after the second 500 us low: a long low resets SWIM, so the target
// is silent then and the rise is the pull-up's alone.
static uint32_t entryRiseCycles;
static void __not_in_flash_func(sendEntryPulses)() {
  uint32_t t = now();
  driveLow();
  waitUntil(t, entryLow);
  for (int i = 0; i < 8; i++) {
    uint32_t half = i < 4 ? entrySlow : entryFast;
    t = now();
    letGo();
    if (i == 2) {
      entryRiseCycles = waitRise(half / 2) ? since(t) : 0xFFFFFF;
    }
    waitUntil(t, half);
    t = now();
    driveLow();
    waitUntil(t, half);
  }
  letGo();
}

// Takes the target's sync pulse after the host lets go of the line at `released`. If the
// line rises within a couple of microseconds (its own rise), the pulse is still to come and
// is measured whole ("clean"); if it stays low, the pulse began before the release and only
// its part after the release is counted. Returns false if no pulse came or the line stuck.
static bool __not_in_flash_func(catchSync)(uint32_t released, uint32_t *length, bool *clean) {
  uint32_t at;
  *clean = false;
  if (waitRise(settleTimeout)) {
    if (!waitFall(syncTimeout, &at)) {
      failedFrame = EntryNoPulse;
      return false;
    }
    waitRise(syncTimeout);
    *length = since(at);
    *clean = true;
    return true;
  }
  if (!waitRise(syncLongest)) {
    failedFrame = EntryNoRise;
    waitRise(stuckTimeout);
    lowCycles = since(released);
    return false;
  }
  *length = since(released);
  return true;
}

// A sync pulse measured whole sets the target's bit timing: 128 SWIM clocks long, a target
// bit is 22 of them, sampled at 11. Only a plausible pulse (12 to 24 us; 16 is right) counts.
static uint32_t calibrateShortest, calibrateLongest, traceWindow;
static void __not_in_flash_func(calibrate)(uint32_t syncCycles) {
  if (syncCycles < calibrateShortest || syncCycles > calibrateLongest) return;
  targetPeriod = syncCycles * 22 / 128;
  sampleAt = syncCycles * 11 / 128;
  edgeWindow = syncCycles * 5 / 128;
}

static uint32_t defaultPeriod, defaultSampleAt, defaultEdgeWindow;
static void __not_in_flash_func(resetCalibration)() {
  targetPeriod = defaultPeriod;
  sampleAt = defaultSampleAt;
  edgeWindow = defaultEdgeWindow;
}

static uint8_t __not_in_flash_func(enterSwim)(uint32_t *syncCycles, bool *clean) {
  uint32_t saved = save_and_disable_interrupts();
  resetCalibration();
  sendEntryPulses();
  uint32_t released = now();
  uint8_t status = StatusNoSync;
  failedFrame = 0;
  lowCycles = 0;
  uint32_t length = 0;
  if (catchSync(released, &length, clean)) {
    lowCycles = length;
    *syncCycles = length;
    if (length >= syncShortest && length <= syncLongest) {
      if (*clean) calibrate(length);
      uint32_t t = now();
      waitUntil(t, afterSync);
      status = StatusOk;
    } else {
      failedFrame = EntryBadPulse;
    }
  }
  lineBusyFor = 0;
  restore_interrupts(saved);
  return status;
}

// SWIM communication reset (UM0470 3.6): the line low for more than 64 SWIM clocks resets
// the target's state machine and low speed, and it answers with a sync pulse. Done after
// NRST is released, when the target's clock is trimmed (3.2 step 9), its sync recalibrates
// the host. UM0470 has the host hold it 128 clocks: 25 us is 200 at 8 MHz and still over 64
// at the slowest HSI it allows (10 MHz, a 5 MHz SWIM clock). If the target's answer is
// hidden under the host's low, the reset has still happened: it simply calibrates nothing.
static uint8_t __not_in_flash_func(resetComms)(uint32_t *syncCycles, bool *clean) {
  uint32_t saved = save_and_disable_interrupts();
  waitUntil(lineBusyFrom, lineBusyFor);
  failedFrame = 0;
  lowCycles = 0;
  uint32_t t = now();
  driveLow();
  waitUntil(t, commResetLow);
  letGo();
  uint32_t released = now();
  uint32_t length = 0;
  uint8_t status = StatusNoSync;
  if (catchSync(released, &length, clean)) {
    *syncCycles = length;
    if (*clean) calibrate(length);
    t = now();
    waitUntil(t, afterSync);
    status = StatusOk;
  } else if (failedFrame == EntryNoPulse) {
    *syncCycles = 0;
    failedFrame = 0;
    status = StatusOk;
  }
  lineBusyFor = 0;
  restore_interrupts(saved);
  return status;
}

// The line check ('D'): measures the wiring without trusting any of it.
// Data: SWIM's level at rest, NRST's level when held low and when let go, the time SWIM
// takes to rise once let go during the entry (ns, 0xFFFF for never), then the entry sequence with NRST low
// and every edge for 300 us after it: the level at release, the count of edges (16 at most)
// and each edge's time after release in units of 100 ns.
// Traces record raw cycle counts and convert them after: the conversion needs 64-bit
// division, a library routine that lives in flash, and code fetched from flash stalls for
// microseconds on a cache miss, which would bend the very timing being recorded.
const int MaxEdges = 16;
static uint8_t edgeCount;
static uint8_t levelAtRelease;
static uint32_t edges[MaxEdges];

static uint16_t tenthsOfMicroseconds(uint32_t c) {
  uint64_t v = (uint64_t)c * 10000000ull / cpuHz;
  return v > 0xFFFF ? 0xFFFF : (uint16_t)v;
}

static uint16_t hundredthsOfMicroseconds(uint32_t c) {
  uint64_t v = (uint64_t)c * 100000000ull / cpuHz;
  return v > 0xFFFF ? 0xFFFF : (uint16_t)v;
}

static void __not_in_flash_func(traceEntry)() {
  uint32_t saved = save_and_disable_interrupts();
  sendEntryPulses();
  uint32_t released = now();
  bool level = lineHigh();
  levelAtRelease = level ? 1 : 0;
  edgeCount = 0;
  uint32_t window = traceWindow;
  while (since(released) < window && edgeCount < MaxEdges) {
    if (lineHigh() != level) {
      level = !level;
      edges[edgeCount++] = since(released);
    }
  }
  restore_interrupts(saved);
}

// The read trace ('T' a2 a1 a0): a communication reset, a ROTF of one byte at address a,
// then every edge on the line for 80 us after the header's last ACK, with nothing decoded
// and no ACK sent; then another communication reset to leave SWIM tidy.
// Data: the header's status and failed frame, the reset's sync (ns, 2 bytes) and whether it
// was measured whole, the level when recording began, the count of edges (64 at most) and
// each edge's time in units of 10 ns (2 bytes each).
const int MaxTraceEdges = 64;
static uint32_t traceEdges[MaxTraceEdges];

// Records edges for `window` cycles from now; returns the count, *level the starting level.
static uint8_t __not_in_flash_func(recordEdges)(uint32_t *out, int max, uint32_t window, uint8_t *level) {
  uint32_t start = now();
  bool high = lineHigh();
  *level = high ? 1 : 0;
  uint8_t count = 0;
  while (since(start) < window && count < max) {
    if (lineHigh() != high) {
      high = !high;
      out[count++] = since(start);
    }
  }
  return count;
}

static uint8_t __not_in_flash_func(headerThenRecord)(uint32_t address, uint8_t *frame, uint8_t *level, uint8_t *count, uint32_t window) {
  uint32_t saved = save_and_disable_interrupts();
  uint8_t status = sendHeader(SwimRotf, 1, address);
  *frame = failedFrame;
  *count = 0;
  if (status == StatusOk) *count = recordEdges(traceEdges, MaxTraceEdges, window, level);
  restore_interrupts(saved);
  return status;
}

static void readTrace(uint32_t address) {
  uint32_t syncCycles = 0;
  bool clean = false;
  uint8_t resetStatus = resetComms(&syncCycles, &clean);
  uint8_t status = resetStatus;
  uint8_t frame = 0;
  uint8_t level = 0, count = 0;
  if (status == StatusOk) status = headerThenRecord(address, &frame, &level, &count, cycles(80000));
  uint32_t ignored;
  bool alsoIgnored;
  resetComms(&ignored, &alsoIgnored);
  uint32_t ns = (uint32_t)((uint64_t)syncCycles * 1000000000ull / cpuHz);
  if (ns > 0xFFFF) ns = 0xFFFF;
  Serial.write(status);
  Serial.write(frame);
  Serial.write((uint8_t)(ns >> 8));
  Serial.write((uint8_t)ns);
  Serial.write(clean ? 1 : 0);
  Serial.write(level);
  Serial.write(count);
  for (int i = 0; i < count; i++) {
    uint16_t v = hundredthsOfMicroseconds(traceEdges[i]);
    Serial.write((uint8_t)(v >> 8));
    Serial.write((uint8_t)v);
  }
}

// The frame trace ('F' c): a communication reset, then the 4-bit command frame c (with its
// parity) sent from a timetable while every edge on the line is recorded, the host's own
// pulses included, and 300 us more after it, sending no ACKs. Then another communication
// reset. Data: the reset's sync (ns, 2 bytes), the level when recording began, the count of
// edges (96 at most) and each edge's time from the frame's start in units of 10 ns.
const int MaxFrameEdges = 96;
static uint32_t frameEdges[MaxFrameEdges];

static uint8_t __not_in_flash_func(sendFrameRecording)(const int *bits, uint32_t end, uint8_t *level) {
  uint32_t saved = save_and_disable_interrupts();
  uint32_t start = now();
  bool high = lineHigh();
  *level = high ? 1 : 0;
  uint8_t count = 0;
  int step = 0;           // 0: drive low next; 1: drive high next; 2: let go next
  int bit = 0;
  uint32_t due = 0;       // when the next step is due, in cycles from start
  for (;;) {
    uint32_t t = since(start);
    if (t >= end) break;
    if (bit < 5 && t >= due) {
      if (step == 0) { driveLow(); due = bit * hostPeriod + (bits[bit] ? shortLow : longLow); step = 1; }
      else if (step == 1) { sio_hw->gpio_set = SwimMask; due += boostHigh; step = 2; }
      else { sio_hw->gpio_oe_clr = SwimMask; sio_hw->gpio_clr = SwimMask; bit++; due = bit * hostPeriod; step = 0; }
    }
    if (lineHigh() != high) {
      high = !high;
      if (count < MaxFrameEdges) frameEdges[count++] = t;
    }
  }
  restore_interrupts(saved);
  return count;
}

static void frameTrace(uint8_t command) {
  uint32_t syncCycles = 0;
  bool clean = false;
  resetComms(&syncCycles, &clean);
  // The bits: the 0 header, the three command bits, the parity.
  int bits[5];
  bits[0] = 0;
  int parity = 0;
  for (int i = 0; i < 3; i++) { bits[1 + i] = (command >> (2 - i)) & 1; parity ^= bits[1 + i]; }
  bits[4] = parity;
  uint8_t level = 0;
  uint8_t count = sendFrameRecording(bits, 5 * hostPeriod + cycles(300000), &level);
  uint32_t ignored;
  bool alsoIgnored;
  resetComms(&ignored, &alsoIgnored);
  uint32_t ns = (uint32_t)((uint64_t)syncCycles * 1000000000ull / cpuHz);
  if (ns > 0xFFFF) ns = 0xFFFF;
  Serial.write((uint8_t)(ns >> 8));
  Serial.write((uint8_t)ns);
  Serial.write(level);
  Serial.write(count);
  for (int i = 0; i < count; i++) {
    uint16_t v = hundredthsOfMicroseconds(frameEdges[i]);
    Serial.write((uint8_t)(v >> 8));
    Serial.write((uint8_t)v);
  }
}

static void lineCheck() {
  sio_hw->gpio_oe_clr = SwimMask | ResetMask;
  delay(20);
  uint8_t swimIdle = lineHigh() ? 1 : 0;
  sio_hw->gpio_oe_set = ResetMask;
  delay(5);
  uint8_t resetHeld = (sio_hw->gpio_in & ResetMask) ? 1 : 0;
  traceEntry();
  uint16_t rise = 0xFFFF;
  if (entryRiseCycles != 0xFFFFFF) {
    uint64_t ns = (uint64_t)entryRiseCycles * 1000000000ull / cpuHz;
    rise = ns > 0xFFFE ? 0xFFFE : (uint16_t)ns;
  }
  sio_hw->gpio_oe_clr = ResetMask;
  delay(20);
  uint8_t resetFree = (sio_hw->gpio_in & ResetMask) ? 1 : 0;
  Serial.write(swimIdle);
  Serial.write(resetHeld);
  Serial.write(resetFree);
  Serial.write((uint8_t)(rise >> 8));
  Serial.write((uint8_t)rise);
  Serial.write(levelAtRelease);
  Serial.write(edgeCount);
  for (int i = 0; i < edgeCount; i++) {
    uint16_t v = tenthsOfMicroseconds(edges[i]);
    Serial.write((uint8_t)(v >> 8));
    Serial.write((uint8_t)v);
  }
}

// The reply's head; after an error status, the failed frame and how long the line was
// last held low (in units of 100 ns, two bytes, high first).
static void reply(uint8_t command, uint8_t status) {
  Serial.write(command);
  Serial.write(status);
  if (status != StatusOk) {
    uint32_t tenths = (uint32_t)((uint64_t)lowCycles * 10000000ull / cpuHz);
    if (tenths > 0xFFFF) tenths = 0xFFFF;
    Serial.write(failedFrame);
    Serial.write((uint8_t)(tenths >> 8));
    Serial.write((uint8_t)tenths);
  }
}

static bool readArguments(uint8_t *buffer, int count) {
  return Serial.readBytes((char *)buffer, count) == (size_t)count;
}

void setup() {
  Serial.begin(115200);
  Serial.setTimeout(1000);

  const uint pins[] = { SwimPin, ResetPin };
  for (uint pin : pins) {
    gpio_init(pin);
    gpio_put(pin, 0);
    gpio_set_dir(pin, GPIO_IN);
    gpio_pull_up(pin);
  }
  pinMode(LED_BUILTIN, OUTPUT);

  systick_hw->rvr = 0x00FFFFFF;
  systick_hw->csr |= 0x5; // enabled, counting CPU clocks

  cpuHz = clock_get_hz(clk_sys);
  hostPeriod = cycles(2750);
  shortLow = cycles(220);
  longLow = cycles(2000);
  defaultPeriod = cycles(2750);
  defaultSampleAt = cycles(1375);
  defaultEdgeWindow = cycles(625);
  resetCalibration();
  commResetLow = cycles(25000);
  calibrateShortest = cycles(12000);
  calibrateLongest = cycles(24000);
  traceWindow = cycles(300000);
  boostHigh = cycles(100);
  interFrameGap = cycles(250);
  answerTimeout = cycles(2000000); // 2 ms
  bitTimeout = cycles(10000);
  riseTimeout = cycles(5000);
  entryLow = cycles(16000);
  entrySlow = cycles(500000);
  entryFast = cycles(250000);
  syncTimeout = cycles(100000);
  afterSync = cycles(1000);
  syncShortest = cycles(4000);  // 16 us is right; allow a target clock well off
  syncLongest = cycles(64000);
  stuckTimeout = cycles(2000000);
  settleTimeout = cycles(2000);  // the line's own rise once let go is well under this
}

void loop() {
  if (Serial.available() <= 0) return;
  uint8_t command = (uint8_t)Serial.read();
  failedFrame = 0;
  lowCycles = 0;
  uint8_t args[4];
  uint8_t data[MaxChunk];
  digitalWrite(LED_BUILTIN, HIGH);

  if (command == 'H') {
    reply(command, StatusOk);
    Serial.write((const uint8_t *)"WREN", 4);
    Serial.write(SketchVersion);
  } else if (command == 'R' && readArguments(args, 1)) {
    if (args[0]) sio_hw->gpio_oe_set = ResetMask;
    else sio_hw->gpio_oe_clr = ResetMask;
    reply(command, StatusOk);
  } else if (command == 'E' || command == 'L') {
    uint32_t syncCycles = 0;
    bool clean = false;
    uint8_t status = command == 'E' ? enterSwim(&syncCycles, &clean) : resetComms(&syncCycles, &clean);
    reply(command, status);
    if (status == StatusOk) {
      uint32_t ns = (uint32_t)((uint64_t)syncCycles * 1000000000ull / cpuHz);
      if (ns > 0xFFFF) ns = 0xFFFF;
      Serial.write((uint8_t)(ns >> 8));
      Serial.write((uint8_t)ns);
      Serial.write(clean ? 1 : 0);
    }
  } else if (command == 'S') {
    reply(command, softReset());
  } else if (command == 'r' && readArguments(args, 4)) {
    int count = args[0];
    uint32_t address = ((uint32_t)args[1] << 16) | ((uint32_t)args[2] << 8) | args[3];
    if (count < 1 || count > MaxChunk) {
      reply(command, StatusBadCommand);
    } else {
      uint8_t status = readMemory(count, address, data);
      reply(command, status);
      if (status == StatusOk) Serial.write(data, count);
    }
  } else if (command == 'w' && readArguments(args, 4)) {
    int count = args[0];
    uint32_t address = ((uint32_t)args[1] << 16) | ((uint32_t)args[2] << 8) | args[3];
    if (count < 1 || count > MaxChunk || !readArguments(data, count)) {
      reply(command, StatusBadCommand);
    } else {
      reply(command, writeMemory(count, address, data));
    }
  } else if (command == 'T' && readArguments(args, 3)) {
    reply(command, StatusOk);
    readTrace(((uint32_t)args[0] << 16) | ((uint32_t)args[1] << 8) | args[2]);
  } else if (command == 'F' && readArguments(args, 1)) {
    reply(command, StatusOk);
    frameTrace(args[0]);
  } else if (command == 'D') {
    reply(command, StatusOk);
    lineCheck();
  } else if (command == 'X') {
    sio_hw->gpio_oe_clr = SwimMask | ResetMask;
    reply(command, StatusOk);
  } else {
    reply(command, StatusBadCommand);
  }

  Serial.flush();
  digitalWrite(LED_BUILTIN, LOW);
}
