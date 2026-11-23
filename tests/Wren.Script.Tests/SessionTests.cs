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
    public void UploadsKeepTheWriteCounts()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        for (int i = 0; i < 5; i++) s.Upload(Source);
        var counts = ProgramStore.WriteCounts(s.ReadFat());
        Assert.Equal(5, counts.Sum());
        Assert.Equal(1, counts.Max());
    }
}
