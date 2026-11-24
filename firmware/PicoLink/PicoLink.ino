// PicoLink: Wren's SWIM programmer for a Raspberry Pi Pico or a Pico-pinout RP2040 board
// (such as the Waveshare RP2040 Plus).
//
// Build it in the Arduino IDE with Earle Philhower's "Raspberry Pi Pico/RP2040/RP2350" core
// (version 4 or later: it brings Pico SDK 2), or drag PicoLink.uf2 onto the board.
// The wiring is docs/Infographics/Pico_Programmer_Wiring.png, the same as for pico-swimlib:
//   GP16     -> the Thing's SWIM, with a 470 Ohm pull-up from SWIM to 3V3(OUT)
//   GP17     -> the Thing's NRST
//   GND      -> GND
//   3V3(OUT) -> the Thing's VCC (the Thing must run at 3.3 V: RP2040 pins are not 5 V tolerant)
//
// SWIM itself is Richard Hulme's pico-swimlib (swimlib.c, swim.h, swim.pio.h; BSD 3-Clause,
// LICENSE-pico-swimlib.txt), used as it is: its PIO program sends and reads the bits, and DMA
// under its interrupt chains each frame to the next, as in pico-swimlib's own firmware, which
// works on a real Thing. This sketch only adds Wren's serial protocol and NRST.
// The protocol (src/Wren.Link/PicoLink.cs): each request is a command byte and its arguments;
// each reply is the command echoed, a status, then data, or after an error three zero bytes.

#include <hardware/gpio.h>
#include <hardware/pio.h>
#include "swim.h"

const uint SwimPin = 16;
const uint ResetPin = 17;
const uint8_t SketchVersion = 16;
const int MaxChunk = 64;
const uint8_t Attempts = 3;  // NACKs before giving up, as pico-swimlib's examples use

enum : uint8_t { StatusOk = 0, StatusNoAnswer = 1, StatusNack = 2, StatusParity = 3,
                 StatusBadFrame = 4, StatusNoSync = 5, StatusBadCommand = 6, StatusBusy = 7 };

// What pico-swimlib's handler reported for the last operation.
static volatile bool finished;
static volatile int outcome;

static void swimDone(swimOp_t operation, int value, void *userData) {
  (void)operation;
  (void)userData;
  outcome = value;
  finished = true;
}

// Waits for the operation just started to finish; returns its value (negative: an error).
static int waitFor(int started) {
  if (started != SWIM_NO_ERROR) return started;
  while (!finished) tight_loop_contents();
  finished = false;
  return outcome;
}

static uint8_t statusOf(int value) {
  if (value >= 0) return StatusOk;
  switch (value) {
    case SWIM_ERROR_TIMEOUT: return StatusNoAnswer;
    case SWIM_ERROR_NAK: return StatusNack;
    case SWIM_ERROR_PARITY: return StatusParity;
    case SWIM_ERROR_HEADER: return StatusBadFrame;
    case SWIM_ERROR_BUSY: return StatusBusy;
    default: return StatusBadCommand;
  }
}

static void reply(uint8_t command, uint8_t status) {
  Serial.write(command);
  Serial.write(status);
  if (status != StatusOk) {
    Serial.write((uint8_t)0);
    Serial.write((uint8_t)0);
    Serial.write((uint8_t)0);
  }
}

static bool readArguments(uint8_t *buffer, int n) {
  return Serial.readBytes((char *)buffer, n) == (size_t)n;
}

// NRST: 0 drives it high, 1 low, anything else lets it go (with the pin's pull-up on).
static void setReset(uint8_t state) {
  if (state == 0) { gpio_put(ResetPin, 1); gpio_set_dir(ResetPin, GPIO_OUT); }
  else if (state == 1) { gpio_put(ResetPin, 0); gpio_set_dir(ResetPin, GPIO_OUT); }
  else { gpio_set_dir(ResetPin, GPIO_IN); }
}

void setup() {
  Serial.begin(115200);
  Serial.setTimeout(1000);
  gpio_init(ResetPin);
  gpio_pull_up(ResetPin);
  gpio_set_dir(ResetPin, GPIO_IN);
  swim_init(swimDone, nullptr, Attempts, SWIM_DEFAULT_TIMEOUT, pio0, SwimPin);
}

void loop() {
  if (Serial.available() <= 0) return;
  uint8_t command = (uint8_t)Serial.read();
  uint8_t args[4];
  uint8_t data[MaxChunk];

  if (command == 'H') {
    reply(command, StatusOk);
    Serial.write((const uint8_t *)"WREN", 4);
    Serial.write(SketchVersion);
  } else if (command == 'R' && readArguments(args, 1)) {
    setReset(args[0]);
    reply(command, StatusOk);
  } else if (command == 'E') {
    int timing = 0;
    int value = waitFor(swim_entry(&timing));
    if (value < 0) {
      reply(command, value == SWIM_ERROR_TIMEOUT ? (uint8_t)StatusNoSync : statusOf(value));
    } else {
      uint32_t ns = (uint32_t)value * 125;  // SWIM clocks (8 MHz) to ns
      if (ns > 0xFFFF) ns = 0xFFFF;
      reply(command, StatusOk);
      Serial.write((uint8_t)(ns >> 8));
      Serial.write((uint8_t)ns);
    }
  } else if (command == 'S') {
    reply(command, statusOf(waitFor(swim_srst())));
  } else if (command == 'r' && readArguments(args, 4)) {
    int n = args[0];
    uint32_t address = ((uint32_t)args[1] << 16) | ((uint32_t)args[2] << 8) | args[3];
    if (n < 1 || n > MaxChunk) {
      reply(command, StatusBadCommand);
    } else {
      uint8_t status = statusOf(waitFor(swim_rotf((uint8_t)n, address, data)));
      reply(command, status);
      if (status == StatusOk) Serial.write(data, n);
    }
  } else if (command == 'w' && readArguments(args, 4)) {
    int n = args[0];
    uint32_t address = ((uint32_t)args[1] << 16) | ((uint32_t)args[2] << 8) | args[3];
    if (n < 1 || n > MaxChunk || !readArguments(data, n)) {
      reply(command, StatusBadCommand);
    } else {
      reply(command, statusOf(waitFor(swim_wotf((uint8_t)n, address, data))));
    }
  } else if (command == 'X') {
    setReset(0xFF);
    reply(command, StatusOk);
  } else {
    reply(command, StatusBadCommand);
  }
  Serial.flush();
}
