using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

public class OledThingTests
{
    // The 5x7 font HexDemo's big digits were made from (3x across, 4x down, 2 rows from the top).
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['3'] = new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['C'] = new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" },
        ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
    };

    private static string ExpectedPanel(string twoDigits, bool inverted)
    {
        var lit = new bool[32, 64];
        for (int d = 0; d < 2; d++)
        {
            int x0 = 16 + 16 * d;
            string[] rows = Font[twoDigits[d]];
            for (int r = 0; r < 7; r++)
                for (int c = 0; c < 5; c++)
                    if (rows[r][c] == '1')
                        for (int dy = 0; dy < 4; dy++)
                            for (int dx = 0; dx < 3; dx++)
                                lit[2 + r * 4 + dy, x0 + c * 3 + dx] = true;
        }
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 64; x++) sb.Append(lit[y, x] != inverted ? '#' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static OledThing Boot(byte inputs)
    {
        var thing = new OledThing();
        thing.Mcu.Load(Firmware.Assemble("demo/HexDemo.asm").Image.Bytes);
        thing.SetInputs(inputs);
        thing.PowerOn();
        Assert.Equal(StopReason.TimeUp, thing.Mcu.Run(0.05));
        Assert.Null(thing.Mcu.Cpu.Fault);
        return thing;
    }

    [Theory]
    [InlineData(0x3C, "3C")]
    [InlineData(0x07, "07")]
    [InlineData(0xAF, "AF")]
    public void HexDemoShowsInputs(byte inputs, string digits)
    {
        var thing = Boot(inputs);
        Assert.True(thing.Oled.Lit);
        Assert.Equal(ExpectedPanel(digits, false), thing.Oled.PanelText());
    }

    [Fact]
    public void HexDemoFollowsChangesAndInvert()
    {
        var thing = Boot(0x00);
        Assert.Equal(ExpectedPanel("00", false), thing.Oled.PanelText());

        thing.SetInputs(0xCA);
        thing.Mcu.Run(0.05);
        Assert.Equal(ExpectedPanel("CA", false), thing.Oled.PanelText());

        thing.InvLow = true;
        thing.Mcu.Run(0.05);
        Assert.True(thing.Oled.Inverse);
        Assert.Equal(ExpectedPanel("CA", true), thing.Oled.PanelText());
    }

    [Fact]
    public void InputPinsMapToBits()
    {
        var thing = new OledThing();
        thing.SetInputs(0b1000_0001); // b7 = PD4, b0 = PC7
        Assert.True(thing.Mcu.PortD.Level(4));
        Assert.True(thing.Mcu.PortC.Level(7));
        Assert.False(thing.Mcu.PortC.Level(6));
        Assert.False(thing.Mcu.PortD.Level(6));
    }

    [Fact]
    public void ModePinsUsePullUps()
    {
        var thing = new OledThing();
        Assert.False(thing.Mcu.PortA.Level(3));       // floating, no pull-up yet
        thing.Mcu.PortA.Write(3, 0x08);               // PA_CR1: pull-up on PA3
        Assert.True(thing.Mcu.PortA.Level(3));
        thing.InvLow = true;
        Assert.False(thing.Mcu.PortA.Level(3));
    }

    [Fact]
    public void NothingWrittenToFlashOrEeprom()
    {
        var thing = Boot(0x12);
        Assert.All(thing.Mcu.FlashWrites, n => Assert.Equal(0, n));
        Assert.All(thing.Mcu.EepromWrites, n => Assert.Equal(0, n));
        Assert.Empty(thing.Mcu.UnmodelledPeripherals);
    }
}
