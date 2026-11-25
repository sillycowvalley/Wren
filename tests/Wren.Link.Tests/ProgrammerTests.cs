using Wren.Asm;
using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

/// <summary>The programmer's jobs on a Thing (read-out protection, writing images, backup, wear), through the simulated Pico.</summary>
public class ProgrammerTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    private static Kernel Kernel => SharedKernel.Value;

    private static (OledThing Thing, FakePico Fake, ThingLink Link) Connect()
    {
        var thing = new OledThing();
        var fake = new FakePico(thing.Mcu);
        var link = new ThingLink(new PicoLink(fake), "COM6");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        return (thing, fake, link);
    }

    [Fact]
    public void BacksUpTheWholeChip()
    {
        var (_, _, link) = Connect();
        Assert.Equal(0x2000 + 128 + 11, link.Programmer.ReadEverything().Count);
    }

    [Fact]
    public void AReadOutProtectedThingConnectsAndSaysSo()
    {
        var thing = new OledThing();
        var fake = new FakePico(thing.Mcu) { ReadOutProtected = true };
        var link = new ThingLink(new PicoLink(fake), "COM7");
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
        var fake = new FakePico(thing.Mcu) { ReadOutProtected = true };
        var link = new ThingLink(new PicoLink(fake), "COM7");
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

    private static MemoryImage Factory => DigicoolStandIn.Image(DigicoolStandIn.OledV11);

    [Fact]
    public void RestoresTheFactoryFirmwareOnAProtectedThing()
    {
        var thing = new OledThing();
        thing.Mcu.Load(new[] { new KeyValuePair<int, byte>(0x8000, 0x82), new(0x4800, 0xAA) });
        var fake = new FakePico(thing.Mcu) { ReadOutProtected = true };
        var link = new ThingLink(new PicoLink(fake), "COM7");
        link.Link.Sleep = _ => { };
        link.Target.Connect();

        link.Programmer.ReplaceFirmware(Factory, "factory firmware");
        Assert.All(Factory.Bytes, kv => Assert.Equal(kv.Value, thing.Mcu.Read(kv.Key)));
        Assert.Equal(0x00, thing.Mcu.Read(0x4800));
        Assert.Equal(ThingProgrammer.DefaultOptions, link.Target.Read(0x4801, 10));
    }

    [Fact]
    public void RestoresTheFactoryFirmwareOverWrensKernel()
    {
        var (thing, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        link.Programmer.UploadProgram(Compiler.Compile("DIM a\nON START\n a = 1\nEND ON\n").Image);

        link.Programmer.ReplaceFirmware(Factory, "factory firmware");
        Assert.All(Factory.Bytes, kv => Assert.Equal(kv.Value, thing.Mcu.Read(kv.Key)));
        Assert.Equal(0, thing.Mcu.ProtectedBlocks);
        Assert.All(Enumerable.Range(0x4000, 128), a => Assert.Equal(0, thing.Mcu.Read(a)));
        Assert.Equal(FlashContents.OtherFirmware, link.Programmer.ReadStatus(Kernel.Image).Flash);

        // And it runs without faulting.
        link.Programmer.Run();
        thing.Mcu.Run(0.5);
        Assert.Null(thing.Mcu.Cpu.Fault);
    }

    [Fact]
    public void ReadsTheProgramStoresWearFromTheThing()
    {
        var (_, _, link) = Connect();
        link.Programmer.InstallKernel(Kernel.Image);
        CompiledProgram big = null!;
        for (int i = 0; i < 3; i++)                                // each different in every block: none is reused
        {
            big = Compiler.Compile("DIM a\nON START\n" + string.Concat(Enumerable.Repeat($" a = a + {1234 + i}\n", 30)) + "END ON\n");
            Assert.True(big.Success);
            link.Programmer.UploadProgram(big.Image);
        }

        var wear = link.Programmer.ReadWear();
        Assert.Equal(3, wear.Uploads);
        Assert.Equal(big.Image.Length, wear.ProgramLength);
        int blocksPerUpload = (big.Image.Length + 63) / 64;
        Assert.Equal(blocksPerUpload, wear.ProgramBlocks.Count);
        Assert.Equal(3 * blocksPerUpload, wear.WriteCounts.Sum());
        Assert.All(wear.ProgramBlocks, b => Assert.True(wear.WriteCounts[b] >= 1));
    }
}
