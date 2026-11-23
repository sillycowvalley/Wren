namespace Wren.Emu;

/// <summary>What the outside world does to a pin.</summary>
public enum PinDrive { Floating, Low, High }

/// <summary>
/// One GPIO port: ODR, IDR, DDR, CR1, CR2.
/// A pin's level: an output in push-pull mode drives ODR. An output in open-drain mode
/// pulls low for ODR=0 and lets go for ODR=1. A pin not driven by the chip follows the
/// outside world; a floating pin reads 1 with the pull-up on (CR1=1 on an input) and 0 without.
/// </summary>
public sealed class GpioPort : Peripheral
{
    private byte odr, ddr, cr1, cr2;
    private readonly PinDrive[] external = new PinDrive[8];
    private byte lastLevels;

    public GpioPort(char letter, int baseAddress, byte trueOpenDrainPins = 0) : base($"P{letter}", baseAddress, 5)
    {
        Letter = letter;
        TrueOpenDrainPins = trueOpenDrainPins;
        Reset();
    }

    public char Letter { get; }

    /// <summary>Pins with no P-buffer (PB4 and PB5 on the STM8S003): they can only pull low.</summary>
    public byte TrueOpenDrainPins { get; }

    /// <summary>Raised with (pin, newLevel) whenever a pin's level changes, from either side.</summary>
    public event Action<int, bool>? PinChanged;

    public byte Odr => odr;
    public byte Ddr => ddr;
    public byte Cr1 => cr1;
    public byte Cr2 => cr2;

    public PinDrive GetExternal(int pin) => external[pin];

    public void SetExternal(int pin, PinDrive drive)
    {
        external[pin] = drive;
        Update();
    }

    /// <summary>The level on the pin, as the outside world sees it.</summary>
    public bool Level(int pin) => (Levels() & (1 << pin)) != 0;

    /// <summary>True when the chip itself drives the pin (output, and not released open-drain).</summary>
    public bool DrivenByChip(int pin)
    {
        int m = 1 << pin;
        if ((ddr & m) == 0) return false;
        bool pushPull = (cr1 & m) != 0 && (TrueOpenDrainPins & m) == 0;
        return pushPull || (odr & m) == 0;
    }

    public byte Levels()
    {
        int levels = 0;
        for (int pin = 0; pin < 8; pin++)
        {
            int m = 1 << pin;
            bool level;
            if (DrivenByChip(pin)) level = (odr & m) != 0;
            else
            {
                level = external[pin] switch
                {
                    PinDrive.High => true,
                    PinDrive.Low => false,
                    // Floating: the pull-up exists only on inputs with CR1 set.
                    _ => (ddr & m) == 0 && (cr1 & m) != 0 && (TrueOpenDrainPins & m) == 0,
                };
            }
            if (level) levels |= m;
        }
        return (byte)levels;
    }

    private void Update()
    {
        byte now = Levels();
        byte changed = (byte)(now ^ lastLevels);
        lastLevels = now;
        if (changed == 0) return;
        for (int pin = 0; pin < 8; pin++)
        {
            if ((changed & (1 << pin)) != 0) PinChanged?.Invoke(pin, (now & (1 << pin)) != 0);
        }
    }

    public override byte Read(int offset) => offset switch
    {
        0 => odr,
        1 => Levels(),
        2 => ddr,
        3 => cr1,
        _ => cr2,
    };

    public override void Write(int offset, byte value)
    {
        switch (offset)
        {
            case 0: odr = value; break;
            case 1: return; // IDR is read-only
            case 2: ddr = value; break;
            case 3: cr1 = value; break;
            default: cr2 = value; break;
        }
        Update();
    }

    public override void Reset()
    {
        odr = 0;
        ddr = 0;
        cr1 = 0;
        cr2 = 0;
        lastLevels = Levels();
    }
}
