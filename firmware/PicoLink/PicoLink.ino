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
// transfer is on the wire. Wren (src/Wren.Link/PicoLink.cs) sends the requests; the protocol
// is described there. SWIM itself is described in ST's UM0470.

#include <hardware/clocks.h>
#include <hardware/gpio.h>
#include <hardware/structs/sio.h>
#include <hardware/structs/systick.h>
#include <hardware/sync.h>

const uint SwimPin = 2;
const uint ResetPin = 3;
const uint32_t SwimMask = 1u << SwimPin;
const uint32_t ResetMask = 1u << ResetPin;
const uint8_t SketchVersion = 1;
const int MaxChunk = 64;

// Reply statuses (as in PicoLink.cs).
enum : uint8_t { StatusOk = 0, StatusNoAnswer = 1, StatusNack = 2, StatusParity = 3,
                 StatusBadFrame = 4, StatusNoSync = 5, StatusBadCommand = 6 };

// SWIM commands: a 0 start bit then three bits.
const uint32_t SwimSrst = 0, SwimRotf = 1, SwimWotf = 2;

// Times in CPU cycles, worked out from the clock in setup().
static uint32_t bitPeriod, shortLow, longLow, sampleAt;
static uint32_t answerTimeout, bitTimeout, riseTimeout;
static uint32_t entryLow, entrySlow, entryFast, syncTimeout, afterSync;
static uint32_t cpuHz;

static uint32_t cycles(uint32_t ns) { return (uint32_t)((uint64_t)cpuHz * ns / 1000000000ull); }

// SysTick counts down from 0xFFFFFF at the CPU clock; now() counts up, modulo 2^24.
static __force_inline uint32_t now() { return ~systick_hw->cvr & 0xFFFFFF; }
static __force_inline uint32_t since(uint32_t t) { return (now() - t) & 0xFFFFFF; }
static __force_inline void waitUntil(uint32_t t, uint32_t c) { while (since(t) < c) { } }

// Open drain: the output value is 0, so enabling the output pulls low and disabling it lets go.
static __force_inline void driveLow() { sio_hw->gpio_oe_set = SwimMask; }
static __force_inline void letGo() { sio_hw->gpio_oe_clr = SwimMask; }
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
  letGo();
  waitUntil(t, bitPeriod);
}

// Reads one bit from the target: 1 or 0, or -1 if the line never fell. *at is its start.
static int __not_in_flash_func(readBit)(uint32_t timeout, uint32_t *at) {
  if (!waitFall(timeout, at)) return -1;
  waitUntil(*at, sampleAt);
  return lineHigh() ? 1 : 0;
}

// Sends a frame (its bits, most significant first, starting with the 0 start bit) and its
// parity, then reads the target's ACK; a NACK sends the frame again.
static uint8_t __not_in_flash_func(sendFrame)(uint32_t value, int bits) {
  for (int attempt = 0; attempt < 16; attempt++) {
    uint32_t t = now();
    int parity = 0;
    for (int i = bits - 1; i >= 0; i--) {
      int bit = (value >> i) & 1;
      parity ^= bit;
      sendBit(t, bit);
      t += bitPeriod;
    }
    sendBit(t, parity);
    uint32_t at;
    int ack = readBit(answerTimeout, &at);
    if (ack < 0) return StatusNoAnswer;
    waitUntil(at, bitPeriod);
    if (!waitRise(riseTimeout)) return StatusBadFrame;
    if (ack) return StatusOk;
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
    uint32_t value = 0;
    for (int i = 0; i < 9; i++) {
      if (!waitRise(riseTimeout)) return StatusBadFrame;
      bit = readBit(bitTimeout, &at);
      if (bit < 0) return StatusBadFrame;
      value = (value << 1) | (uint32_t)bit;
    }
    int parity = 0;
    for (int i = 1; i <= 8; i++) parity ^= (value >> i) & 1;
    bool good = parity == (int)(value & 1);
    waitUntil(at, bitPeriod);
    sendBit(now(), good ? 1 : 0);
    if (good) {
      *out = (uint8_t)(value >> 1);
      return StatusOk;
    }
  }
  return StatusParity;
}

