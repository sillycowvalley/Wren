using System.Reflection;
using Avalonia.Controls;

namespace Wren;

/// <summary>The start-up screen, shown while the main window is being built.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Version}   ·   Starting…";
    }

    /// <summary>The app's version, without any source revision suffix.</summary>
    public static string Version
    {
        get
        {
            var info = typeof(SplashWindow).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }
}
