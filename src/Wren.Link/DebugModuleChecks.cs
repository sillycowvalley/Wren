using Wren.Script;

namespace Wren.Link;

/// <summary>What the debug module checks found: null where a check could not run.</summary>
public sealed record DebugModuleFindings(
    bool? ReadBreakpointOnFlash,
    bool? LiveReadsWhileIdle,
    bool? StallStopsTheCpu,
    bool? Tim4Freezes);

/// <summary>
/// Bench checks of the STM8's debug module (UM0470 chapter 4) on a real Thing running Wren's
/// kernel, for an on-chip BASIC debugger. They answer three questions the manual leaves open:
/// does a data-read breakpoint fire on the VM reading its bytecode from flash; can SWIM read
/// RAM while the kernel idles in WFI (SWIM_CSR's NO_ACCESS is documented as set in WFI); and
/// does DM_ENFCTR freeze TIM4 (the 1 ms tick) while the CPU is stalled. They write only debug
/// module registers and SWIM_CSR, never flash or EEPROM, and leave the CPU stalled.
/// </summary>
public sealed class DebugModuleChecks
{
    public const int CpuPc = 0x7F01, CpuX = 0x7F04;
    public const int DmBk1 = 0x7F90, DmBk2 = 0x7F93, DmCr1 = 0x7F96, DmCsr1 = 0x7F98, DmCsr2 = 0x7F99, DmEnfctr = 0x7F9A;
    public const int Tim4Cntr = 0x5346;

    /// <summary>DM_CR1: BC = 001 (data on BK1 &lt;= @ &lt;= BK2), BIR (break on read). UM0470 table 4.</summary>
    public const byte BreakOnReadInRange = 0x0C;

    public const byte Stall = 0x08;
    public const byte Bk1Flag = 0x02;
    public const byte NoAccessBit = 0x40;

    /// <summary>SWIM_CSR: SAFE_MASK and SWIM_DM, with PRI clear, so SWIM reads do not stall the CPU.</summary>
    public const byte CsrNonIntrusive = 0xA0;

    /// <summary>DM_ENFCTR with ENFCT0 (TIM4) and ENFCT7 (I2C) cleared: both frozen while stalled. UM0470 table 6.</summary>
    public const byte FreezeTim4AndI2c = 0x7E;

    private readonly SwimTarget target;
    private readonly Kernel kernel;
    private readonly Action<string> log;

    public DebugModuleChecks(SwimTarget target, Kernel kernel, Action<string> log)
    {
        this.target = target;
        this.kernel = kernel;
        this.log = log;
    }

    /// <summary>
    /// Runs the checks on a Thing just connected (its CPU stalled at reset) under Wren's kernel.
    /// With hasProgram false the breakpoint check is skipped: the kernel reads the store only
    /// when there is a program to load.
    /// </summary>
    public DebugModuleFindings Run(bool hasProgram)
    {
        bool? breakpoint = null, live = null, stall = null, freeze = null;
        log($"After Connect: DM_CSR1 = ${target.ReadByte(DmCsr1):X2}, DM_CSR2 = ${target.ReadByte(DmCsr2):X2} (UM0470 4.3.1: the CPU waits at the reset vector).");

        if (hasProgram) breakpoint = Attempt("1. A data-read breakpoint on the program store", CheckReadBreakpoint);
        else log("1. Skipped: the Thing holds no program, so the kernel never reads the program store.");

        live = Attempt("2. Reading RAM while the kernel runs and idles in WFI", CheckLiveReads);
        stall = Attempt("3. Stopping the CPU with STALL", CheckStall);
        freeze = Attempt("4. Freezing TIM4 while stalled (DM_ENFCTR)", CheckFreeze);

        target.WriteByte(DmEnfctr, 0xFF);
        target.WriteByte(DmCr1, 0x00);
        log("Done. The CPU is left stalled, the debug module's settings back as they were. Click Run to start the Thing.");
        return new DebugModuleFindings(breakpoint, live, stall, freeze);
    }

    private bool? Attempt(string title, Func<bool> check)
    {
        log($"--- {title}");
        try
        {
            bool passed = check();
            log(passed ? "    Result: YES." : "    Result: NO.");
            return passed;
        }
        catch (LinkException ex)
        {
            log($"    The programmer failed: {ex.Message}");
            log("    Result: unknown. Disconnect, Connect and run the checks again; if it repeats, paste the log back.");
            return null;
        }
    }

