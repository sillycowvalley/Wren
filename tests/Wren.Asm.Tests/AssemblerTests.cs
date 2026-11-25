using Wren.Asm;
using Xunit;

namespace Wren.Asm.Tests;

public class AssemblerTests
{
    private static AssemblyResult Assemble(string source)
    {
        var result = new Assembler().Assemble(source);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result;
    }

    private static string Hex(AssemblyResult r) => string.Join(" ", r.Image.Bytes.Select(kv => kv.Value.ToString("X2")));

    [Fact]
    public void LabelsInColumnOneWithOrWithoutColon()
    {
        var r = Assemble("    org $8000\nstart\n    nop\nnext:\n    jra start\n");
        Assert.Equal(0x8000, r.Symbols["start"]);
        Assert.Equal(0x8001, r.Symbols["next"]);
        Assert.Equal("9D 20 FD", Hex(r));
    }

    [Fact]
    public void ForwardReferenceToShortAddressStaysLong()
    {
        // VAR is defined after use, so the first pass cannot know it is short; the long form is kept.
        var r = Assemble("    org $8000\n    ld a,VAR\nVAR equ $10\n");
        Assert.Equal("C6 00 10", Hex(r));
    }

    [Fact]
    public void BackwardReferenceToShortAddressIsShort()
    {
        var r = Assemble("VAR equ $10\n    org $8000\n    ld a,VAR\n");
        Assert.Equal("B6 10", Hex(r));
    }

    [Fact]
    public void ForwardBranchResolves()
    {
        var r = Assemble("    org $8000\n    jreq done\n    nop\ndone: ret\n");
        Assert.Equal("27 01 9D 81", Hex(r));
    }

    [Fact]
    public void DataDirectives()
    {
        var r = Assemble("    org $8000\n    dc.b 1,$FF,\"Hi\",'A'\n    dc.w $1234,label\nlabel:\n");
        Assert.Equal("01 FF 48 69 41 12 34 80 09", Hex(r));
    }

    [Fact]
    public void ReserveSpaceMovesTheAddress()
    {
        var r = Assemble("    org $0000\nbuf ds.b 16\nwbuf ds.w 2\nafter\n");
        Assert.Equal(0x00, r.Symbols["buf"]);
        Assert.Equal(0x10, r.Symbols["wbuf"]);
        Assert.Equal(0x14, r.Symbols["after"]);
        Assert.Equal(0, r.Image.Count);
    }

    [Fact]
    public void ExpressionsAndFunctions()
    {
        var r = Assemble("BASE = $5000\nX2 EQU BASE+5*2\n    org $8000\n    ld a,#HIGH($1234)\n    ld a,#LOW($1234)\n    ld a,#%1010|1\n    ldw x,#X2\n");
        Assert.Equal("A6 12 A6 34 A6 0B AE 50 0A", Hex(r));
    }

    [Fact]
    public void Aliases()
    {
        var r = Assemble("    org $8000\n    sla a\n    slaw x\n    jrt $8000\n    jruge $8000\n    jrult $8000\n");
        Assert.Equal("48 58 20 FC 24 FA 25 F8", Hex(r));
    }

    [Fact]
    public void RelaxShortensNearJumpsAndCalls()
    {
        var r = Assemble("    org $8000\n    relax\nstart: nop\n    jp start\n    call far\n    call start\n    org $9000\nfar: ret\n");
        // jp start -> JRA (20 FD); call far stays long (CD 90 00); call start -> CALLR (AD F8)
        Assert.Equal("9D 20 FD CD 90 00 AD F8 81", Hex(r));
    }

    [Fact]
    public void IfElseEndifAssembleOneSideAndNest()
    {
        const string source = "    org $8000\nIF BOARD = 2\n    nop\n    IF 0\nskipped: ret\n    ELSE\n    rim\n    ENDIF\nELSE\n    sim\nled: ret\nENDIF\n    halt\n";
        var led = Assemble("BOARD equ 2\n" + source);
        Assert.Equal("9D 9A 8E", Hex(led));
        Assert.False(led.Symbols.ContainsKey("skipped"));
        Assert.False(led.Symbols.ContainsKey("led"));
        var oled = Assemble("BOARD equ 1\n" + source);
        Assert.Equal("9B 81 8E", Hex(oled));
        Assert.True(oled.Symbols.ContainsKey("led"));
    }

    [Fact]
    public void IfNeedsAKnownValueAndAnEndif()
    {
        Assert.False(new Assembler().Assemble("    if LATER\n    nop\n    endif\nLATER equ 1\n").Success);
        Assert.False(new Assembler().Assemble("    if 1\n    nop\n").Success);
        Assert.False(new Assembler().Assemble("    nop\n    endif\n").Success);
    }

    [Fact]
    public void RelaxOffKeepsLongForms()
    {
        var r = Assemble("    org $8000\nstart: nop\n    jp start\n");
        Assert.Equal("9D CC 80 00", Hex(r));
    }

    [Fact]
    public void Include()
    {
        var asm = new Assembler { IncludeResolver = name => name == "regs.inc" ? "PB_ODR equ $5005\n" : null };
        var r = asm.Assemble("    include \"regs.inc\"\n    org $8000\n    bset PB_ODR,#5\n");
        Assert.True(r.Success, string.Join("\n", r.Errors));
        Assert.Equal("72 1A 50 05", Hex(r));
    }

    [Fact]
    public void CommentsAndCase()
    {
        var r = Assemble("    ORG $8000 ; start\n    LD A,#';' ; a semicolon\n    Ld a,(X)\n");
        Assert.Equal("A6 3B F6", Hex(r));
    }

    [Theory]
    [InlineData("    ld a,undefined", "Undefined symbol 'undefined'")]
    [InlineData("    ld x,a", "LD cannot take 'x,a'")]
    [InlineData("    ld a,#300", "Immediate out of range: 300")]
    [InlineData("    bset $5005,#8", "Bit number must be 0 to 7")]
    [InlineData("    frob a", "Unknown instruction or directive 'frob'")]
    [InlineData("    ld a,([$1234.w],y)", "([long.w],Y) does not exist")]
    [InlineData("    jra $9000", "Branch target out of range")]
    public void Errors(string line, string message)
    {
        var r = new Assembler().Assemble("    org $8000\n" + line + "\n");
        Assert.False(r.Success);
        Assert.Contains(message, r.Errors[0].Message);
        Assert.Equal(2, r.Errors[0].Line);
    }

    [Fact]
    public void OverlapIsAnError()
    {
        var r = new Assembler().Assemble("    org $8000\n    dc.b 1,2,3\n    org $8002\n    dc.b 4\n");
        Assert.False(r.Success);
        Assert.Contains("overlaps", r.Errors[0].Message);
        Assert.Equal(4, r.Errors[0].Line);
    }

    [Fact]
    public void DuplicateLabel()
    {
        var r = new Assembler().Assemble("a1:\na1:\n");
        Assert.False(r.Success);
        Assert.Contains("already defined", r.Errors[0].Message);
    }

    [Fact]
    public void IntelHexAndSRecordsRoundTrip()
    {
        var r = Assemble("    org $8000\n    dc.b 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17\n    org $4000\n    dc.b $AA\n");
        foreach (string text in new[] { r.Image.ToIntelHex(), r.Image.ToSRecords() })
        {
            var back = MemoryImage.Parse(text);
            Assert.Equal(r.Image.Bytes, back.Bytes);
        }
        Assert.StartsWith("S1044000AA", r.Image.ToSRecords());
    }
}
