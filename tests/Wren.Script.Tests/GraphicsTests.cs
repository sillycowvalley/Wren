using Xunit;

namespace Wren.Script.Tests;

/// <summary>What the kernel draws, against reference drawings in C#.</summary>
public class GraphicsTests
{
    private static string Draw(string statements) =>
        Run.Start($"ON START\n CLS\n{statements}\n SHOW\nEND ON\n").Panel;

    [Fact]
    public void Shapes()
    {
        var p = new Panel();
        p.Set(0, 0);
        p.Set(63, 31);
        p.Fill(10, 3, 7, 5);
        p.Box(20, 10, 12, 9);
        p.Fill(-5, 25, 10, 10);             // clipped
        p.Box(40, 2, 1, 1);
        p.Fill(30, 1, 20, 6, 2);            // INVERT over the others
        Assert.Equal(p.ToString(), Draw(
            " PIXEL 0, 0\n PIXEL 63, 31\n PIXEL 64, 5\n PIXEL -1, 5\n" +
            " FILL 10, 3, 7, 5\n BOX 20, 10, 12, 9\n FILL -5, 25, 10, 10\n BOX 40, 2, 1, 1\n INVERT 30, 1, 20, 6"));
    }

    [Theory]
    [InlineData(0, 0, 63, 31)]
    [InlineData(63, 0, 0, 31)]
    [InlineData(5, 20, 50, 20)]
    [InlineData(10, 2, 12, 29)]
    [InlineData(-20, -5, 70, 40)]
    public void Lines(int x1, int y1, int x2, int y2)
    {
        // Bresenham, the same as the kernel
        var p = new Panel();
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1, dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1, err = dx + dy;
        int x = x1, y = y1;
        while (true)
        {
            p.Set(x, y);
            if (x == x2 && y == y2) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        Assert.Equal(p.ToString(), Draw($" LINE {x1}, {y1}, {x2}, {y2}"));
    }

    [Theory]
    [InlineData(0, "Hello! 42 {}")]
    [InlineData(1, "Wren: #$%")]
    [InlineData(2, "AB12")]
    [InlineData(3, "F0")]
    public void TextInEachFont(int font, string text)
    {
        var p = new Panel();
        p.Text(1, 2, text, font);
        Assert.Equal(p.ToString(), Draw($" TEXT 1, 2, \"{text}\", {font}"));
    }

    [Fact]
    public void Numbers()
    {
        var p = new Panel();
        p.Text(0, 0, "03C", 1);
        p.Text(30, 0, "-1234", 1);
        p.Text(0, 10, "0101", 0);
        p.Text(0, 20, "32767", 0);
        p.Text(30, 20, "FFFF", 0);
        Assert.Equal(p.ToString(), Draw(
            " HEX 0, 0, 60, 3\n DEC 30, 0, -1234\n BIN 0, 10, 5, 4, SMALL\n DEC 0, 20, 32767, SMALL\n HEX 30, 20, -1, 4, SMALL"));
    }

    [Theory]
    [InlineData(0xA5)]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void Bits(int value)
    {
        var p = new Panel();
        for (int i = 0; i < 8; i++)
        {
            int x = 4 + i * 7;
            if ((value & (0x80 >> i)) != 0) p.Fill(x, 21, 6, 9);
            else p.Box(x, 21, 6, 9);
        }
        Assert.Equal(p.ToString(), Draw($" BITS 4, 21, 56, 9, {value}"));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(50, 100)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    [InlineData(-5, 100)]
    public void Bar(int value, int max)
    {
        var p = new Panel();
        p.Box(2, 4, 60, 10);
        int filled = Math.Clamp(value, 0, max) * 58 / max;
        p.Fill(3, 5, filled, 8);
        Assert.Equal(p.ToString(), Draw($" BAR 2, 4, 60, 10, {value}, {max}"));
    }

    [Fact]
    public void InverseReachesTheOled()
    {
        var run = Run.Start("ON START\n INVERSE ON\nEND ON\n");
        Assert.True(run.Thing.Oled.Inverse);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" FLIP ON\n", true)]
    [InlineData(" FLIP ON\n FLIP OFF\n", false)]
    public void FlipTurnsThePictureUpsideDown(string flip, bool upsideDown)
    {
        // A small box in the top-left corner; FLIP turns the picture 180 degrees on the panel
        // as the Thing's module (mounted rotated) shows it.
        var run = Run.Start("ON START\n CLS\n BOX 0, 0, 3, 3\n SHOW\n" + flip + "END ON\n");
        var oled = run.Thing.Oled;
        int w = Wren.Emu.Ssd1315.PanelWidth, h = Wren.Emu.Ssd1315.PanelHeight;
        Assert.Equal(!upsideDown, oled.Pixel(0, 0));
        Assert.Equal(upsideDown, oled.Pixel(w - 1, h - 1));
    }
}
