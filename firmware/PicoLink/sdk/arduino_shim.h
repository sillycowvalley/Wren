#pragma once
#include "pico/stdlib.h"
#include <cstddef>
#include <cstdint>
struct SerialShim {
  int peeked = -1; uint32_t timeoutMs = 1000;
  void begin(int) {} void setTimeout(uint32_t ms) { timeoutMs = ms; }
  int available() { if (peeked < 0) { int c = getchar_timeout_us(0); if (c >= 0) peeked = c; } return peeked >= 0 ? 1 : 0; }
  int read() { if (available()) { int c = peeked; peeked = -1; return c; } return -1; }
  size_t readBytes(char* b, int n) { int k = 0; while (k < n) { int c = (peeked >= 0) ? read() : getchar_timeout_us(timeoutMs * 1000); if (c < 0) break; b[k++] = (char)c; } return (size_t)k; }
  void write(uint8_t c) { putchar_raw(c); }
  void write(const uint8_t* b, int n) { for (int i = 0; i < n; i++) putchar_raw(b[i]); }
  void flush() { stdio_flush(); }
};
static SerialShim Serial;
