namespace Wren.Emu;

/// <summary>A device on the I2C bus, such as the OLED's SSD1315.</summary>
public interface II2cDevice
{
    /// <summary>The device was addressed after a START. read is the R/W bit.</summary>
    void Start(bool read);

    /// <summary>A byte from the master. Returns true to acknowledge.</summary>
    bool Write(byte value);

    /// <summary>A byte for the master.</summary>
    byte Read();

    void Stop();
}

/// <summary>The I2C bus: devices by 7-bit address.</summary>
public sealed class I2cBus
{
    private readonly Dictionary<int, II2cDevice> devices = new();
    private II2cDevice? selected;

    public void Attach(int address7, II2cDevice device) => devices[address7] = device;

    /// <summary>Sends the address byte after a START. Returns true when a device acknowledges.</summary>
    public bool Address(byte addressByte)
    {
        selected?.Stop();
        selected = devices.GetValueOrDefault(addressByte >> 1);
        selected?.Start((addressByte & 1) != 0);
        return selected != null;
    }

    public bool Write(byte value) => selected?.Write(value) ?? false;

    public byte Read() => selected?.Read() ?? 0xFF;

    public void Stop()
    {
        selected?.Stop();
        selected = null;
    }
}

/// <summary>
/// The I2C peripheral in master mode. Transfers complete at once: after a write to DR the
/// byte is already on the bus, so TXE and BTF are set immediately. Slave mode is not modelled.
/// </summary>
public sealed class I2cPeripheral : Peripheral
{
    public const int Address = 0x5210;

    // Offsets from 0x5210
    private const int Cr1 = 0, Cr2 = 1, Freqr = 2, Oarl = 3, Oarh = 4, Dr = 6, Sr1 = 7, Sr2 = 8, Sr3 = 9,
        Itr = 0xA, Ccrl = 0xB, Ccrh = 0xC, Triser = 0xD;

    // SR1
    private const byte Sb = 0x01, AddrFlag = 0x02, Btf = 0x04, Rxne = 0x40, Txe = 0x80;
    // SR2
    private const byte Af = 0x04;
    // SR3
    private const byte Msl = 0x01, Busy = 0x02, Tra = 0x04;

    private readonly byte[] regs = new byte[15];

    public I2cPeripheral(InterruptController itc) : base("I2C", Address, 15)
    {
        itc.Connect(Irq.I2c, RequestingInterrupt);
        Reset();
    }

    public I2cBus Bus { get; } = new();

    private bool Enabled => (regs[Cr1] & 1) != 0;

    private bool RequestingInterrupt()
    {
        byte itr = regs[Itr];
        byte sr1 = regs[Sr1];
        bool events = (itr & 0x02) != 0 && ((sr1 & (Sb | AddrFlag | Btf | 0x10)) != 0
            || ((itr & 0x04) != 0 && (sr1 & (Txe | Rxne)) != 0));
        bool errors = (itr & 0x01) != 0 && (regs[Sr2] & 0x2F) != 0;
        return events || errors;
    }

    public override byte Read(int offset)
    {
        switch (offset)
        {
            case Dr:
            {
                byte v = regs[Dr];
                regs[Sr1] &= unchecked((byte)~(Rxne | Btf));
                bool receiving = (regs[Sr3] & (Msl | Tra)) == Msl;
                if (receiving && (regs[Cr2] & 0x04) != 0)
                {
                    // ACK set: the next byte is wanted.
                    regs[Dr] = Bus.Read();
                    regs[Sr1] |= Rxne;
                }
                return v;
            }
            case Sr3:
            {
                byte v = regs[Sr3];
                if ((regs[Sr1] & AddrFlag) != 0)
                {
                    // Reading SR1 then SR3 clears ADDR and starts the data phase.
                    regs[Sr1] &= unchecked((byte)~AddrFlag);
                    if ((regs[Sr3] & Tra) != 0) regs[Sr1] |= Txe;
                    else
                    {
                        regs[Dr] = Bus.Read();
                        regs[Sr1] |= Rxne;
                    }
                }
                return v;
            }
            default:
                return regs[offset];
        }
    }

    public override void Write(int offset, byte value)
    {
        switch (offset)
        {
            case Cr1:
                regs[Cr1] = value;
                if (!Enabled)
                {
                    regs[Sr1] = 0;
                    regs[Sr3] = 0;
                }
                break;
            case Cr2:
                if ((value & 0x80) != 0) { Reset(); return; } // SWRST
                regs[Cr2] = (byte)(value & 0x0C);
                if (!Enabled) break;
                if ((value & 0x01) != 0)
                {
                    // START (or repeated START)
                    regs[Sr1] = Sb;
                    regs[Sr3] |= Msl | Busy;
                }
                if ((value & 0x02) != 0)
                {
                    // STOP
                    Bus.Stop();
                    regs[Sr1] = 0;
                    regs[Sr3] = 0;
                }
                break;
            case Dr:
                regs[Dr] = value;
                if (!Enabled) break;
                if ((regs[Sr1] & Sb) != 0)
                {
                    // Address phase.
                    regs[Sr1] &= unchecked((byte)~Sb);
                    bool read = (value & 1) != 0;
                    if (Bus.Address(value))
                    {
                        // A master transmitter's data register is empty as the address is
                        // acknowledged: the chip sets TXE with ADDR (ST's library waits for both).
                        regs[Sr1] |= read ? AddrFlag : (byte)(AddrFlag | Txe);
                        regs[Sr3] = (byte)(read ? regs[Sr3] & ~Tra : regs[Sr3] | Tra);
                    }
                    else
                    {
                        regs[Sr2] |= Af;
                    }
                }
                else if ((regs[Sr3] & (Msl | Tra)) == (Msl | Tra))
                {
                    if (!Bus.Write(value)) regs[Sr2] |= Af;
                    regs[Sr1] |= Txe | Btf;
                }
                break;
            case Sr1:
            case Sr3:
                break; // read-only
            case Sr2:
                regs[Sr2] &= value; // write 0 to clear
                break;
            default:
                regs[offset] = value;
                break;
        }
    }

    public override void Reset()
    {
        Array.Clear(regs);
        regs[Triser] = 0x02;
        Bus.Stop();
    }
}
