namespace Wren.Emu;

/// <summary>
/// ADC1 (RM0016 section 24), single and continuous conversion, not buffered or scanning.
/// Registers at $5400: CSR (EOC, AWD, EOCIE, AWDIE, CH), CR1 (SPSEL, CONT, ADON), CR2
/// (EXTTRIG, EXTSEL, ALIGN, SCAN), CR3, DRH, DRL, TDRH, TDRL.
/// Writing ADON=1 while it is 0 powers the ADC on; writing it again (with no other CR1 bit
/// changing) starts a conversion, which takes 14 ADC clocks (fMASTER / 2..18) and sets EOC.
/// </summary>
public sealed class Adc : Peripheral
{
    public const int Address = 0x5400;

    private static readonly int[] Prescalers = { 2, 3, 4, 6, 8, 10, 12, 18 };
    private readonly byte[] regs = new byte[8];
    private readonly Func<int, int> channelLevel;
    private int remaining;      // master cycles until the conversion finishes; 0 = idle
    private int result;

    /// <param name="channelLevel">The 10-bit level on a channel (AINn), 0-1023.</param>
    public Adc(InterruptController itc, Func<int, int> channelLevel) : base("ADC1", Address, 8)
    {
        this.channelLevel = channelLevel;
        itc.Connect(Irq.Adc1, () => (regs[0] & 0xA0) == 0xA0);
        Reset();
    }

    public bool Converting => remaining > 0;

    public override byte Read(int offset) => offset switch
    {
        4 => Aligned(high: true),
        5 => Aligned(high: false),
        _ => regs[offset],
    };

    private byte Aligned(bool high)
    {
        bool right = (regs[2] & 0x08) != 0;
        if (right) return high ? (byte)(result >> 8) : (byte)result;
        return high ? (byte)(result >> 2) : (byte)(result & 3);
    }

    public override void Write(int offset, byte value)
    {
        switch (offset)
        {
            case 0:
                // EOC and AWD are cleared by writing 0; the others are plain bits.
                regs[0] = (byte)((value & 0x3F) | (regs[0] & value & 0xC0));
                break;
            case 1:
            {
                byte old = regs[1];
                regs[1] = (byte)(value & 0x73);
                bool wasOn = (old & 1) != 0, on = (value & 1) != 0;
                if (!on) remaining = 0;
                else if (wasOn && (old & 0x72) == (value & 0x72)) Start();
                break;
            }
            case 4:
            case 5:
                break; // read-only
            default:
                regs[offset] = value;
                break;
        }
    }

    private void Start() => remaining = 14 * Prescalers[(regs[1] >> 4) & 7];

    public override void Tick(int masterCycles)
    {
        if (remaining <= 0) return;
        remaining -= masterCycles;
        if (remaining > 0) return;
        remaining = 0;
        result = Math.Clamp(channelLevel(regs[0] & 0x0F), 0, 1023);
        regs[0] |= 0x80;
        if ((regs[1] & 0x02) != 0) Start(); // continuous
    }

    public override void Reset()
    {
        Array.Clear(regs);
        remaining = 0;
        result = 0;
    }
}
