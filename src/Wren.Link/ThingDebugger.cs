using Wren.Script;

namespace Wren.Link;

/// <summary>Where a program on a real Thing stands.</summary>
public enum ThingRunState { Running, Paused, Stepping }

/// <summary>
/// Runs and debugs a BASIC program on a real Thing over SWIM, with the chip's debug module
/// (UM0470 chapter 4) and no kernel code:
/// <list type="bullet">
/// <item>A line breakpoint is a data-read breakpoint on the line's first bytecode in flash,
/// where the VM fetches it. The chip has two, so at most two lines at a time.</item>
/// <item>Stepping and pausing use an instruction breakpoint on the kernel's vm_next, the
/// start of every VM instruction, and stop at the next statement as the emulator does.</item>
/// <item>The variables are read from RAM while the CPU is stalled; a running program is
/// stalled briefly for each read, with the 1 ms tick frozen meanwhile (DM_ENFCTR).</item>
/// </list>
/// The CPU is stalled for every read and decision, so its calls are slow (milliseconds);
/// call them off the UI thread.
/// </summary>
public sealed class ThingDebugger : IDisposable
{
    private const int DmBk1 = 0x7F90, DmBk2 = 0x7F93, DmCr1 = 0x7F96, DmCsr1 = 0x7F98, DmCsr2 = 0x7F99, DmEnfctr = 0x7F9A;
    private const int CpuX = 0x7F04;
    private const byte Stall = 0x08, BreakFlags = 0x06;
    private const byte DataReadOnEither = 0x24, InstructionFetchOnEither = 0x28;
    private const int CpuPc = 0x7F01;
    private const byte CsrDebug = 0xA0;            // SAFE_MASK, SWIM_DM; PRI clear so reads do not stall the CPU

    private enum StepKind { None, Into, Over, Out }

    private readonly ThingLink link;
    private readonly Kernel kernel;
    private readonly ProgramView view;
    private readonly int vmNext;
    private readonly int vmDone;
    private readonly byte[] ram = new byte[1024];
    private CompiledProgram? program;
    private IReadOnlyList<int> blocks = Array.Empty<int>();
    private HashSet<int> statementStarts = new();
    private StepKind step;
    private int stepDepth;
    private int targetLine;
    private int skipVpc = -1;
    private bool pausedAtStatement;
    private bool pausedBeforeFetch;
    private bool started;

    public ThingDebugger(ThingLink link, Kernel kernel)
    {
        this.link = link;
        this.kernel = kernel;
        view = new ProgramView(kernel);
        vmNext = kernel.Symbol("vm_next");
        vmDone = kernel.Symbol("vm_done");
    }

    /// <summary>Messages for the user as the work goes.</summary>
    public event Action<string>? Progress
    {
        add => link.Programmer.Progress += value;
        remove => link.Programmer.Progress -= value;
    }

    /// <summary>Source lines with a breakpoint. Call UpdateBreakpoints after changing them.</summary>
    public HashSet<int> Breakpoints { get; } = new();

    /// <summary>The breakpoints the chip holds now: the first two lines with a statement.</summary>
    public IReadOnlyList<int> ArmedBreakpoints { get; private set; } = Array.Empty<int>();

    public ThingRunState State { get; private set; }
    public CompiledProgram? Program => program;

    /// <summary>
    /// Opens the programmer, connects to the Thing (its CPU stalled) and checks it runs this
    /// Wren's kernel: one whose info block says the current program store and an OLED Thing.
    /// </summary>
    public static ThingDebugger Open(ProgrammerInfo programmer, Kernel kernel) => Attach(ThingLink.Open(programmer), kernel);

    /// <summary>
    /// Finds the programmer on its own: tries the one that worked last, then every other one
    /// found (Raspberry Pi boards and ST-LINKs first), and uses the first that answers as a
    /// programmer; ports that are something else are passed over. found says which it was.
    /// </summary>
    public static ThingDebugger Find(Kernel kernel, string? last, out ProgrammerInfo found)
    {
        var all = ThingLink.Find(out _);
        int preferred = ThingLink.Preferred(all, last);
        var order = preferred < 0 ? all.ToList() : all.Skip(preferred).Take(1).Concat(all.Where((_, i) => i != preferred)).ToList();
        var tried = new List<string>();
        foreach (var programmer in order)
        {
            ThingLink link;
            try
            {
                link = ThingLink.Open(programmer);
            }
            catch (LinkException ex) when (!ex.Message.Contains("already open in Wren", StringComparison.Ordinal))
            {
                tried.Add(programmer.ToString()!);
                continue;
            }
            found = programmer;
            return Attach(link, kernel);
        }
        throw new LinkException(tried.Count == 0
            ? "no programmer is plugged in (a Pico shows in Device Manager under Ports, an ST-LINK under Universal Serial Bus devices)."
            : $"no programmer answered on {string.Join(", ", tried)}.");
    }

