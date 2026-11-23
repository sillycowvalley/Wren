namespace Wren.Emu;

/// <summary>The sizes that differ between parts of the family.</summary>
public sealed record DeviceInfo(string Name, int FlashSize, int EepromSize, int RamSize, int FlashEndurance, int EepromEndurance)
{
    /// <summary>The OLED Thing's chip: 8 KB flash (100 writes), 128 B EEPROM (100k writes), 1 KB RAM.</summary>
    public static readonly DeviceInfo Stm8s003f3 = new("STM8S003F3", 8192, 128, 1024, 100, 100_000);

    /// <summary>The development board's chip: 8 KB flash (10k writes), 640 B EEPROM (300k writes), 1 KB RAM.</summary>
    public static readonly DeviceInfo Stm8s103f3 = new("STM8S103F3", 8192, 640, 1024, 10_000, 300_000);
}

public enum StopReason { TimeUp, Break, Breakpoint, Fault }

/// <summary>
/// An STM8S003F3 or STM8S103F3: CPU, memory and the peripherals Wren models
/// (GPIO, EXTI, ITC, CLK, TIM4, I2C master, flash/EEPROM programming).
/// Other peripheral registers hold their values but do nothing; touching one is recorded
/// in UnmodelledPeripherals.
/// </summary>
public sealed class Stm8Device : IBus
{
    public const int RamStart = 0x0000;
    public const int EepromStart = 0x4000;
    public const int OptionStart = 0x4800;
    public const int OptionSize = 0x80;
    public const int RegisterStart = 0x5000;
    public const int RegisterEnd = 0x57FF;
    public const int CpuRegisterStart = 0x7F00;
    public const int FlashStart = 0x8000;

    private const int CfgGcr = 0x7F60;

    private readonly byte[] ram;
    private readonly byte[] eeprom;
    private readonly byte[] flash;
    private readonly byte[] options = new byte[OptionSize];
    private readonly byte[] plainRegisters = new byte[RegisterEnd - RegisterStart + 1];
    private readonly Peripheral?[] registerMap = new Peripheral?[RegisterEnd - RegisterStart + 1];
    private readonly List<Peripheral> peripherals = new();
    private byte cfgGcr;

    public Stm8Device(DeviceInfo info)
    {
        Info = info;
        ram = new byte[info.RamSize];
        eeprom = new byte[info.EepromSize];
        flash = new byte[info.FlashSize];
        EepromWrites = new int[info.EepromSize];
        FlashWrites = new int[info.FlashSize];

        Itc = new InterruptController();
        PortA = new GpioPort('A', 0x5000);
        PortB = new GpioPort('B', 0x5005, trueOpenDrainPins: 0x30);
        PortC = new GpioPort('C', 0x500A);
        PortD = new GpioPort('D', 0x500F);
        PortE = new GpioPort('E', 0x5014);
        PortF = new GpioPort('F', 0x5019);
        Exti = new Exti(Itc, new[] { PortA, PortB, PortC, PortD, PortE });
        Clock = new ClockController();
        Tim4 = new Tim4(Itc);
        I2c = new I2cPeripheral(Itc);
        FlashControl = new FlashController(Itc);
        Adc = new Adc(Itc, ChannelLevel);

        foreach (var p in new Peripheral[] { PortA, PortB, PortC, PortD, PortE, PortF, Exti, Clock, Tim4, I2c, FlashControl, Adc })
        {
            peripherals.Add(p);
            for (int i = 0; i < p.Size; i++) registerMap[p.BaseAddress - RegisterStart + i] = p;
        }

        Cpu = new Cpu(this, info.RamSize - 1) { Interrupts = Itc };
        ResetOptionBytes();
    }

    public DeviceInfo Info { get; }
    public Cpu Cpu { get; }
    public InterruptController Itc { get; }
    public GpioPort PortA { get; }
    public GpioPort PortB { get; }
    public GpioPort PortC { get; }
    public GpioPort PortD { get; }
    public GpioPort PortE { get; }
    public GpioPort PortF { get; }
    public Exti Exti { get; }
    public ClockController Clock { get; }
    public Tim4 Tim4 { get; }
    public I2cPeripheral I2c { get; }
    public FlashController FlashControl { get; }
    public Adc Adc { get; }

