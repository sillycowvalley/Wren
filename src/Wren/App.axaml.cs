using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Wren;

public partial class App : Application
{
    /// <summary>The shortest time the splash screen stays up, so it does not flash.</summary>
    private static readonly TimeSpan SplashTime = TimeSpan.FromMilliseconds(1200);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The splash is the main window until the real one is ready. Closing it must not
            // end the app, so shutdown is explicit until the swap is done.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) =>
            {
                var shown = Stopwatch.StartNew();
                // Let the splash paint before the slow part starts.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(50);

                string? startPath = desktop.Args is { Length: > 0 } args ? args[0] : null;
                var main = new MainWindow(startPath);

                var left = SplashTime - shown.Elapsed;
                if (left > TimeSpan.Zero)
                    await Task.Delay(left);

                desktop.MainWindow = main;
                main.Show();
                splash.Close();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
