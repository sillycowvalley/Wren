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

    [Fact]
    public void TheLatchCatchesTheInputsAtPd3sRisingEdge()
    {
        var run = Run.Start(Program("OledThing", "Latch.bas"), seconds: 0.1);
        run.Thing.SetInputs(0x5A);
        run.For(0.05);
        Assert.Equal(0, run.Global("count"));                // no edge yet
        run.Thing.Pd3Low = true;
        run.For(0.05);
        Assert.Equal(0, run.Global("count"));                // a falling edge: not latched
        run.Thing.Pd3Low = false;
        run.For(0.05);
        Assert.Equal(0x5A, run.Global("captured"));
        Assert.Equal(1, run.Global("count"));
        run.Thing.SetInputs(0xC3);
        run.For(0.05);
        Assert.Equal(0x5A, run.Global("captured"));          // held until the next edge
        Assert.Equal(0, run.VmFault);
    }

    [Theory]
    [InlineData(3.0, 30, 3333)]
    [InlineData(4.7, 47, 2128)]
    [InlineData(27.3, 273, 366)]
    [InlineData(50.0, 500, 200)]
    public void TheFrequencyCounterTimesAClockOnPd3(double hertz, int tenths, int periodTenths)
    {
        var run = Run.Start(Program("OledThing", "Frequency.bas"), seconds: 0.1);
        double half = 0.5 / hertz;
        for (int i = 0; i < (int)(5 * hertz) + 2; i++)
        {
            run.Thing.Pd3Low = true;
            run.For(half);
            run.Thing.Pd3Low = false;
            run.For(half);
        }
        Assert.Equal(0, run.VmFault);
        Assert.Equal(tenths, run.Global("tenths"));
        Assert.InRange(run.Global("ptenths"), periodTenths - 1, periodTenths + 1);
        run.For(3.5);                                          // the clock stops
        Assert.Equal(0, run.Global("running"));
    }

    [Fact]
    public void TheLatchTestStopsAtTheFirstByteMissed()
    {
        var run = Run.Start(Program("OledThing", "LatchTest.bas"), t => t.Pd3Low = true, seconds: 0.1);
        void Strobe(int value)
        {
            run.Thing.SetInputs((byte)value);
            run.For(0.0001);
            run.Thing.Pd3Low = false;                           // the rising edge
            run.For(0.0001);
            run.Thing.Pd3Low = true;
            run.Thing.SetInputs((byte)~value);                  // noise between strobes
            run.For(0.0048);                                    // 200 strobes a second
        }
        for (int v = 250; v < 250 + 40; v++) Strobe(v & 255);  // round past FF
        Assert.Equal(0, run.Global("failed"));
        Strobe((250 + 41) & 255);                                // one missed
        Assert.Equal(-1, run.Global("failed"));
        Assert.Equal((250 + 41) & 255, run.Global("got"));
        Assert.Equal((250 + 40) & 255, run.Global("want"));

        run.Thing.Pd2Low = true;                                 // PD2 starts it again
        run.For(0.05);
        run.Thing.Pd2Low = false;
        run.For(0.05);
        Assert.Equal(0, run.Global("failed"));
        Assert.Equal(0, run.VmFault);
    }

    [Fact]
    public void TheLogicAnalyzerCapturesACounterOnAChange()
    {
        var run = Run.Start(Program("OledThing", "LogicAnalyzer.bas"), t => t.SetInputs(0), seconds: 0.3);
        Assert.Equal(0, run.Global("taken"));                     // the start-up call is not a trigger
        for (int c = 1; c <= 60; c++)                              // a binary count, a step every 2 ms
        {
            run.Thing.SetInputs((byte)c);
            run.For(0.002);
        }
        Assert.Equal(0, run.VmFault);
        Assert.Equal(64, run.Global("taken"));
        for (int i = 2; i < 64; i++)
        {
            int now = run.Element("samples", i), earlier = run.Element("samples", i - 2);
            Assert.Equal((earlier + 1) & 255, now);                // one count every two samples
        }
    }

    [Fact]
    public void TheBusSnifferLogsTheWritesAndNotTheReads()
    {
        var run = Run.Start(Program("OledThing", "BusSniffer.bas"), t => t.Pd3Low = true, seconds: 0.1);
        string message = "WREN";
        for (int cycle = 0; cycle < 40; cycle++)
        {
            bool write = cycle % 8 == 7;
            run.Thing.Pd2Low = write;                            // the write enable
            run.Thing.SetInputs(write ? (byte)message[cycle / 8 % 4] : (byte)(cycle * 37));
            run.For(0.00005);
            run.Thing.Pd3Low = false;                           // the clock's rising edge
            run.For(0.0001);
            run.Thing.Pd3Low = true;
            run.Thing.Pd2Low = false;
            run.For(0.005);
        }
        Assert.Equal(0, run.VmFault);
        Assert.Equal(5, run.Global("writes"));                  // 40 cycles, one in eight a write
        Assert.Equal((byte)'W', run.Element("written", 0));
        Assert.Equal((byte)'R', run.Element("written", 1));
        Assert.Equal((byte)'W', run.Element("written", 4));
    }

    [Fact]
    public void TheSerialDecoderAssemblesBytesABitAtATime()
    {
        var run = Run.Start(Program("OledThing", "SerialDecoder.bas"), t => t.Pd3Low = true, seconds: 0.1);
        string message = "WREN";
        foreach (char ch in message)
        {
            run.Thing.Pd2Low = true;                            // the select
            run.For(0.002);
            for (int bit = 7; bit >= 0; bit--)
            {
                int noise = (ch * 37 + bit * 11) & 0xFE;        // b1-b7 are noise; b0 is the data
                run.Thing.SetInputs((byte)(noise | ((ch >> bit) & 1)));
                run.For(0.002);
                run.Thing.Pd3Low = false;                       // the clock's rising edge
                run.For(0.002);
                run.Thing.Pd3Low = true;
            }
            run.Thing.Pd2Low = false;
            run.For(0.03);
        }
        Assert.Equal(0, run.VmFault);
        Assert.Equal(4, run.Global("taken"));
        Assert.Equal((byte)'W', run.Element("heard", 0));
        Assert.Equal((byte)'R', run.Element("heard", 1));
        Assert.Equal((byte)'N', run.Element("heard", 3));
    }

    [Fact]
    public void TheFaultDemoCountsDownThenStopsOnADivisionByZero()
    {
        var run = Run.Start(Program("OledThing", "FaultDemo.bas"), seconds: 3);
        Assert.Equal(0, run.VmFault);
        run.For(4);
        Assert.Equal(5, run.VmFault);                           // ERR 5: division by zero
    }

    [Fact]
    public void TheQueueDemoLosesEventsOnlyWhileItsHandlerIsHeavy()
    {
        var run = Run.Start(Program("OledThing", "QueueDemo.bas"), seconds: 4.5);
        Assert.Equal(0, run.Global("heavy"));
        Assert.Equal(0, run.Global("lostRate"));                // light: keeps up
        Assert.InRange(run.Global("rate"), 495, 505);
        run.For(4);
        Assert.Equal(1, run.Global("heavy"));
        Assert.True(run.Global("lostRate") > 100);              // heavy: the queue overflows
        Assert.Equal(0, run.VmFault);
    }

    [Fact]
    public void TheLibraryTourStepsThroughEveryPageWithoutAFault()
    {
        var run = Run.Start(Program("OledThing", "LibraryTour.bas"), seconds: 25);
        Assert.Equal(0, run.VmFault);
    }
}
