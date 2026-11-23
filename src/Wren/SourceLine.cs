using Avalonia.Media;

namespace Wren;

/// <summary>One line of the program as the source view shows it.</summary>
public sealed class SourceLine
{
    private static readonly IBrush CurrentBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xD0, 0x40));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x40, 0x40));

    public required int Number { get; init; }
    public required string Text { get; init; }
    public bool HasBreakpoint { get; init; }
    public bool IsCurrent { get; init; }
    public bool HasError { get; init; }

    public string Marker => HasBreakpoint ? "●" : "";
    public IBrush Background => IsCurrent ? CurrentBrush : HasError ? ErrorBrush : Brushes.Transparent;
}
