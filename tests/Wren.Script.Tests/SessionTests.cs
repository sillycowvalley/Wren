using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

public class SessionTests
{
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

    [Fact]
    public void StepsStatementByStatementIntoSubs()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        Assert.True(s.Upload(Source).Success);
        var lines = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(SessionStop.Statement, s.Step(0.5));
            lines.Add(s.CurrentLine);
        }
        Assert.Equal(new[] { 3, 4, 9, 10, 5 }, lines);
        Assert.Equal(SessionStop.TimeUp, s.Step(0.05)); // nothing more to run: waiting for events
    }

    [Fact]
    public void BreakpointShowsLocals()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Source);
        s.Breakpoints.Add(10);
        s.UpdateBreakpoints();
        Assert.Equal(SessionStop.Breakpoint, s.Run(1));
        Assert.Equal(10, s.CurrentLine);
        Assert.Equal("SUB Add".Split(' ')[1], s.CurrentRoutine!.Name);
        var locals = s.Locals().ToDictionary(v => v.Name, v => v.Value);
        Assert.Equal("5", locals["n"]);
        Assert.Equal("10  ($000A)", locals["t"]);
        Assert.Equal("1", s.Globals().Single(v => v.Name == "a").Value);
        Assert.Equal(SessionStop.TimeUp, s.Run(0.1));
        Assert.Equal("11  ($000B)", s.Globals().Single(v => v.Name == "b").Value);
    }

    [Fact]
    public void StepOverRunsASubWithoutStopping()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Source);
        Assert.Equal(SessionStop.Statement, s.Step(0.5));
        var lines = new List<int> { s.CurrentLine };
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(SessionStop.Statement, s.StepOver(0.5, s.CallDepth));
            lines.Add(s.CurrentLine);
        }
        Assert.Equal(new[] { 3, 4, 5 }, lines);
        Assert.Equal("11  ($000B)", s.Globals().Single(v => v.Name == "a").Value);
    }

    [Fact]
    public void StepOverStopsAtABreakpointInsideTheSub()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Source);
        s.Breakpoints.Add(10);
        s.UpdateBreakpoints();
        s.Step(0.5);                                    // line 3
        s.StepOver(0.5, s.CallDepth);                   // line 4
        Assert.Equal(SessionStop.Statement, s.StepOver(0.5, s.CallDepth));
        Assert.Equal(10, s.CurrentLine);
    }

    [Fact]
    public void StepOutReturnsToTheCaller()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Source);
        for (int i = 0; i < 3; i++) s.Step(0.5);       // 3, 4, 9
        Assert.Equal(9, s.CurrentLine);
        Assert.Equal(SessionStop.Statement, s.StepOut(0.5, s.CallDepth));
        Assert.Equal(5, s.CurrentLine);
    }

    [Fact]
    public void SteppingOverOrOutOfTheEndOfAHandlerRunsOn()
    {
        const string events = "DIM n\nON CHANGE\n n = n + 1\n n = n + 1\nEND ON\n";
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(events);
        Assert.Equal(SessionStop.Statement, s.Step(0.5));           // 3, in the CHANGE at start
        Assert.Equal(SessionStop.Statement, s.StepOver(0.5, s.CallDepth));
        Assert.Equal(4, s.CurrentLine);
        Assert.Equal(SessionStop.HandlerEnded, s.StepOver(0.5, s.CallDepth));
        Assert.Equal(SessionStop.TimeUp, s.Run(0.01));              // the caller runs on
        s.SetInputs(0x0F);                                    // the next CHANGE: no stop
        Assert.Equal(SessionStop.TimeUp, s.Run(0.05));
        Assert.Equal("4", s.Globals().Single().Value);

        var t = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        t.Upload(events);
        t.Step(0.5);
        Assert.Equal(SessionStop.HandlerEnded, t.StepOut(0.5, t.CallDepth));
    }

    [Fact]
    public void RunToCursorStopsAtTheLine()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Source);
        Assert.True(s.HasStatement(10));
        Assert.False(s.HasStatement(6));
        Assert.Equal(SessionStop.Statement, s.RunTo(0.5, 10));
        Assert.Equal(10, s.CurrentLine);
    }

    [Fact]
    public void RunFreeIgnoresBreakpoints()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Source);
        s.Breakpoints.Add(10);
        s.UpdateBreakpoints();
        Assert.Equal(SessionStop.TimeUp, s.RunFree(0.2));
        Assert.Equal("11  ($000B)", s.Globals().Single(v => v.Name == "b").Value);
    }

    [Fact]
    public void LoadingTheSameProgramAgainDoesNotUploadIt()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        var program = Compiler.Compile(Source);
        s.Load(program);
        s.Load(Compiler.Compile(Source));
        Assert.Equal(1, ProgramStore.WriteCounts(s.ReadFat()).Sum());
        s.Load(Compiler.Compile(Source + "' changed\n"));
        Assert.Equal(1, ProgramStore.WriteCounts(s.ReadFat()).Sum()); // a comment changes no bytes
        s.Load(Compiler.Compile(Source.Replace("a = 1", "a = 2")));
        Assert.Equal(2, ProgramStore.WriteCounts(s.ReadFat()).Sum());
    }

    [Fact]
    public void UploadsKeepTheWriteCounts()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        for (int i = 0; i < 5; i++) s.Upload(Source.Replace("a = 1", $"a = {i + 1}"));
        var counts = ProgramStore.WriteCounts(s.ReadFat());
        Assert.Equal(5, counts.Sum());
        Assert.Equal(1, counts.Max());
    }
}
