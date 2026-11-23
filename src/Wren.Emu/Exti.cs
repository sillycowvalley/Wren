namespace Wren.Emu;

/// <summary>
/// External interrupts. One IRQ per port (A to E). A pin takes part when it is an input
/// with CR2 set. EXTI_CR1 sets the edge for ports A-D, EXTI_CR2 for port E:
/// 00 falling edge and low level, 01 rising, 10 falling, 11 both.
/// The low-level part of mode 00 is modelled as a falling edge only.
/// </summary>
public sealed class Exti : Peripheral
{
    public const int Address = 0x50A0;

    private byte cr1, cr2;
    private readonly InterruptController itc;

    public Exti(InterruptController itc, IEnumerable<GpioPort> ports) : base("EXTI", Address, 2)
    {
        this.itc = itc;
        foreach (var port in ports)
        {
            int index = port.Letter - 'A';
            if (index > 4) continue;
            port.PinChanged += (pin, level) => OnPinChanged(port, index, pin, level);
        }
    }

    private void OnPinChanged(GpioPort port, int index, int pin, bool level)
    {
        int m = 1 << pin;
        if ((port.Ddr & m) != 0 || (port.Cr2 & m) == 0) return;

        int sensitivity = index < 4 ? (cr1 >> (2 * index)) & 3 : cr2 & 3;
        bool fire = sensitivity switch
        {
            0 => !level,
            1 => level,
            2 => !level,
            _ => true,
        };
        if (fire) itc.Raise(Irq.ExtiA + index);
    }

    public override byte Read(int offset) => offset == 0 ? cr1 : cr2;

    public override void Write(int offset, byte value)
    {
        if (offset == 0) cr1 = value;
        else cr2 = (byte)(value & 0x07);
    }

    public override void Reset()
    {
        cr1 = 0;
        cr2 = 0;
    }
}
