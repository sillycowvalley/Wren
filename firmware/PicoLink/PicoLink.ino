// PicoLink: Wren's SWIM programmer for a Raspberry Pi Pico or a Pico-pinout RP2040 board
// (such as the Waveshare RP2040 Plus).
//
// Build it in the Arduino IDE with Earle Philhower's "Raspberry Pi Pico/RP2040/RP2350" core.
// The wiring is docs/Infographics/Pico_Programmer_Wiring.png, the same as for pico-swimlib:
//   GP16     -> the Thing's SWIM, with a 470 Ohm pull-up from SWIM to 3V3(OUT)
//   GP17     -> the Thing's NRST
//   GND      -> GND
//   3V3(OUT) -> the Thing's VCC (the Thing must run at 3.3 V: RP2040 pins are not 5 V tolerant)
//
// SWIM is timed by the RP2040's PIO with the method of Richard Hulme's pico-swimlib
// (https://github.com/rhulme/pico-swimlib, BSD 3-Clause; its notice is below, with its PIO
// program), which works on a real Thing:
// - one PIO program both sends bits (each as a low and a high time) and reads the target's
//   (by measuring how long each low lasts), with no gap between the two;
// - low-speed bits are exactly 2/20 and 20/2 SWIM clocks; the line is pure open drain;
// - the host feeds the last frame's ACK read and the target's next frame read before they
//   are needed, so the PIO goes straight from one to the next.
// The serial protocol is Wren's own (src/Wren.Link/PicoLink.cs): each request is a command
// byte and its arguments; each reply is the command echoed, a status, then data, or after an
// error the frame that failed and two zero bytes.

#include <hardware/clocks.h>
#include <hardware/gpio.h>
#include <hardware/pio.h>
#include <hardware/sync.h>

// ---------------------------------------------------------------------------------------
// pico-swimlib's PIO program (swim.pio), assembled.
//
// Copyright (c) 2024 Richard Hulme
// SPDX-License-Identifier: BSD-3-Clause
//
// Redistribution and use in source and binary forms, with or without modification, are
// permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright notice, this list of
//    conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright notice, this list of
//    conditions and the following disclaimer in the documentation and/or other materials
//    provided with the distribution.
// 3. Neither the name of the copyright holder nor the names of its contributors may be used
//    to endorse or promote products derived from this software without specific prior
//    written permission.
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS
// OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
// COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
// EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
// GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED
// AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
// NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
// ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
//   write:  out y, 32        side 0    ; number of bits to write
//   loop:   out x, 16 [1]              ; low time
//   low:    jmp x--, low     side 1    ; pin an output (low)
//           out x, 16 [2]              ; high time
//   high:   jmp x--, high    side 0    ; pin an input (high)
//           jmp y--, loop
//   read:   out y, 32                  ; (wrap target) number of bits to read
//   bit:    mov x, !null
//           wait 0 pin 0
//   count:  jmp pin, done
//           jmp x--, count
//   done:   in x, 32                   ; the remaining count: how long the pin was low
//           jmp y--, bit               ; (wrap)
static const uint16_t swimProgramInstructions[] = {
  0x7040, 0x6130, 0x1842, 0x6230, 0x1044, 0x0081,
  0x6040, 0xa02b, 0x2020, 0x00cb, 0x0049, 0x4020, 0x0087,
};
const uint SwimWriteOffset = 0, SwimWrapTarget = 6, SwimWrap = 12;
// The PIO runs at 4 cycles per SWIM clock (8 MHz); the read loop takes 2 cycles a turn.
const uint32_t PioClockRate = 4 * 8000000;
// ---------------------------------------------------------------------------------------

const uint SwimPin = 16;
const uint ResetPin = 17;
const uint8_t SketchVersion = 15;
const int MaxChunk = 64;
const int Attempts = 16;
const uint32_t TimeoutUs = 50000;

enum : uint8_t { StatusOk = 0, StatusNoAnswer = 1, StatusNack = 2, StatusParity = 3,
                 StatusBadFrame = 4, StatusNoSync = 5, StatusBadCommand = 6 };

// SWIM commands: three bits after the 0 header bit.
const uint32_t SwimSrst = 0, SwimRotf = 1, SwimWotf = 2;

static PIO pio = pio0;
static uint sm;
static uint programOffset;
static uint8_t failedFrame;

// A PIO word for one bit: a low time then a high time, each in SWIM clocks.
static uint32_t timing(uint32_t low, uint32_t high) { return ((4 * low - 4) << 16) | (4 * high - 4); }
static uint32_t count(uint32_t bits) { return bits - 1; }
static uint32_t microseconds(uint32_t us) { return us * 8; }
static const uint32_t One = ((4 * 2 - 4) << 16) | (4 * 20 - 4);
static const uint32_t Zero = ((4 * 20 - 4) << 16) | (4 * 2 - 4);

// A read word holds the loop's remaining count: from it, the SWIM clocks the pin was low.
static uint32_t lowClocks(uint32_t word) { return (~word) / 2; }
static bool bitOf(uint32_t word) { return lowClocks(word) <= 8; }

