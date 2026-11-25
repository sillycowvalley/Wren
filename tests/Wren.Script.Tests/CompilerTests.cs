using Wren.Script;
using Xunit;

namespace Wren.Script.Tests;

public class CompilerTests
{
    private static string Code(string source)
    {
        var p = Run.Compile(source);
        // the first handler's code, without the header
        var start = p.Routines[0].Start;
        var lines = new List<string>();
        for (int a = start; a < p.Routines[0].End;)
        {
            var (text, length) = Disassembler.Instruction(p.Image, a);
            lines.Add(text);
            a += length;
        }
        return string.Join("; ", lines);
    }

    [Fact]
    public void ConstantsAreFoldedAndPushedSmall()
    {
        Assert.Equal("PUSH 0; PUSH 10; PUSH 200; PUSH -5; PUSH 1000; BITS; RET",
            Code("ON START\n BITS 2 - 2, 5 * 2, 200, -5, 1000 + 0 * 7\nEND ON\n"));
    }

    [Fact]
    public void BuiltinDefaultsAreAdded()
    {
        Assert.Equal("PUSH 1; PUSH 2; PUSH 1; PIXEL; RET", Code("ON START\n PIXEL 1, 2\nEND ON\n"));
        Assert.Equal("PUSH 0; PUSH 0; PUSH 1; TEXT \"Hi\"; RET", Code("ON START\n TEXT 0, 0, \"Hi\"\nEND ON\n"));
    }

    [Fact]
    public void FirstSixteenGlobalsUseOneByteOpcodes()
    {
        var names = string.Join(", ", Enumerable.Range(0, 17).Select(i => $"v{i}"));
        string code = Code($"DIM {names}\nON START\n v0 = v15\n v16 = 1\nEND ON\n");
        Assert.Equal("LOADG 15; STOREG 0; PUSH 1; STOREG 16; RET", code);
    }

    [Fact]
    public void HexStatementIsSevenBytes()
    {
        var p = Run.Compile("DIM shown\nON START\n HEX 21, 2, shown, 2, LARGE\nEND ON\n");
        Assert.Equal(7 + 1, p.Routines[0].End - p.Routines[0].Start); // 7 bytes and RET
    }

    [Fact]
    public void LongJumpsWhenFarAway()
    {
        var body = string.Concat(Enumerable.Repeat(" PIXEL 1000, 1000, 1\n", 30));
        var p = Run.Compile($"DIM a\nON START\n IF a THEN\n{body} END IF\nEND ON\n");
        Assert.Contains("JZ", Disassembler.Listing(p));
        Assert.Equal(Op.JumpZeroLong, p.Image[p.Routines[0].Start + 1]);
    }

    [Fact]
    public void HandlerTable()
    {
        var p = Run.Compile("ON START\nEND ON\nON LATCH PD3 RISING WHILE PD2 LOW\nEND ON\nON EVERY 250\nEND ON\n");
        byte[] i = p.Image;
        Assert.Equal(new byte[] { 1, 0, 3 }, i.Take(3));
        Assert.Equal(Op.EventStart, i[3]);
        Assert.Equal(Op.EventLatch + 4, i[6]);
        Assert.Equal(0x02 | 0x04 | (3 << 4), i[9]);       // rising, qualifier PD2, low
        Assert.Equal(Op.EventEvery, i[10]);
        Assert.Equal(250, (i[13] << 8) | i[14]);
    }

    [Fact]
    public void LineTable()
    {
        var p = Run.Compile("DIM a\nON START\n a = 1\n\n a = 2\nEND ON\n");
        Assert.Equal(new[] { 3, 5, 6 }, p.Lines.Select(l => l.Line));      // END ON is a stop
        Assert.Equal(3, p.LineAt(p.Lines[0].Address + 1));
    }

    [Fact]
    public void ElseIfTestsAreStops()
    {
        var p = Run.Compile("DIM a\nON START\n IF a = 1 THEN\n  a = 2\n ELSEIF a = 0 THEN\n  a = 3\n END IF\nEND ON\n");
        Assert.Equal(new[] { 3, 4, 5, 6, 8 }, p.Lines.Select(l => l.Line));
    }

    [Theory]
    [InlineData("ON START\n x = 1\nEND ON\n", 2, "'x' is not declared")]
    [InlineData("DIM a\nON START\n a = \nEND ON\n", 3, "Expected a value")]
    [InlineData("ON START\n FROB 1\nEND ON\n", 2, "Unknown statement or SUB 'FROB'")]
    [InlineData("ON START\n PIXEL 1\nEND ON\n", 2, "PIXEL takes 2 to 3 arguments")]
    [InlineData("ON START\nEND ON\nON START\nEND ON\n", 3, "already an ON START handler")]
    [InlineData("ON LATCH PB4\nEND ON\n", 1, "Expected a pin")]
    [InlineData("DIM a(300)\n", 1, "Array size must be 1 to 127")]
    [InlineData("ON START\n IF 1 THEN\n CLS\nEND ON\n", 4, "END ON here, but the block it is in is not closed")]
    [InlineData("CLS\n", 1, "Statements belong inside a SUB or an ON handler")]
    [InlineData("DIM cls\n", 1, "reserved word")]
    [InlineData("SUB S(a)\nEND SUB\nON START\n S\nEND ON\n", 4, "S takes 1 argument")]
    [InlineData("ON START\n TEXT 0, 0, 5\nEND ON\n", 2, "must be a string")]
    [InlineData("ON START\n EXIT FOR\nEND ON\n", 2, "not inside a FOR loop")]
    [InlineData("DIM a\nON START\n a = 1 / 0\nEND ON\n", 3, "Division by zero")]
    public void Errors(string source, int line, string message)
    {
        var p = Compiler.Compile(source);
        Assert.False(p.Success);
        Assert.Equal(line, p.Errors[0].Line);
        Assert.Contains(message, p.Errors[0].Message);
    }

    [Fact]
    public void VariablesMustFitBesideTheProgram()
    {
        var p = Compiler.Compile("DIM a(127), b(1)\nON START\nEND ON\n");
        Assert.False(p.Success);
        Assert.Contains("bytes, but only", p.Errors[0].Message);
    }
}
