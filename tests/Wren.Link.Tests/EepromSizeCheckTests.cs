using Wren.Emu;
using Xunit;

namespace Wren.Link.Tests;

public class EepromSizeCheckTests
{
    private static (OledThing Thing, ThingLink Link) Connect(DeviceInfo chip)
    {
        var thing = new OledThing(chip);
        var link = new ThingLink(new PicoLink(new FakePico(thing.Mcu)), "COM9");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        return (thing, link);
    }

    private static byte[] Read(OledThing thing, int address, int length) =>
        Enumerable.Range(address, length).Select(a => thing.Mcu.Read(a)).ToArray();

    [Fact]
    public void A640ByteEepromIsFoundAndGetsItsBytesBack()
    {
        var (thing, link) = Connect(DeviceInfo.Stm8s103f3);
        var original = Enumerable.Range(0, 64).Select(i => (byte)(i * 3)).ToArray();
        link.Target.WriteBlocks(EepromSizeCheck.ExtraStart, original, TargetMemory.Eeprom);
        byte[] wren = Read(thing, EepromSizeCheck.WrenEeprom, EepromSizeCheck.WrenSize);
        Assert.True(EepromSizeCheck.HasExtraEeprom(link.Target));
        Assert.Equal(original, Read(thing, EepromSizeCheck.ExtraStart, 64));
        Assert.Equal(wren, Read(thing, EepromSizeCheck.WrenEeprom, EepromSizeCheck.WrenSize));
    }

    [Fact]
    public void A128ByteEepromIsFoundWantingAndWrensBytesAreUntouched()
    {
        var (thing, link) = Connect(DeviceInfo.Stm8s003f3);
        byte[] wren = Read(thing, EepromSizeCheck.WrenEeprom, EepromSizeCheck.WrenSize);
        int[] flash = (int[])thing.Mcu.FlashWrites.Clone();
        Assert.False(EepromSizeCheck.HasExtraEeprom(link.Target));
        Assert.Equal(wren, Read(thing, EepromSizeCheck.WrenEeprom, EepromSizeCheck.WrenSize));
        Assert.Equal(flash, thing.Mcu.FlashWrites);
    }
}
