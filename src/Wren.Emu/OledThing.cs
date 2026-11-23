namespace Wren.Emu;

/// <summary>
/// The Digicool "8-bit OLED Thing" v1.0: an STM8S003F3 with a 0.49" 64x32 SSD1315 OLED.
/// Built with the STM8S103F3 instead, it models the development board wired the same way.
///
///   b0-b4  PC7, PC6, PC5, PC4, PC3      b5-b7  PD6, PD5, PD4
///   HEX    PA1 (J4, jumper JP2)         BAR    PA2 (J3)
///   INV    PA3 (J5, jumper JP1)         PD2, PD3 on J5
///   OLED   SCL PB4, SDA PB5 (pull-ups on the module), I2C address $3C
///   SWIM   PD1, NRST (J2)
/// </summary>
public sealed class OledThing
{
    public const int OledAddress = 0x3C;

    private static readonly (char Port, int Pin)[] InputPins =
    {
        ('C', 7), ('C', 6), ('C', 5), ('C', 4), ('C', 3), ('D', 6), ('D', 5), ('D', 4),
    };

    public OledThing() : this(DeviceInfo.Stm8s003f3) { }

    public OledThing(DeviceInfo chip)
    {
        Mcu = new Stm8Device(chip);
        Oled = new Ssd1315();
        Mcu.I2c.Bus.Attach(OledAddress, Oled);

        // The OLED module's pull-ups hold SCL and SDA high.
        Mcu.PortB.SetExternal(4, PinDrive.High);
        Mcu.PortB.SetExternal(5, PinDrive.High);

        // Mode pins float (the firmware turns on their pull-ups) until a jumper or switch pulls them low.
        SetInputs(0);
    }

    public Stm8Device Mcu { get; }
    public Ssd1315 Oled { get; }

    /// <summary>The value on b0-b7 as last set.</summary>
    public byte Inputs { get; private set; }

    /// <summary>Drives b0-b7 from a byte (bit 0 = b0).</summary>
    public void SetInputs(byte value)
    {
        Inputs = value;
        for (int bit = 0; bit < 8; bit++) SetInput(bit, (value & (1 << bit)) != 0 ? PinDrive.High : PinDrive.Low);
    }

    /// <summary>Drives one input bn. Floating inputs read as 0 (they have no pull-up).</summary>
    public void SetInput(int bit, PinDrive drive)
    {
        var (port, pin) = InputPins[bit];
        Port(port).SetExternal(pin, drive);
        if (drive == PinDrive.High) Inputs |= (byte)(1 << bit);
        else Inputs &= (byte)~(1 << bit);
    }

    /// <summary>HEX (PA1): true pulls it to GND, as jumper JP2 does.</summary>
    public bool HexLow { get => Low('A', 1); set => SetLow('A', 1, value); }

    /// <summary>BAR (PA2): true pulls it to GND.</summary>
    public bool BarLow { get => Low('A', 2); set => SetLow('A', 2, value); }

    /// <summary>INV (PA3): true pulls it to GND, as jumper JP1 does.</summary>
    public bool InvLow { get => Low('A', 3); set => SetLow('A', 3, value); }

    /// <summary>PD2 on J5: true pulls it to GND.</summary>
    public bool Pd2Low { get => Low('D', 2); set => SetLow('D', 2, value); }

    /// <summary>PD3 on J5: true pulls it to GND.</summary>
    public bool Pd3Low { get => Low('D', 3); set => SetLow('D', 3, value); }

    /// <summary>An analog level (0-1023) on PD2 (AIN3), or null for its digital level.</summary>
    public int? AnalogPd2 { get => Mcu.AnalogLevels[3]; set => Mcu.AnalogLevels[3] = value; }

    /// <summary>An analog level (0-1023) on PD3 (AIN4), or null for its digital level.</summary>
    public int? AnalogPd3 { get => Mcu.AnalogLevels[4]; set => Mcu.AnalogLevels[4] = value; }

    private bool Low(char port, int pin) => Port(port).GetExternal(pin) == PinDrive.Low;

    private void SetLow(char port, int pin, bool low) =>
        Port(port).SetExternal(pin, low ? PinDrive.Low : PinDrive.Floating);

    private GpioPort Port(char letter) => letter switch
    {
        'A' => Mcu.PortA,
        'B' => Mcu.PortB,
        'C' => Mcu.PortC,
        _ => Mcu.PortD,
    };

    /// <summary>Power on: clears RAM, resets the chip and the OLED.</summary>
    public void PowerOn()
    {
        Oled.Reset();
        Mcu.PowerOn();
    }

    /// <summary>NRST pulled low: the chip restarts, the OLED keeps its state.</summary>
    public void Reset() => Mcu.Reset();
}
