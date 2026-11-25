using Wren.Asm;
using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>
/// programs/LedThing.bas: the LED Thing's own behaviour in Wren BASIC, on the LED kernel,
/// checked against Digicool's factory firmware (firmware/digicool) run in the emulator.
/// </summary>
public class LedThingProgramTests
{
    private static readonly Lazy<Kernel> LedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware"), Board.LedThing));

    private static readonly Lazy<MemoryImage> Factory = new(() => MemoryImage.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "firmware", "digicool", "8-bit_led_thing.s19"))));

    /// <summary>Sets the mode pins for mode 0-7 (M0 + 2 M1 + 4 M2): a 0 pulls the pin to GND, a 1 lets it float high.</summary>
    private static void SetMode(LedThing thing, int mode)
    {
        thing.M0Low = (mode & 1) == 0;
        thing.M1Low = (mode & 2) == 0;
        thing.M2Low = (mode & 4) == 0;
    }

    private static LedThing Wren(int mode, byte inputs = 0)
    {
        var session = new ThingSession(LedKernel.Value, DeviceInfo.Stm8s003f3);
        var program = session.Upload(Run.ProgramText("LedThing.bas"));
        Assert.True(program.Success, string.Join("\n", program.Errors));
        var thing = session.Leds!;
        thing.SetInputs(inputs);
        SetMode(thing, mode);
        thing.PowerOn();
        return thing;
    }

    private static LedThing Digicool(int mode, byte inputs = 0)
    {
        var thing = new LedThing();
        thing.Mcu.Load(Factory.Value.Bytes);
        thing.SetInputs(inputs);
        SetMode(thing, mode);
        thing.PowerOn();
        return thing;
    }

    /// <summary>The LEDs lit over the next 10 ms (each LED's share of the time above 2%).</summary>
    private static byte Sample(LedThing thing)
    {
        thing.TakeBrightness();
        thing.Mcu.Run(0.010);
        Assert.Null(thing.Mcu.Cpu.Fault);
        var shares = thing.TakeBrightness();
        int lit = 0;
        for (int i = 0; i < 8; i++) if (shares[i] > 0.02) lit |= 1 << i;
        return (byte)lit;
    }

    /// <summary>
    /// The patterns shown, in order, over some time: a pattern counts once it has held for two
    /// samples in a row (so a sample across a step's edge is left out), and a pattern held on
    /// is counted once.
    /// </summary>
    private static List<byte> Patterns(LedThing thing, double seconds)
    {
        var seen = new List<byte>();
        byte? previous = null;
        for (double t = 0; t < seconds; t += 0.010)
        {
            byte now = Sample(thing);
            if (now == previous && (seen.Count == 0 || seen[^1] != now)) seen.Add(now);
            previous = now;
        }
        return seen;
    }

    private static string Hex(IEnumerable<byte> values) => string.Join(" ", values.Select(v => v.ToString("X2")));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EachPatternIsDigicools(int mode)
    {
        var wren = Patterns(Wren(mode), 1.8);
        var digicool = Patterns(Digicool(mode), 1.8);
        int n = Math.Min(wren.Count, digicool.Count) - 1;    // the last may be cut short
        Assert.True(n >= 8, $"too few patterns: {Hex(wren)}");
        Assert.Equal(Hex(digicool.Take(n)), Hex(wren.Take(n)));
    }

    [Fact]
    public void TheDefaultModeShowsTheInputs()
    {
        var thing = Wren(7, 0x5A);
        thing.Mcu.Run(0.05);
        Assert.Equal(0x5A, Sample(thing));
        thing.SetInputs(0x81);
        thing.Mcu.Run(0.02);
        Assert.Equal(0x81, Sample(thing));
        Assert.Equal(Sample(Run7(Digicool(7, 0x81))), Sample(thing));
    }

    private static LedThing Run7(LedThing thing)
    {
        thing.Mcu.Run(0.05);
        return thing;
    }

    private static readonly byte[] Faces = { 0x10, 0x18, 0x34, 0x3C, 0x7A, 0x7E };

    [Fact]
    public void ARollAtPowerUpInMode6EndsOnAFaceAndHoldsIt()
    {
        var thing = Wren(6);
        var shown = Patterns(thing, 2.6);
        // Seven faces, a blank, then the seventh face again, held.
        Assert.Equal(9, shown.Count);
        Assert.All(shown.Take(7), f => Assert.Contains(f, Faces));
        for (int i = 1; i < 7; i++) Assert.NotEqual(shown[i - 1], shown[i]);
        Assert.Equal(0, shown[7]);
        Assert.Equal(shown[6], shown[8]);
        Assert.Equal(shown[8], Sample(thing));

        var digicool = Patterns(Digicool(6), 2.6);
        Assert.Equal(9, digicool.Count);                      // the same shape of roll
    }

    [Fact]
    public void ReleasingTheSwitchOnM0RollsAgain()
    {
        var thing = Wren(6);
        thing.Mcu.Run(2.6);                                   // the power-up roll is over
        SetMode(thing, 7);                                    // the push-to-break switch pressed: M0 floats high
        thing.Mcu.Run(0.1);
        SetMode(thing, 6);                                    // released
        thing.Mcu.Run(0.05);
        var shown = Patterns(thing, 2.6);
        Assert.True(shown.Count >= 8, Hex(shown));
        Assert.Contains((byte)0, shown);
    }

    [Fact]
    public void TheOledThingsWordsAreRefusedForTheLedThing()
    {
        var cls = Compiler.Compile("ON START\n CLS\nEND ON\n", Board.LedThing);
        Assert.False(cls.Success);
        Assert.Contains("OLED Thing", cls.Errors[0].Message);
        var leds = Compiler.Compile("ON START\n LEDS 1\nEND ON\n", Board.OledThing);
        Assert.False(leds.Success);
        Assert.Contains("LED Thing", leds.Errors[0].Message);
        Assert.False(Compiler.Compile("ON PRESS PD2\nEND ON\n", Board.LedThing).Success);
        Assert.True(Compiler.Compile("ON PRESS M0\n LEDS PIN(M1)\nEND ON\n", Board.LedThing).Success);
    }
}
