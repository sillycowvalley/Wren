//------------------------------------------------------------------------------
// pico-swimlib's swim.c (https://github.com/rhulme/pico-swimlib, commit 3cf28f9), BSD 3-Clause,
// used by Wren's PicoLink sketch. Changed only to build in an Arduino sketch folder: the
// header is included as "swim.h", and the three settings its CMakeLists.txt passes are
// defined here with its defaults (DMA IRQ 0, an exclusive handler).
//------------------------------------------------------------------------------
#ifndef SWIM_DMA_IRQ
#define SWIM_DMA_IRQ 0
#endif
#ifndef SWIM_EXCLUSIVE_IRQ
#define SWIM_EXCLUSIVE_IRQ 1
#endif
#ifndef SWIM_IRQ_PRIORITY
#define SWIM_IRQ_PRIORITY PICO_SHARED_IRQ_HANDLER_DEFAULT_ORDER_PRIORITY
#endif

//------------------------------------------------------------------------------
// Copyright (c) 2025 Richard Hulme
//
// SPDX-License-Identifier: BSD-3-Clause
//------------------------------------------------------------------------------

#include "hardware/gpio.h"
#include "hardware/dma.h"
#include "pico/time.h"

#include "swim.pio.h"
#include "swim.h"

#define SWIM_BITS(x)        ((uint32_t)(x - 1))
// PIO counter required for 1 clock pulse @ 8MHz (i.e. 0.125uS)
#define SWIM_PULSE(x)       ((uint32_t)(((PIO_CLOCK_RATE / 8000000) * (x)) - 4))
#define SWIM_US(x)          ((x) / 0.125)
#define SWIM_CYCLE(lo, hi)  ((SWIM_PULSE((lo)) << 16) | SWIM_PULSE((hi)))
#define SWIM_ONE            SWIM_CYCLE(2, 20)
#define SWIM_ZERO           SWIM_CYCLE(20, 2)

#define BIT_COUNT_BYTE      (1 + 8 + 1)     // Start + 8 data bits + parity

#define SRST 0
#define ROTF 1
#define WOTF 2

typedef enum
{
    STATE_UNINITIALIZED = 0,
    STATE_IDLE,
    STATE_ENTRY,
    STATE_WRITE,
    STATE_WRITE_READ,
    STATE_WRITE_LAST,
    STATE_READ,
    STATE_READ_ACK,
}swimState_t;

typedef struct
{
    PIO                  pio;
    uint                 sm;
    int                  pioOffset;
    int                  dmaWrite;
    int                  dmaRead;
    uint32_t             bufWrite[13];   // Up to 8 outgoing bits and 1 incoming
    uint32_t             bufRead[12];    // Up to 8 incoming bits
    uint8_t              bitsToWrite;
    uint8_t              bitsToRead;
    uint8_t              cmdBuf[5];      // Buffer for command header
    uint8_t              cmdOffset;      // Current offset within cmdBuf
    uint8_t              cmdLength;      // Size of command header
    uint8_t*             userBuf;
    uint8_t              bufOffset;      // Current offset within userBuf
    uint8_t              bufTotal;       // Total number of bytes to read/write
    volatile swimState_t state;
    uint8_t              attemptMax;
    uint8_t              attemptCount;
    uint32_t             timeout;
    alarm_id_t           alarmID;
    swimHandler_t        handler;
    void*                userData;
    swimOp_t             operation;
}swim_t;

static swim_t gSWIM = {.state = STATE_UNINITIALIZED};

// Forward declarations
static int64_t _alarmHandler(alarm_id_t id, void *user_data);
static bool _startDMA(swim_t* swim, uint32_t writeBits, uint32_t readBits);

//------------------------------------------------------------------------------
static swim_t* _checkInit()
{
    swim_t* swim = (swim_t*)0;

    // Check whether SWIM structure has been initialised
    if(gSWIM.state != STATE_UNINITIALIZED)
        swim = &gSWIM;

    return swim;
}

//------------------------------------------------------------------------------
static uint32_t _getCycles(uint32_t timing)
{
    timing ^= 0xffffffff;

    // Convert PIO timing to SWIM cycles.
    // Each timing loop takes two PIO cycles, so we need to halve the PIO
    // clock rate before calculating how many of those cycles correspond to
    // one SWIM clock pulses (at 8MHz)
    return(timing / ((PIO_CLOCK_RATE / 2) / 8000000));
}

