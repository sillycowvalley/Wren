using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Wren;

/// <summary>
/// The margin left of the line numbers: a red dot on each line with a breakpoint; a click
/// beside a line asks for its breakpoint to be set or cleared.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private static readonly IBrush Dot = new SolidColorBrush(Color.FromRgb(0xD0, 0x30, 0x30));

    /// <summary>Whether a line (from 1) has a breakpoint.</summary>
    public Func<int, bool> HasBreakpoint { get; set; } = _ => false;

    /// <summary>A click beside a line (from 1).</summary>
    public event Action<int>? LineClicked;

    public BreakpointMargin() => Cursor = new Cursor(StandardCursorType.Hand);

    protected override Size MeasureOverride(Size availableSize) => new(18, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        // The theme's margin colour (Win7.axaml), so the margin follows View → Dark Mode.
        var back = this.TryFindResource("Win7Margin", ActualThemeVariant, out var found) && found is IBrush brush
            ? brush : Brushes.Transparent;
        context.FillRectangle(back, new Rect(Bounds.Size));
        var view = TextView;
        if (view == null || !view.VisualLinesValid) return;
        foreach (var line in view.VisualLines)
        {
            if (!HasBreakpoint(line.FirstDocumentLine.LineNumber)) continue;
            double y = line.VisualTop - view.VerticalOffset + line.Height / 2;
            context.DrawEllipse(Dot, null, new Point(9, y), 5, 5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var view = TextView;
        if (view == null) return;
        var line = view.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.VerticalOffset);
        if (line != null) LineClicked?.Invoke(line.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }
}

/// <summary>Shades the line the program has stopped at (yellow) and the line with an error (pink).</summary>
public sealed class LineHighlighter : IBackgroundRenderer
{
    private static readonly IBrush CurrentBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xD0, 0x40));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x40, 0x40));

    /// <summary>The line the program has stopped at (from 1), or 0.</summary>
    public int CurrentLine { get; set; }

    /// <summary>The line with the first compile error (from 1), or 0.</summary>
    public int ErrorLine { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        foreach (var line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            IBrush? brush = number == CurrentLine ? CurrentBrush : number == ErrorLine ? ErrorBrush : null;
            if (brush == null) continue;
            drawingContext.FillRectangle(brush,
                new Rect(0, line.VisualTop - textView.VerticalOffset, textView.Bounds.Width, line.Height));
        }
    }
}