// A command with its count and 24-bit address (ROTF and WOTF).
static uint8_t __not_in_flash_func(sendHeader)(uint32_t command, int count, uint32_t address) {
  uint8_t status = sendFrame(command, 4);
  if (status == StatusOk) status = sendFrame((uint32_t)count, 9);
  if (status == StatusOk) status = sendFrame((address >> 16) & 0xFF, 9);
  if (status == StatusOk) status = sendFrame((address >> 8) & 0xFF, 9);
  if (status == StatusOk) status = sendFrame(address & 0xFF, 9);
  return status;
}

static uint8_t __not_in_flash_func(readMemory)(int count, uint32_t address, uint8_t *data) {
  uint32_t saved = save_and_disable_interrupts();
  uint8_t status = sendHeader(SwimRotf, count, address);
  for (int i = 0; i < count && status == StatusOk; i++) status = receiveFrame(&data[i]);
  restore_interrupts(saved);
  return status;
}

static uint8_t __not_in_flash_func(writeMemory)(int count, uint32_t address, const uint8_t *data) {
  uint32_t saved = save_and_disable_interrupts();
  uint8_t status = sendHeader(SwimWotf, count, address);
  for (int i = 0; i < count && status == StatusOk; i++) status = sendFrame(data[i], 9);
  restore_interrupts(saved);
  return status;
}

static uint8_t __not_in_flash_func(softReset)() {
  uint32_t saved = save_and_disable_interrupts();
  uint8_t status = sendFrame(SwimSrst, 4);
  restore_interrupts(saved);
  return status;
}

// The entry sequence: low for 16 us, four pulses at 1 kHz, four at 2 kHz; then the target
// answers with a sync pulse 128 SWIM clocks (16 us) long. *syncCycles is how long it was.
static uint8_t __not_in_flash_func(enterSwim)(uint32_t *syncCycles) {
  uint32_t saved = save_and_disable_interrupts();
  uint32_t t = now();
  driveLow();
  waitUntil(t, entryLow);
  for (int i = 0; i < 8; i++) {
    uint32_t half = i < 4 ? entrySlow : entryFast;
    t = now();
    letGo();
    waitUntil(t, half);
    t = now();
    driveLow();
    waitUntil(t, half);
  }
  letGo();
  uint32_t at;
  uint8_t status = StatusNoSync;
  if (waitFall(syncTimeout, &at) && waitRise(syncTimeout)) {
    *syncCycles = since(at);
    t = now();
    waitUntil(t, afterSync);
    status = StatusOk;
  }
  restore_interrupts(saved);
  return status;
}

static void reply(uint8_t command, uint8_t status) {
  Serial.write(command);
  Serial.write(status);
}

static bool readArguments(uint8_t *buffer, int count) {
  return Serial.readBytes((char *)buffer, count) == (size_t)count;
}

void setup() {
  Serial.begin(115200);
  Serial.setTimeout(1000);

  for (uint pin : { SwimPin, ResetPin }) {
    gpio_init(pin);
    gpio_put(pin, 0);
    gpio_set_dir(pin, GPIO_IN);
    gpio_pull_up(pin);
  }
  pinMode(LED_BUILTIN, OUTPUT);

  systick_hw->rvr = 0x00FFFFFF;
  systick_hw->csr |= 0x5; // enabled, counting CPU clocks

  cpuHz = clock_get_hz(clk_sys);
  bitPeriod = cycles(2750);
  shortLow = cycles(250);
  longLow = cycles(2500);
  sampleAt = cycles(1125);
  answerTimeout = cycles(2000000); // 2 ms
  bitTimeout = cycles(10000);
  riseTimeout = cycles(5000);
  entryLow = cycles(16000);
  entrySlow = cycles(500000);
  entryFast = cycles(250000);
  syncTimeout = cycles(100000);
  afterSync = cycles(1000);
}

void loop() {
  if (Serial.available() <= 0) return;
  uint8_t command = (uint8_t)Serial.read();
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
  } else if (command == 'E') {
    uint32_t syncCycles = 0;
    uint8_t status = enterSwim(&syncCycles);
    reply(command, status);
    if (status == StatusOk) {
      uint32_t ns = (uint32_t)((uint64_t)syncCycles * 1000000000ull / cpuHz);
      if (ns > 0xFFFF) ns = 0xFFFF;
      Serial.write((uint8_t)(ns >> 8));
      Serial.write((uint8_t)ns);
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
  } else if (command == 'X') {
    sio_hw->gpio_oe_clr = SwimMask | ResetMask;
    reply(command, StatusOk);
  } else {
    reply(command, StatusBadCommand);
  }

  Serial.flush();
  digitalWrite(LED_BUILTIN, LOW);
}
