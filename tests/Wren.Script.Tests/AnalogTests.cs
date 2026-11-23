using Xunit;

namespace Wren.Script.Tests;

public class AnalogTests
{
    private const string Source =
        "DIM a, b, c, p\n" +
        "ON EVERY 5\n a = ANALOG(PD2)\n b = ANALOG(PD3)\n c = ANALOG(PA1)\n p = PIN(PD2)\nEND ON\n";

    [Fact]
    public void ReadsTheLevelsOnPd2AndPd3()
    {
        var run = Run.Start(Source, t => { t.AnalogPd2 = 512; t.AnalogPd3 = 1023; });
        Assert.Equal(512, run.Global("a"));
        Assert.Equal(1023, run.Global("b"));
        Assert.Equal(0, run.Global("c"));   // PA1 has no ADC channel

        run.Thing.AnalogPd2 = 17;
        run.For(0.02);
        Assert.Equal(17, run.Global("a"));
    }

    [Fact]
    public void TurnsThePullUpOff()
    {
        // Nothing drives PD2: with its pull-up on it reads 1; ANALOG turns the pull-up off.
        var run = Run.Start(Source);
        Assert.Equal(0, run.Global("p"));
        Assert.Equal(0, run.Global("a"));
    }

    [Fact]
    public void AnalogBarExample()
    {
        var run = Run.Start("ON EVERY 20\n CLS\n BAR 0, 10, 64, 12, ANALOG(PD3), 1023\n SHOW\nEND ON\n",
            t => t.AnalogPd3 = 1023);
        var p = new Panel();
        p.Box(0, 10, 64, 12);
        p.Fill(1, 11, 62, 10);
        Assert.Equal(p.ToString(), run.Panel);
    }
}
