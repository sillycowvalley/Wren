using Wren.Emu;
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
        Assert.True(program.Image.Length < SizeLimit, $"{program.Image.Length} bytes");
    }

    // ---- The tester's testbenches (programs/OledThing/Testbench, written by TTLSim from
    // ---- the test ROM on its 8086 core: what each Thing sees, from power-on) ----

    private const int SizeLimit = 18 * 64;                         // 1,053 bytes, 17 blocks

    /// <summary>Plays a testbench to its end into Touchstone.bas set to the role its name
    /// gives; every panel shown, one a millisecond, and the session.</summary>
    private static (ThingSession Session, List<(double Ms, string Panel)> Panels) Play(string scenario, int role)
    {
        string source = Run.ProgramText(Path.Combine("OledThing", "Touchstone.bas"));
        if (role == 1) source = source.Replace("CONST ROLE = 2", "CONST ROLE = 1");
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        Assert.True(s.Upload(source).Success);
        var bench = Testbench.Load(Path.Combine(AppContext.BaseDirectory, "programs", "OledThing", "Testbench",
            $"Touchstone_{scenario}_DS{role}.bench"));
        var player = new TestbenchPlayer(bench);
        var panels = new List<(double, string)>();
        string blank = new Panel().ToString();                           // before the first SHOW
        while (!player.Finished(s))
        {
            Assert.Equal(SessionStop.TimeUp, player.Run(s, 0.001, s.RunFree));
            string panel = s.Oled!.Oled.PanelText();
            if (panel != blank && (panels.Count == 0 || panels[^1].Item2 != panel)) panels.Add((TestbenchPlayer.NowMs(s), panel));
        }
        Assert.Equal(0, s.Mcu.Read(Run.Kernel.Symbol("vm_fault")));
        return (s, panels);
    }

    private static string Lines(params (int X, int Y, string Text)[] texts)
    {
        var p = new Panel();
        foreach (var (x, y, text) in texts) p.Text(x, y, text, 1);
        return p.ToString();
    }

    private static string RamWord(string at, string bank, string want, string read, string xor) =>
        Lines((0, 0, "@"), (12, 0, at), (42, 0, bank), (0, 8, "WANT"), (30, 8, want),
            (0, 16, "READ"), (30, 16, read), (0, 24, "XOR"), (30, 24, xor));

    private static string Row(string row, string reg, string input, string want, string got) =>
        Lines((0, 0, "ROW"), (24, 0, row), (48, 0, reg), (0, 8, "IN"), (30, 8, input),
            (0, 16, "WANT"), (30, 16, want), (0, 24, "GOT"), (30, 24, got));

    private static string CaseAlone(int c)
    {
        var p = new Panel();
        p.Text(0, 12, "CASE", 1);
        p.Text(28, 0, c.ToString("X2"), 3);
        return p.ToString();
    }

    [Theory]
    [InlineData("tour", "CORE", "RESULT")]
    [InlineData("ram_fault", "RAM", "FAIL")]
    [InlineData("alu_fault", "ALU", "FAIL")]
    [InlineData("mul_fault", "MULDIV", "FAIL")]
    [InlineData("case_fault", "REGS", "FAIL")]
    [InlineData("soak_fault", "RAM", "FAIL")]
    public void Ds2EndsOnTheTestersWords(string scenario, string test, string state)
    {
        var (_, panels) = Play(scenario, 2);
        Assert.Equal(NotYetWritten(0xC3), panels[0].Panel);              // C3 at power-up: not "STACK PASS"
        Assert.Equal(Words(test, state), panels[^1].Panel);
    }

    [Fact]
    public void WithNoCpuBothKeepTheirPowerUpBytes()
    {
        Assert.Equal(new[] { NotYetWritten(0xC3) }, Play("no_cpu", 2).Panels.Select(p => p.Panel));
        Assert.Equal(new[] { Digits(0x5A) }, Play("no_cpu", 1).Panels.Select(p => p.Panel));
    }

    [Fact]
    public void Ds1FollowsTheTourInHex()
    {
        var shown = Play("tour", 1).Panels.Select(p => p.Panel).ToList();
        Assert.Equal(Digits(0x5A), shown[0]);
        Assert.Equal(Digits(0x07), shown[^1]);                          // BE 07: the core's answers
        Assert.Contains(Digits(0x86), shown);                           // BB 86: an 8086
        for (int count = 1; count <= 9; count++) Assert.Contains(Digits(count), shown);   // the static test
    }

    [Theory]
    [InlineData("ram_fault")]
    [InlineData("soak_fault")]
    public void Ds1ShowsTheRamFailure(string scenario)
    {
        var panels = Play(scenario, 1).Panels;
        Assert.Equal(RamWord("0000", "ODD", "FFFF", "DFFF", "2000"), panels[^1].Panel);
        Assert.DoesNotContain(panels, p => p.Panel == Digits(0xFF));   // the lead-in is never drawn
    }

    [Fact]
    public void Ds1ShowsTheTableRows()
    {
        Assert.Equal(Row("0C", "AX", "1234", "0001", "0000"), Play("alu_fault", 1).Panels[^1].Panel);
        Assert.Equal(Row("07", "DX", "FFFF", "FFFE", "0000"), Play("mul_fault", 1).Panels[^1].Panel);
    }

    [Fact]
    public void Ds1ShowsACaseWithNoDetail()
    {
        Assert.Equal(CaseAlone(0x08), Play("case_fault", 1).Panels[^1].Panel);
    }

    /// <summary>
    /// The frame is never drawn as it arrives: from the 00 the test showed while it ran to the
    /// failure, DS1 shows nothing between, and the failure within 50 ms of the frame's end.
    /// </summary>
    [Fact]
    public void Ds1DrawsNothingInTheMiddleOfAFrame()
    {
        var panels = Play("ram_fault", 1).Panels;
        int at = panels.FindIndex(p => p.Panel == RamWord("0000", "ODD", "FFFF", "DFFF", "2000"));
        Assert.Equal(Digits(0x00), panels[at - 1].Panel);
        Assert.InRange(panels[at].Ms - 1347.803, 0, 50);                // Ft nn written at 1347.803 ms
    }
}
