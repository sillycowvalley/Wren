namespace Wren.Emu;

/// <summary>IRQ numbers of the STM8S003/S103. The vector is at 0x8008 + 4 * IRQ.</summary>
public static class Irq
{
    public const int Tli = 0, Awu = 1, Clk = 2, ExtiA = 3, ExtiB = 4, ExtiC = 5, ExtiD = 6, ExtiE = 7;
    public const int Spi = 10, Tim1Update = 11, Tim1Capture = 12, Tim2Update = 13, Tim2Capture = 14;
    public const int Uart1Tx = 17, Uart1Rx = 18, I2c = 19, Adc1 = 22, Tim4Update = 23, Flash = 24;
    public const int Count = 30;
}

/// <summary>
/// The interrupt controller (ITC). Peripherals either hold a request line (level) or
/// latch an edge request that is cleared when the CPU takes it. Priorities come from ITC_SPR1-8.
/// </summary>
public sealed class InterruptController : Peripheral, IInterruptSource
{
    public const int SprBase = 0x7F70;

    private readonly byte[] spr = new byte[8];
    private readonly Func<bool>?[] lines = new Func<bool>?[Irq.Count];
    private readonly bool[] latched = new bool[Irq.Count];

    public InterruptController() : base("ITC", SprBase, 8)
    {
        Reset();
    }

    /// <summary>Connects a level request: the peripheral's flags decide whether it is asking.</summary>
    public void Connect(int irq, Func<bool> line) => lines[irq] = line;

    /// <summary>An edge request (external interrupts). Held until the CPU takes it.</summary>
    public void Raise(int irq) => latched[irq] = true;

    public bool IsPending(int irq) => latched[irq] || (lines[irq]?.Invoke() ?? false);

    public int Next(int currentLevel)
    {
        if (currentLevel >= 3) return -1;
        for (int irq = 0; irq < Irq.Count; irq++)
        {
            if (IsPending(irq) && LevelOf(irq) > currentLevel) return irq;
        }
        return -1;
    }

    public int LevelOf(int irq)
    {
        int bits = (spr[irq / 4] >> (2 * (irq % 4))) & 3;
        return bits switch { 1 => 1, 0 => 2, _ => 3 }; // 10 is reserved; treat it as level 3.
    }

    public void Taken(int irq) => latched[irq] = false;

    public override byte Read(int offset) => spr[offset];

    public override void Write(int offset, byte value) => spr[offset] = value;

    public override void Reset()
    {
        Array.Fill(spr, (byte)0xFF);
        Array.Clear(latched);
    }
}
