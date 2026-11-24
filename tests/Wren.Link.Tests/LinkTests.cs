using Wren.Asm;
using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

public class LinkTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    private static Kernel Kernel => SharedKernel.Value;

    /// <summary>A blank Thing behind a fake ST-LINK, connected over SWIM.</summary>
    private static (OledThing Thing, FakeStLink Fake, ThingLink Link) Connect(int busyPolls = 0)
    {
        var thing = new OledThing();
        var fake = new FakeStLink(thing.Mcu) { BusyPolls = busyPolls };
        var link = new ThingLink(fake);
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        return (thing, fake, link);
    }

    private static void RunThing(OledThing thing, ThingLink link, double seconds = 0.1)
    {
        link.Programmer.Run();
        thing.Mcu.Run(seconds);
        Assert.Null(thing.Mcu.Cpu.Fault);
    }

    [Fact]
    public void ConnectsOverSwimAtHighSpeed()
    {
        var (_, fake, link) = Connect(busyPolls: 2);
        Assert.Equal(2, link.Version!.StLink);
        Assert.Equal(StLink.ModeSwim, fake.Mode);
        Assert.True(fake.Connected);
        Assert.True(fake.HighSpeed);
    }

    [Fact]
    public void ReadsAndWritesRam()
    {
        var (thing, _, link) = Connect();
        link.Target.WriteByte(0x0123, 0x5A);
        Assert.Equal(0x5A, thing.Mcu.Read(0x0123));
        Assert.Equal(0x5A, link.Target.ReadByte(0x0123));
    }

    [Fact]
    public void InstallsTheKernelAndLocksIt()
    {
        var (thing, _, link) = Connect();
        int written = link.Programmer.InstallKernel(Kernel.Image);
        Assert.True(written > 60);
        link.Target.Connect(); // the chip loads its option bytes at reset: the lock counts from here
        int kernelBlocks = (ProgramStore.StoreAddress - 0x8000) / 64;
        Assert.Equal(kernelBlocks, thing.Mcu.ProtectedBlocks);

        var status = link.Programmer.ReadStatus(Kernel.Image);
        Assert.True(status.HasKernel);
        Assert.False(status.ReadOutProtected);
        Assert.Equal(0, status.ProgramLength);

        RunThing(thing, link);
        Assert.True(thing.Oled.Lit);                       // the kernel runs: "No program"
    }

    [Fact]
    public void InstallingTheSameKernelAgainWritesNothing()
    {
        var (thing, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        var cycles = thing.Mcu.FlashBlockCycles.ToArray();
        Assert.Equal(0, link.Programmer.InstallKernel(Kernel.Image));
        Assert.Equal(cycles, thing.Mcu.FlashBlockCycles);
    }

    [Fact]
    public void UploadsAProgramThatRuns()
    {
        var (thing, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        var program = Compiler.Compile("DIM a\nON START\n a = 1234\nEND ON\n");
        Assert.True(program.Success);
        link.Programmer.UploadProgram(program.Image);

        var status = link.Programmer.ReadStatus();
        Assert.Equal(program.Image.Length, status.ProgramLength);
        Assert.Equal(1, status.Uploads);
        Assert.Equal(1, status.WriteCounts.Sum());

        RunThing(thing, link);
        int vars = Kernel.Symbol("VARS");
        Assert.Equal(1234, (thing.Mcu.Read(vars) << 8) | thing.Mcu.Read(vars + 1));
    }

    [Fact]
    public void UploadsSpreadTheWear()
    {
        var (_, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        var program = Compiler.Compile("DIM a\nON START\n a = 1\nEND ON\n").Image;
        for (int i = 0; i < 10; i++) link.Programmer.UploadProgram(program);
        var counts = link.Programmer.ReadStatus().WriteCounts;
        Assert.Equal(10, counts.Sum());
        Assert.Equal(1, counts.Max());
    }

    [Fact]
    public void TheLockedKernelCannotBeWrittenByAccident()
    {
        var (_, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        link.Target.Connect(); // reset: the lock takes effect
        var ex = Assert.Throws<LinkException>(() => link.Target.WriteBlocks(0x8100, new byte[64], TargetMemory.Flash));
        Assert.Contains("write-protected", ex.Message);
    }

    [Fact]
    public void AChangedKernelInstallsOverALockedOne()
    {
        // As on a real Thing: the lock is lifted, the chip reset so it counts, then only the
        // changed block is written.
        var (thing, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        link.Target.Connect();
        Assert.True(thing.Mcu.ProtectedBlocks > 0);

        var changed = new Wren.Asm.MemoryImage();
        foreach (var (address, value) in Kernel.Image.Bytes) changed[address] = value;
        changed[0x8100] = (byte)(Kernel.Image[0x8100] ^ 0xFF);
        Assert.Equal(1, link.Programmer.InstallKernel(changed));
        Assert.Equal((byte)(Kernel.Image[0x8100] ^ 0xFF), thing.Mcu.Read(0x8100));
    }

    [Fact]
    public void ReportsATargetThatDoesNotAnswer()
    {
        var thing = new OledThing();
        var fake = new FakeStLink(thing.Mcu) { StatusError = StLink.StatusNoResponse };
        var link = new ThingLink(fake);
        link.Link.Sleep = _ => { };
        var ex = Assert.Throws<LinkException>(() => link.Target.Connect());
        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public void CommandPacketsAreSixteenBytesAndLongWritesFollowThem()
    {
        var (thing, _, link) = Connect();
        var data = Enumerable.Range(0, 64).Select(i => (byte)(i + 1)).ToArray();
        link.Link.WriteMemory(0x0200, data);              // 8 bytes in the packet, 56 after it
        Assert.Equal(data, Enumerable.Range(0x200, 64).Select(a => thing.Mcu.Read(a)));
    }

    [Fact]
    public void OtherFirmwareIsRecognisedAndBackedUpWithoutWriting()
    {
        var thing = new OledThing();
        var other = Enumerable.Range(0, 0x2000).Select(i => new KeyValuePair<int, byte>(0x8000 + i, (byte)(i * 13))).ToList();
        other[0] = new(0x8000, 0x82);
        thing.Mcu.Load(other);
        thing.Mcu.Load(new[] { new KeyValuePair<int, byte>(0x4000, 0x01), new(0x4001, 0x07) }); // looks like a FAT
        var fake = new FakeStLink(thing.Mcu);
        var link = new ThingLink(fake);
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        byte[] flashBefore = Enumerable.Range(0x8000, 0x2000).Select(a => thing.Mcu.Read(a)).ToArray();

        var status = link.Programmer.ReadStatus(Kernel.Image);
        Assert.Equal(FlashContents.OtherFirmware, status.Flash);
        Assert.Equal(0, status.ProgramLength);
        Assert.Equal(0, status.WriteCounts.Sum());

        var image = link.Programmer.ReadEverything();
        Assert.Equal(0x2000 + 128 + 11, image.Count);
        Assert.Equal(flashBefore, Enumerable.Range(0x8000, 0x2000).Select(a => image[a]));
        Assert.Equal(0x07, image[0x4001]);
        Assert.Equal(flashBefore, Enumerable.Range(0x8000, 0x2000).Select(a => thing.Mcu.Read(a)));
        Assert.Equal(image.Bytes, MemoryImage.Parse(image.ToSRecords()).Bytes);
    }

    [Fact]
    public void AnEmptyChipIsReportedAsEmpty()
    {
        var (_, _, link) = Connect();
        Assert.Equal(FlashContents.Empty, link.Programmer.ReadStatus(Kernel.Image).Flash);
    }
}
