namespace Wren.Emu;

/// <summary>
/// The clock controller. Only the internal 16 MHz HSI is modelled (the Thing has no crystal).
/// CLK_CKDIVR sets the master clock (HSI / 1, 2, 4, 8) and the CPU clock (master / 1 to 128).
/// </summary>
public sealed class ClockController : Peripheral
{
    public const int Address = 0x50C0;
    public const int HsiFrequency = 16_000_000;

    private static readonly byte[] ResetValues =
    {
        0x03, // ICKR: HSI on and ready
        0x00, // ECKR
        0x00, // (reserved)
        0xE1, // CMSR: HSI selected
        0xE1, // SWR
        0x00, // SWCR
        0x18, // CKDIVR: HSI/8, CPU/1
        0xFF, // PCKENR1
        0x00, // CSSR
        0x00, // CCOR
        0xFF, // PCKENR2
        0x00, // (reserved)
        0x00, // HSITRIMR
        0x00, // SWIMCCR
    };

    private readonly byte[] values = new byte[ResetValues.Length];

    public ClockController() : base("CLK", Address, ResetValues.Length)
    {
        Reset();
    }

    public int Ckdivr => values[6];

    /// <summary>Master clock in Hz, which the peripherals run from.</summary>
    public int MasterFrequency => HsiFrequency >> ((Ckdivr >> 3) & 3);

    /// <summary>Master clock cycles per CPU cycle.</summary>
    public int CpuDivider => 1 << (Ckdivr & 7);

    public override byte Read(int offset) => values[offset];

    public override void Write(int offset, byte value)
    {
        switch (offset)
        {
            case 0: values[0] = (byte)((value & 0x3D) | 0x02); break; // keep HSIRDY
            case 3: break; // CMSR is read-only
            case 6: values[6] = (byte)(value & 0x1F); break;
            default: values[offset] = value; break;
        }
    }

    public override void Reset() => Array.Copy(ResetValues, values, values.Length);
}
