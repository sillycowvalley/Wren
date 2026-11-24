namespace Wren.Link;

/// <summary>Where a block write goes: program flash, data EEPROM.</summary>
public enum TargetMemory { Flash, Eeprom }

/// <summary>
/// An STM8S over SWIM, through an ST-LINK or a Pico: connect (the CPU stalled), read and write memory,
/// program 64-byte blocks of flash or EEPROM, option bytes, and reset.
/// Register addresses are the STM8S003/S103's (RM0016, UM0470).
/// </summary>
public sealed class SwimTarget
{
    public const int SwimCsr = 0x7F80, DmCsr2 = 0x7F99;
    public const int ClkCkdivr = 0x50C6;
    public const int FlashCr2 = 0x505B, FlashNcr2 = 0x505C, FlashIapsr = 0x505F, FlashPukr = 0x5062, FlashDukr = 0x5064;
    public const int BlockSize = 64;

    private const byte IapsrWrPgDis = 0x01, IapsrEop = 0x04;

    public SwimTarget(ITargetLink link) => Link = link;

    /// <summary>How long Connect waited, in milliseconds, for NO_ACCESS to clear.</summary>
    public int WaitedForAccess { get; private set; }

    /// <summary>SWIM_CSR as read at the end of Connect.</summary>
    public byte ConnectedCsr { get; private set; }

    /// <summary>
    /// SWIM_CSR's NO_ACCESS bit at Connect: the chip refuses memory access, because read-out
    /// protection is on (or it is halted). SWIM_CSR itself can still be read and written.
    /// </summary>
    public bool NoAccess => (ConnectedCsr & 0x40) != 0;

    public ITargetLink Link { get; }

    /// <summary>
    /// Enters SWIM: NRST held low, the entry sequence, SWIM_CSR set (interrupts masked, all of
    /// memory reachable, SWIM first, CPU stalled), NRST released, a communication reset so
    /// SWIM runs on the trimmed clock, then high-speed SWIM if
    /// the target offers it and the programmer can. The CPU stays stalled until Run.
    /// </summary>
    public void Connect()
    {
        WaitedForAccess = 0;
        Link.EnterSwimMode();
        Link.AssertReset();
        Link.EnterSequence();
        WriteByte(SwimCsr, 0xA1);
        Link.DeassertReset();
        Link.Sleep(1);
        Link.ResetSwim(); // the target's clock is trimmed now: resynchronise (UM0470 3.2 step 9)

        byte csr = 0;
        for (int i = 0; i < 10; i++)
        {
            csr = ReadByte(SwimCsr);
            if ((csr & 0x02) != 0) break; // HSIT: high speed is possible
            Link.Sleep(1);
        }
        // NO_ACCESS while the chip is still starting up (NRST rising slowly through its
        // capacitor, option bytes not loaded, HSIT clear) is not yet read-out protection:
        // give it up to half a second to clear.
        for (int i = 0; i < 50 && (csr & 0x40) != 0; i++)
        {
            Link.Sleep(10);
            csr = ReadByte(SwimCsr);
            WaitedForAccess += 10;
        }
        ConnectedCsr = csr;
        Link.ResetSwim();
        if ((csr & 0x02) != 0 && Link.OffersHighSpeed)
        {
            WriteByte(SwimCsr, (byte)(csr | 0x10)); // HS
            Link.SetHighSpeed(true);
        }
    }

    /// <summary>Resets the target through SWIM and leaves SWIM, so the firmware runs.</summary>
    public void Run()
    {
        WriteByte(SwimCsr, (byte)(ReadByte(SwimCsr) | 0x04)); // RST
        Link.GenerateReset();
        Link.LeaveSwimMode();
    }

    public byte ReadByte(int address) => Link.ReadMemory(address, 1)[0];

    public byte[] Read(int address, int length) => Link.ReadMemory(address, length);

    public void WriteByte(int address, byte value) => Link.WriteMemory(address, new[] { value });

    /// <summary>
    /// Programs whole 64-byte blocks (address block-aligned, data a multiple of 64 bytes).
    /// Each block is one program/erase cycle.
    /// </summary>
    public void WriteBlocks(int address, byte[] data, TargetMemory memory)
    {
        if (address % BlockSize != 0 || data.Length % BlockSize != 0)
            throw new ArgumentException("Blocks must be whole and aligned to 64 bytes");

        WriteByte(ClkCkdivr, 0x00); // 16 MHz
        Unlock(memory);
        try
        {
            for (int offset = 0; offset < data.Length; offset += BlockSize)
            {
                WriteByte(DmCsr2, (byte)(ReadByte(DmCsr2) | 0x08)); // keep the CPU stalled
                WriteByte(FlashCr2, 0x01);                           // standard block programming
                WriteByte(FlashNcr2, 0xFE);
                Link.WriteMemory(address + offset, data.AsSpan(offset, BlockSize));
                WaitForEndOfProgramming(6, address + offset);
            }
        }
        finally
        {
            Lock();
        }
    }

    /// <summary>Writes one option byte (with its complement written by the caller where one exists).</summary>
    public void WriteOptionByte(int address, byte value)
    {
        Unlock(TargetMemory.Eeprom);
        try
        {
            WriteByte(FlashCr2, 0x80); // OPT
            WriteByte(FlashNcr2, 0x7F);
            WriteByte(address, value);
            WaitForEndOfProgramming(6, address);
        }
        finally
        {
            WriteByte(FlashCr2, 0x00);
            WriteByte(FlashNcr2, 0xFF);
            Lock();
        }
    }

    private void Unlock(TargetMemory memory)
    {
        if (memory == TargetMemory.Flash)
        {
            WriteByte(FlashPukr, 0x56);
            WriteByte(FlashPukr, 0xAE);
        }
        else
        {
            WriteByte(FlashDukr, 0xAE);
            WriteByte(FlashDukr, 0x56);
        }
    }

    private void Lock()
    {
        byte iapsr = ReadByte(FlashIapsr);
        WriteByte(FlashIapsr, (byte)(iapsr & ~0x0A)); // PUL and DUL off
    }

    /// <summary>Waits (t_prog is 6 ms typical) and polls FLASH_IAPSR for EOP.</summary>
    private void WaitForEndOfProgramming(int milliseconds, int address)
    {
        Link.Sleep(milliseconds);
        for (int tries = 0; tries < 10; tries++)
        {
            byte iapsr = ReadByte(FlashIapsr);
            if ((iapsr & IapsrEop) != 0) return;
            if ((iapsr & IapsrWrPgDis) != 0)
                throw new LinkException($"The write to ${address:X4} was refused: the area is write-protected (UBC) or read-out protection is on");
            Link.Sleep(5);
        }
        throw new LinkException($"Programming ${address:X4} did not finish");
    }
}
