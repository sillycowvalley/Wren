using System.Security.Cryptography;
using System.Text;
using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>
/// programs/OledThing/OledThing.bas: the Thing's own behaviour in Wren BASIC, checked against
/// what Digicool's factory firmware showed when run in the emulator: its bit cells, drawn here
/// by the same rule, and its die faces, recorded as hashes (the faces are Digicool's artwork).
/// Only the hex digits differ: they are in Wren's font.
/// </summary>
public class OledThingProgramTests
{
    private static Run Start(byte inputs, Action<OledThing>? modes = null) =>
        Run.Start(Run.ProgramText(Path.Combine("OledThing", "OledThing.bas")), t =>
        {
            t.SetInputs(inputs);
            modes?.Invoke(t);
        }, seconds: 0.1);

    /// <summary>
    /// Digicool's bit cells for v, as its firmware draws them in rows top to bottom: eight cells
    /// 7 pixels wide on an 8-pixel pitch, bit 7 at the left, filled for a 1 and outlined for a 0.
    /// </summary>
    private static string Cells(int v, int top, int bottom)
    {
        var p = new Panel();
        for (int bit = 0; bit < 8; bit++)
        {
            bool one = (v >> (7 - bit) & 1) != 0;
            for (int y = top; y <= bottom; y++)
                for (int x = bit * 8; x < bit * 8 + 7; x++)
                    if (one || y == top || y == bottom || x == bit * 8 || x == bit * 8 + 6) p.Set(x, y);
        }
        return p.ToString();
    }

    /// <summary>The bit cells along the bottom (rows 24-31), under the hex digits.</summary>
    private static string FactoryBits(int v) => Rows(Cells(v, 24, 31), 24, 31);

    /// <summary>Digicool's die faces 1-6 as its firmware shows them: the first 16 hex digits of the SHA-256 of the panel's text.</summary>
    private static readonly string[] FaceHashes =
    {
        "DB72D1F1E894F871", "A27DE48C231388A9", "7CECECA77472C761", "167D6998340F7FB6", "AE8F78EF80ED5CDF", "A4AD75470E36B2BB",
    };

    private static string Hash(string panel) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(panel)))[..16];

    /// <summary>The die face shown (1-6), or 0 when the panel shows none of Digicool's faces.</summary>
    private static int FaceShown(string panel) => Array.IndexOf(FaceHashes, Hash(panel)) + 1;

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
        Assert.Equal(FactoryBits(0xA5), Rows(run.Panel, 24, 31));   // the bit cells
        Assert.Equal(Rows(Digits(21, 2, 0xA5, 2), 0, 23), Rows(run.Panel, 0, 23));  // Wren's digits
        Assert.False(run.Thing.Oled.Inverse);
        run.Thing.SetInputs(0x3C);
        run.For(0.05);
        Assert.Equal(FactoryBits(0x3C), Rows(run.Panel, 24, 31));
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
        Assert.Equal(Cells(0x96, 4, 27), run.Panel);
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
        Assert.Equal(Cells(0x12, 4, 27), run.Panel);
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
        Assert.Equal(FactoryBits(0x42), Rows(run.Panel, 24, 31));
        run.Thing.Reset();
        run.For(0.1);
        Assert.Equal(FactoryBits(0x99), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void Pd2HoldsWhileLow()
    {
        var run = Start(0x11);
        run.Thing.Pd2Low = true;
        run.For(0.05);
        run.Thing.SetInputs(0x77);
        run.For(0.05);
        Assert.Equal(FactoryBits(0x11), Rows(run.Panel, 24, 31));
        run.Thing.Pd2Low = false;
        run.For(0.05);
        Assert.Equal(FactoryBits(0x77), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void EnteringDiceShowsDigicoolsSix()
    {
        var run = Start(0xFF, t => { t.HexLow = true; t.BarLow = true; });
        Assert.Equal(6, FaceShown(run.Panel));
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
        Assert.Equal(FactoryBits(0xFF), Rows(run.Panel, 24, 31));
    }

    [Fact]
    public void DiceRollsWhileHeldThenSlowsToAResult()
    {
        var run = Start(0xFF, t => { t.HexLow = true; t.BarLow = true; });
        int Shown() => FaceShown(run.Panel);

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
        Assert.Equal(result, Shown());
    }

    [Fact]
    public void SizeAndWear()
    {
        var program = Run.Compile(Run.ProgramText(Path.Combine("OledThing", "OledThing.bas")));
        Assert.True(program.Image.Length < 512, $"{program.Image.Length} bytes");
    }
}
