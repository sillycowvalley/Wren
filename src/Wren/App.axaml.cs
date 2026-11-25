using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Wren;

public partial class App : Application
{
    /// <summary>The shortest time the splash screen stays up, so it does not flash.</summary>
    private static readonly TimeSpan SplashTime = TimeSpan.FromMilliseconds(1200);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = SavedSettings.Current.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        // A last line of defence for the programmer's port: a failure talking to it that a
        // handler missed is reported in the Messages pane (and the diagnostic log) rather than
        // ending the app.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is not (IOException or UnauthorizedAccessException or TimeoutException or ObjectDisposedException
                or Wren.Link.LinkException)) return;
            Wren.Script.DiagnosticLog.Write($"Unhandled: {e.Exception}");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main })
                main.Report($"The programmer: {e.Exception.Message}");
            e.Handled = true;
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The splash is the main window until the real one is ready. Closing it must not
            // end the app, so shutdown is explicit until the swap is done.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var splash = new SplashWindow();
            WindowLayout.CentreOn(splash, SavedSettings.Current.MainWindow); // where the main window will open
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