static void setupPio() {
  pio_program_t swimProgram = {};  // field by field: newer SDKs add fields to this structure
  swimProgram.instructions = swimProgramInstructions;
  swimProgram.length = 13;
  swimProgram.origin = -1;
  programOffset = pio_add_program(pio, &swimProgram);
  sm = pio_claim_unused_sm(pio, true);
  pio_sm_config c = pio_get_default_sm_config();
  sm_config_set_wrap(&c, programOffset + SwimWrapTarget, programOffset + SwimWrap);
  sm_config_set_sideset(&c, 2, true, true);       // one optional side-set bit, on pin directions
  sm_config_set_in_shift(&c, true, true, 32);      // right, autopush at 32
  sm_config_set_out_shift(&c, false, true, 32);    // left, autopull at 32
  sm_config_set_in_pins(&c, SwimPin);
  sm_config_set_jmp_pin(&c, SwimPin);
  sm_config_set_set_pins(&c, SwimPin, 1);
  sm_config_set_sideset_pins(&c, SwimPin);
  gpio_init(SwimPin);
  pio_gpio_init(pio, SwimPin);
  pio_sm_set_consecutive_pindirs(pio, sm, SwimPin, 1, false);  // an input, with its output low,
  pio_sm_set_pins_with_mask(pio, sm, 0, 1u << SwimPin);        // so turning it to an output pulls low
  sm_config_set_clkdiv(&c, clock_get_hz(clk_sys) / (float)PioClockRate);
  pio_sm_init(pio, sm, programOffset + SwimWriteOffset, &c);
  pio_sm_set_enabled(pio, sm, true);
}

// Stops whatever the state machine was doing, lets the line go and waits to be fed a write.
static void restartWrite() {
  pio_sm_set_enabled(pio, sm, false);
  pio_sm_clear_fifos(pio, sm);
  pio_sm_restart(pio, sm);
  pio_sm_set_consecutive_pindirs(pio, sm, SwimPin, 1, false);
  pio_sm_exec(pio, sm, pio_encode_jmp(programOffset + SwimWriteOffset));
  pio_sm_set_enabled(pio, sm, true);
}

// Feeds words to the state machine with interrupts off, so that the words after a frame
// (its ACK read, the target's next frame) are always there before they are needed.
static void feed(const uint32_t *words, int n) {
  uint32_t saved = save_and_disable_interrupts();
  for (int i = 0; i < n; i++) pio_sm_put_blocking(pio, sm, words[i]);
  restore_interrupts(saved);
}

static bool take(uint32_t *word, uint32_t timeoutUs) {
  uint32_t start = time_us_32();
  while (pio_sm_is_rx_fifo_empty(pio, sm)) {
    if (time_us_32() - start > timeoutUs) return false;
  }
  *word = pio_sm_get(pio, sm);
  return true;
}

// Sends a frame (the 0 header, `bits` bits of value, parity) and reads the ACK; then, if
// readAfter, the target's frame that follows at once (10 words into rx). A NACK sends the
// frame again.
static uint8_t sendFrame(uint32_t value, int bits, bool readAfter, uint32_t *rx) {
  for (int attempt = 0; attempt < Attempts; attempt++) {
    uint32_t words[14];
    int n = 0;
    words[n++] = count(bits + 2);
    words[n++] = Zero;
    int parity = 0;
    for (int i = bits - 1; i >= 0; i--) {
      int bit = (value >> i) & 1;
      parity ^= bit;
      words[n++] = bit ? One : Zero;
    }
    words[n++] = parity ? One : Zero;
    words[n++] = count(1);
    if (readAfter) words[n++] = count(10);
    restartWrite();
    feed(words, n);
    uint32_t ack;
    if (!take(&ack, TimeoutUs)) { restartWrite(); return StatusNoAnswer; }
    if (!bitOf(ack)) continue;  // NACK: send it again
    if (readAfter) {
      for (int i = 0; i < 10; i++) {
        if (!take(&rx[i], TimeoutUs)) { restartWrite(); return StatusNoAnswer; }
      }
    }
    return StatusOk;
  }
  return StatusNack;
}

// Decodes a target frame: a 1 header, eight data bits, parity.
static uint8_t decodeFrame(const uint32_t *rx, uint8_t *value) {
  if (!bitOf(rx[0])) return StatusBadFrame;
  int parity = 0;
  uint8_t v = 0;
  for (int i = 1; i <= 8; i++) {
    int bit = bitOf(rx[i]) ? 1 : 0;
    v = (uint8_t)((v << 1) | bit);
    parity ^= bit;
  }
  if (parity != (bitOf(rx[9]) ? 1 : 0)) return StatusParity;
  *value = v;
  return StatusOk;
}

