using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

public class EventTests
{
    [Fact]
    public void StartPinsAndChangeRunAtStartInOrder()
    {
        var run = Run.Start(
            "DIM order, seen\n" +
            "ON START\n order = order * 10 + 1\nEND ON\n" +
            "ON PINS\n order = order * 10 + 2\nEND ON\n" +
            "ON CHANGE\n order = order * 10 + 3\n seen = VALUE\nEND ON\n",
            t => t.SetInputs(0x5A));
        Assert.Equal(123, run.Global("order"));
        Assert.Equal(0x5A, run.Global("seen"));
    }

    [Fact]
    public void ChangeFollowsTheInputs()
    {
        var run = Run.Start("DIM n, v\nON CHANGE\n n = n + 1\n v = VALUE\nEND ON\n");
        run.Thing.SetInputs(0x81);
        run.For(0.01);
        Assert.Equal(0x81, run.Global("v"));
        Assert.Equal(2, run.Global("n"));
    }

    [Fact]
    public void LatchCapturesOnTheChosenEdge()
    {
        var run = Run.Start(
            "DIM f, r, fv, rv\n" +
            "ON LATCH PD3\n f = f + 1\n fv = VALUE\nEND ON\n" +
            "ON LATCH PA1 RISING\n r = r + 1\n rv = VALUE\nEND ON\n");
        run.Thing.SetInputs(0x11);
        run.Thing.Pd3Low = true;            // falling: counted
        run.For(0.005);
        run.Thing.Pd3Low = false;           // rising: not counted
        run.For(0.005);
        Assert.Equal(1, run.Global("f"));
        Assert.Equal(0x11, run.Global("fv"));

        run.Thing.SetInputs(0x22);
        run.Thing.HexLow = true;            // PA1 falling: not counted
        run.For(0.005);
        run.Thing.HexLow = false;           // rising: counted
        run.For(0.005);
        Assert.Equal(1, run.Global("r"));
        Assert.Equal(0x22, run.Global("rv"));
    }

    [Fact]
    public void LatchQualifierLikeWriteEnable()
    {
        // PD3 is the clock, PD2 is /WE: only rising clocks while /WE is low count.
        var run = Run.Start("DIM n, v\nON LATCH PD3 RISING WHILE PD2 LOW\n n = n + 1\n v = VALUE\nEND ON\n",
            t => t.Pd3Low = true);
        void Clock(byte value)
        {
            run.Thing.SetInputs(value);
            run.Thing.Pd3Low = false;
            run.For(0.003);
            run.Thing.Pd3Low = true;
            run.For(0.003);
        }
        Clock(0x01);                        // /WE high: ignored
        run.Thing.Pd2Low = true;
        Clock(0x02);                        // counted
        Clock(0x03);                        // counted
        run.Thing.Pd2Low = false;
        Clock(0x04);                        // ignored
        Assert.Equal(2, run.Global("n"));
        Assert.Equal(0x03, run.Global("v"));
    }

    [Theory]
    [InlineData("", true)]                  // falling
    [InlineData(" RISING", false)]
    public void ALatchAloneOnPortDCatchesAShortStrobe(string edge, bool falling)
    {
        // An 8 MHz 8086's write strobe: 210 ns, far shorter than the interrupt takes to react.
        // The byte stays a few microseconds, as behind a 74HCT574 or a stretched strobe.
        var run = Run.Start($"DIM n, v\nON LATCH PD3{edge}\n n = n + 1\n v = VALUE\nEND ON\n",
            t => t.Pd3Low = !falling);
        for (int i = 1; i <= 10; i++)
        {
            run.Thing.SetInputs((byte)(0x30 + i));
            run.Thing.Pd3Low = falling;
            run.For(210e-9);
            run.Thing.Pd3Low = !falling;
            run.For(4e-6);
            run.Thing.SetInputs(0);
            run.For(0.002);
        }
        Assert.Equal(10, run.Global("n"));
        Assert.Equal(0x3A, run.Global("v"));
    }

