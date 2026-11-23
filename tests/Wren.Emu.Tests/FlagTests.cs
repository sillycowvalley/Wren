using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

/// <summary>Results and flags of single instructions, compared with ucsim (see Data/Flags.txt).</summary>
public class FlagTests
{
    public static IEnumerable<object[]> Cases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "Flags.txt");
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.Trim().Length == 0) continue;
            string[] parts = line.Split('|');
            yield return new object[] { parts[0], parts[1], parts[2] };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesUcsim(string instruction, string before, string expected)
    {
        var bus = new TestBus();
        // "$+5" in the reference data means "skip the following NOP".
        string code = instruction.Contains("$+5")
            ? $"    {instruction.Replace("$+5", "next")}\n    nop\nnext:\n"
            : $"    {instruction}\n";
        bus.Load("    org $8000\n" + code);

        var cpu = new Cpu(bus, 0x3FF);
        int[] b = before.Split(' ').Select(s => Convert.ToInt32(s, 16)).ToArray();
        cpu.A = (byte)b[0];
        cpu.X = (ushort)b[1];
        cpu.Y = (ushort)b[2];
        bus.Memory[0x10] = (byte)b[3];
        bus.Memory[0x11] = (byte)b[4];
        cpu.CC = (byte)b[5];
        cpu.PC = 0x8000;

        cpu.Step();

        // ucsim 0.6.4 sets Z after DIV and DIVW when the quotient is NOT zero. PM0044 says Z is set
        // when the quotient is zero, and Wren follows PM0044 (see DivideSetsZeroFlagForZeroQuotient).
        // So Z is left out of the comparison for those two.
        int ccMask = instruction.StartsWith("div", StringComparison.OrdinalIgnoreCase) ? ~Cpu.FlagZ & 0xFF : 0xFF;
        int expectedCc = Convert.ToInt32(expected[^2..], 16) & ccMask;

        string actual = $"{cpu.A:X2} {cpu.X:X4} {cpu.Y:X4} {bus.Memory[0x10]:X2} {bus.Memory[0x11]:X2} {cpu.CC & ccMask:X2}";
        Assert.Null(cpu.Fault);
        Assert.Equal($"{expected[..^2]}{expectedCc:X2}", actual);
    }

    [Theory]
    [InlineData("div x,a", 0x0005, 0x10, true)]
    [InlineData("div x,a", 0x0100, 0x10, false)]
    [InlineData("divw x,y", 0x0005, 0x0010, true)]
    [InlineData("divw x,y", 0x1000, 0x0010, false)]
    public void DivideSetsZeroFlagForZeroQuotient(string instruction, int x, int divisor, bool zero)
    {
        var bus = new TestBus();
        bus.Load("    org $8000\n    " + instruction + "\n");
        var cpu = new Cpu(bus, 0x3FF) { X = (ushort)x, A = (byte)divisor, Y = (ushort)divisor, PC = 0x8000 };
        cpu.Step();
        Assert.Equal(zero, (cpu.CC & Cpu.FlagZ) != 0);
        Assert.Equal(0, cpu.CC & Cpu.FlagC);
    }
}