    /// <summary>
    /// Resets the chip into the debugger, sets BK1-BK2 over the program store, break on read,
    /// and lets the kernel start: loading the program reads the store, which should stall the
    /// CPU in read_program.
    /// </summary>
    private bool CheckReadBreakpoint()
    {
        // A reset with SWIM kept (SWIM_CSR.RST clear): the debug module's registers return to
        // their reset values and the CPU waits at the reset vector (UM0470 4.3.1), before the
        // kernel has read anything.
        target.WriteByte(SwimTarget.SwimCsr, CsrNonIntrusive);
        target.Link.GenerateReset();
        target.Link.Sleep(20);
        log($"    After a reset: DM_CSR1 = ${target.ReadByte(DmCsr1):X2}, DM_CSR2 = ${target.ReadByte(DmCsr2):X2}.");
        int store = ProgramStore.StoreAddress, end = 0xA000 - 1;
        WriteAddress(DmBk1, store);
        WriteAddress(DmBk2, end);
        target.WriteByte(DmCr1, BreakOnReadInRange);
        log($"    Breakpoint: data read in ${store:X4}-${end:X4}. Letting the kernel start.");
        target.WriteByte(DmCsr2, 0x00);
        target.Link.Sleep(300);
        byte csr2 = target.ReadByte(DmCsr2);
        byte csr1 = target.ReadByte(DmCsr1);
        target.WriteByte(DmCr1, 0x00);
        log($"    DM_CSR2 = ${csr2:X2} (STALL {((csr2 & Stall) != 0 ? "set" : "clear")}), DM_CSR1 = ${csr1:X2} (BK1F {((csr1 & Bk1Flag) != 0 ? "set" : "clear")}).");
        if ((csr2 & Stall) == 0)
        {
            log("    The CPU ran on: the breakpoint did not fire. Stopping it for the next checks.");
            target.WriteByte(DmCsr2, Stall);
            return false;
        }
        byte[] pc = target.Read(CpuPc, 3);
        byte[] x = target.Read(CpuX, 2);
        int pcValue = (pc[0] << 16) | (pc[1] << 8) | pc[2];
        int xValue = (x[0] << 8) | x[1];
        int readProgram = kernel.Symbol("read_program");
        bool inReadProgram = pcValue >= readProgram && pcValue < readProgram + 48;
        bool xInStore = xValue >= store && xValue <= end;
        log($"    Stopped at PC ${pcValue:X6} (read_program is ${readProgram:X4}), X = ${xValue:X4}.");
        log(inReadProgram && xInStore
            ? "    That is the kernel reading a program byte from flash: a BASIC line breakpoint can be a data-read breakpoint."
            : "    It stopped, but not where expected: paste the log back.");
        return inReadProgram && xInStore;
    }

    /// <summary>Lets the kernel run, then reads its millisecond count and SWIM_CSR over SWIM five times.</summary>
    private bool CheckLiveReads()
    {
        target.WriteByte(DmCr1, 0x00);
        target.WriteByte(SwimTarget.SwimCsr, CsrNonIntrusive);
        target.WriteByte(DmCsr2, 0x00);
        log("    The CPU runs. Waiting half a second for the kernel to settle into its idle WFI.");
        target.Link.Sleep(500);
        int millis = kernel.Symbol("millis");
        var counts = new List<int>();
        int noAccess = 0;
        for (int i = 0; i < 5; i++)
        {
            byte csr = target.ReadByte(SwimTarget.SwimCsr);
            if ((csr & NoAccessBit) != 0) noAccess++;
            byte[] m = target.Read(millis, 2);
            counts.Add((m[0] << 8) | m[1]);
            log($"    SWIM_CSR = ${csr:X2} (NO_ACCESS {((csr & NoAccessBit) != 0 ? "set" : "clear")}), millis = {counts[^1]}");
            target.Link.Sleep(100);
        }
        bool advancing = counts.Zip(counts.Skip(1), (a, b) => (ushort)(b - a)).All(d => d is >= 50 and <= 400);
        log(advancing
            ? $"    The kernel's clock advanced between reads ({noAccess} of 5 showed NO_ACCESS): live reads work while it idles."
            : "    The reads did not show the clock advancing about 100 ms each time: live reads are not reliable in WFI.");
        return advancing;
    }

    /// <summary>Sets STALL and checks the kernel's millisecond count stops.</summary>
    private bool CheckStall()
    {
        target.WriteByte(DmCsr2, Stall);
        int millis = kernel.Symbol("millis");
        byte[] a = target.Read(millis, 2);
        target.Link.Sleep(200);
        byte[] b = target.Read(millis, 2);
        int first = (a[0] << 8) | a[1], second = (b[0] << 8) | b[1];
        log($"    millis = {first}, 200 ms later {second}.");
        return first == second;
    }

    /// <summary>With the CPU stalled, reads TIM4's counter with DM_ENFCTR at its reset value, then with TIM4 frozen.</summary>
    private bool CheckFreeze()
    {
        target.WriteByte(DmEnfctr, 0xFF);
        var running = ReadCounter();
        log($"    TIM4_CNTR, everything running: {string.Join(" ", running)}");
        target.WriteByte(DmEnfctr, FreezeTim4AndI2c);
        var frozen = ReadCounter();
        log($"    TIM4_CNTR, TIM4 and I2C frozen: {string.Join(" ", frozen)}");
        bool runs = running.Distinct().Count() > 1, holds = frozen.Distinct().Count() == 1;
        if (!runs) log("    The counter held still even unfrozen: TIM4 may be stopped some other way, so this proves little.");
        return runs && holds;
    }

    private List<int> ReadCounter()
    {
        var values = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            values.Add(target.ReadByte(Tim4Cntr));
            target.Link.Sleep(7);
        }
        return values;
    }

    private void WriteAddress(int register, int address)
    {
        target.WriteByte(register, (byte)(address >> 16));
        target.WriteByte(register + 1, (byte)(address >> 8));
        target.WriteByte(register + 2, (byte)address);
    }
}