    [Fact]
    public void ALatchIsNotHeldUpByTheTick()
    {
        // Strobes 1.013 ms apart walk across the 1 ms tick, so some land while it runs.
        // The byte is there for 8 us only: a LATCH that waited for the tick (26 us) would
        // miss it. An EVERY 1 handler has the tick adding to the queue as the LATCHes do,
        // which it does with interrupts off for up to 4.6 us (emulator cycles).
        var run = Run.Start("DIM n, good, ticks, lostn\n" +
            "ON LATCH PD3\n n = n + 1\n IF VALUE = &H5A THEN good = good + 1\n lostn = LOST\nEND ON\n" +
            "ON EVERY 1\n ticks = ticks + 1\nEND ON\n");
        for (int i = 0; i < 150; i++)
        {
            run.Thing.SetInputs(0x5A);
            run.Thing.Pd3Low = true;
            run.For(210e-9);
            run.Thing.Pd3Low = false;
            run.For(8e-6);
            run.Thing.SetInputs(0);
            run.For(1.013e-3 - 8.21e-6);
        }
        run.For(0.002);
        Assert.Equal(150, run.Global("n"));
        Assert.Equal(150, run.Global("good"));
        Assert.Equal(0, run.Global("lostn"));
        Assert.InRange(run.Global("ticks"), 195, 215);  // 50 before the strobes
    }

    [Theory]
    [InlineData("ON LATCH PD3\nEND ON\n", 4, true)]
    [InlineData("ON LATCH PD2 RISING\nEND ON\n", 3, true)]
    [InlineData("ON LATCH PD3 BOTH\nEND ON\n", 4, false)]
    [InlineData("ON LATCH PD3 WHILE PA1 LOW\nEND ON\n", 4, false)]
    [InlineData("ON LATCH PD3\nEND ON\nON LATCH PD2\nEND ON\n", 4, false)]
    [InlineData("ON LATCH PD3\nEND ON\nON LATCH PA1\nEND ON\n", 4, true)]
    [InlineData("ON LATCH PA1\nEND ON\n", 0, false)]
    public void OnlyALatchAloneOnPortDWithOneEdgeIsSolo(string source, int pin, bool solo)
    {
        var program = Run.Compile(source);
        byte[] image = program.Image;
        int handlers = image[2];
        for (int i = 0, at = 3; i < handlers; i++, at += 4)
        {
            if (image[at] != 3 + pin) continue;
            Assert.Equal(solo, (image[at + 3] & Compiler.SoloLatch) != 0);
            return;
        }
        Assert.Fail("no LATCH handler for the pin");
    }

    [Fact]
    public void PressAndReleaseAreDebounced()
    {
        var run = Run.Start("DIM p, r\nON PRESS PA2\n p = p + 1\nEND ON\nON RELEASE PA2\n r = r + 1\nEND ON\n");
        run.Thing.BarLow = true;
        run.For(0.010);                     // 10 ms: not yet
        Assert.Equal(0, run.Global("p"));
        run.For(0.015);
        Assert.Equal(1, run.Global("p"));
        // a 5 ms glitch is ignored
        run.Thing.BarLow = false;
        run.For(0.005);
        run.Thing.BarLow = true;
        run.For(0.040);
        Assert.Equal(0, run.Global("r"));
        run.Thing.BarLow = false;
        run.For(0.040);
        Assert.Equal(1, run.Global("r"));
    }

    [Fact]
    public void EveryRunsOnTime()
    {
        var run = Run.Start("DIM a, b\nON EVERY 10\n a = a + 1\nEND ON\nON EVERY 25 MS\n b = b + 1\nEND ON\n", seconds: 0.2005);
        Assert.InRange(run.Global("a"), 19, 21);
        Assert.InRange(run.Global("b"), 7, 9);
    }

    [Fact]
    public void WaitAndMillis()
    {
        var run = Run.Start("DIM t0, t1\nON START\n t0 = MILLIS\n WAIT 30\n t1 = MILLIS\nEND ON\n", seconds: 0.1);
        Assert.InRange(run.Global("t1") - run.Global("t0"), 30, 31);
    }

    [Fact]
    public void SettingsSurvivePowerOff()
    {
        var run = Run.Start("DIM r\nON START\n r = EEREAD(3)\n EEWRITE 3, r + 1\nEND ON\n");
        Assert.Equal(0, run.Global("r"));
        run.Thing.PowerOn();
        run.For(0.05);
        Assert.Equal(1, run.Global("r"));
        Assert.Equal(2, run.Mcu.Read(ProgramStore.FatAddress + ProgramStore.SettingsOffset + 3));
    }

    [Fact]
    public void RndStaysInRange()
    {
        var run = Run.Start("DIM r(50), bad\nON START\n DIM i\n FOR i = 0 TO 49\n  r(i) = RND(6)\n NEXT\nEND ON\n");
        var values = Enumerable.Range(0, 50).Select(i => (int)run.Element("r", i)).ToList();
        Assert.All(values, v => Assert.InRange(v, 0, 5));
        Assert.True(values.Distinct().Count() >= 4);
    }
}