//------------------------------------------------------------------------------
static bool _readBit(uint32_t timing)
{
    // Get timing for single bit
    // <= 8 is a 1, >8 is a zero
    return(_getCycles(timing) <= 8);
}

//------------------------------------------------------------------------------
static int _readByte(const swim_t* swim, uint8_t* val)
{
    // Assumes the PIO SM has already been primed to read 1 + 8 + 1 bits
    const uint32_t* entry = swim->bufRead;
    int result = SWIM_NO_ERROR;
    bool parity = false;
    bool sentParity;
    bool start = _readBit(*(entry++));

    *val = 0;

    // "bitsToRead - 2" to exclude the start and parity bit from the count
    for(int i = 0; i < swim->bitsToRead - 2; i++)
    {
        bool bit = _readBit(*(entry++));
        *val <<= 1;
        *val |= bit ? 0x01 : 0x00;
        parity ^= bit;
    }

    sentParity = _readBit(*entry);

    if(start != 1)
        result = SWIM_ERROR_HEADER;
    else
    if(parity != sentParity)
        result = SWIM_ERROR_PARITY;

    return(result);
}

//------------------------------------------------------------------------------
// Prepare the PIO timings for each bit in the provided value.
// Convert each bit in the provided value to the corresponding pair of
// low/high timings
static uint8_t _prepareTimings(swim_t* swim,
                               uint8_t val,
                               uint8_t bits,
                               bool readByte)
{
    uint8_t retval = 0;
    uint32_t* cycles = swim->bufWrite;

    int idx = 0;

    // Length is payload plus an additional 2 bits (start bit and parity bit)
    cycles[idx++] = SWIM_BITS(bits + 2);

    // Start bit (always zero from host)
    cycles[idx++] = SWIM_ZERO;

    bool parity = false;

    for(int mask = 1 << (bits - 1); mask != 0; mask >>= 1)
    {
        bool bit = !!(val & mask);
        cycles[idx++] = bit ? SWIM_ONE : SWIM_ZERO;
        parity ^= bit;
    }

    // Add parity bit
    cycles[idx++] = parity ? SWIM_ONE : SWIM_ZERO;

    // Read one bit (ACK/NAK)
    cycles[idx++] = SWIM_BITS(1);

    if(readByte)
    {
        // If the other side could start sending data immediately after
        // the ACK, be ready to read a start bit, eight data bits and a
        // parity bit
        cycles[idx++] = SWIM_BITS(BIT_COUNT_BYTE);
    }

    swim->bitsToWrite = idx;
    return retval;
}

//------------------------------------------------------------------------------
static bool _writeNextByte(swim_t* swim)
{
    bool finished = false;
    uint8_t value;
    uint8_t bits;
    bool rotfHeader = (swim->cmdBuf[0] == ROTF);

    if(swim->cmdOffset < swim->cmdLength)
    {
        // Value comes from command buffer
        bits = (swim->cmdOffset == 0) ? 3 : 8;
        value = swim->cmdBuf[swim->cmdOffset++];
    }
    else
    if(!rotfHeader && (swim->bufOffset < swim->bufTotal))
    {
        // Value comes from user buffer
        bits = 8;
        value = swim->userBuf[swim->bufOffset++];
    }

    finished = ((swim->cmdOffset == swim->cmdLength) &&
                (rotfHeader || (swim->bufOffset == swim->bufTotal)));

    // Send value and immediately read a byte if we are issuing a read command
    // and this is the last byte of the command header.
    _prepareTimings(swim, value, bits, rotfHeader && finished);

    swim->attemptCount = 1;
    _startDMA(swim, swim->bitsToWrite, 1);

    return finished;
}

