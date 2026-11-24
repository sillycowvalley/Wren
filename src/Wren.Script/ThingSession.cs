using Wren.Emu;

namespace Wren.Script;

/// <summary>Why running stopped.</summary>
public enum SessionStop { TimeUp, Breakpoint, Statement, Fault }

/// <summary>
/// A BASIC program on an emulated Thing, with what a debugger needs: run, step one
/// statement, line breakpoints, variables and the handler that is running.
/// Free of UI code.
/// </summary>
public sealed class ThingSession
{
    private readonly int vmNext;
    private readonly int vpcAddress;
    private readonly int fpAddress;
    private readonly int cspAddress;
    private readonly int cstackTop;
    private readonly int varsAddress;
    private readonly int faultAddress;
    private readonly int eventValueAddress;
    private HashSet<int> statementStarts = new();
    private HashSet<int> breakAddresses = new();
    private HashSet<int> handlerStarts = new();

    public ThingSession(Kernel kernel, DeviceInfo chip)
    {
        Kernel = kernel;
        Board = new OledThing(chip);
        vmNext = kernel.Symbol("vm_next");
        vpcAddress = kernel.Symbol("vpc");
        fpAddress = kernel.Symbol("fp");
        cspAddress = kernel.Symbol("csp");
        cstackTop = kernel.Symbol("CSTACK_TOP");
        varsAddress = kernel.Symbol("VARS");
        faultAddress = kernel.Symbol("vm_fault");
        eventValueAddress = kernel.Symbol("evt_value");
        // The kernel as Wren installs it on this chip: the development board's 640 bytes of
        // EEPROM keep the program settings at $4080.
        Board.Mcu.Load(KernelInfo.ForEeprom(kernel.Image, chip.EepromSize).Bytes);
        Board.PowerOn();
    }

    public Kernel Kernel { get; }
    public OledThing Board { get; }
    public CompiledProgram? Program { get; private set; }

    /// <summary>Source lines with a breakpoint.</summary>
    public HashSet<int> Breakpoints { get; } = new();

