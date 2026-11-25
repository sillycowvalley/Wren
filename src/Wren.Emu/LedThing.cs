namespace Wren.Emu;

/// <summary>
/// The Digicool "8-bit LED Thing" v1.0 (docs/LedThing.md): an STM8S003F3 with eight LEDs
/// charlieplexed on PD3-PD6.
///
///   b0  PD2      b1-b5  PC7, PC6, PC5, PC4, PC3      b6, b7  PB4, PB5 (true open-drain)
///   M0  PA3      M1     PA2                          M2      PA1
///   LEDs PD3-PD6, each lit when its anode pin drives high and its cathode pin low
///   SWIM PD1, NRST
/// </summary>
public sealed class LedThing
{
    private static readonly (char Port, int Pin)[] InputPins =
    {
        ('D', 2), ('C', 7), ('C', 6), ('C', 5), ('C', 4), ('C', 3), ('B', 4), ('B', 5),
    };

    /// <summary>Each LED's anode and cathode pin on port D, b0 to b7.</summary>
    public static readonly (int Anode, int Cathode)[] LedPins =
    {
        (3, 4), (3, 5), (4, 3), (4, 5), (5, 3), (5, 4), (3, 6), (6, 3),
    };

    private readonly long[] litTicks = new long[8];
    private long since, windowStart;

    public LedThing() : this(DeviceInfo.Stm8s003f3) { }

    public LedThing(DeviceInfo chip)
    {
        Mcu = new Stm8Device(chip);
        Mcu.PortD.Written += Account;
        SetInputs(0);
    }

    public Stm8Device Mcu { get; }

    /// <summary>The value on b0-b7 as last set.</summary>
    public byte Inputs { get; private set; }

    /// <summary>Drives b0-b7 from a byte (bit 0 = b0).</summary>
    public void SetInputs(byte value)
    {
        Inputs = value;
        for (int bit = 0; bit < 8; bit++) SetInput(bit, (value & (1 << bit)) != 0 ? PinDrive.High : PinDrive.Low);
    }

    /// <summary>Drives one input bn.</summary>
    public void SetInput(int bit, PinDrive drive)
    {
        var (port, pin) = InputPins[bit];
        Port(port).SetExternal(pin, drive);
        if (drive == PinDrive.High) Inputs |= (byte)(1 << bit);
        else Inputs &= (byte)~(1 << bit);
    }

    /// <summary>M0 (PA3): true pulls it to GND.</summary>
    public bool M0Low { get => Low(3); set => SetLow(3, value); }

    /// <summary>M1 (PA2): true pulls it to GND.</summary>
    public bool M1Low { get => Low(2); set => SetLow(2, value); }

    /// <summary>M2 (PA1): true pulls it to GND.</summary>
    public bool M2Low { get => Low(1); set => SetLow(1, value); }

    /// <summary>The LEDs lit at this moment, b0 = bit 0.</summary>
    public byte Lit
    {
        get
        {
            var d = Mcu.PortD;
            int lit = 0;
            for (int i = 0; i < 8; i++)
            {
                var (anode, cathode) = LedPins[i];
                bool high = d.DrivenByChip(anode) && (d.Odr & (1 << anode)) != 0;
                bool low = d.DrivenByChip(cathode) && (d.Odr & (1 << cathode)) == 0;
                if (high && low) lit |= 1 << i;
            }
            return (byte)lit;
        }
    }

    /// <summary>
    /// How much of the time each LED was lit since the last call, 0 to 1 (b0 first): the
    /// brightness a person sees. A scan that lights one LED at a time gives each lit LED 1/8.
    /// </summary>
    public double[] TakeBrightness()
    {
        Account();
        long window = Mcu.HsiTicks - windowStart;
        var shares = new double[8];
        for (int i = 0; i < 8; i++)
        {
            shares[i] = window > 0 ? litTicks[i] / (double)window : ((Lit >> i) & 1);
            litTicks[i] = 0;
        }
        windowStart = Mcu.HsiTicks;
        return shares;
    }

    /// <summary>Adds the time since the last change of port D to the LEDs lit during it.</summary>
    private void Account()
    {
        long now = Mcu.HsiTicks;
        byte lit = previousLit;
        for (int i = 0; i < 8; i++)
            if ((lit & (1 << i)) != 0) litTicks[i] += now - since;
        since = now;
        previousLit = Lit;
    }

    private byte previousLit;

    private bool Low(int pin) => Mcu.PortA.GetExternal(pin) == PinDrive.Low;

    private void SetLow(int pin, bool low) => Mcu.PortA.SetExternal(pin, low ? PinDrive.Low : PinDrive.Floating);

    private GpioPort Port(char letter) => letter switch
    {
        'A' => Mcu.PortA,
        'B' => Mcu.PortB,
        'C' => Mcu.PortC,
        _ => Mcu.PortD,
    };

    /// <summary>Power on: clears RAM and resets the chip; the LEDs go dark.</summary>
    public void PowerOn()
    {
        Mcu.PowerOn();
        Array.Clear(litTicks);
        since = windowStart = Mcu.HsiTicks;
        previousLit = 0;
    }

    /// <summary>NRST pulled low: the chip restarts.</summary>
    public void Reset() => Mcu.Reset();
}