//------------------------------------------------------------------------------
static int _readNextByte(swim_t* swim)
{
    uint8_t value;
    int result = _readByte(swim, &value);
    bool good = (result == SWIM_NO_ERROR);

    uint8_t bitsToRead = BIT_COUNT_BYTE;

    if(good)
    {
        swim->userBuf[swim->bufOffset++] = value;

        // Byte was read successfully.  Get ready to return the number of
        // bytes left to read.
        result = swim->bufTotal - swim->bufOffset;

        // This was the last byte if it was received correctly and we've
        // filled the receive buffer
        if(result == 0)
            bitsToRead = 0;
    }
    else
    if((swim->attemptMax != 0) && (swim->attemptCount++ >= swim->attemptMax))
    {
        // Byte was not good and we've reached our retry limit so don't try
        // to read again.
        bitsToRead = 0;
    }

    // Send the ACK/NAK bit
    swim->bitsToWrite = 0;
    swim->bufWrite[swim->bitsToWrite++] = SWIM_BITS(1);
    swim->bufWrite[swim->bitsToWrite++] = good ? SWIM_ONE : SWIM_ZERO;

    // And read another byte if we're not done
    if(bitsToRead > 0)
        swim->bufWrite[swim->bitsToWrite++] = SWIM_BITS(bitsToRead);

    _startDMA(swim, swim->bitsToWrite, bitsToRead);

    return(result);
}

//------------------------------------------------------------------------------
static bool _startDMA(swim_t* swim, uint32_t writeBits, uint32_t readBits)
{
    swim->bitsToRead = readBits;
    swim->bitsToWrite = writeBits;

    if(writeBits > 0)
    {
        // Prepare read channel (will be triggered by write DMA channel)
        dma_channel_set_write_addr(swim->dmaRead, swim->bufRead, false);
        dma_channel_set_trans_count(swim->dmaRead, readBits, false);

        // Make sure PIO state machine is waiting to write
        pio_sm_exec(swim->pio,
                    swim->sm,
                    pio_encode_jmp(swim->pioOffset + swim_bits_offset_write));

        // Start DMA write channel
        dma_channel_set_read_addr(swim->dmaWrite, swim->bufWrite, false);
        dma_channel_set_trans_count(swim->dmaWrite, writeBits, true);
    }
    else
    {
        // Prepare read channel (will be triggered by write DMA channel)
        dma_channel_set_write_addr(swim->dmaRead, swim->bufRead, false);
        dma_channel_set_trans_count(swim->dmaRead, readBits, true);

    }

    swim->alarmID = add_alarm_in_us(swim->timeout,
                                    _alarmHandler,
                                    swim,
                                    false);
}

//------------------------------------------------------------------------------
static void _callUserHandler(swim_t* swim, int value)
{
    swim->state = STATE_IDLE;
    swim->handler(swim->operation, value, swim->userData);
}

//------------------------------------------------------------------------------
static int64_t _alarmHandler(alarm_id_t id, void *user_data)
{
    swim_t* swim = (swim_t*)user_data;

    if(id == swim->alarmID)
    {
        // Abort the current DMA transfer(s) but don't inform the user as
        // we want to indicate a timeout, not an abort.
        swim_abort(false);

        // Inform the user about the timeout
        _callUserHandler(swim, SWIM_ERROR_TIMEOUT);
    }

    // Don't re-trigger alarm
    return 0;
}

