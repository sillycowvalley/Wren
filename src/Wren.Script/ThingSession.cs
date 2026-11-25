using Wren.Emu;

namespace Wren.Script;

/// <summary>Why running stopped.</summary>
public enum SessionStop { TimeUp, Breakpoint, Statement, Fault, HandlerEnded }

/// <summary>
/// A BASIC program on an emulated Thing, with what a debugger needs: run, step one
/// statement, line breakpoints, variables and the handler that is running.
/// Free of UI code.
/// </summary>
public sealed class ThingSession
{
    private readonly int vmNext;
    private readonly int vmDone;
    private readonly ProgramView view;
    private HashSet<int> statementStarts = new();
    private HashSet<int> breakAddresses = new();

    /// <summary>
    /// An emulated Thing for the kernel's board: an OLED Thing (or, with the STM8S103F3, the
    /// development board) for the OLED kernel, an LED Thing for the LED kernel.
    /// </summary>
    public ThingSession(Kernel kernel, DeviceInfo chip)
    {
        Kernel = kernel;
        if (kernel.Board == Board.LedThing)
        {
            Leds = new LedThing(chip);
            Mcu = Leds.Mcu;
        }
        else
        {
            Oled = new OledThing(chip);
            Mcu = Oled.Mcu;
        }
        vmNext = kernel.Symbol("vm_next");
        vmDone = kernel.Symbol("vm_done");
        view = new ProgramView(kernel);
        // The kernel as Wren installs it on this chip: the development board's 640 bytes of
        // EEPROM keep the program settings at $4080.
        Mcu.Load(KernelInfo.ForEeprom(kernel.Image, chip.EepromSize).Bytes);
        PowerOn();
    }

    public Kernel Kernel { get; }

    /// <summary>The board the session emulates (the kernel's).</summary>
    public Board Board => Kernel.Board;

    /// <summary>The chip.</summary>
    public Stm8Device Mcu { get; }

    /// <summary>The OLED Thing, on an OLED Thing session; null on an LED Thing session.</summary>
    public OledThing? Oled { get; }

    /// <summary>The LED Thing, on an LED Thing session; null on an OLED Thing session.</summary>
    public LedThing? Leds { get; }

    public CompiledProgram? Program { get; private set; }

    /// <summary>The board's own b0-b7 inputs (bit 0 = b0).</summary>
    public void SetInputs(byte value)
    {
        if (Leds != null) Leds.SetInputs(value);
        else Oled!.SetInputs(value);
    }

    /// <summary>The value on b0-b7 as last set.</summary>
    public byte Inputs => Leds?.Inputs ?? Oled!.Inputs;

    /// <summary>Power on: the chip restarts with the program.</summary>
    public void PowerOn()
    {
        if (Leds != null) Leds.PowerOn();
        else Oled!.PowerOn();
    }

    /// <summary>Source lines with a breakpoint.</summary>
    public HashSet<int> Breakpoints { get; } = new();

    /// <summary>
    /// Compiles source and, if it compiles, uploads it through the FAT (so the block write
    /// counts carry on from the last upload) and powers the Thing on.
    /// </summary>
    public CompiledProgram Upload(string source)
    {
        var program = Compiler.Compile(source, Board);
        if (program.Success) Load(program);
        return program;
    }

    /// <summary>
    /// Loads a compiled program through the FAT and powers the Thing on, as Upload does. The
    /// same program loaded again is not uploaded again: the Thing is only powered on, so the
    /// write counts do not climb with every start.
    /// </summary>
    public void Load(CompiledProgram program)
    {
        if (!program.Success) throw new ArgumentException("The program has errors", nameof(program));
        if (Program != null && Program.Image.AsSpan().SequenceEqual(program.Image))
        {
            Program = program;
            PowerOn();
            return;
        }
        Program = program;
        statementStarts = program.Lines.Select(l => l.Address).ToHashSet();
        var store = new byte[ProgramStore.Blocks * ProgramStore.BlockSize];
        for (int i = 0; i < store.Length; i++) store[i] = Mcu.Read(ProgramStore.StoreAddress + i);
        var upload = ProgramStore.Plan(ReadFat(), program.Image, store);
        Mcu.Load(upload.Bytes());
        PowerOn();
        UpdateBreakpoints();
    }

    public byte[] ReadFat()
    {
        var fat = new byte[ProgramStore.FatSize];
        for (int i = 0; i < fat.Length; i++) fat[i] = Mcu.Read(ProgramStore.FatAddress + i);
        return fat;
    }

    /// <summary>Call after changing Breakpoints.</summary>
    public void UpdateBreakpoints()
    {
        breakAddresses = new HashSet<int>();
        if (Program == null) return;
        foreach (int line in Breakpoints)
        {
            var first = Program.Lines.FirstOrDefault(l => l.Line == line);
            if (first != default) breakAddresses.Add(first.Address);
        }
    }

    // ---- Where the VM is ----

