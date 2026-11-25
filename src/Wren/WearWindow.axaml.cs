using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wren.Link;
using Wren.Script;

namespace Wren;

/// <summary>A grid of the program store's 54 blocks and the writes the Thing has recorded for each.</summary>
public partial class WearWindow : Window
{
    /// <summary>The rated program/erase cycles of a Thing's (STM8S003) flash.</summary>
    public const int RatedWrites = 100;

    public WearWindow() : this(new StoreWear(new int[ProgramStore.Blocks], Array.Empty<int>(), 0, 0)) { }

    private readonly WindowLayout layout;

    public WearWindow(StoreWear wear)
    {
        InitializeComponent();
        layout = new WindowLayout(this, SavedSettings.Current.WearWindow);
        Closing += (_, _) =>
        {
            KeepLayout();
            SavedSettings.Save();
        };
        Fill(wear);
    }

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>Puts this window's placement in the settings (saved when a window closes).</summary>
    public void KeepLayout() => SavedSettings.Current.WearWindow = layout.Placement;

    /// <summary>Fills the summary and the grid from a fresh reading.</summary>
    public void Fill(StoreWear wear)
    {
        Cells.Children.Clear();
        var program = wear.ProgramBlocks.ToHashSet();
        int most = wear.WriteCounts.Count > 0 ? wear.WriteCounts.Max() : 0;
        int total = wear.WriteCounts.Sum();
        Summary.Text = $"{wear.Uploads} upload{(wear.Uploads == 1 ? "" : "s")}.  " +
                       (wear.ProgramLength > 0
                           ? $"Program: {wear.ProgramLength} bytes in block{(program.Count == 1 ? "" : "s")} {string.Join(", ", wear.ProgramBlocks)}.  "
                           : "No program.  ") +
                       $"Most writes to one block: {most} of about {RatedWrites}.  Writes in all: {total}.";

        for (int block = 0; block < ProgramStore.Blocks; block++)
        {
            int writes = block < wear.WriteCounts.Count ? wear.WriteCounts[block] : 0;
            bool holdsProgram = program.Contains(block);
            int address = ProgramStore.StoreAddress + block * ProgramStore.BlockSize;
            var cell = new Border
            {
                Background = new SolidColorBrush(Shade(writes)),
                BorderBrush = new SolidColorBrush(holdsProgram ? Color.Parse("#1F4E99") : Color.Parse("#A3BDE3")),
                BorderThickness = new Thickness(holdsProgram ? 3 : 1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(2),
                Padding = new Thickness(2, 4),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = $"Block {block}", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = writes.ToString(), FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"${address:X4}", FontSize = 10, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                },
            };
            ToolTip.SetTip(cell, $"Store block {block} at ${address:X4}-${address + 63:X4}: written {writes} time{(writes == 1 ? "" : "s")}" +
                                 (holdsProgram ? "; holds part of the current program" : ""));
            Cells.Children.Add(cell);
        }
    }

    /// <summary>Pale green when unused, amber at half the rating, red at the rating and beyond.</summary>
    public static Color Shade(int writes)
    {
        double t = Math.Clamp(writes / (double)RatedWrites, 0, 1);
        Color green = Color.Parse("#E3F4DC"), amber = Color.Parse("#FCEFC2"), red = Color.Parse("#F4B8B0");
        return t < 0.5 ? Mix(green, amber, t * 2) : Mix(amber, red, (t - 0.5) * 2);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