//------------------------------------------------------------------------------
static void _dmaHandler()
{
    swim_t* swim = _checkInit();

    if(swim && dma_irqn_get_channel_status(SWIM_DMA_IRQ, swim->dmaRead))
    {
        // Clear the interrupt request.
        dma_irqn_acknowledge_channel(SWIM_DMA_IRQ, swim->dmaRead);

        // Make sure no timeout occurs
        cancel_alarm(swim->alarmID);
        swim->alarmID = -1;

        bool ack;
        int result;

        switch(swim->state)
        {
            case STATE_ENTRY:
                result = _getCycles(swim->bufRead[0]);
                if(swim->userBuf)
                    *((int*)swim->userBuf) = result;

                _callUserHandler(swim, result);
                break;
            case STATE_WRITE:
            case STATE_WRITE_READ:
            case STATE_WRITE_LAST:
                ack = _readBit(swim->bufRead[0]);
                if(!ack)
                {
                    // NAK
                    if((swim->attemptMax != 0) &&
                        (swim->attemptCount++ >= swim->attemptMax))
                    {
                        // Retries are active and have been exhausted.
                        // Abort and report back.
                        _callUserHandler(swim, SWIM_ERROR_NAK);
                    }
                    else
                    {
                        // Max retries not reached so send byte again.
                        _startDMA(swim,
                                  swim->bitsToWrite,
                                  swim->bitsToRead);
                    }
                }
                else
                {
                    // Bit was acknowledged so reset the attempt counter
                    swim->attemptCount = 1;

                    if(swim->state == STATE_WRITE_READ)
                    {
                        // We've just written the last byte of the header
                        // for a read command, which has been ackowledged,
                        // so we have to start reading immediately.
                        // The number of bits to read has already been
                        // passed to the state machine, we just need to
                        // start the DMA to read them.
                        swim->state = STATE_READ;

                        _startDMA(swim, 0, BIT_COUNT_BYTE);
                    }
                    else
                    if(swim->state == STATE_WRITE_LAST)
                    {
                        // We're done.  Report back
                        _callUserHandler(swim, swim->bufTotal);
                    }
                    else
                    if(_writeNextByte(swim))
                    {
                        // Now transmitting the last byte we have.
                        if(swim->cmdBuf[0] == ROTF)
                        {
                            // This is a 'read' command so make sure we
                            // switch straight to reading when it's been
                            // acknowledged.
                            swim->state = STATE_WRITE_READ;
                        }
                        else
                            swim->state = STATE_WRITE_LAST;
                    }
                }
                break;
            case STATE_READ:
                result = _readNextByte(swim);
                if(result < 0)
                {
                    // An error occurred.
                    _callUserHandler(swim, result);
                }
                else
                if(result == 0)
                {
                    // We read all the data that was requested.
                    // Report back.
                    _callUserHandler(swim, swim->bufTotal);
                }
                break;
            default:
                break;
        }
    }
}

//------------------------------------------------------------------------------
static int _isBusy(swim_t** swim)
{
    int result = SWIM_ERROR_INIT;
    *swim = _checkInit();

    if(*swim)
    {
        if((*swim)->state == STATE_IDLE)
            result = SWIM_NO_ERROR;
        else
            result = SWIM_ERROR_BUSY;
    }

    return result;
}

//------------------------------------------------------------------------------
int swim_is_busy()
{
    swim_t* swim;
    return(_isBusy(&swim));
}

//------------------------------------------------------------------------------
int swim_abort(bool report)
{
    swim_t* swim = _checkInit();

    if(swim)
    {
        // Make sure we don't trigger the DMA handler by aborting the channel
        dma_irqn_set_channel_enabled(SWIM_DMA_IRQ, swim->dmaRead, false);

        // Abort both DMA channels
        dma_channel_abort(swim->dmaWrite);
        dma_channel_abort(swim->dmaRead);

        // Clear the spurious IRQ (if there was one)
        dma_irqn_acknowledge_channel(SWIM_DMA_IRQ, swim->dmaRead);

        // Make sure PIO state machine is waiting to write
        pio_sm_exec(swim->pio,
                    swim->sm,
                    pio_encode_jmp(swim->pioOffset + swim_bits_offset_write));

        // Make sure there's nothing left in the PIO's FIFO
        pio_sm_clear_fifos(swim->pio, swim->sm);

        // Re-enable the channel on the IRQ
        dma_irqn_set_channel_enabled(SWIM_DMA_IRQ, swim->dmaRead, true);

        if(report)
        {
            // Inform the user
            _callUserHandler(swim, SWIM_ERROR_ABORT);
        }
    }

    return(swim ? SWIM_NO_ERROR : SWIM_ERROR_INIT);
}

