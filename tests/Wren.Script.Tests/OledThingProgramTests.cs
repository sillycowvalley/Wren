using Wren.Asm;
using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>
/// programs/OledThing/OledThing.bas: the Thing's own behaviour in Wren BASIC, checked against
/// Digicool's factory firmware (firmware/digicool) run in the emulator, and against the
/// die faces read from its image. Only the hex digits differ: they are in Wren's font.
/// </summary>
public class OledThingProgramTests
{
    private static Run Start(byte inputs, Action<OledThing>? modes = null) =>
        Run.Start(Run.ProgramText(Path.Combine("OledThing", "OledThing.bas")), t =>
        {
            t.SetInputs(inputs);
            modes?.Invoke(t);
        }, seconds: 0.1);

    private static readonly Lazy<MemoryImage> Factory = new(() => MemoryImage.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "firmware", "digicool", "8-bit_oled_thing_v1.1.s19"))));

    /// <summary>What Digicool's firmware shows for these inputs and mode pins.</summary>
    private static string FactoryPanel(byte inputs, Action<OledThing>? modes = null)
    {
        var thing = new OledThing();
        thing.Mcu.Load(Factory.Value.Bytes);
        thing.SetInputs(inputs);
        modes?.Invoke(thing);
        thing.PowerOn();
        thing.Mcu.Run(0.1);
        return thing.Oled.PanelText();
    }

    /// <summary>Digicool's die face n (1-6): its 32 x 32 bitmap at x 16, from the image.</summary>
    private static string FactoryFace(int n)
    {
        var p = new Panel();
        int table = 0x897B + (n - 1) * 128;
        for (int i = 0; i < 128; i++)
        {
            byte b = Factory.Value[table + i];
            for (int bit = 0; bit < 8; bit++)
                if ((b >> bit & 1) != 0) p.Set(16 + i / 4, (i % 4) * 8 + bit);
        }
        return p.ToString();
    }

    private static string Rows(string panel, int from, int to) =>
        string.Join("\n", panel.Split('\n').Skip(from).Take(to - from + 1));

    private static string Digits(int x, int y, int v, int font, int x2 = -1)
    {
        var p = new Panel();
        if (x2 < 0) p.Text(x, y, v.ToString("X2"), font);
        else
        {
            p.Text(x, y, (v >> 4).ToString("X"), font);
            p.Text(x2, y, (v & 15).ToString("X"), font);
        }
        return p.ToString();
    }

    private static readonly string Blank = new Panel().ToString();

    [Fact]
    public void HexAndBarByDefault()
    {
        var run = Start(0xA5);
        Assert.Equal(Rows(FactoryPanel(0xA5), 24, 31), Rows(run.Panel, 24, 31));   // the bit cells
        Assert.Equal(Rows(Digits(21, 2, 0xA5, 2), 0, 23), Rows(run.Panel, 0, 23));  // Wren's digits
        Assert.False(run.Thing.Oled.Inverse);
        run.Thing.SetInputs(0x3C);
        run.For(0.05);
        Assert.Equal(Rows(FactoryPanel(0x3C), 24, 31), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void BarLowShowsBigDigitsWhereDigicoolsAre()
    {
        var run = Start(0xF0, t => t.BarLow = true);
        Assert.Equal(Digits(10, 0, 0xF0, 3, 37), run.Panel);
    }

    [Fact]
    public void HexLowShowsBitsOnlyAsDigicoolsDoes()
    {
        var run = Start(0x96, t => t.HexLow = true);
        Assert.Equal(FactoryPanel(0x96, t => t.HexLow = true), run.Panel);
    }

    [Fact]
    public void ModePinsChangeTheViewWhileRunning()
    {
        var run = Start(0x12);
        run.Thing.BarLow = true;
        run.For(0.05);
        Assert.Equal(Digits(10, 0, 0x12, 3, 37), run.Panel);
        run.Thing.BarLow = false;
        run.Thing.HexLow = true;
        run.For(0.05);
        Assert.Equal(FactoryPanel(0x12, t => t.HexLow = true), run.Panel);
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
        Assert.Equal(Rows(FactoryPanel(0x42), 24, 31), Rows(run.Panel, 24, 31));
        run.Thing.Reset();
        run.For(0.1);
        Assert.Equal(Rows(FactoryPanel(0x99), 24, 31), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void Pd2HoldsWhileLow()
    {
        var run = Start(0x11);
        run.Thing.Pd2Low = true;
        run.For(0.05);
        run.Thing.SetInputs(0x77);
        run.For(0.05);
        Assert.Equal(Rows(FactoryPanel(0x11), 24, 31), Rows(run.Panel, 24, 31));
        run.Thing.Pd2Low = false;
        run.For(0.05);
        Assert.Equal(Rows(FactoryPanel(0x77), 24, 31), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void EnteringDiceShowsDigicoolsSix()
    {
        var run = Start(0xFF, t => { t.HexLow = true; t.BarLow = true; });
        Assert.Equal(FactoryFace(6), run.Panel);
        Assert.Equal(FactoryPanel(0xFF, t => { t.HexLow = true; t.BarLow = true; }), run.Panel);
    }

    [Fact]
    public void LeavingDiceShowsTheLiveInputs()
    {
        var run = Start(0x11);                  // 11 on screen
        run.Thing.HexLow = true;                // into Dice
        run.Thing.BarLow = true;
        run.Thing.SetInputs(0xFF);              // the inputs change while the die shows
        run.For(0.1);
        run.Thing.HexLow = false;               // back to hex and bits
        run.Thing.BarLow = false;
        run.For(0.1);
        Assert.Equal(Rows(FactoryPanel(0xFF), 24, 31), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void DiceRollsWhileHeldThenSlowsToAResult()
    {
        var run = Start(0xFF, t => { t.HexLow = true; t.BarLow = true; });
        int Shown() => Enumerable.Range(1, 6).SingleOrDefault(n => FactoryFace(n) == run.Panel);

        // Held: a new face every 117 ms, never the same twice in a row.
        run.Thing.SetInputs(0xFE);
        var faces = new List<int>();
        for (int i = 0; i < 100; i++)               // 2 s
        {
            run.For(0.02);
            int face = Shown();
            Assert.InRange(face, 1, 6);
            if (faces.Count == 0 || faces[^1] != face) faces.Add(face);
        }
        Assert.InRange(faces.Count, 15, 19);        // about 2000 / 117 steps
        Assert.True(faces.Distinct().Count() >= 4);

        // Released: it slows, blanks once, then shows the result and stays.
        run.Thing.SetInputs(0xFF);
        bool blank = false;
        for (int i = 0; i < 125; i++)               // 2.5 s
        {
            run.For(0.02);
            if (run.Panel == Blank) blank = true;
            else Assert.InRange(Shown(), 1, 6);
        }
        Assert.True(blank);
        int result = Shown();
        Assert.Equal(result, run.Global("die"));
        run.For(0.5);
        Assert.Equal(FactoryFace(result), run.Panel);
    }

    [Fact]
    public void SizeAndWear()
    {
        var program = Run.Compile(Run.ProgramText(Path.Combine("OledThing", "OledThing.bas")));
        Assert.True(program.Image.Length < 512, $"{program.Image.Length} bytes");
    }
}
