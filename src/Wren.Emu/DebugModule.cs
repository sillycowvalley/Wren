namespace Wren.Emu;

/// <summary>
/// The STM8's debug module (UM0470 chapter 4), as far as a debugger over SWIM uses it:
/// the two breakpoint registers (DM_BKR1/2, $7F90-$7F95), DM_CR1's data-read conditions
/// (on BK1 or BK2, and BK1 &lt;= address &lt;= BK2), DM_CSR1's flags, DM_CSR2's STALL and
/// DM_ENFCTR's freezing of TIM4 and I2C while stalled. It acts only while SWIM is active
/// (UM0470 4.3.5); a reset with SWIM active leaves the CPU stalled at the reset vector (4.3.1).
/// </summary>
public sealed class DebugModule
{
    public const int Base = 0x7F90, Size = 11;
    private const int Cr1 = 6, Cr2 = 7, Csr1 = 8, Csr2 = 9, Enfctr = 10;
    public const byte Stall = 0x08, Bk1Flag = 0x02, Bk2Flag = 0x04, BreakOnRead = 0x04;

    private readonly byte[] regs = new byte[Size];
    private bool pendingBreak;

    public DebugModule() => Reset(false);

    /// <summary>Set by the programmer while SWIM is active with SWIM_CSR.SWIM_DM.</summary>
    public bool SwimActive { get; set; }

    /// <summary>True while the CPU is stalled by the debug module.</summary>
    public bool Stalled => SwimActive && (regs[Csr2] & Stall) != 0;

    /// <summary>TIM4 runs on while stalled unless DM_ENFCTR's ENFCT0 is clear.</summary>
    public bool FreezesTim4 => Stalled && (regs[Enfctr] & 0x01) == 0;

    /// <summary>I2C runs on while stalled unless DM_ENFCTR's ENFCT7 is clear.</summary>
    public bool FreezesI2c => Stalled && (regs[Enfctr] & 0x80) == 0;

    /// <summary>A chip reset: the registers return to their reset values; with SWIM active the CPU waits at the reset vector.</summary>
    public void Reset(bool swimActive)
    {
        for (int i = 0; i < 6; i++) regs[i] = 0xFF;
        regs[Cr1] = regs[Cr2] = 0;
        regs[Csr1] = 0x10;
        regs[Csr2] = swimActive ? Stall : (byte)0;
        regs[Enfctr] = 0xFF;
        pendingBreak = false;
    }

    public byte Read(int offset) => regs[offset];

    public void Write(int offset, byte value)
    {
        switch (offset)
        {
            case Csr1:
                regs[Csr1] = (byte)((regs[Csr1] & 0x1E) | (value & 0x40));      // only STE is writable
                break;
            case Csr2:
                bool stall = (value & Stall) != 0;
                if (!stall && (regs[Csr2] & Stall) != 0) regs[Csr1] &= 0x41;   // flags clear with STALL
                regs[Csr2] = (byte)((value & 0x29) | (stall ? Stall : 0));
                break;
            default:
                regs[offset] = value;
                break;
        }
    }

    private int Bk1 => (regs[0] << 16) | (regs[1] << 8) | regs[2];
    private int Bk2 => (regs[3] << 16) | (regs[4] << 8) | regs[5];

    /// <summary>The CPU read data at address (not an instruction fetch).</summary>
    public void DataRead(int address)
    {
        if (!SwimActive || (regs[Cr1] & BreakOnRead) == 0) return;
        int bc = (regs[Cr1] >> 3) & 7;
        byte flags = 0;
        if (bc == 0b001 && address >= Bk1 && address <= Bk2) flags = Bk1Flag;
        else if (bc == 0b100)
        {
            if (address == Bk1) flags |= Bk1Flag;
            if (address == Bk2) flags |= Bk2Flag;
        }
        if (flags == 0) return;
        regs[Csr1] = (byte)((regs[Csr1] & 0x40) | flags | 0x08);              // BRW: on a read
        pendingBreak = true;
    }

    /// <summary>After each instruction: a data breakpoint stalls the CPU once the instruction ends (UM0470 4.8).</summary>
    public void EndOfInstruction()
    {
        if (!pendingBreak) return;
        pendingBreak = false;
        regs[Csr2] |= Stall;
    }
}