//------------------------------------------------------------------------------
int swim_init(swimHandler_t handler,
              void* user_data,
              uint8_t max_attemps,
              uint32_t timeout_us,
              PIO pio,
              uint pin)
{
    int result = SWIM_ERROR_INIT;

    if(!_checkInit())
    {
        // Nothing has been initialised yet, so we can proceed.
        swim_t* swim = &gSWIM;

        swim->state = STATE_IDLE;
        swim->handler = handler;
        swim->userData = user_data;
        swim->attemptCount = 1;
        swim->attemptMax = max_attemps;
        swim->timeout = timeout_us,

        swim->sm = pio_claim_unused_sm(pio, false);

        if(swim->sm != -1)
        {
            swim->pio = pio;
            swim->pioOffset = pio_add_program(pio, &swim_bits_program);
            swimbits_sm_init(swim->pio, swim->sm, swim->pioOffset, pin);

            // We need two DMA channels
            swim->dmaWrite = dma_claim_unused_channel(true);
            swim->dmaRead  = dma_claim_unused_channel(true);

            dma_channel_config c;

            // Configure the DMA channel for reading received data
            c = dma_channel_get_default_config(swim->dmaRead);

            channel_config_set_transfer_data_size(&c, DMA_SIZE_32);
            channel_config_set_read_increment(&c, false);
            channel_config_set_write_increment(&c, true);
            channel_config_set_dreq(&c, pio_get_dreq(pio, swim->sm, false));

            dma_channel_configure(
                swim->dmaRead,
                &c,
                swim->bufRead,          // Write to buffer
                &pio->rxf[swim->sm],    // Read from state machine's RX FIFO
                1,                      // Transfer count (will be set later)
                false                   // Don't start yet
            );

            // Now set up the channel for writing data
            c = dma_channel_get_default_config(swim->dmaWrite);

            channel_config_set_transfer_data_size(&c, DMA_SIZE_32);
            channel_config_set_read_increment(&c, true);
            channel_config_set_write_increment(&c, false);
            channel_config_set_dreq(&c, pio_get_dreq(pio, swim->sm, true));

            // Trigger the read channel when done
            channel_config_set_chain_to(&c, swim->dmaRead);

            dma_channel_configure(
                swim->dmaWrite,
                &c,
                &pio->txf[swim->sm],    // Write to state machine's TX FIFO
                swim->bufWrite,         // Read from buffer
                1,                      // Transfer count (will be set later)
                false                   // Don't start yet
            );

            // Configure the processor to run _dmaHandler() when DMA IRQ is
            // asserted
#if SWIM_EXCLUSIVE_IRQ
            irq_set_exclusive_handler(DMA_IRQ_NUM(SWIM_DMA_IRQ), _dmaHandler);
#else
            irq_add_shared_handler(DMA_IRQ_NUM(SWIM_DMA_IRQ),
                                   SWIM_IRQ_PRIORITY,
                                   _dmaHandler);
#endif
            irq_set_enabled(DMA_IRQ_NUM(SWIM_DMA_IRQ), true);

            // Tell the DMA to raise IRQ line when the channel finishes a
            // block
            dma_irqn_set_channel_enabled(SWIM_DMA_IRQ, swim->dmaRead, true);

            result = SWIM_NO_ERROR;
        }
    }

    return result;
}

//------------------------------------------------------------------------------
int swim_deinit()
{
    int result = SWIM_ERROR_INIT;

    swim_t *swim = _checkInit();

    if(swim)
    {
        // Disable and release DMA channels
        dma_irqn_set_channel_enabled(SWIM_DMA_IRQ, swim->dmaRead, false);
        dma_channel_abort(swim->dmaRead);
        dma_channel_abort(swim->dmaWrite);
        dma_channel_unclaim(swim->dmaRead);
        dma_channel_unclaim(swim->dmaWrite);

        // Disable/unhook interrupt handler
        irq_set_enabled(DMA_IRQ_NUM(SWIM_DMA_IRQ), false);
        irq_remove_handler(DMA_IRQ_NUM(SWIM_DMA_IRQ), _dmaHandler);

        // Disable the state machine
        pio_sm_set_enabled(swim->pio, swim->sm, false);

        // Release it
        pio_sm_unclaim(swim->pio, swim->sm);

        // Remove the program from PIO memory
        pio_remove_program(swim->pio, &swim_bits_program, swim->pioOffset);

        swim->state = STATE_UNINITIALIZED;

        result = SWIM_NO_ERROR;
    }

    return result;
}

