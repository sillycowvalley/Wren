using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Wren.Script;

namespace Wren;

/// <summary>The settings for this run: read once at start, written when a window closes.</summary>
internal static class SavedSettings
{
    public static AppSettings Current { get; } = AppSettings.Load(AppSettings.DefaultPath);

    public static void Save() => Current.Save(AppSettings.DefaultPath);
}

/// <summary>
/// Puts a window back where it was and keeps track of its normal (not maximised) bounds, which
/// Avalonia does not expose, so they can be saved when it closes.
/// </summary>
internal sealed class WindowLayout
{
    private readonly Window window;
    private PixelPoint? position;
    private Size size;

    /// <summary>Restores the saved placement; call before the window first shows.</summary>
    public WindowLayout(Window window, WindowPlacement? saved)
    {
        this.window = window;
        size = new Size(window.Width, window.Height);
        Restore(saved);

        window.PositionChanged += (_, _) => Remember();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ClientSizeProperty || e.Property == Window.WindowStateProperty) Remember();
        };
        window.Opened += (_, _) => Remember();
    }

    /// <summary>The placement to save: the normal bounds, and whether it is maximised now.</summary>
    public WindowPlacement Placement => new()
    {
        X = position?.X,
        Y = position?.Y,
        Width = size.Width,
        Height = size.Height,
        Maximized = window.WindowState == WindowState.Maximized,
    };

    private void Restore(WindowPlacement? saved)
    {
        if (saved == null || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height)
            || saved.Width <= 0 || saved.Height <= 0)
            return;

        double width = Math.Max(saved.Width, window.MinWidth);
        double height = Math.Max(saved.Height, window.MinHeight);
        window.Width = width;
        window.Height = height;
        size = new Size(width, height);

        if (saved.X is int x && saved.Y is int y)
        {
            var areas = window.Screens.All.Select(s =>
                new ScreenRect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height));
            if (AppSettings.IsOnScreen(x, y, (int)width, areas))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(x, y);
                position = window.Position;
            }
        }

        if (saved.Maximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Places a window (the splash screen) at the centre of where a saved window will open:
    /// its remembered bounds, or the middle of its screen if it was maximised. Leaves the
    /// window's own start-up location alone when there is no usable placement.
    /// </summary>
    public static void CentreOn(Window window, WindowPlacement? saved)
    {
        if (saved?.X is not int x || saved.Y is not int y || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height))
            return;
        var screens = window.Screens;
        var areas = screens.All.Select(s =>
            new ScreenRect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height));
        if (!AppSettings.IsOnScreen(x, y, (int)saved.Width, areas)) return;
        var screen = screens.ScreenFromPoint(new PixelPoint(x, y)) ?? screens.Primary;
        if (screen == null) return;

        double scale = screen.Scaling;
        double centreX, centreY;
        if (saved.Maximized)
        {
            centreX = screen.WorkingArea.X + screen.WorkingArea.Width / 2.0;
            centreY = screen.WorkingArea.Y + screen.WorkingArea.Height / 2.0;
        }
        else
        {
            centreX = x + saved.Width * scale / 2;
            centreY = y + saved.Height * scale / 2;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(
            (int)(centreX - window.Width * scale / 2),
            (int)(centreY - window.Height * scale / 2));
    }

    /// <summary>
    /// Records the bounds once the change has settled, and only if the window is then normal:
    /// maximising moves and resizes the window before its state says so.
    /// </summary>
    private void Remember() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!window.IsVisible || window.WindowState != WindowState.Normal) return;
            position = window.Position;
            size = window.ClientSize;
        }, DispatcherPriority.Background);
}