    /// <summary>
    /// Compiles source and, if it compiles, uploads it through the FAT (so the block write
    /// counts carry on from the last upload) and powers the Thing on.
    /// </summary>
    public CompiledProgram Upload(string source)
    {
        var program = Compiler.Compile(source);
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
            Board.PowerOn();
            return;
        }
        Program = program;
        statementStarts = program.Lines.Select(l => l.Address).ToHashSet();
        handlerStarts = program.Routines.Where(r => r.Name.StartsWith("ON "))
            .Select(r => program.Lines.Where(l => l.Address >= r.Start && l.Address < r.End).Select(l => l.Address).DefaultIfEmpty(-1).Min())
            .Where(a => a >= 0).ToHashSet();
        var upload = ProgramStore.Plan(ReadFat(), program.Image);
        Board.Mcu.Load(upload.Bytes());
        Board.PowerOn();
        UpdateBreakpoints();
    }

    public byte[] ReadFat()
    {
        var fat = new byte[ProgramStore.FatSize];
        for (int i = 0; i < fat.Length; i++) fat[i] = Board.Mcu.Read(ProgramStore.FatAddress + i);
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

    public int Vpc => Word(vpcAddress);

    /// <summary>True when the CPU is about to run a BASIC instruction (stopped at vm_next).</summary>
    public bool AtVmInstruction => Board.Mcu.Cpu.PC == vmNext;

    /// <summary>The BASIC line about to run, or 0 when no statement is starting.</summary>
    public int CurrentLine => AtVmInstruction && Program != null && statementStarts.Contains(Vpc) ? Program.LineAt(Vpc) : 0;

    /// <summary>The handler, SUB or FUNCTION the VM is in, when stopped at a statement.</summary>
    public RoutineInfo? CurrentRoutine => CurrentLine > 0 ? Program?.RoutineAt(Vpc) : null;

    /// <summary>The program's fault number, 0 while it runs.</summary>
    public int Fault => Board.Mcu.Read(faultAddress);

    public int EventValue => Board.Mcu.Read(eventValueAddress);

    /// <summary>Calls in progress below the current routine.</summary>
    public int CallDepth => (cstackTop - Word(cspAddress)) / 4;

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
        var stop = Board.Mcu.Run(seconds);
        return stop == StopReason.Fault ? SessionStop.Fault : SessionStop.TimeUp;
    }

    /// <summary>
    /// Step Over: runs until a statement starts no deeper in calls than depth (CallDepth when the
    /// step began), or a breakpoint. A SUB's statements run without stopping.
    /// </summary>
    public SessionStop StepOver(double seconds, int depth) =>
        RunUntil(seconds, address => breakAddresses.Contains(address) || (statementStarts.Contains(address) && CallDepth <= depth),
            SessionStop.Statement);

    /// <summary>
    /// Step Out: runs until a statement starts in the routine that called this one (fewer than
    /// depth calls deep), or a breakpoint. In a handler (depth 0) there is no caller: it runs on
    /// to the next statement of a later event.
    /// </summary>
    public SessionStop StepOut(double seconds, int depth) =>
        RunUntil(seconds, address => breakAddresses.Contains(address) || (statementStarts.Contains(address) && (CallDepth < depth || depth == 0 && IsHandlerStart(address))),
            SessionStop.Statement);

    /// <summary>Run To Cursor: runs until line starts, or a breakpoint.</summary>
    public SessionStop RunTo(double seconds, int line)
    {
        var first = Program?.Lines.FirstOrDefault(l => l.Line == line) ?? default;
        int target = first == default ? -1 : first.Address;
        return RunUntil(seconds, address => breakAddresses.Contains(address) || address == target, SessionStop.Statement);
    }

    /// <summary>True for the first statement of an event handler.</summary>
    private bool IsHandlerStart(int address) => handlerStarts.Contains(address);

    /// <summary>True when line has a statement a breakpoint or Run To Cursor can stop on.</summary>
    public bool HasStatement(int line) => Program?.Lines.Any(l => l.Line == line) ?? false;

    private SessionStop RunUntil(double seconds, Func<int, bool> stopAt, SessionStop reason)
    {
        var mcu = Board.Mcu;
        mcu.Breakpoints.Clear();
        mcu.Breakpoints.Add(vmNext);
        mcu.BreakpointFilter = _ => Program != null && stopAt(Vpc);
        try
        {
            var stop = mcu.Run(seconds);
            if (stop == StopReason.Fault) return SessionStop.Fault;
            if (stop == StopReason.Breakpoint) return reason;
            return SessionStop.TimeUp;
        }
        finally
        {
            mcu.Breakpoints.Clear();
            mcu.BreakpointFilter = null;
        }
    }

    // ---- Variables ----

    public sealed record Variable(string Name, string Value);

    public IEnumerable<Variable> Globals()
    {
        if (Program == null) yield break;
        foreach (var g in Program.Globals)
        {
            switch (g.Kind)
            {
                case VariableKind.Integer:
                    yield return new Variable(g.Name, Format(Word(varsAddress + g.Offset)));
                    break;
                default:
                    int shown = Math.Min(g.Length, 8);
                    var items = Enumerable.Range(0, shown).Select(i => g.Kind == VariableKind.ByteArray
                        ? Board.Mcu.Read(varsAddress + g.Offset + i).ToString()
                        : Word(varsAddress + g.Offset + 2 * i).ToString());
                    yield return new Variable($"{g.Name}({g.Length})", string.Join(", ", items) + (g.Length > shown ? ", ..." : ""));
                    break;
            }
        }
    }

    /// <summary>Parameters and locals of the routine at the current statement.</summary>
    public IEnumerable<Variable> Locals()
    {
        var routine = CurrentRoutine;
        if (routine == null) yield break;
        int fp = Word(fpAddress);
        for (int i = 0; i < routine.Locals.Count; i++)
        {
            if (routine.Locals[i].StartsWith('(')) continue; // hidden FOR limits
            yield return new Variable(routine.Locals[i], Format(Word(fp - 2 * i)));
        }
    }

    private static string Format(short v) => v is >= 0 and <= 9 ? v.ToString() : $"{v}  (${(ushort)v:X4})";

    private short Word(int address) => (short)((Board.Mcu.Read(address) << 8) | Board.Mcu.Read(address + 1));
}
