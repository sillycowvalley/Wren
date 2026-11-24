using Wren.Asm;
using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

/// <summary>Telling the boards apart, and Install kernel's checks and the program table's new layout.</summary>
public class BoardAndLayoutTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    private static Kernel Kernel => SharedKernel.Value;

    private static MemoryImage Digicool(string file) =>
        MemoryImage.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "firmware", "digicool", file)));

    private static ThingLink Connect(Stm8Device mcu)
    {
        var link = new ThingLink(new PicoLink(new FakePico(mcu)), "COM9");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        return link;
    }

    /// <summary>A chip with no OLED on its I2C pins, as on an LED Thing.</summary>
    private static Stm8Device LedThingChip() => new(DeviceInfo.Stm8s003f3);

    [Fact]
    public void ABlankOledThingIsFoundByItsDisplay()
    {
        var thing = new OledThing();
        var found = Connect(thing.Mcu).Programmer.DetectBoard();
        Assert.Equal(Board.OledThing, found.Board);
        Assert.Contains("answered", found.How);
    }

    [Fact]
    public void ABlankChipWithNoDisplayIsNotTakenForAnOledThing()
    {
        var found = Connect(LedThingChip()).Programmer.DetectBoard();
        Assert.Equal(Board.Unknown, found.Board);
    }

    [Fact]
    public void DigicoolsFirmwareNamesItsBoard()
    {
        var oled = new OledThing();
        oled.Mcu.Load(Digicool("8-bit_oled_thing_v1.1.s19").Bytes);
        Assert.Equal(Board.OledThing, Connect(oled.Mcu).Programmer.DetectBoard().Board);

        var led = LedThingChip();
        led.Load(Digicool("8-bit_led_thing.s19").Bytes);
        Assert.Equal(Board.LedThing, Connect(led).Programmer.DetectBoard().Board);
    }

    [Fact]
    public void InstallRefusesAnLedThing()
    {
        var led = LedThingChip();
        led.Load(Digicool("8-bit_led_thing.s19").Bytes);
        var link = Connect(led);
        var ex = Assert.Throws<LinkException>(() => link.Programmer.InstallKernel(Kernel.Image));
        Assert.Contains("LED Thing", ex.Message);
        Assert.Equal(Digicool("8-bit_led_thing.s19")[0x8150], led.Read(0x8150));    // untouched
    }

    [Theory]
    [InlineData(false, 128, KernelInfo.SmallSettingsAddress)]
    [InlineData(true, 640, KernelInfo.LargeSettingsAddress)]
    public void InstallRecordsTheEepromInTheInfoBlock(bool developmentBoard, int eeprom, int settings)
    {
        var thing = new OledThing(developmentBoard ? DeviceInfo.Stm8s103f3 : DeviceInfo.Stm8s003f3);
        var link = Connect(thing.Mcu);
        link.Programmer.InstallKernel(Kernel.Image);
        link.Target.Connect();
        var info = link.Programmer.ReadInfo()!;
        Assert.Equal(Board.OledThing, info.Board);
        Assert.Equal(eeprom, info.EepromSize);
        Assert.Equal(settings, info.SettingsAddress);
        Assert.Equal(Board.OledThing, link.Programmer.DetectBoard().Board);
        Assert.Equal(info, link.Programmer.ReadStatus(Kernel.Image).Info);
    }

    [Fact]
    public void AnOldKernelsProgramSurvivesTheUpdate()
    {
        // A Thing as the previous kernel left it: program table version 1 (47 blocks at $9440),
        // and a program in old blocks 2, 4, 6 ...
        var program = Compiler.Compile("DIM a\nON START\n a = 2468\nEND ON\n" +
            string.Concat(Enumerable.Range(0, 20).Select(i => $"SUB S{i}\n a = a + {i}\nEND SUB\n")));
        Assert.True(program.Success);
        Assert.True(program.Blocks >= 2);
        var thing = new OledThing();
        var image = new MemoryImage();
        int[] oldBlocks = Enumerable.Range(1, program.Blocks).Select(i => 2 * i).ToArray();
        for (int i = 0; i < program.Image.Length; i++) image[0x9440 + oldBlocks[i / 64] * 64 + i % 64] = program.Image[i];
        var v1 = new byte[128];
        v1[0] = 1; v1[1] = 2; v1[2] = (byte)(program.Image.Length >> 8); v1[3] = (byte)program.Image.Length;
        ushort crc = ProgramStore.Crc16(program.Image);
        v1[4] = (byte)(crc >> 8); v1[5] = (byte)crc; v1[7] = 3;
        for (int b = 0; b < 47; b++) v1[8 + b] = ProgramStore.FreeBlock;
        for (int i = 0; i < oldBlocks.Length; i++)
        {
            v1[8 + oldBlocks[i]] = i + 1 < oldBlocks.Length ? (byte)oldBlocks[i + 1] : ProgramStore.LastBlock;
            v1[55 + oldBlocks[i]] = (byte)(5 + i);
        }
        image.Write(0x4000, v1);
        thing.Mcu.Load(image.Bytes);

        var link = Connect(thing.Mcu);
        Assert.Throws<LinkException>(() => link.Programmer.UploadProgram(program.Image));   // the old kernel's layout
        link.Programmer.InstallKernel(Kernel.Image);
        link.Target.Connect();
        var wear = link.Programmer.ReadWear();
        Assert.Equal(oldBlocks.Select(b => b + 7), wear.ProgramBlocks);
        Assert.Equal(5, wear.WriteCounts[9]);
        Assert.Equal(3, wear.Uploads);

        link.Programmer.Run();
        thing.Mcu.Run(0.1);
        int vars = Kernel.Symbol("VARS");
        Assert.Equal(2468, (thing.Mcu.Read(vars) << 8) | thing.Mcu.Read(vars + 1));
    }
}
