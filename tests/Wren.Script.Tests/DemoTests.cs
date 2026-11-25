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
}
