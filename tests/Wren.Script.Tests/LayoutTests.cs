using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>PULLUPS, and where EEREAD/EEWRITE keep the settings on a 128-byte and a 640-byte EEPROM.</summary>
public class LayoutTests
{
    [Theory]
    [InlineData(0xFF, 0xF8, 0x70)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0x0F, 0xF0, 0x00)]      // b0-b3: PC7-PC4
    [InlineData(0xE1, 0x80, 0x70)]      // b0: PC7; b5-b7: PD6-PD4
    [InlineData(0x30, 0x08, 0x40)]      // b4: PC3; b5: PD6
    public void PullupsSetsEachInputsPullUp(int mask, int portC, int portD)
    {
        var run = Run.Start($"ON START\n PULLUPS {mask}\nEND ON\n");
        Assert.Equal(portC, run.Mcu.PortC.Cr1 & 0xF8);
        Assert.Equal(portD, run.Mcu.PortD.Cr1 & 0x70);
        Assert.Equal(0x0C, run.Mcu.PortD.Cr1 & 0x0C);     // PD2 and PD3 keep theirs
    }

    [Fact]
    public void A128ByteEepromKeepsTwelveSettingsBesideTheFat()
    {
        var run = Run.Start("DIM a, b\nON START\n EEWRITE 11, 77\n EEWRITE 12, 99\n a = EEREAD(11)\n b = EEREAD(12)\nEND ON\n");
        Assert.Equal(77, run.Global("a"));
        Assert.Equal(0, run.Global("b"));                  // beyond the 12: ignored, reads 0
        Assert.Equal(77, run.Mcu.Read(KernelInfo.SmallSettingsAddress + 11));
    }

    [Fact]
    public void A640ByteEepromKeepsTheSettingsAt4080()
    {
        var program = Run.Compile("DIM a\nON START\n EEWRITE 300, 42\n EEWRITE 511, 7\n a = EEREAD(300)\nEND ON\n");
        var thing = new OledThing(DeviceInfo.Stm8s103f3);
        thing.Mcu.Load(KernelInfo.ForEeprom(Run.Kernel.Image, 640).Bytes);
        thing.Mcu.Load(ProgramStore.Plan(null, program.Image).Bytes());
        thing.PowerOn();
        thing.Mcu.Run(0.1);
        Assert.Equal(42, thing.Mcu.Read(0x4080 + 300));
        Assert.Equal(7, thing.Mcu.Read(0x4080 + 511));
        int vars = Run.Kernel.Symbol("VARS");
        Assert.Equal(42, (thing.Mcu.Read(vars) << 8) | thing.Mcu.Read(vars + 1));
    }
}
