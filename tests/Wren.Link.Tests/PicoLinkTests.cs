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
    public void ConnectsAsPicoSwimlibDoes()
    {
        var (_, fake, link) = Connect();
        Assert.True(fake.Entered);
        Assert.False(fake.ResetHeld);
        Assert.False(fake.ResetLetGo); // NRST driven high, not let go
        Assert.Equal(1, fake.Srsts);   // an SRST after the entry
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
    public void UploadsOnlyWhenTheProgramChanged()
    {
        var (thing, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        var first = Compiler.Compile("DIM a\nON START\n a = 1\nEND ON\n").Image;
        Assert.False(link.Programmer.HoldsProgram(first));
        Assert.True(link.Programmer.UploadIfChanged(first));
        int[] writes = (int[])thing.Mcu.FlashWrites.Clone();
        Assert.True(link.Programmer.HoldsProgram(first));
        Assert.False(link.Programmer.UploadIfChanged(first));
        Assert.Equal(writes, thing.Mcu.FlashWrites);
        var second = Compiler.Compile("DIM a\nON START\n a = 2\nEND ON\n").Image;
        Assert.False(link.Programmer.HoldsProgram(second));
        Assert.True(link.Programmer.UploadIfChanged(second));
        Assert.True(link.Programmer.HoldsProgram(second));
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
    }

    [Fact]
    public void RefusesHighSpeed() =>
        Assert.Throws<LinkException>(() => new PicoLink(new MemoryStream()).SetHighSpeed(true));
}