//------------------------------------------------------------------------------
int swim_entry(int* timing)
{
    swim_t* swim;
    int result = _isBusy(&swim);

    if(result == SWIM_NO_ERROR)
    {
        uint32_t* cycles = swim->bufWrite;

        int idx = 0;

        // We need a low pulse for 16uS, followed by four cycles at 1kHz and
        // four cycles at 2kHz.

        // The PIO code always performs a cycle of low/high but in the entry
        // sequence, the first high period after the 16uS is considered part of
        // 1kHz sequence.  This means the timings for the 1kHz/2kHz cycles are
        // shifted by half a cycle.

        cycles[idx++] = SWIM_BITS(9);   // low for 16us then 8 high/low cycles
        cycles[idx++] = SWIM_CYCLE(SWIM_US(16),SWIM_US(500));

        for(int i = 0; i < 8; i++)
            cycles[idx++] = SWIM_CYCLE(SWIM_US(i < 4 ? 500 : 250),
                                       SWIM_US(i < 3 ? 500 : i < 7 ? 250 : 1));

        // Read 1 Sync pulse
        cycles[idx++] = SWIM_BITS(1);

        swim->state = STATE_ENTRY;
        swim->operation = OP_ENTRY;

        swim->userBuf = (uint8_t*)timing;

        _startDMA(swim, idx, 1);
    }

    return(result);
}

//------------------------------------------------------------------------------
int swim_commrst(int* timing)
{
    swim_t* swim;
    int result = _isBusy(&swim);

    if(result == SWIM_NO_ERROR)
    {
        uint32_t* cycles = swim->bufWrite;

        int idx = 0;

        // We need a low pulse for 128 cycles.

        // The PIO code always performs a cycle of low/high so we'll have
        // a single cycle high.
        cycles[idx++] = SWIM_BITS(1);
        cycles[idx++] = SWIM_CYCLE(128, 1);

        // Read 1 Sync pulse
        cycles[idx++] = SWIM_BITS(1);

        swim->state = STATE_ENTRY;
        swim->operation = OP_COMMRESET;
        swim->userBuf = (uint8_t*)timing;

        _startDMA(swim, idx, 1);
    }

    return(result);
}

//------------------------------------------------------------------------------
int swim_srst()
{
    swim_t* swim;
    int result = _isBusy(&swim);

    if(result == SWIM_NO_ERROR)
    {
        swim->cmdBuf[0] = SRST;
        swim->cmdOffset = 0;
        swim->cmdLength = 1;
        swim->userBuf = 0;
        swim->bufOffset = 0;
        swim->bufTotal = 0;

        // We're only sending a single byte so this is also the last byte.
        swim->state = STATE_WRITE_LAST;
        swim->operation = OP_SRESET;

        _writeNextByte(swim);
    }

    return(result);
}

//------------------------------------------------------------------------------
int swim_rotf(uint8_t len, uint32_t addr, uint8_t* data)
{
    swim_t* swim;
    int result = _isBusy(&swim);

    if(result == SWIM_NO_ERROR)
    {
        int idx = 0;

        swim->cmdBuf[idx++] = ROTF;
        swim->cmdBuf[idx++] = len;
        swim->cmdBuf[idx++] = (addr >> 16) & 0xff;
        swim->cmdBuf[idx++] = (addr >> 8) & 0xff;
        swim->cmdBuf[idx++] = addr & 0xff;
        swim->cmdOffset = 0;
        swim->cmdLength = idx;
        swim->userBuf = data;
        swim->bufOffset = 0;
        swim->bufTotal = len;

        swim->state = STATE_WRITE;
        swim->operation = OP_READ;

        _writeNextByte(swim);
    }

    return(result);
}

//------------------------------------------------------------------------------
int swim_wotf(uint8_t len, uint32_t addr, uint8_t* data)
{
    swim_t* swim;
    int result = _isBusy(&swim);

    if(result == SWIM_NO_ERROR)
    {
        int idx = 0;

        swim->cmdBuf[idx++] = WOTF;
        swim->cmdBuf[idx++] = len;
        swim->cmdBuf[idx++] = (addr >> 16) & 0xff;
        swim->cmdBuf[idx++] = (addr >> 8) & 0xff;
        swim->cmdBuf[idx++] = addr & 0xff;
        swim->cmdOffset = 0;
        swim->cmdLength = idx;
        swim->userBuf = data;
        swim->bufOffset = 0;
        swim->bufTotal = len;

        swim->state = STATE_WRITE;
        swim->operation = OP_WRITE;

        _writeNextByte(swim);
    }

    return(result);
}