static uint8_t sendHeader(uint32_t command, int n, uint32_t address, bool readAfter, uint32_t *rx) {
  failedFrame = 1;
  uint8_t status = sendFrame(command, 3, false, nullptr);
  if (status == StatusOk) { failedFrame = 2; status = sendFrame((uint32_t)n, 8, false, nullptr); }
  if (status == StatusOk) { failedFrame = 3; status = sendFrame((address >> 16) & 0xFF, 8, false, nullptr); }
  if (status == StatusOk) { failedFrame = 4; status = sendFrame((address >> 8) & 0xFF, 8, false, nullptr); }
  if (status == StatusOk) { failedFrame = 5; status = sendFrame(address & 0xFF, 8, readAfter, rx); }
  return status;
}

static uint8_t readMemory(int n, uint32_t address, uint8_t *data) {
  uint32_t rx[10];
  uint8_t status = sendHeader(SwimRotf, n, address, true, rx);
  int attempts = 0;
  for (int i = 0; i < n && status == StatusOk; ) {
    failedFrame = (uint8_t)(6 + i);
    uint8_t got = decodeFrame(rx, &data[i]);
    bool good = got == StatusOk;
    if (!good && ++attempts >= Attempts) { status = got; break; }
    bool more = !good || i + 1 < n;
    // The ACK (or NACK) bit, then the target's next frame (or the same one again).
    uint32_t words[3] = { count(1), good ? One : Zero, count(10) };
    restartWrite();
    feed(words, more ? 3 : 2);
    if (good) { i++; attempts = 0; }
    if (more) {
      for (int k = 0; k < 10; k++) {
        if (!take(&rx[k], TimeoutUs)) { restartWrite(); return StatusNoAnswer; }
      }
    }
  }
  return status;
}

static uint8_t writeMemory(int n, uint32_t address, const uint8_t *data) {
  uint8_t status = sendHeader(SwimWotf, n, address, false, nullptr);
  for (int i = 0; i < n && status == StatusOk; i++) {
    failedFrame = (uint8_t)(6 + i);
    status = sendFrame(data[i], 8, false, nullptr);
  }
  return status;
}

// The entry sequence: low for 16 us, four pulses at 1 kHz, four at 2 kHz, then the line let
// go; the target answers with a sync pulse, 128 SWIM clocks low. As pico-swimlib does it:
// each word is a low then a high time, so the first high belongs to the 1 kHz pulses.
static uint8_t enterSwim(uint32_t *syncNs) {
  uint32_t words[11];
  int n = 0;
  words[n++] = count(9);
  words[n++] = timing(microseconds(16), microseconds(500));
  for (int i = 0; i < 8; i++)
    words[n++] = timing(microseconds(i < 4 ? 500 : 250), microseconds(i < 3 ? 500 : i < 7 ? 250 : 1));
  words[n++] = count(1);
  restartWrite();
  feed(words, n);
  uint32_t sync;
  failedFrame = 0;
  if (!take(&sync, TimeoutUs)) { restartWrite(); return StatusNoSync; }
  *syncNs = lowClocks(sync) * 125;
  return StatusOk;
}

static void reply(uint8_t command, uint8_t status) {
  Serial.write(command);
  Serial.write(status);
  if (status != StatusOk) {
    Serial.write(failedFrame);
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
  setupPio();
}

void loop() {
  if (Serial.available() <= 0) return;
  uint8_t command = (uint8_t)Serial.read();
  uint8_t args[4];
  uint8_t data[MaxChunk];
  failedFrame = 0;

  if (command == 'H') {
    reply(command, StatusOk);
    Serial.write((const uint8_t *)"WREN", 4);
    Serial.write(SketchVersion);
  } else if (command == 'R' && readArguments(args, 1)) {
    setReset(args[0]);
    reply(command, StatusOk);
  } else if (command == 'E') {
    uint32_t ns = 0;
    uint8_t status = enterSwim(&ns);
    reply(command, status);
    if (status == StatusOk) {
      if (ns > 0xFFFF) ns = 0xFFFF;
      Serial.write((uint8_t)(ns >> 8));
      Serial.write((uint8_t)ns);
    }
  } else if (command == 'S') {
    failedFrame = 1;
    reply(command, sendFrame(SwimSrst, 3, false, nullptr));
  } else if (command == 'r' && readArguments(args, 4)) {
    int n = args[0];
    uint32_t address = ((uint32_t)args[1] << 16) | ((uint32_t)args[2] << 8) | args[3];
    if (n < 1 || n > MaxChunk) {
      reply(command, StatusBadCommand);
    } else {
      uint8_t status = readMemory(n, address, data);
      reply(command, status);
      if (status == StatusOk) Serial.write(data, n);
    }
  } else if (command == 'w' && readArguments(args, 4)) {
    int n = args[0];
    uint32_t address = ((uint32_t)args[1] << 16) | ((uint32_t)args[2] << 8) | args[3];
    if (n < 1 || n > MaxChunk || !readArguments(data, n)) {
      reply(command, StatusBadCommand);
    } else {
      reply(command, writeMemory(n, address, data));
    }
  } else if (command == 'X') {
    restartWrite();
    setReset(0xFF);
    reply(command, StatusOk);
  } else {
    reply(command, StatusBadCommand);
  }
  Serial.flush();
}