    /// <summary>Connects to the Thing through an open programmer and checks its kernel; closes the programmer if that fails.</summary>
    private static ThingDebugger Attach(ThingLink link, Kernel kernel)
    {
        try
        {
            link.Target.Connect();
            if (link.Target.NoAccess)
                throw new LinkException("The Thing refuses memory access (read-out protection?): see Tools → Programmer….");
            var info = link.Programmer.ReadInfo();
            if (info == null || info.FatVersion != ProgramStore.FatVersion || info.StoreAddress != ProgramStore.StoreAddress)
                throw new LinkException("The Thing does not run this Wren's kernel: install it with Tools → Programmer… first.");
            if (info.Board != Board.OledThing)
                throw new LinkException("The Thing's kernel is not the OLED Thing's.");
            return new ThingDebugger(link, kernel);
        }
        catch
        {
            link.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Uploads the program if the Thing holds anything else, then starts it: under the
    /// debugger (reset with the debug module on, stopping at the first statement when
    /// stopAtFirst, or at line runTo), or on its own (debug false: the Thing is reset, left running, and this
    /// debugger is done).
    /// </summary>
    public void Start(CompiledProgram compiled, bool debug, bool stopAtFirst = false, int runTo = 0)
    {
        program = compiled;
        statementStarts = compiled.Lines.Select(l => l.Address).ToHashSet();
        link.Programmer.UploadIfChanged(compiled.Image);
        blocks = ProgramStore.ProgramBlocks(link.Target.Read(ProgramStore.FatAddress, ProgramStore.FatSize));
        if (!debug)
        {
            link.Programmer.Run();
            return;
        }
        // A reset with SWIM kept: the debug module's registers return to their reset values
        // and the CPU waits at the reset vector (UM0470 4.3.1).
        link.Target.WriteByte(SwimTarget.SwimCsr, CsrDebug);
        link.Link.GenerateReset();
        link.Link.Sleep(20);
        link.Target.WriteByte(DmEnfctr, 0xFE);         // TIM4 frozen while stalled: MILLIS and EVERY pause too
        skipVpc = -1;
        pausedAtStatement = pausedBeforeFetch = false;
        targetLine = runTo;
        started = true;
        if (stopAtFirst) Go(StepKind.Into, 0, ThingRunState.Stepping);
        else Go(StepKind.None, 0, ThingRunState.Running);
    }

    // ---- Running ----

    /// <summary>Checks whether the program has stopped (a breakpoint or a step); call it often while running or stepping.</summary>
    public void Poll()
    {
        if (State == ThingRunState.Paused || Asleep) return;
        if ((link.Target.ReadByte(DmCsr2) & Stall) != 0) Stopped();
    }

    /// <summary>
    /// Reads the variables while the program runs: stalls it, reads RAM and lets it go on,
    /// unless a breakpoint stopped it meanwhile.
    /// </summary>
    public void Refresh()
    {
        if (State == ThingRunState.Paused || Asleep) return;
        if ((link.Target.ReadByte(DmCsr2) & Stall) != 0)
        {
            Stopped();
            return;
        }
        link.Target.WriteByte(DmCsr2, Stall);
        if ((link.Target.ReadByte(DmCsr1) & BreakFlags) != 0)
        {
            Stopped();                                 // a breakpoint came first
            return;
        }
        ReadRam();
        if (view.Fault(Read) != 0)
        {
            State = ThingRunState.Paused;              // the program has stopped with a fault
            pausedAtStatement = pausedBeforeFetch = false;
            return;
        }
        link.Target.WriteByte(DmCsr2, 0x00);
    }

    public void Continue()
    {
        if (State != ThingRunState.Paused) return;
        Go(StepKind.None, 0, ThingRunState.Running);
    }

    public void StepInto() => StepFromPause(StepKind.Into);
    public void StepOver() => StepFromPause(StepKind.Over);
    public void StepOut() => StepFromPause(StepKind.Out);

    /// <summary>Runs until line starts (or a breakpoint).</summary>
    public void RunToCursor(int line)
    {
        if (State != ThingRunState.Paused) return;
        targetLine = line;
        Go(StepKind.None, 0, ThingRunState.Running);
    }

    /// <summary>Break All: stops the program at the next statement it starts.</summary>
    public void Pause()
    {
        if (State == ThingRunState.Paused) return;
        link.Target.WriteByte(DmCsr2, Stall);
        if ((link.Target.ReadByte(DmCsr1) & BreakFlags) != 0)
        {
            Stopped();
            if (State == ThingRunState.Paused) return;
            link.Target.WriteByte(DmCsr2, Stall);
        }
        ReadRam();
        Go(StepKind.Into, 0, ThingRunState.Stepping);
    }

    /// <summary>
    /// Stops debugging: the debug module's breakpoints off, the Thing reset and left running
    /// its program on its own, and the programmer let go.
    /// </summary>
    public void Stop()
    {
        link.Target.WriteByte(DmCr1, 0x00);
        link.Target.WriteByte(DmEnfctr, 0xFF);
        link.Programmer.Run();
    }

    /// <summary>Call after changing Breakpoints: re-arms the chip if the program is running.</summary>
    public void UpdateBreakpoints()
    {
        if (!started) return;
        if (State == ThingRunState.Running && step == StepKind.None)
        {
            link.Target.WriteByte(DmCsr2, Stall);
            if ((link.Target.ReadByte(DmCsr1) & BreakFlags) != 0)
            {
                Stopped();
                return;
            }
            Arm();
            link.Target.WriteByte(DmCsr2, 0x00);
        }
        else ArmedBreakpoints = Lines(Breakpoints).Take(2).ToList();
    }

    private void StepFromPause(StepKind kind)
    {
        if (State != ThingRunState.Paused) return;
        Go(kind, CallDepth, ThingRunState.Stepping);
    }

    /// <summary>Arms the chip for the next stop and lets the CPU run.</summary>
    private void Go(StepKind kind, int depth, ThingRunState state)
    {
        step = kind;
        stepDepth = depth;
        // Leaving a pause before a statement's first bytecode is fetched (a step stopped
        // there), the breakpoint on that bytecode must not stop the program again at once.
        skipVpc = State == ThingRunState.Paused && pausedBeforeFetch ? Vpc : -1;
        Arm();
        State = state;
        link.Target.WriteByte(DmCsr2, 0x00);
    }

    private void Arm()
    {
        if (step != StepKind.None)
        {
            WriteAddress(DmBk1, vmNext);
            WriteAddress(DmBk2, vmDone);                // the handler finishing
            link.Target.WriteByte(DmCr1, InstructionFetchOnEither);
            return;
        }
        var lines = Lines(Breakpoints).ToList();
        if (targetLine > 0 && program!.Lines.Any(l => l.Line == targetLine)) lines.Insert(0, targetLine);
        var armed = lines.Distinct().Take(2).ToList();
        ArmedBreakpoints = Lines(Breakpoints).Take(2).ToList();
        if (armed.Count == 0)
        {
            link.Target.WriteByte(DmCr1, 0x00);
            return;
        }
        WriteAddress(DmBk1, Physical(FirstAddress(armed[0])));
        WriteAddress(DmBk2, Physical(FirstAddress(armed[^1])));
        link.Target.WriteByte(DmCr1, DataReadOnEither);
    }

    /// <summary>The CPU is stalled: decide whether this is a stop for the user, or go on.</summary>
    private void Stopped()
    {
        ReadRam();
        int vpc = Vpc;
        bool atStatement = statementStarts.Contains(vpc);
        if (step != StepKind.None && (step == StepKind.Into || stepDepth > 0) && Pc() == vmDone)
        {
            link.Target.WriteByte(DmCsr2, 0x00);       // a handler finished: F11 waits for the next statement anywhere
            return;
        }
        if (step != StepKind.None && Pc() == vmDone)
        {
            // Stepped over or out of the end of an event handler: there is no caller, so the
            // program runs on, stopping only at breakpoints (as Visual Studio does).
            step = StepKind.None;
            Arm();
            State = ThingRunState.Running;
            link.Target.WriteByte(DmCsr2, 0x00);
            return;
        }
        bool stop;
        if (step != StepKind.None)
        {
            // Stalled before vm_next: the VM is about to fetch the instruction at vpc.
            int line = atStatement ? program!.LineAt(vpc) : 0;
            int depth = CallDepth;
            stop = atStatement && (step switch
            {
                StepKind.Over => depth <= stepDepth,
                StepKind.Out => depth < stepDepth,
                _ => true,
            } || Breakpoints.Contains(line));
        }
        else
        {
            // Stalled after a data read of a breakpoint's address: a stop only if it was the
            // VM fetching that statement (not, say, the start-up check reading the program).
            byte[] x = link.Target.Read(CpuX, 2);
            bool fetching = atStatement && ((x[0] << 8) | x[1]) == Physical(vpc);
            int line = fetching ? program!.LineAt(vpc) : 0;
            stop = fetching && vpc != skipVpc && (Breakpoints.Contains(line) || line == targetLine);
            if (fetching && vpc == skipVpc) skipVpc = -1;
        }
        if (stop)
        {
            State = ThingRunState.Paused;
            pausedAtStatement = true;
            pausedBeforeFetch = step != StepKind.None;
            targetLine = 0;
            step = StepKind.None;
            skipVpc = -1;
            Arm();
            return;
        }
        link.Target.WriteByte(DmCsr2, 0x00);
    }

    /// <summary>
    /// The chip asleep in WFI (SWIM_CSR's NO_ACCESS): SWIM cannot reach its RAM or debug module
    /// (UM0470 3.8). Wren's kernel never sleeps for this reason; an older one did. A sleeping
    /// chip cannot have stopped at a breakpoint, so there is nothing to check.
    /// </summary>
    private bool Asleep => (link.Target.ReadByte(SwimTarget.SwimCsr) & 0x40) != 0;

    // ---- Where the program is ----

    private int Vpc => view.Vpc(Read);

    /// <summary>Where the stalled CPU is (PCE, PCH, PCL at $7F01).</summary>
    private int Pc()
    {
        byte[] pc = link.Target.Read(CpuPc, 3);
        return (pc[0] << 16) | (pc[1] << 8) | pc[2];
    }

    /// <summary>The line about to run, when paused at a statement; 0 otherwise.</summary>
    public int CurrentLine => State == ThingRunState.Paused && pausedAtStatement && program != null ? program.LineAt(Vpc) : 0;

    public RoutineInfo? CurrentRoutine => CurrentLine > 0 ? program?.RoutineAt(Vpc) : null;
    public int CallDepth => view.CallDepth(Read);
    public int Fault => view.Fault(Read);
    public int EventValue => view.EventValue(Read);
    public IEnumerable<Variable> Globals() => program == null ? Enumerable.Empty<Variable>() : view.Globals(program, Read);
    public IEnumerable<Variable> Locals() => CurrentRoutine is { } routine ? view.Locals(routine, Read) : Enumerable.Empty<Variable>();

    private byte Read(int address) => address >= 0 && address < ram.Length ? ram[address] : (byte)0;

    /// <summary>Kernel variables and the value stack, the program's variables, the call stack.</summary>
    private void ReadRam()
    {
        foreach (var (start, length) in new[] { (0x000, 0x100), (0x200, 0x100), (0x300, 0x60) })
            Array.Copy(link.Target.Read(start, length), 0, ram, start, length);
    }

    private IEnumerable<int> Lines(IEnumerable<int> lines) =>
        program == null ? Enumerable.Empty<int>() : lines.Where(l => program.Lines.Any(p => p.Line == l)).OrderBy(l => l);

    private int FirstAddress(int line) => program!.Lines.First(l => l.Line == line).Address;

    /// <summary>Where program address a sits in flash, through the program table's chain.</summary>
    private int Physical(int a) => ProgramStore.StoreAddress + blocks[a / ProgramStore.BlockSize] * ProgramStore.BlockSize + a % ProgramStore.BlockSize;

    private void WriteAddress(int register, int address)
    {
        link.Target.WriteByte(register, (byte)(address >> 16));
        link.Target.WriteByte(register + 1, (byte)(address >> 8));
        link.Target.WriteByte(register + 2, (byte)address);
    }

    public void Dispose() => link.Dispose();
}
