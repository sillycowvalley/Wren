namespace Wren.Emu;

/// <summary>
/// TIM4, the 8-bit basic timer. It counts up at master clock / 2^PSCR and on passing ARR
/// goes back to 0 and sets UIF, which requests IRQ 23 when UIE is set.
/// Registers: CR1 (CEN, UDIS, URS, OPM, ARPE), IER, SR, EGR, CNTR, PSCR, ARR.
/// </summary>
public sealed class Tim4 : Peripheral
{
    public const int Address = 0x5340;

    private byte cr1, ier, sr, cntr, pscr, arr;
    private int prescaleCount;

    public Tim4(InterruptController itc) : base("TIM4", Address, 9)
    {
        itc.Connect(Irq.Tim4Update, () => (sr & ier & 1) != 0);
        Reset();
    }

    public override byte Read(int offset) => offset switch
    {
        0 => cr1,
        3 => ier,
        4 => sr,
        6 => cntr,
        7 => pscr,
        8 => arr,
        _ => 0,
    };

    public override void Write(int offset, byte value)
    {
        switch (offset)
        {
            case 0: cr1 = value; break;
            case 3: ier = (byte)(value & 1); break;
            case 4: sr &= value; break; // write 0 to clear
            case 5:
                if ((value & 1) != 0)
                {
                    cntr = 0;
                    prescaleCount = 0;
                    if ((cr1 & 0x04) == 0) sr |= 1; // UG sets UIF unless URS
                }
                break;
            case 6: cntr = value; break;
            case 7: pscr = (byte)(value & 7); break;
            case 8: arr = value; break;
        }
    }

    public override void Tick(int masterCycles)
    {
        if ((cr1 & 1) == 0) return;
        prescaleCount += masterCycles;
        int divider = 1 << pscr;
        while (prescaleCount >= divider)
        {
            prescaleCount -= divider;
            if (cntr >= arr)
            {
                cntr = 0;
                if ((cr1 & 0x02) == 0) sr |= 1;
                if ((cr1 & 0x08) != 0) { cr1 &= 0xFE; return; } // one-pulse mode stops
            }
            else
            {
                cntr++;
            }
        }
    }

    public override void Reset()
    {
        cr1 = 0;
        ier = 0;
        sr = 0;
        cntr = 0;
        pscr = 0;
        arr = 0xFF;
        prescaleCount = 0;
    }
}
