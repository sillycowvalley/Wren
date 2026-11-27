using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>
/// Testbenches (docs/Testbench.md): the file read, its mistakes named by line, and its
/// changes made on the Thing's own clock, at their times, whatever steps the caller runs.
/// </summary>
public class TestbenchTests
{
    [Fact]
    public void ReadsTimesPinsAndTheEnd()
    {
        var bench = Testbench.Parse("# a comment\nTHING OledThing\n0 B=5A\n250.024 B=00 PD3=0   # two pins\nEND 1848\n");
        Assert.Equal(Board.OledThing, bench.Board);
        Assert.Equal(new[] { (0.0, "B", 0x5A), (250.024, "B", 0x00), (250.024, "PD3", 0) },
            bench.Steps.Select(s => (s.Ms, s.Pin, s.Value)).ToArray());
        Assert.Equal(1848, bench.EndMs);
        Assert.Equal(250.024, Testbench.Parse("0 B=01\n250.024 B=02\n").EndMs);
    }

    [Theory]
    [InlineData("10 B=01\n5 B=02\n", "Line 2: times must not go back.")]
    [InlineData("0 B=100\n", "Line 1: '")]
    [InlineData("0 Q=1\n", "Line 1: 'Q=1': pins are B, HEX, BAR, INV, PD2, PD3")]
    [InlineData("THING LedThing\n0 PD2=0\n", "Line 2: 'PD2=0': pins are B, M0, M1, M2")]
    [InlineData("0 HEX=2\n", "Line 1: 'HEX=2': HEX takes 0 or 1.")]
    [InlineData("soon B=01\n", "Line 1: 'soon' is not a time in ms.")]
    [InlineData("THING Toaster\n", "Line 1: THING takes OledThing or LedThing.")]
    public void NamesTheLineThatIsWrong(string text, string message)
    {
        var e = Assert.Throws<FormatException>(() => Testbench.Parse(text));
        Assert.StartsWith(message, e.Message);
    }

    [Fact]
    public void MakesEachChangeAtItsTimeWhateverTheSteps()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        Assert.True(s.Upload("DIM v\nON CHANGE\n  v = VALUE\nEND ON\n").Success);
        var player = new TestbenchPlayer(Testbench.Parse("0 B=11\n3.25 B=22 BAR=0\n7.5 B=33\n"));
        var seen = new List<(double Ms, byte B)>();
        Assert.Equal(SessionStop.TimeUp, player.Run(s, 0.05, step =>
        {
            seen.Add((TestbenchPlayer.NowMs(s), s.Inputs));
            return s.RunFree(step);
        }));
        Assert.Equal(0x11, seen[0].B);                                    // set before the first step
        Assert.Contains(seen, x => Math.Abs(x.Ms - 3.25) < 0.01 && x.B == 0x22);
        Assert.Contains(seen, x => Math.Abs(x.Ms - 7.5) < 0.01 && x.B == 0x33);
        Assert.True(s.Oled!.BarLow);
        Assert.True(player.Finished(s));

        s.PowerOn();                                                       // a reset starts it again
        player.Run(s, 0.001, s.RunFree);
        Assert.Equal(0x11, s.Inputs);
    }
}
