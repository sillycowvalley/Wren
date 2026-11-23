using System.Text;

namespace Wren.Script.Tests;

/// <summary>Reference drawings in C#, to compare with what the kernel draws.</summary>
public sealed class Panel
{
    public readonly bool[,] Lit = new bool[32, 64];

    public void Set(int x, int y, int colour = 1)
    {
        if (x < 0 || x >= 64 || y < 0 || y >= 32) return;
        Lit[y, x] = colour switch { 0 => false, 1 => true, _ => !Lit[y, x] };
    }

    public void Fill(int x, int y, int w, int h, int colour = 1)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                Set(i, j, colour);
    }

    public void Box(int x, int y, int w, int h, int colour = 1)
    {
        if (w < 1 || h < 1) return;
        Fill(x, y, w, 1, colour);
        Fill(x, y + h - 1, w, 1, colour);
        Fill(x, y, 1, h, colour);
        Fill(x + w - 1, y, 1, h, colour);
    }

    /// <summary>The panel as text, as Ssd1315.PanelText gives it.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 64; x++) sb.Append(Lit[y, x] ? '#' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Font tables read from firmware/kernel/Fonts.inc: label -> bytes.</summary>
    public static Dictionary<string, List<byte>> Fonts()
    {
        var fonts = new Dictionary<string, List<byte>>();
        List<byte>? current = null;
        foreach (string raw in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "firmware", "kernel", "Fonts.inc")))
        {
            string line = raw.Split(';')[0].Trim();
            if (line.EndsWith(':')) fonts[line.TrimEnd(':')] = current = new List<byte>();
            else if (line.StartsWith("dc.b") && current != null)
                current.AddRange(line[4..].Split(',').Select(v => Convert.ToByte(v.Trim().TrimStart('$'), 16)));
        }
        return fonts;
    }

    /// <summary>Draws text as the kernel does: each cell cleared, glyph columns, scaled.</summary>
    public void Text(int x, int y, string text, int font)
    {
        var fonts = Fonts();
        var table = font == 0 ? fonts["FontSmall"] : fonts["FontNormal"];
        int cols = font == 0 ? 3 : 5, rows = font == 0 ? 6 : 8, pitch = font == 0 ? 4 : 6;
        int sx = font switch { 2 => 2, 3 => 3, _ => 1 }, sy = font switch { 2 => 2, 3 => 4, _ => 1 };
        foreach (char ch in text)
        {
            int index = (ch is >= ' ' and <= '~' ? ch : '?') - 32;
            for (int c = 0; c < pitch; c++)
            {
                int bits = c < cols ? table[index * cols + c] : 0;
                for (int r = 0; r < rows; r++)
                    Fill(x + c * sx, y + r * sy, sx, sy, (bits >> r) & 1);
            }
            x += pitch * sx;
        }
    }
}
