using Avalonia.Controls;
using Avalonia.Layout;

namespace Wren;

public enum SaveChangesAnswer { Save, DontSave, Cancel }

/// <summary>The "save your changes?" question, as Notepad asks it.</summary>
public static class SaveChangesDialog
{
    /// <summary>Asks about unsaved edits to the named program. With discardOnly, offers only to throw them away.</summary>
    public static async Task<SaveChangesAnswer> Ask(Window owner, string name, bool discardOnly = false)
    {
        var answer = SaveChangesAnswer.Cancel;
        var dialog = new Window
        {
            Title = "Wren",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string text, SaveChangesAnswer result, bool isDefault = false)
        {
            var b = new Button { Content = text, MinWidth = 80, IsDefault = isDefault, IsCancel = result == SaveChangesAnswer.Cancel };
            b.Click += (_, _) => { answer = result; dialog.Close(); };
            buttons.Children.Add(b);
        }
        if (discardOnly)
        {
            Add("Discard edits", SaveChangesAnswer.DontSave, true);
        }
        else
        {
            Add("Save", SaveChangesAnswer.Save, true);
            Add("Don't save", SaveChangesAnswer.DontSave);
        }
        Add("Cancel", SaveChangesAnswer.Cancel);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = discardOnly
                        ? $"Throw away your unsaved edits to {name} and go back to the saved program?"
                        : $"Save your changes to {name}?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                buttons,
            },
        };
        await dialog.ShowDialog(owner);
        return answer;
    }
}