    /// <summary>
    /// Analog levels (0-1023) on the ADC channels, by channel number. A channel with no
    /// level set reads its pin's digital level: 1023 high, 0 low.
    /// </summary>
    public int?[] AnalogLevels { get; } = new int?[16];

    /// <summary>The pin of each ADC channel on the 20-pin parts: AIN2 PC4, AIN3 PD2, AIN4 PD3, AIN5 PD5, AIN6 PD6.</summary>
    private (GpioPort Port, int Pin)? ChannelPin(int channel) => channel switch
    {
        2 => (PortC, 4),
        3 => (PortD, 2),
        4 => (PortD, 3),
        5 => (PortD, 5),
        6 => (PortD, 6),
        _ => null,
    };

    private int ChannelLevel(int channel)
    {
        if (AnalogLevels[channel] is int level) return level;
        return ChannelPin(channel) is { } p && p.Port.Level(p.Pin) ? 1023 : 0;
    }

    /// <summary>How many times firmware has written each EEPROM byte since loading.</summary>
    public int[] EepromWrites { get; }

    /// <summary>How many times firmware has written each flash byte since loading.</summary>
    public int[] FlashWrites { get; }

    /// <summary>Names of peripherals the firmware used that Wren does not model.</summary>
    public HashSet<string> UnmodelledPeripherals { get; } = new();

    /// <summary>Time since reset, in 16 MHz HSI ticks.</summary>
    public long HsiTicks { get; private set; }

    public double Seconds => HsiTicks / (double)ClockController.HsiFrequency;

    /// <summary>Addresses where Run stops before executing the instruction.</summary>
    public HashSet<int> Breakpoints { get; } = new();

    /// <summary>When set, a breakpoint only stops Run if this returns true for its address.</summary>
    public Func<int, bool>? BreakpointFilter { get; set; }

    /// <summary>The SWIM pin (PD1) is a GPIO when CFG_GCR.SWD is set.</summary>
    public bool SwimDisabled => (cfgGcr & 1) != 0;

    // ---- Loading and reset ----

    /// <summary>Puts bytes into flash, EEPROM, option bytes or RAM, as a programmer would.</summary>
    public void Load(IEnumerable<KeyValuePair<int, byte>> bytes)
    {
        foreach (var (address, value) in bytes)
        {
            if (address >= FlashStart && address < FlashStart + flash.Length) flash[address - FlashStart] = value;
            else if (address >= EepromStart && address < EepromStart + eeprom.Length) eeprom[address - EepromStart] = value;
            else if (address >= OptionStart && address < OptionStart + OptionSize) options[address - OptionStart] = value;
            else if (address >= RamStart && address < ram.Length) ram[address] = value;
            else throw new ArgumentOutOfRangeException(nameof(bytes), $"Address ${address:X4} is not flash, EEPROM, option bytes or RAM on the {Info.Name}");
        }
        Array.Clear(EepromWrites);
        Array.Clear(FlashWrites);
    }

    /// <summary>Clears flash, EEPROM and RAM, as a programmer's erase would.</summary>
    public void Erase()
    {
        Array.Clear(flash);
        Array.Clear(eeprom);
        Array.Clear(ram);
        ResetOptionBytes();
    }

    private void ResetOptionBytes()
    {
        Array.Clear(options);
        // OPT1-OPT5 and their complements NOPT1-NOPT5 (OPT0, read-out protection, is 0).
        for (int i = 1; i <= 0x0A; i += 2) options[i + 1] = 0xFF;
    }

    /// <summary>A reset (NRST pulled low): RAM is kept, everything else restarts.</summary>
    public void Reset()
    {
        foreach (var p in peripherals) p.Reset();
        Itc.Reset();
        Array.Clear(plainRegisters);
        cfgGcr = 0;
        Cpu.Reset();
        HsiTicks = 0;
    }

    /// <summary>Power on: RAM is cleared, then a reset.</summary>
    public void PowerOn()
    {
        Array.Clear(ram);
        Reset();
    }

    // ---- Running ----

