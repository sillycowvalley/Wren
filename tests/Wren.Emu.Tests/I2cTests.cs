using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

/// <summary>The emulated I2C peripheral as the OLED Thing's display driver uses it.</summary>
public class I2cTests
{
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
}
