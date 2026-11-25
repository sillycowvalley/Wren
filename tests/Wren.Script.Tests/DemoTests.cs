using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>The demos in programs/OledThing and programs/LedThing: each runs on its board and does what it says.</summary>
public class DemoTests
{
    private static readonly Lazy<Kernel> LedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware"), Board.LedThing));

    private static string Program(string board, string name) => Run.ProgramText(Path.Combine(board, name));

    [Theory]
    [InlineData("KnightRider.bas")]
    [InlineData("Cylon.bas")]
    public void TheLedThingDemosSweepWithDimmedLeds(string name)
    {
        var session = new ThingSession(LedKernel.Value, DeviceInfo.Stm8s003f3);
        var program = session.Upload(Program("LedThing", name));
        Assert.True(program.Success, string.Join("\n", program.Errors));
        var leds = session.Leds!;
        leds.Mcu.Run(0.05);
        var brightest = new HashSet<int>();
        bool dimmed = false;
        for (int i = 0; i < 60; i++)
        {
            leds.TakeBrightness();
            leds.Mcu.Run(0.032);
            Assert.Equal(0, session.Fault);
            var shares = leds.TakeBrightness();
            double top = shares.Max();
            brightest.Add(Array.IndexOf(shares, top));
            // A dimmed LED: lit, but for half its bright neighbour's time or less.
            if (shares.Any(s => s > 0.01 && s <= top * 0.6)) dimmed = true;
        }
        Assert.Contains(0, brightest);
        Assert.Contains(7, brightest);
        Assert.True(dimmed, "no LED was dimmed");
    }

    [Theory]
    [InlineData("KnightRider.bas", 4, 60)]
    [InlineData("Cylon.bas", 3, 59)]
    public void TheOledThingDemosSweepAcrossTheScreen(string name, int left, int right)
    {
        var run = Run.Start(Program("OledThing", name));
        var panels = new HashSet<string>();
        bool atLeft = false, atRight = false;
        for (int i = 0; i < 40; i++)
        {
            run.For(0.05);
            Assert.Equal(0, run.VmFault);
            panels.Add(run.Panel);
            // The light at either end: the middle row lit there.
            atLeft |= run.Thing.Oled.Pixel(left, 16);
            atRight |= run.Thing.Oled.Pixel(right, 16) && !run.Thing.Oled.Pixel(left, 16);
        }
        Assert.True(panels.Count >= 10, $"only {panels.Count} different pictures");
        Assert.True(atLeft && atRight, $"left {atLeft}, right {atRight}");
    }