    public int Vpc => view.Vpc(Mcu.Read);

    /// <summary>True when the CPU is about to run a BASIC instruction (stopped at vm_next).</summary>
    public bool AtVmInstruction => Mcu.Cpu.PC == vmNext;

    /// <summary>The BASIC line about to run, or 0 when no statement is starting.</summary>
    public int CurrentLine => AtVmInstruction && Program != null && statementStarts.Contains(Vpc) ? Program.LineAt(Vpc) : 0;

    /// <summary>The handler, SUB or FUNCTION the VM is in, when stopped at a statement.</summary>
    public RoutineInfo? CurrentRoutine => CurrentLine > 0 ? Program?.RoutineAt(Vpc) : null;

    /// <summary>The program's fault number, 0 while it runs.</summary>
    public int Fault => view.Fault(Mcu.Read);

    public int EventValue => view.EventValue(Mcu.Read);

    /// <summary>Calls in progress below the current routine.</summary>
    public int CallDepth => view.CallDepth(Mcu.Read);

    // ---- Running ----

    /// <summary>Runs for some emulated time, stopping at a BASIC breakpoint.</summary>
    public SessionStop Run(double seconds) =>
        RunUntil(seconds, address => breakAddresses.Contains(address), SessionStop.Breakpoint);

    /// <summary>
    /// Runs until the next statement starts (stepping into SUBs), or the time is up. The
    /// statement it is stopped on runs first: the emulator never stops on its first step.
    /// </summary>
    public SessionStop Step(double seconds) =>
        RunUntil(seconds, address => statementStarts.Contains(address), SessionStop.Statement);

    /// <summary>Runs for some emulated time without stopping at breakpoints (Start Without Debugging).</summary>
    public SessionStop RunFree(double seconds)
    {
        var stop = Mcu.Run(seconds);
        return stop == StopReason.Fault ? SessionStop.Fault : SessionStop.TimeUp;
    }

    /// <summary>
    /// Step Over: runs until a statement starts no deeper in calls than depth (CallDepth when the
    /// step began), or a breakpoint. A SUB's statements run without stopping. Stepping over the
    /// end of an event handler (depth 0) returns HandlerEnded: the caller then runs on, as
    /// Visual Studio does stepping off the end of a function with no caller.
    /// </summary>
    public SessionStop StepOver(double seconds, int depth) =>
        RunUntil(seconds, address => breakAddresses.Contains(address) || (statementStarts.Contains(address) && CallDepth <= depth),
            SessionStop.Statement, endsHandler: depth == 0);

    /// <summary>
    /// Step Out: runs until a statement starts in the routine that called this one (fewer than
    /// depth calls deep), or a breakpoint. In an event handler (depth 0) there is no caller: it
    /// returns HandlerEnded when the handler finishes, and the caller runs on.
    /// </summary>
    public SessionStop StepOut(double seconds, int depth) =>
        RunUntil(seconds, address => breakAddresses.Contains(address) || (statementStarts.Contains(address) && CallDepth < depth),
            SessionStop.Statement, endsHandler: depth == 0);

    /// <summary>Run To Cursor: runs until line starts, or a breakpoint.</summary>
    public SessionStop RunTo(double seconds, int line)
    {
        var first = Program?.Lines.FirstOrDefault(l => l.Line == line) ?? default;
        int target = first == default ? -1 : first.Address;
        return RunUntil(seconds, address => breakAddresses.Contains(address) || address == target, SessionStop.Statement);
    }

    /// <summary>True when line has a statement a breakpoint or Run To Cursor can stop on.</summary>
    public bool HasStatement(int line) => Program?.Lines.Any(l => l.Line == line) ?? false;

    private SessionStop RunUntil(double seconds, Func<int, bool> stopAt, SessionStop reason, bool endsHandler = false)
    {
        var mcu = Mcu;
        mcu.Breakpoints.Clear();
        mcu.Breakpoints.Add(vmNext);
        if (endsHandler) mcu.Breakpoints.Add(vmDone);
        mcu.BreakpointFilter = pc => Program != null && (pc == vmDone || stopAt(Vpc));
        try
        {
            var stop = mcu.Run(seconds);
            if (stop == StopReason.Fault) return SessionStop.Fault;
            if (stop == StopReason.Breakpoint) return mcu.Cpu.PC == vmDone ? SessionStop.HandlerEnded : reason;
            return SessionStop.TimeUp;
        }
        finally
        {
            mcu.Breakpoints.Clear();
            mcu.BreakpointFilter = null;
        }
    }

    // ---- Variables ----

    public IEnumerable<Variable> Globals() => Program == null ? Enumerable.Empty<Variable>() : view.Globals(Program, Mcu.Read);

    /// <summary>Parameters and locals of the routine at the current statement.</summary>
    public IEnumerable<Variable> Locals() => CurrentRoutine is { } routine ? view.Locals(routine, Mcu.Read) : Enumerable.Empty<Variable>();
}
