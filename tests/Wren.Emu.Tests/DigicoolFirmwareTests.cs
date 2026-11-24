using Wren.Asm;
using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

/// <summary>Digicool's factory firmware (firmware/digicool) runs unchanged on the emulated boards.</summary>
public class DigicoolFirmwareTests
{
    private static MemoryImage Image(string file) =>
        MemoryImage.Parse(File.ReadAllText(Path.Combine(Firmware.Folder, "digicool", file)));

    [Fact]
    public void AMasterTransmitterSeesTxeWithAddr()
    {
        var thing = new OledThing();
        var mcu = thing.Mcu;
        mcu.PowerOn();
        const int Cr1 = 0x5210, Cr2 = 0x5211, Freqr = 0x5212, Dr = 0x5216, Sr1 = 0x5217;
        mcu.Write(Freqr, 16);
        mcu.Write(Cr1, 0x01);       // PE
        mcu.Write(Cr2, 0x01);       // START
        Assert.Equal(0x01, mcu.Read(Sr1) & 0x01);                 // SB
        mcu.Write(Dr, OledThing.OledAddress << 1);                // write address
        Assert.Equal(0x82, mcu.Read(Sr1) & 0x82);                 // ADDR and TXE together
    }

    [Fact]
    public void TheOledThingsFirmwareShowsTheInputs()
    {
        var thing = new OledThing();
        thing.Mcu.Load(Image("8-bit_oled_thing_v1.1.s19").Bytes);
        thing.SetInputs(0xA5);
        thing.PowerOn();
        thing.Mcu.Run(0.1);
        Assert.Null(thing.Mcu.Cpu.Fault);
        Assert.True(thing.Oled.Lit);
        // Eight bit cells on the bottom row, 7 x 8 at x = 8i, b7 on the left: a 1 is filled,
        // a 0 an outline (dark in the middle).
        for (int i = 0; i < 8; i++)
        {
            bool one = (0xA5 & (0x80 >> i)) != 0;
            Assert.True(thing.Oled.Pixel(8 * i, 24), $"cell {i} outline");
            Assert.Equal(one, thing.Oled.Pixel(8 * i + 3, 28));
        }
    }

    [Fact]
    public void TheLedThingsFirmwareRunsOnTheChip()
    {
        var mcu = new Stm8Device(DeviceInfo.Stm8s003f3);
        mcu.Load(Image("8-bit_led_thing.s19").Bytes);
        mcu.PortD.SetExternal(2, PinDrive.High);                  // b0 high, the others low
        foreach (int pin in new[] { 7, 6, 5, 4, 3 }) mcu.PortC.SetExternal(pin, PinDrive.Low);
        foreach (int pin in new[] { 4, 5 }) mcu.PortB.SetExternal(pin, PinDrive.Low);
        mcu.PowerOn();
        mcu.Run(0.05);
        bool lit = false;
        for (int i = 0; i < 2000 && !lit; i++)
        {
            mcu.Run(5e-6);
            // b0's LED: PD3 driven high, PD4 driven low, PD5 and PD6 inputs.
            lit = (mcu.PortD.Ddr & 0x78) == 0x18 && (mcu.PortD.Odr & 0x18) == 0x08;
        }
        Assert.True(lit);
    }
}
