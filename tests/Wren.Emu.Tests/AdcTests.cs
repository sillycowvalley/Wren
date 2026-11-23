using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

public class AdcTests
{
    private const int Csr = 0x5400, Cr1 = 0x5401, Cr2 = 0x5402, Drh = 0x5404, Drl = 0x5405;

    private static Stm8Device Device(int level, int channel = 3)
    {
        var d = new Stm8Device(DeviceInfo.Stm8s003f3);
        d.AnalogLevels[channel] = level;
        d.Write(Csr, (byte)channel);
        return d;
    }

    [Fact]
    public void FirstAdonWakesSecondStarts()
    {
        var d = Device(0x2C5);
        d.Write(Cr1, 0x01);                 // wake up
        d.Adc.Tick(1000);
        Assert.False(d.Adc.Converting);
        Assert.Equal(0, d.Read(Csr) & 0x80);
        d.Write(Cr1, 0x01);                 // start
        Assert.True(d.Adc.Converting);
        d.Adc.Tick(27);                     // 14 ADC clocks at fMASTER/2 = 28 cycles
        Assert.Equal(0, d.Read(Csr) & 0x80);
        d.Adc.Tick(1);
        Assert.Equal(0x80, d.Read(Csr) & 0x80);
    }

    [Theory]
    [InlineData(true, 0x02, 0xC5)]
    [InlineData(false, 0xB1, 0x01)]
    public void Alignment(bool right, int high, int low)
    {
        var d = Device(0x2C5);              // 10 1100 0101
        d.Write(Cr2, (byte)(right ? 0x08 : 0x00));
        d.Write(Cr1, 0x01);
        d.Write(Cr1, 0x01);
        d.Adc.Tick(100);
        Assert.Equal(high, d.Read(Drh));
        Assert.Equal(low, d.Read(Drl));
    }

    [Fact]
    public void EocIsClearedByWritingZero()
    {
        var d = Device(5);
        d.Write(Cr1, 0x01);
        d.Write(Cr1, 0x01);
        d.Adc.Tick(100);
        d.Write(Csr, 0x03);                 // EOC written as 0
        Assert.Equal(0x03, d.Read(Csr));
    }

    [Fact]
    public void UnsetChannelReadsThePinsDigitalLevel()
    {
        var d = new Stm8Device(DeviceInfo.Stm8s003f3);
        d.PortD.SetExternal(3, PinDrive.High);
        d.Write(Csr, 4);                    // AIN4 = PD3
        d.Write(Cr2, 0x08);
        d.Write(Cr1, 0x01);
        d.Write(Cr1, 0x01);
        d.Adc.Tick(100);
        Assert.Equal(1023, (d.Read(Drh) << 8) | d.Read(Drl));
    }
}
