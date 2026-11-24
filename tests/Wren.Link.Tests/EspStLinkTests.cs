using Wren.Asm;
using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

public class EspStLinkTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    private static Kernel Kernel => SharedKernel.Value;

    private static (OledThing Thing, FakeEspStLink Fake, ThingLink Link) Connect()
    {
        var thing = new OledThing();
        var fake = new FakeEspStLink(thing.Mcu);
        var link = new ThingLink(new EspStLink(fake), "COM6");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        return (thing, fake, link);
    }

    [Fact]
    public void ConnectsWithResetReleasedAndAnSrstAfterTheEntry()
    {
        var (_, fake, link) = Connect();
        Assert.True(fake.Entered);
        Assert.False(fake.ResetHeld);
        Assert.False(fake.ResetLetGo); // driven high: a let-go RP2040 pin pulls NRST down
        Assert.Equal(1, fake.Srsts);
        Assert.Equal(16.0, ((EspStLink)link.Link).SyncMicroseconds);
        Assert.Contains("0.1", link.About[0]);
        Assert.Contains("COM6", link.About[0]);
    }

    [Fact]
    public void InstallsTheKernelAndUploadsAProgramThatRuns()
    {
        var (thing, fake, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        var program = Compiler.Compile("DIM a\nON START\n a = 2468\nEND ON\n");
        Assert.True(program.Success);
        link.Programmer.UploadProgram(program.Image);
        Assert.Equal(program.Image.Length, link.Programmer.ReadStatus(Kernel.Image).ProgramLength);
        Assert.True(link.Programmer.ReadStatus(Kernel.Image).HasKernel);

        link.Programmer.Run();
        Assert.False(fake.ResetHeld);
        thing.Mcu.Run(0.1);
        Assert.Null(thing.Mcu.Cpu.Fault);
        int vars = Kernel.Symbol("VARS");
        Assert.Equal(2468, (thing.Mcu.Read(vars) << 8) | thing.Mcu.Read(vars + 1));
    }

    [Fact]
    public void LongReadsAndWritesGoInPieces()
    {
        var (thing, fake, link) = Connect();
        var data = Enumerable.Range(0, 300).Select(i => (byte)(i * 11)).ToArray();
        link.Link.WriteMemory(0x0100, data);
        Assert.Equal(data, Enumerable.Range(0x100, 300).Select(a => thing.Mcu.Read(a)));
        Assert.Equal(data, link.Target.Read(0x0100, 300));
        Assert.Equal(EspStLink.MaxWrite, fake.LargestWrite);
        Assert.Equal(EspStLink.MaxRead, fake.LargestRead);
    }

    [Fact]
    public void BacksUpTheWholeChip()
    {
        var (_, _, link) = Connect();
        Assert.Equal(0x2000 + 128 + 11, link.Programmer.ReadEverything().Count);
    }

    [Fact]
    public void ReportsTheProgrammersErrors()
    {
        var (_, fake, link) = Connect();
        fake.Error = EspStLink.ErrorTimeout;
        var ex = Assert.Throws<LinkException>(() => link.Target.ReadByte(0x7F80));
        Assert.Contains("did not answer in time", ex.Message);
        fake.Error = EspStLink.ErrorNak;
        Assert.Contains("NACK", Assert.Throws<LinkException>(() => link.Target.WriteByte(0x100, 1)).Message);
    }

    [Fact]
    public void AReadOutProtectedThingConnectsAndSaysSo()
    {
        var thing = new OledThing();
        var fake = new FakeEspStLink(thing.Mcu) { ReadOutProtected = true };
        var link = new ThingLink(new EspStLink(fake), "COM7");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        Assert.True(link.Target.NoAccess);
        Assert.Throws<LinkException>(() => link.Programmer.ReadStatus());
    }

    [Fact]
    public void RemovingTheProtectionLeavesABlankUnprotectedChipThatTakesAnImage()
    {
        var thing = new OledThing();
        thing.Mcu.Load(new[] { new KeyValuePair<int, byte>(0x8000, 0x82), new(0x8001, 0x00), new(0x4800, 0xAA), new(0x4010, 0x55) });
        var fake = new FakeEspStLink(thing.Mcu) { ReadOutProtected = true };
        var link = new ThingLink(new EspStLink(fake), "COM7");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        Assert.True(link.Target.NoAccess);

        link.Programmer.RemoveReadOutProtection();
        Assert.False(link.Target.NoAccess);
        var status = link.Programmer.ReadStatus(Kernel.Image);
        Assert.False(status.ReadOutProtected);
        Assert.Equal(FlashContents.Empty, status.Flash);
        Assert.Equal(0, thing.Mcu.Read(0x4010));
        Assert.Equal(ThingProgrammer.DefaultOptions, link.Target.Read(0x4801, 10));

        // A factory-style image: flash only, no option bytes.
        var image = new MemoryImage();
        image.Write(0x8000, Enumerable.Range(0, 300).Select(i => (byte)(i == 0 ? 0x82 : i * 3)).ToArray());
        int written = link.Programmer.WriteImage(image, "factory firmware");
        Assert.Equal(5, written);
        Assert.Equal(0x00, thing.Mcu.Read(0x4800)); // still unprotected
        Assert.Equal(FlashContents.OtherFirmware, link.Programmer.ReadStatus(Kernel.Image).Flash);
    }

    [Fact]
    public void ADeviceThatDoesNotAnswerIsNotAProgrammer()
    {
        var link = new EspStLink(new FakeEspStLink(new OledThing().Mcu) { Silent = true });
        Assert.Throws<TimeoutException>(() => link.ReadVersion());
    }
}
