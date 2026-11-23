using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wren.Emu;

namespace Wren;

/// <summary>Draws the 64x32 panel, scaled up, with small gaps between pixels like the real thing.</summary>
public sealed class OledView : Control
{
    private static readonly IBrush Glass = new SolidColorBrush(Color.FromRgb(0x08, 0x0A, 0x10));
    private static readonly IBrush Lit = new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xFF));

    private long drawnVersion = -1;

    public Ssd1315? Oled { get; set; }

    /// <summary>Redraws if the display has changed since the last draw.</summary>
    public void Refresh()
    {
        if (Oled == null || Oled.Version == drawnVersion) return;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Ssd1315.PanelWidth * 6, Ssd1315.PanelHeight * 6);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Glass, bounds);
        if (Oled == null) return;
        drawnVersion = Oled.Version;

        double scale = Math.Min(bounds.Width / Ssd1315.PanelWidth, bounds.Height / Ssd1315.PanelHeight);
        double left = (bounds.Width - scale * Ssd1315.PanelWidth) / 2;
        double top = (bounds.Height - scale * Ssd1315.PanelHeight) / 2;
        double gap = scale >= 4 ? 1 : 0;

        for (int y = 0; y < Ssd1315.PanelHeight; y++)
        {
            for (int x = 0; x < Ssd1315.PanelWidth; x++)
            {
                if (!Oled.Pixel(x, y)) continue;
                context.FillRectangle(Lit, new Rect(left + x * scale, top + y * scale, scale - gap, scale - gap));
            }
        }
    }
}
