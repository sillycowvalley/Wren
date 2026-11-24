using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

public class PicoLinkTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    private static Kernel Kernel => SharedKernel.Value;

    private static (OledThing Thing, FakePico Fake, ThingLink Link) Connect()
    {
        var thing = new OledThing();
        var fake = new FakePico(thing.Mcu);
        var link = new ThingLink(new PicoLink(fake), "COM9");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        return (thing, fake, link);
    }

    [Fact]
    public void ConnectsAtLowSpeedWithResetReleased()
    {
        var (_, fake, link) = Connect();
        Assert.True(fake.Entered);
        Assert.False(fake.ResetHeld);
        Assert.Equal(16.0, ((PicoLink)link.Link).SyncMicroseconds);
        Assert.Contains("COM9", link.About[0]);
        Assert.Null(link.Version);
    }

    [Fact]
    public void InstallsTheKernelAndUploadsAProgramThatRuns()
    {
        var (thing, fake, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        var program = Compiler.Compile("DIM a\nON START\n a = 4321\nEND ON\n");
        Assert.True(program.Success);
        link.Programmer.UploadProgram(program.Image);
        Assert.Equal(program.Image.Length, link.Programmer.ReadStatus().ProgramLength);
        Assert.True(fake.LargestTransfer <= PicoLink.MaxChunk);

        link.Programmer.Run();
        Assert.False(fake.Entered);
        thing.Mcu.Run(0.1);
        Assert.Null(thing.Mcu.Cpu.Fault);
        int vars = Kernel.Symbol("VARS");
        Assert.Equal(4321, (thing.Mcu.Read(vars) << 8) | thing.Mcu.Read(vars + 1));
    }

    [Fact]
    public void LongReadsAndWritesGoInPieces()
    {
        var (thing, fake, link) = Connect();
        var data = Enumerable.Range(0, 200).Select(i => (byte)(i * 7)).ToArray();
        link.Link.WriteMemory(0x0100, data);
        Assert.Equal(data, Enumerable.Range(0x100, 200).Select(a => thing.Mcu.Read(a)));
        Assert.Equal(data, link.Target.Read(0x0100, 200));
        Assert.Equal(PicoLink.MaxChunk, fake.LargestTransfer);
    }

    [Fact]
    public void ReportsATargetThatDoesNotAnswer()
    {
        var thing = new OledThing();
        var fake = new FakePico(thing.Mcu) { SwimError = PicoLink.StatusNoAnswer };
        var link = new ThingLink(new PicoLink(fake), "COM9");
        link.Link.Sleep = _ => { };
        var ex = Assert.Throws<LinkException>(() => link.Target.Connect());
        Assert.Contains("entry sequence", ex.Message);
        Assert.Contains("line low 16.0 µs", ex.Message);
    }

    [Fact]
    public void TheLineCheckReadsAHealthyLine()
    {
        var (_, _, link) = Connect();
        var lines = ((PicoLink)link.Link).CheckLine();
        Assert.Contains(lines, l => l.Contains("SWIM at rest: high"));
        Assert.Contains(lines, l => l.Contains("90 ns (good"));
        Assert.Contains(lines, l => l.Contains("16 µs sync pulse (good)"));
        Assert.DoesNotContain(lines, l => l.Contains("LOW") || l.Contains("did not answer"));
    }

    [Fact]
    public void RefusesHighSpeed() =>
        Assert.Throws<LinkException>(() => new PicoLink(new MemoryStream()).SetHighSpeed(true));
}