    [Fact]
    public void TheSnakePlaysAndEats()
    {
        var run = Run.Start(Program("OledThing", "Snake.bas"));
        var panels = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            run.For(0.1);
            Assert.Equal(0, run.VmFault);
            panels.Add(run.Panel);
        }
        Assert.True(panels.Count >= 20, $"only {panels.Count} different pictures");
        Assert.True(run.Global("score") > 0, "the snake ate nothing in 4 seconds");
    }

    [Fact]
    public void PongPlaysItself()
    {
        var run = Run.Start(Program("OledThing", "Pong.bas"));
        var balls = new HashSet<int>();
        for (int i = 0; i < 40; i++)
        {
            run.For(0.1);
            Assert.Equal(0, run.VmFault);
            balls.Add(run.Global("bx") / 4);
        }
        Assert.True(balls.Min() < 16 && balls.Max() > 48, "the ball did not cross the court");
    }

    /// <summary>The seven-segment readout with ANGLE set to angle.</summary>
    private static Run SevenSegment(byte inputs, int angle) =>
        Run.Start(Program("OledThing", "SevenSegment.bas").Replace("CONST ANGLE = 90 ", $"CONST ANGLE = {angle} "),
            t => t.SetInputs(inputs), seconds: 0.15);

    private static bool[,] Pixels(Run run)
    {
        var p = new bool[64, 32];
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 32; y++) p[x, y] = run.Thing.Oled.Pixel(x, y);
        return p;
    }

    [Fact]
    public void TheSevenSegmentReadoutTurnsAndFollowsTheInputs()
    {
        var at0 = Pixels(SevenSegment(0x5A, 0));
        var at90 = Pixels(SevenSegment(0x5A, 90));
        var at180 = Pixels(SevenSegment(0x5A, 180));
        var at270 = Pixels(SevenSegment(0x5A, 270));
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 32; y++)
            {
                Assert.Equal(at0[x, y], at180[63 - x, 31 - y]);         // half a turn apart
                Assert.Equal(at90[x, y], at270[63 - x, 31 - y]);
            }
        var run = SevenSegment(0x5A, 0);
        run.Thing.SetInputs(0xC3);
        run.For(0.05);
        Assert.Equal(0, run.VmFault);
        var now = Pixels(run);
        Assert.Contains(Enumerable.Range(0, 64 * 32), i => now[i % 64, i / 64] != at0[i % 64, i / 64]);
    }

    [Fact]
    public void RotatedHexTurnsAndFollowsTheInputs()
    {
        Run At(int angle, byte inputs) => Run.Start(Program("OledThing", "RotatedHex.bas").Replace("CONST ANGLE = 90 ", $"CONST ANGLE = {angle} "),
            t => t.SetInputs(inputs), seconds: 0.6);
        var at0 = Pixels(At(0, 0x5A));
        var at180 = Pixels(At(180, 0x5A));
        var at90 = Pixels(At(90, 0x5A));
        var at270 = Pixels(At(270, 0x5A));
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 32; y++)
            {
                Assert.Equal(at0[x, y], at180[63 - x, 31 - y]);
                Assert.Equal(at90[x, y], at270[63 - x, 31 - y]);
            }
        Assert.Contains(Enumerable.Range(0, 64 * 32), i => at0[i % 64, i / 64]);
        var run = At(0, 0x5A);
        run.Thing.SetInputs(0xC3);
        run.For(0.3);
        Assert.Equal(0, run.VmFault);
        var now = Pixels(run);
        Assert.Contains(Enumerable.Range(0, 64 * 32), i => now[i % 64, i / 64] != at0[i % 64, i / 64]);
    }

    [Fact]
    public void TheStopwatchKeepsTimeAndRemembersTheBest()
    {
        var run = Run.Start(Program("OledThing", "Stopwatch.bas"), seconds: 0.1);
        // A button: the pin pulled low for 50 ms, bouncing for the first 3 ms.
        void Press(Action<bool> pin)
        {
            for (int i = 0; i < 3; i++)
            {
                pin(true);
                run.For(0.0005);
                pin(false);
                run.For(0.0005);
            }
            pin(true);
            run.For(0.05);
            pin(false);
            run.For(0.05);
        }
        void StartStop() => Press(low => run.Thing.Pd3Low = low);
        int Time() => run.Global("seconds") * 100 + run.Global("hundredths");

        StartStop();
        run.For(3.0);
        StartStop();
        Assert.Equal(0, run.VmFault);
        Assert.InRange(Time(), 306, 314);                   // 3.1 s from press to press, give or take the debounce
        int best = (run.Mcu.Read(Run.Kernel.Symbol("SETTINGS")) << 8) | run.Mcu.Read(Run.Kernel.Symbol("SETTINGS") + 1);
        Assert.Equal(Time(), best);

        Press(low => run.Thing.Pd2Low = low);                // back to 0
        Assert.Equal(0, Time());
    }

    [Fact]
    public void TheVoltmeterShowsTheVoltageOnPd2()
    {
        foreach (var (level, mv) in new[] { (0, 0), (512, 1652), (767, 2474), (1023, 3300) })
        {
            var run = Run.Start(Program("OledThing", "Voltmeter.bas"), t => t.AnalogPd2 = level, seconds: 0.3);
            Assert.Equal(0, run.VmFault);
            Assert.Equal(level, run.Global("raw"));
            Assert.Equal(mv, run.Global("mv"));
        }
    }
}
