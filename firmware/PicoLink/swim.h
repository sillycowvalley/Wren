//------------------------------------------------------------------------------
// Copyright (c) 2025 Richard Hulme
//
// SPDX-License-Identifier: BSD-3-Clause
//------------------------------------------------------------------------------
#ifndef __PICO_SWIMLIB_INCL__
#define __PICO_SWIMLIB_INCL__

#include "pico.h"
#include "hardware/pio.h"
#include "stdint.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum
{
    OP_ENTRY,
    OP_COMMRESET,
    OP_SRESET,
    OP_WRITE,
    OP_READ,
}swimOp_t;

static const int SWIM_NO_ERROR      =  0;
static const int SWIM_ERROR_INIT    = -1;
static const int SWIM_ERROR_BUSY    = -2;
static const int SWIM_ERROR_HEADER  = -3;
static const int SWIM_ERROR_PARITY  = -4;
static const int SWIM_ERROR_NAK     = -5;
static const int SWIM_ERROR_TIMEOUT = -6;
static const int SWIM_ERROR_ABORT   = -7;

static const uint8_t  SWIM_INFINITE_RETRIES =     0;
static const uint32_t SWIM_DEFAULT_TIMEOUT  = 50000;    // 50ms

// Asynchronous callback
// Params:
//      operation - The operation that has finished
//      value     - The result of the operation.  Negative if an error occurred.
//                  For read/write operations, the number of bytes read/written.
//                  For entry/communication reset, the length of the
//                  synchronization pulse (in SWIM clock cycles - 8MHz)
//      userData  - User-defined pointer passed to swim_init
typedef void (*swimHandler_t)(swimOp_t operation,
                              int value,
                              void* userData);

// Indicates whether a SWIM transaction is currently in progress
// Returns:
//      SWIM_NO_ERROR   - Not busy
//      SWIM_ERROR_INIT - SWIM code has not been initialised
//      SWIM_ERROR_BUSY - Another transaction is currently in progress
int swim_is_busy();

// Aborts the current transaction
// Params:
//      report - True if callback should be called, False if not
// Returns:
//      SWIM_NO_ERROR   - Transaction aborted
//      SWIM_ERROR_INIT - SWIM code has not been initialised
int swim_abort(bool report);

// Initialise the library
// Starts the PIO state machine and allocates DMA channels for read/write
// Params:
//      handler    - Handler function to call when a transaction ends
//      userData   - User-defined data to pass to handler.  Not interpreted by
//                   SWIM code.
//      timeout_us - Timeout transacations after this many microseconds
//      pio        - The PIO module to use
//      pin        - The GPIO pin to use
// Returns:
//      SWIM_NO_ERROR   - Library initialised
//      SWIM_ERROR_INIT - SWIM code has already been initialised
int swim_init(swimHandler_t handler,
              void* userData,
              uint8_t max_attempts,
              uint32_t timeout_us,
              PIO pio,
              uint pin);

// Release resources used by the library.  If a transaction is in progress,
// it will be aborted immediately.
int swim_deinit();

// Perform a SWIM entry sequence.
// This is steps 1-5 in section 3.2 of UM0470 (init pulses and measurement of
// synchronization frame).
// Params:
//      timing - Pointer to variable to receive the length of the
//               synchronization frame (in SWIM clock cycles - 8MHz).
//               May be NULL.
// Returns:
//      SWIM_NO_ERROR   - Entry sequence started
//      SWIM_ERROR_INIT - SWIM code has not been initialised.
//      SWIM_ERROR_BUSY - Another transaction is currently in progress
int swim_entry(int* timing);

// Reset communications by pulling data line low for 128 cycles.
// Params:
//      timing - Pointer to variable to receive the length of the
//               synchronization frame (in SWIM clock cycles - 8MHz).
//               May be NULL.
// Returns:
//      SWIM_NO_ERROR   - Sequence started
//      SWIM_ERROR_INIT - SWIM code has not been initialised.
//      SWIM_ERROR_BUSY - Another transaction is currently in progress
int swim_commrst(int* timing);

// Send "System reset" command
// Returns:
//      SWIM_NO_ERROR   - Sequence started
//      SWIM_ERROR_INIT - SWIM code has not been initialised.
//      SWIM_ERROR_BUSY - Another transaction is currently in progress
int swim_srst();

// Read data from target chip
// Params:
//      len  - Number of bytes to read
//      addr - Address to read from
//      data - Where read data should be stored
// Returns:
//      SWIM_NO_ERROR   - Sequence started
//      SWIM_ERROR_INIT - SWIM code has not been initialised.
//      SWIM_ERROR_BUSY - Another transaction is currently in progress
int swim_rotf(uint8_t len, uint32_t addr, uint8_t* data);

// Write data to target chip
// Params:
//      len  - Number of bytes to write
//      addr - Address to write to
//      data - Data to send.  This buffer must remain valid until the
//             transaction has completed.
// Returns:
//      SWIM_NO_ERROR   - Sequence started
//      SWIM_ERROR_INIT - SWIM code has not been initialised.
//      SWIM_ERROR_BUSY - Another transaction is currently in progress
int swim_wotf(uint8_t len, uint32_t addr, uint8_t* data);

#ifdef __cplusplus
}
#endif

#endif //  __PICO_SWIMLIB_INCL__