using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>programs/OledThing.bas: the Thing's own behaviour in Wren BASIC.</summary>
public class OledThingProgramTests
{
    private static Run Start(byte inputs, Action<OledThing>? modes = null) =>
        Run.Start(Run.ProgramText("OledThing.bas"), t =>
        {
            t.SetInputs(inputs);
            modes?.Invoke(t);
        }, seconds: 0.1);

    private static string HexAndBits(int v)
    {
        var p = new Panel();
        p.Text(21, 2, v.ToString("X2"), 2);
        for (int i = 0; i < 8; i++)
        {
            if ((v & (0x80 >> i)) != 0) p.Fill(4 + i * 7, 21, 6, 9);
            else p.Box(4 + i * 7, 21, 6, 9);
        }
        return p.ToString();
    }

    private static string BigHex(int v)
    {
        var p = new Panel();
        p.Text(15, 0, v.ToString("X2"), 3);
        return p.ToString();
    }

    private static string BitsOnly(int v)
    {
        var p = new Panel();
        for (int i = 0; i < 8; i++)
        {
            if ((v & (0x80 >> i)) != 0) p.Fill(i * 8, 8, 7, 16);
            else p.Box(i * 8, 8, 7, 16);
        }
        return p.ToString();
    }

    [Fact]
    public void HexAndBarByDefault()
    {
        var run = Start(0xA5);
        Assert.Equal(HexAndBits(0xA5), run.Panel);
        Assert.False(run.Thing.Oled.Inverse);
        run.Thing.SetInputs(0x3C);
        run.For(0.05);
        Assert.Equal(HexAndBits(0x3C), run.Panel);
    }

    [Fact]
    public void BarLowShowsBigDigits()
    {
        var run = Start(0xF0, t => t.BarLow = true);
        Assert.Equal(BigHex(0xF0), run.Panel);
    }

    [Fact]
    public void HexLowShowsBitsOnly()
    {
        var run = Start(0x96, t => t.HexLow = true);
        Assert.Equal(BitsOnly(0x96), run.Panel);
    }

    [Fact]
    public void ModePinsChangeTheViewWhileRunning()
    {
        var run = Start(0x12);
        run.Thing.BarLow = true;
        run.For(0.05);
        Assert.Equal(BigHex(0x12), run.Panel);
        run.Thing.BarLow = false;
        run.Thing.HexLow = true;
        run.For(0.05);
        Assert.Equal(BitsOnly(0x12), run.Panel);
    }

    [Fact]
    public void InvLowInverts()
    {
        var run = Start(0x00, t => t.InvLow = true);
        Assert.True(run.Thing.Oled.Inverse);
        run.Thing.InvLow = false;
        run.For(0.05);
        Assert.False(run.Thing.Oled.Inverse);
    }

    [Fact]
    public void Pd3LatchesUntilReset()
    {
        var run = Start(0x11);
        run.Thing.SetInputs(0x42);
        run.Thing.Pd3Low = true;
        run.For(0.05);
        run.Thing.SetInputs(0x99);
        run.Thing.Pd3Low = false;
        run.For(0.05);
        Assert.Equal(HexAndBits(0x42), run.Panel);
        run.Thing.Reset();
        run.For(0.1);
        Assert.Equal(HexAndBits(0x99), run.Panel);
    }

    [Fact]
    public void Pd2HoldsWhileLow()
    {
        var run = Start(0x11);
        run.Thing.Pd2Low = true;
        run.For(0.05);
        run.Thing.SetInputs(0x77);
        run.For(0.05);
        Assert.Equal(HexAndBits(0x11), run.Panel);
        run.Thing.Pd2Low = false;
        run.For(0.05);
        Assert.Equal(HexAndBits(0x77), run.Panel);
    }

    [Fact]
    public void DiceRoll()
    {
        // Both mode pins low; the inputs rest high (pulled up) and a button pulls one low.
        var run = Start(0xFF, t => { t.HexLow = true; t.BarLow = true; });
        Assert.Equal(Face(1), run.Panel);

        var faces = new HashSet<int>();
        for (int roll = 0; roll < 12; roll++)
        {
            run.Thing.SetInputs(0xFE);      // b0 pressed
            run.For(0.02);
            run.Thing.SetInputs(0xFF);
            run.For(0.6);                   // the roll animates for about half a second
            int face = Enumerable.Range(1, 6).SingleOrDefault(n => Face(n) == run.Panel);
            Assert.InRange(face, 1, 6);
            Assert.Equal(face, run.Global("die"));
            faces.Add(face);
        }
        Assert.True(faces.Count >= 3, $"Only saw faces {string.Join(",", faces)}");
    }

    private static string Face(int n)
    {
        var p = new Panel();
        p.Box(17, 0, 31, 32);
        void Pip(int x, int y) => p.Fill(x - 2, y - 2, 5, 5);
        if (n % 2 == 1) Pip(32, 16);
        if (n > 1) { Pip(24, 8); Pip(40, 24); }
        if (n > 3) { Pip(40, 8); Pip(24, 24); }
        if (n == 6) { Pip(24, 16); Pip(40, 16); }
        return p.ToString();
    }

    [Fact]
    public void SizeAndWear()
    {
        var program = Run.Compile(Run.ProgramText("OledThing.bas"));
        Assert.True(program.Image.Length < 512, $"{program.Image.Length} bytes");
    }
}
