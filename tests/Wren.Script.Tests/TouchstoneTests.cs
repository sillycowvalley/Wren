using Xunit;

namespace Wren.Script.Tests;

/// <summary>
/// programs/OledThing/Touchstone.bas: the Touchstone-86's displays (docs/Touchstone.md), run
/// through the tester's own example sequence. Each new byte must show within 50 ms (R8).
/// </summary>
public class TouchstoneTests
{
    private static Run Start(int role, byte powerUp)
    {
        string source = Run.ProgramText(Path.Combine("OledThing", "Touchstone.bas"));
        if (role == 1) source = source.Replace("CONST ROLE = 2", "CONST ROLE = 1");
        Assert.Contains($"CONST ROLE = {role}", source);
        return Run.Start(source, t => t.SetInputs(powerUp), seconds: 0.1);
    }

    private static void Latch(Run run, byte value)
    {
        run.Thing.SetInputs(value);
        run.For(0.05);
        Assert.Equal(0, run.VmFault);
    }

    private static string Words(string test, string state)
    {
        var p = new Panel();
        if (test.Length <= 5) p.Text(33 - 6 * test.Length, 0, test, 2);
        else p.Text(14, 3, test, 1);
        if (state.Length <= 5) p.Text(33 - 6 * state.Length, 17, state, 2);
        else p.Text(14, 20, state, 1);
        return p.ToString();
    }

    private static string Digits(int v)
    {
        var p = new Panel();
        p.Text(10, 0, (v >> 4).ToString("X"), 3);
        p.Text(37, 0, (v & 15).ToString("X"), 3);
        return p.ToString();
    }

    private static string NotYetWritten(int v)
    {
        var p = new Panel();
        p.Text(6, 2, "?" + v.ToString("X2"), 3);
        return p.ToString();
    }

    [Fact]
    public void Ds2FollowsTheTestersExampleInWords()
    {
        var run = Start(2, 0xC3);                     // the latch powered up holding C3
        Assert.Equal(NotYetWritten(0xC3), run.Panel); // not "the stack passed" (R6)
        Latch(run, 0x01);
        Assert.Equal(Words("REGS", "READY"), run.Panel);
        Latch(run, 0x02);
        Assert.Equal(Words("RAM", "READY"), run.Panel);
        Latch(run, 0xA2);
        Assert.Equal(Words("RAM", "RUN"), run.Panel);
        Latch(run, 0xF2);
        Assert.Equal(Words("RAM", "FAIL"), run.Panel);
        Latch(run, 0x0B);
        Assert.Equal(Words("ID", "READY"), run.Panel);
        Latch(run, 0xBB);
        Assert.Equal(Words("ID", "RESULT"), run.Panel);
        Latch(run, 0xAD);
        Assert.Equal(Words("SOAK", "RUN"), run.Panel);
        Latch(run, 0xC6);
        Assert.Equal(Words("MULDIV", "PASS"), run.Panel);
        Latch(run, 0xFC);
        Assert.Equal(Words("STATIC", "FAIL"), run.Panel);
    }

    [Fact]
    public void Ds2ShowsEveryTestsName()
    {
        string[] names = { "REGS", "RAM", "STACK", "ALU", "SHIFT", "MULDIV", "STRING", "BCD", "ADDR", "TRAP", "ID", "STATIC", "SOAK", "CORE" };
        var run = Start(2, 0x00);
        for (int t = 1; t <= 14; t++)
        {
            Latch(run, (byte)(0xC0 + t));
            Assert.Equal(Words(names[t - 1], "PASS"), run.Panel);
        }
    }

    [Theory]
    [InlineData(0x10)]                                // a high digit not in the code
    [InlineData(0x2F)]                                // a low digit of F
    [InlineData(0xA0)]                                // a low digit of 0
    [InlineData(0xDD)]
    public void Ds2ShowsAByteOutsideTheCodeAsHex(byte value)
    {
        var run = Start(2, 0x55);
        Latch(run, 0x01);
        Latch(run, value);
        Assert.Equal(Digits(value), run.Panel);
    }

    [Fact]
    public void APowerUpByteEqualToTheFirstWriteKeepsItsMarkTillTheNext()
    {
        var run = Start(2, 0x01);
        Assert.Equal(NotYetWritten(0x01), run.Panel);
        Latch(run, 0x01);                             // written, but no change to see
        Assert.Equal(NotYetWritten(0x01), run.Panel);
        Latch(run, 0x02);                             // NEXT
        Assert.Equal(Words("RAM", "READY"), run.Panel);
    }

    [Fact]
    public void Ds1ShowsItsByteAsTwoBigHexDigitsOnly()
    {
        var run = Start(1, 0xC3);
        Assert.Equal(Digits(0xC3), run.Panel);
        foreach (byte v in new byte[] { 0x00, 0x05, 0x86, 0x30, 0xEE, 0x07 })
        {
            Latch(run, v);
            Assert.Equal(Digits(v), run.Panel);
        }
    }

    [Fact]
    public void Ds1KeepsUpWithTheStaticTestsCount()
    {
        var run = Start(1, 0x00);
        for (int count = 1; count <= 12; count++)    // every 100 ms
        {
            run.Thing.SetInputs((byte)count);
            run.For(0.05);
            Assert.Equal(Digits(count), run.Panel);
            run.For(0.05);
        }
    }

    [Fact]
    public void Size()
    {
        var program = Run.Compile(Run.ProgramText(Path.Combine("OledThing", "Touchstone.bas")));
        Assert.True(program.Image.Length < 512, $"{program.Image.Length} bytes");
    }
}
