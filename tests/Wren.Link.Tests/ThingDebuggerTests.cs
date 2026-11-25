using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

/// <summary>
/// The on-chip debugger against the emulated Thing through the simulated Pico: the chip runs
/// only while a test runs it, between the debugger's calls, as a real one runs between SWIM
/// requests.
/// </summary>
public class ThingDebuggerTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    private static Kernel Kernel => SharedKernel.Value;

    private const string Source =
        "DIM a, b\n" +                  // 1
        "ON START\n" +                  // 2
        " a = 1\n" +                    // 3
        " Add 5\n" +                    // 4
        " b = a\n" +                    // 5
        "END ON\n" +                    // 6
        "SUB Add(n)\n" +                // 7
        " DIM t\n" +                    // 8
        " t = n * 2\n" +                // 9
        " a = a + t\n" +                // 10
        "END SUB\n";                    // 11

    private static (OledThing Thing, ThingDebugger Debugger) Ready()
    {
        var thing = new OledThing();
        var link = new ThingLink(new PicoLink(new FakePico(thing.Mcu)), "COM9");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        link.Target.Connect();
        return (thing, new ThingDebugger(link, Kernel));
    }

    private static CompiledProgram Compile(string source)
    {
        var program = Compiler.Compile(source);
        Assert.True(program.Success, string.Join("\n", program.Errors));
        return program;
    }

    /// <summary>Runs the chip until the debugger reports a pause, as the IDE's tick does.</summary>
    private static void UntilPaused(OledThing thing, ThingDebugger d, double seconds = 1)
    {
        for (int i = 0; i < seconds / 0.002 && d.State != ThingRunState.Paused; i++)
        {
            thing.Mcu.Run(0.002);
            d.Poll();
        }
        Assert.Equal(ThingRunState.Paused, d.State);
    }

    /// <summary>Runs the chip for a while, polling as the IDE's tick does.</summary>
    private static void RunPolling(OledThing thing, ThingDebugger d, double seconds)
    {
        for (int i = 0; i < seconds / 0.002; i++)
        {
            thing.Mcu.Run(0.002);
            d.Poll();
        }
    }

    private static short Global(ThingDebugger d, string name) =>
        short.Parse(d.Globals().Single(v => v.Name == name).Value.Split(' ')[0]);

    [Fact]
    public void StartWithoutDebuggingUploadsAndRuns()
    {
        var (thing, d) = Ready();
        d.Start(Compile(Source), debug: false);
        Assert.False(thing.Mcu.Debug.SwimActive);
        thing.Mcu.Run(0.1);
        int vars = Kernel.Symbol("VARS");
        Assert.Equal(11, (thing.Mcu.Read(vars + 2) << 8) | thing.Mcu.Read(vars + 3));   // b
    }

    [Fact]
    public void StepsStatementByStatementIntoSubs()
    {
        var (thing, d) = Ready();
        d.Start(Compile(Source), debug: true, stopAtFirst: true);
        UntilPaused(thing, d);
        var lines = new List<int> { d.CurrentLine };
        for (int i = 0; i < 4; i++)
        {
            d.StepInto();
            UntilPaused(thing, d);
            lines.Add(d.CurrentLine);
        }
        Assert.Equal(new[] { 3, 4, 9, 10, 5 }, lines);
    }

    [Fact]
    public void StepOverAndStepOut()
    {
        var (thing, d) = Ready();
        d.Start(Compile(Source), debug: true, stopAtFirst: true);
        UntilPaused(thing, d);                       // 3
        d.StepOver(); UntilPaused(thing, d);         // 4
        d.StepOver(); UntilPaused(thing, d);
        Assert.Equal(5, d.CurrentLine);              // the SUB ran without stopping
        Assert.Equal(11, Global(d, "a"));

        var (thing2, d2) = Ready();
        d2.Start(Compile(Source), debug: true, stopAtFirst: true);
        UntilPaused(thing2, d2);
        d2.StepInto(); UntilPaused(thing2, d2);      // 4
        d2.StepInto(); UntilPaused(thing2, d2);      // 9, in the SUB
        Assert.Equal(1, d2.CallDepth);
        d2.StepOut(); UntilPaused(thing2, d2);
        Assert.Equal(5, d2.CurrentLine);
    }

    [Fact]
    public void ABreakpointShowsLocalsAndContinueRunsOn()
    {
        var (thing, d) = Ready();
        d.Breakpoints.Add(10);
        d.Start(Compile(Source), debug: true);
        UntilPaused(thing, d);
        Assert.Equal(10, d.CurrentLine);
        Assert.Equal("Add", d.CurrentRoutine!.Name);
        var locals = d.Locals().ToDictionary(v => v.Name, v => v.Value);
        Assert.Equal("5", locals["n"]);
        Assert.Equal("10  ($000A)", locals["t"]);
        Assert.Equal(1, Global(d, "a"));
        d.Continue();
        thing.Mcu.Run(0.05);
        d.Refresh();
        Assert.Equal(ThingRunState.Running, d.State);
        Assert.Equal(11, Global(d, "b"));
    }

    [Fact]
    public void ABreakpointInALoopStopsEveryTimeRound()
    {
        var (thing, d) = Ready();
        d.Breakpoints.Add(4);
        d.Start(Compile("DIM i, s\nON START\n FOR i = 1 TO 3\n  s = s + i\n NEXT\nEND ON\n"), debug: true);
        var seen = new List<short>();
        for (int round = 0; round < 3; round++)
        {
            UntilPaused(thing, d);
            Assert.Equal(4, d.CurrentLine);
            seen.Add(Global(d, "i"));
            d.Continue();
        }
        Assert.Equal(new short[] { 1, 2, 3 }, seen);
        thing.Mcu.Run(0.05);
        d.Refresh();
        Assert.Equal(6, Global(d, "s"));
    }

    [Fact]
    public void AStepThenContinueDoesNotStopAgainOnTheSameLine()
    {
        var (thing, d) = Ready();
        d.Breakpoints.Add(4);
        d.Start(Compile(Source), debug: true, stopAtFirst: true);
        UntilPaused(thing, d);                       // 3, before its fetch
        d.StepInto(); UntilPaused(thing, d);         // 4, which has the breakpoint
        Assert.Equal(4, d.CurrentLine);
        d.Continue();
        RunPolling(thing, d, 0.05);
        d.Refresh();
        Assert.Equal(ThingRunState.Running, d.State);
        Assert.Equal(11, Global(d, "b"));
    }

    [Fact]
    public void RunToCursorAndBreakAll()
    {
        var (thing, d) = Ready();
        d.Start(Compile(Source), debug: true, stopAtFirst: true);
        UntilPaused(thing, d);
        d.RunToCursor(10);
        UntilPaused(thing, d);
        Assert.Equal(10, d.CurrentLine);

        var (thing2, d2) = Ready();
        d2.Start(Compile("DIM n\nON EVERY 5\n n = n + 1\nEND ON\n"), debug: true);
        thing2.Mcu.Run(0.05);
        d2.Pause();
        UntilPaused(thing2, d2);
        Assert.Equal(3, d2.CurrentLine);
    }

    [Fact]
    public void RefreshReadsAProgramWhileItRunsAndTheTickFreezesWhileStalled()
    {
        var (thing, d) = Ready();
        d.Start(Compile("DIM n\nON EVERY 10\n n = n + 1\nEND ON\n"), debug: true);
        thing.Mcu.Run(0.105);
        d.Refresh();
        short first = Global(d, "n");
        Assert.InRange(first, 9, 11);
        thing.Mcu.Run(0.1);
        d.Refresh();
        Assert.InRange(Global(d, "n") - first, 9, 11);
    }

    [Fact]
    public void StopLeavesTheThingRunningOnItsOwn()
    {
        var (thing, d) = Ready();
        d.Breakpoints.Add(10);
        d.Start(Compile(Source), debug: true);
        UntilPaused(thing, d);
        d.Stop();
        Assert.False(thing.Mcu.Debug.SwimActive);
        thing.Mcu.Run(0.1);
        int vars = Kernel.Symbol("VARS");
        Assert.Equal(11, (thing.Mcu.Read(vars + 2) << 8) | thing.Mcu.Read(vars + 3));   // ran to the end, no breakpoint
    }

    [Fact]
    public void OnlyTwoBreakpointsAreArmed()
    {
        var (thing, d) = Ready();
        d.Breakpoints.UnionWith(new[] { 3, 4, 5, 6 });
        d.Start(Compile(Source), debug: true);
        UntilPaused(thing, d);
        Assert.Equal(new[] { 3, 4 }, d.ArmedBreakpoints);   // 6 has no statement
    }

    [Fact]
    public void TheKernelNeverSleepsSoAnIdleProgramCanBeReadAndPaused()
    {
        // The program finishes ON START and waits for events; a kernel that slept in WFI
        // would be out of SWIM's reach now (the simulated Pico refuses it, as the chip does).
        var (thing, d) = Ready();
        d.Start(Compile("DIM n, m\nON START\n n = 42\nEND ON\nON CHANGE\n m = VALUE\nEND ON\n"), debug: true);
        RunPolling(thing, d, 0.05);
        Assert.Equal(CpuState.Running, thing.Mcu.Cpu.State);
        d.Refresh();
        Assert.Equal(ThingRunState.Running, d.State);
        Assert.Equal(42, Global(d, "n"));
        d.Pause();                                  // idle: it stops at the next statement to come
        thing.SetInputs(0x5A);
        UntilPaused(thing, d);
        Assert.Equal(6, d.CurrentLine);
        Assert.Equal(0x5A, d.EventValue);
    }

    [Fact]
    public void SteppingOverTheEndOfAHandlerRunsOn()
    {
        var (thing, d) = Ready();
        d.Start(Compile("DIM n\nON CHANGE\n n = n + 1\n n = n + 1\nEND ON\n"), debug: true, stopAtFirst: true);
        UntilPaused(thing, d);                       // 3, in the CHANGE at start
        d.StepOver(); UntilPaused(thing, d);
        Assert.Equal(4, d.CurrentLine);
        d.StepOver();                                // off the end of the handler
        RunPolling(thing, d, 0.02);
        Assert.Equal(ThingRunState.Running, d.State);
        thing.SetInputs(0x0F);                       // the next CHANGE: it runs, no stop
        RunPolling(thing, d, 0.05);
        d.Refresh();
        Assert.Equal(ThingRunState.Running, d.State);
        Assert.Equal(4, Global(d, "n"));
    }
}
