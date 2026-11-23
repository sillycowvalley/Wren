using Xunit;

namespace Wren.Script.Tests;

/// <summary>The kernel's VM, run on the emulated Thing, against C#'s 16-bit arithmetic.</summary>
public class VmTests
{
    private static readonly short[] Values = { 0, 1, -1, 2, -2, 7, -7, 100, -100, 255, 256, 1000, -1000, 32767, -32768, 12345 };

    private static short Reference(string op, short a, short b) => op switch
    {
        "+" => (short)(a + b),
        "-" => (short)(a - b),
        "*" => (short)(a * b),
        "/" => (short)(a / b),
        "MOD" => (short)(a % b),
        "AND" => (short)(a & b),
        "OR" => (short)(a | b),
        "XOR" => (short)(a ^ b),
        "SHL" => (short)(a << (b & 15)),
        "SHR" => (short)((ushort)a >> (b & 15)),
        "=" => (short)(a == b ? -1 : 0),
        "<>" => (short)(a != b ? -1 : 0),
        "<" => (short)(a < b ? -1 : 0),
        "<=" => (short)(a <= b ? -1 : 0),
        ">" => (short)(a > b ? -1 : 0),
        _ => (short)(a >= b ? -1 : 0),
    };

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    [InlineData("MOD")]
    [InlineData("AND")]
    [InlineData("OR")]
    [InlineData("XOR")]
    [InlineData("SHL")]
    [InlineData("SHR")]
    [InlineData("=")]
    [InlineData("<>")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void Operator(string op)
    {
        var rnd = new Random(op.GetHashCode() & 0x7FFF);
        var pairs = new List<(short A, short B)>();
        for (int i = 0; i < 30; i++)
        {
            short a = rnd.Next(2) == 0 ? Values[rnd.Next(Values.Length)] : (short)rnd.Next(-32768, 32768);
            short b = rnd.Next(2) == 0 ? Values[rnd.Next(Values.Length)] : (short)rnd.Next(-32768, 32768);
            if (op is "/" or "MOD" && b == 0) b = 3;
            if (op is "SHL" or "SHR") b = (short)rnd.Next(0, 16);
            pairs.Add((a, b));
        }
        var source = new System.Text.StringBuilder("DIM a(30), b(30), r(30)\nON START\n");
        for (int i = 0; i < pairs.Count; i++)
            source.Append($" a({i}) = {pairs[i].A}\n b({i}) = {pairs[i].B}\n");
        source.Append(" DIM i\n FOR i = 0 TO 29\n");
        source.Append($"  r(i) = a(i) {op} b(i)\n NEXT\nEND ON\n");

        var run = Run.Start(source.ToString());
        Assert.Equal(0, run.VmFault);
        for (int i = 0; i < pairs.Count; i++)
            Assert.True(Reference(op, pairs[i].A, pairs[i].B) == run.Element("r", i),
                $"{pairs[i].A} {op} {pairs[i].B}: expected {Reference(op, pairs[i].A, pairs[i].B)}, got {run.Element("r", i)}");
    }

    [Fact]
    public void UnaryAndBuiltins()
    {
        var run = Run.Start(
            "DIM a, b, n1, n2, n3, ng, ab, mn, mx\n" +
            "ON START\n a = 5\n b = 0\n" +
            " n1 = NOT a\n n2 = NOT b\n n3 = NOT (a > 3)\n ng = -a\n" +
            " ab = ABS(-a)\n mn = MIN(a, -3)\n mx = MAX(a, -3)\nEND ON\n");
        Assert.Equal(0, run.Global("n1"));
        Assert.Equal(-1, run.Global("n2"));
        Assert.Equal(0, run.Global("n3"));
        Assert.Equal(-5, run.Global("ng"));
        Assert.Equal(5, run.Global("ab"));
        Assert.Equal(-3, run.Global("mn"));
        Assert.Equal(5, run.Global("mx"));
    }

    [Fact]
    public void ControlFlow()
    {
        var run = Run.Start(
            "DIM sum, down, w, kind, ex\n" +
            "ON START\n" +
            " DIM i\n" +
            " FOR i = 1 TO 10\n  sum = sum + i\n NEXT\n" +
            " FOR i = 10 TO 1 STEP -3\n  down = down * 10 + i\n NEXT\n" +
            " WHILE w < 100\n  w = w * 2 + 1\n WEND\n" +
            " IF sum = 55 THEN\n  kind = 1\n ELSEIF sum > 0 THEN\n  kind = 2\n ELSE\n  kind = 3\n END IF\n" +
            " FOR i = 1 TO 1000\n  IF i = 7 THEN EXIT FOR\n NEXT\n ex = i\n" +
            "END ON\n");
        Assert.Equal(55, run.Global("sum"));
        Assert.Equal(10741, run.Global("down"));        // 10, 7, 4, 1
        Assert.Equal(127, run.Global("w"));
        Assert.Equal(1, run.Global("kind"));
        Assert.Equal(7, run.Global("ex"));
    }

    [Fact]
    public void ForWithVariableLimit()
    {
        var run = Run.Start("DIM n, c\nON START\n DIM i\n n = 4\n FOR i = 1 TO n * 2\n  c = c + 1\n NEXT\nEND ON\n");
        Assert.Equal(8, run.Global("c"));
    }

    [Fact]
    public void SubsFunctionsLocalsAndRecursion()
    {
        var run = Run.Start(
            "DIM f, s, t\n" +
            "FUNCTION Fact(n)\n IF n <= 1 THEN RETURN 1\n RETURN n * Fact(n - 1)\nEND FUNCTION\n" +
            "SUB AddTo(x, y)\n DIM tmp\n tmp = x + y\n s = tmp\nEND SUB\n" +
            "FUNCTION Twice(v)\n RETURN v * 2\nEND FUNCTION\n" +
            "ON START\n f = Fact(7)\n AddTo 3, 4\n t = Twice(Twice(5)) + Twice(1)\nEND ON\n");
        Assert.Equal(5040, run.Global("f"));
        Assert.Equal(7, run.Global("s"));
        Assert.Equal(22, run.Global("t"));
    }

    [Fact]
    public void ByteArraysWrap()
    {
        var run = Run.Start("DIM b(4) AS BYTE\nON START\n b(0) = 300\n b(1) = -1\nEND ON\n");
        Assert.Equal(44, run.Element("b", 0));
        Assert.Equal(255, run.Element("b", 1));
    }

    [Theory]
    [InlineData("DIM a(3)\nDIM i\nON START\n i = 3\n a(i) = 1\nEND ON\n", 4)]
    [InlineData("DIM z\nON START\n z = 5 / z\nEND ON\n", 5)]
    [InlineData("FUNCTION F(n)\n RETURN F(n + 1)\nEND FUNCTION\nON START\n F 1\nEND ON\n", 3)]
    public void FaultsStopTheProgram(string source, int fault)
    {
        var run = Run.Start(source);
        Assert.Equal(fault, run.VmFault);
        var expected = new Panel();
        expected.Text(0, 0, $"ERR {fault}", 1);
        Assert.Equal(TopLeft(expected.ToString()), TopLeft(run.Panel));
    }

    /// <summary>The first text line (8 rows) and 5 characters (30 columns) of a panel.</summary>
    private static string TopLeft(string panel) =>
        string.Join("\n", panel.Split('\n').Take(8).Select(r => r[..30]));
}
