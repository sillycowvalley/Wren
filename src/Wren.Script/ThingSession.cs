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
        Board.Mcu.Load(kernel.Image.Bytes);
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
        if (!program.Success) return program;
        Program = program;
        statementStarts = program.Lines.Select(l => l.Address).ToHashSet();
        var upload = ProgramStore.Plan(ReadFat(), program.Image);
        Board.Mcu.Load(upload.Bytes());
        Board.PowerOn();
        UpdateBreakpoints();
        return program;
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