    /// <summary>Runs one instruction (or one interrupt entry), and the peripherals alongside it.</summary>
    public void Step()
    {
        int cpuCycles;
        if (Cpu.State == CpuState.Running || Itc.Next(Cpu.Level) >= 0)
        {
            cpuCycles = Cpu.Step();
        }
        else
        {
            // Waiting for an interrupt: let time pass in larger steps.
            cpuCycles = 16;
            Cpu.Step();
        }
        int master = cpuCycles * Clock.CpuDivider;
        foreach (var p in peripherals) p.Tick(master);
        HsiTicks += (long)master * (ClockController.HsiFrequency / Clock.MasterFrequency);
    }

    /// <summary>Runs until the given time has passed, a BREAK, a breakpoint or a fault.</summary>
    public StopReason Run(double seconds)
    {
        long end = HsiTicks + (long)(seconds * ClockController.HsiFrequency);
        bool first = true;
        while (HsiTicks < end)
        {
            if (!first && Cpu.State == CpuState.Running && Breakpoints.Contains(Cpu.PC)
                && (BreakpointFilter?.Invoke(Cpu.PC) ?? true)) return StopReason.Breakpoint;
            first = false;
            Step();
            if (Cpu.Fault != null) return StopReason.Fault;
            if (Cpu.BreakHit)
            {
                Cpu.BreakHit = false;
                return StopReason.Break;
            }
        }
        return StopReason.TimeUp;
    }

    // ---- The bus ----

    public byte Read(int address)
    {
        if (address < ram.Length) return ram[address];
        if (address >= FlashStart && address < FlashStart + flash.Length) return flash[address - FlashStart];
        if (address >= EepromStart && address < EepromStart + eeprom.Length) return eeprom[address - EepromStart];
        if (address >= OptionStart && address < OptionStart + OptionSize) return options[address - OptionStart];
        if (address >= RegisterStart && address <= RegisterEnd)
        {
            var p = registerMap[address - RegisterStart];
            if (p != null) return p.Read(address - p.BaseAddress);
            NoteUnmodelled(address);
            return plainRegisters[address - RegisterStart];
        }
        if (address >= InterruptController.SprBase && address < InterruptController.SprBase + 8)
            return Itc.Read(address - InterruptController.SprBase);
        if (address == CfgGcr) return cfgGcr;
        return 0;
    }

    public void Write(int address, byte value)
    {
        if (address < ram.Length)
        {
            ram[address] = value;
            return;
        }
        if (address >= FlashStart && address < FlashStart + flash.Length)
        {
            if (FlashControl.TryWrite(program: true))
            {
                flash[address - FlashStart] = value;
                FlashWrites[address - FlashStart]++;
            }
            return;
        }
        if (address >= EepromStart && address < EepromStart + eeprom.Length)
        {
            if (FlashControl.TryWrite(program: false))
            {
                eeprom[address - EepromStart] = value;
                EepromWrites[address - EepromStart]++;
            }
            return;
        }
        if (address >= OptionStart && address < OptionStart + OptionSize)
        {
            if (FlashControl.TryWrite(program: false)) options[address - OptionStart] = value;
            return;
        }
        if (address >= RegisterStart && address <= RegisterEnd)
        {
            var p = registerMap[address - RegisterStart];
            if (p != null)
            {
                p.Write(address - p.BaseAddress, value);
                return;
            }
            NoteUnmodelled(address);
            plainRegisters[address - RegisterStart] = value;
            return;
        }
        if (address >= InterruptController.SprBase && address < InterruptController.SprBase + 8)
        {
            Itc.Write(address - InterruptController.SprBase, value);
            return;
        }
        if (address == CfgGcr) cfgGcr = (byte)(value & 3);
    }

    private void NoteUnmodelled(int address)
    {
        string name = address switch
        {
            >= 0x50B3 and <= 0x50B3 => "RST",
            >= 0x50D1 and <= 0x50D2 => "WWDG",
            >= 0x50E0 and <= 0x50E2 => "IWDG",
            >= 0x50F0 and <= 0x50F2 => "AWU",
            0x50F3 => "BEEP",
            >= 0x5200 and <= 0x5207 => "SPI",
            >= 0x5230 and <= 0x523A => "UART1",
            >= 0x5250 and <= 0x527F => "TIM1",
            >= 0x5300 and <= 0x532F => "TIM2",
            >= 0x53E0 and <= 0x540F => "ADC1 (buffered or watchdog modes)",
            _ => $"register ${address:X4}",
        };
        UnmodelledPeripherals.Add(name);
    }
}
