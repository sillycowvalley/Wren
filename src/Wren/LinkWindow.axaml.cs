using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Wren.Link;
using Wren.Script;

namespace Wren;

/// <summary>Programs a real Thing through an ST-LINK: find, connect, install the kernel, upload, run.</summary>
public partial class LinkWindow : Window
{
    private readonly Kernel? kernel;
    private readonly Func<CompiledProgram?> currentProgram;
    private IReadOnlyList<UsbDeviceInfo> devices = Array.Empty<UsbDeviceInfo>();
    private ThingLink? link;
    private bool kernelConfirmed;

    public LinkWindow() : this(null, () => null) { }

    public LinkWindow(Kernel? kernel, Func<CompiledProgram?> currentProgram)
    {
        InitializeComponent();
        this.kernel = kernel;
        this.currentProgram = currentProgram;
        Closed += (_, _) => link?.Dispose();
        UpdateButtons();
        OnFind(null, new RoutedEventArgs());
    }

    private void Log(string message) =>
        Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += message + Environment.NewLine;
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });

    private void UpdateButtons()
    {
        bool connected = link != null;
        ConnectButton.IsEnabled = !connected && devices.Count > 0;
        DisconnectButton.IsEnabled = connected;
        KernelButton.IsEnabled = connected && kernel != null;
        UploadButton.IsEnabled = connected;
        RunButton.IsEnabled = connected;
        KernelButton.Content = kernelConfirmed ? "Click again to install" : "Install kernel";
    }

    /// <summary>Runs work off the UI thread, logging what it says and any failure.</summary>
    private async Task Work(string title, Action action)
    {
        IsEnabled = false;
        Log($"--- {title}");
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex) when (ex is LinkException or InvalidOperationException or ArgumentException or IOException)
        {
            Log($"Failed: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
            UpdateButtons();
        }
    }

    private void OnFind(object? sender, RoutedEventArgs e)
    {
        devices = ThingLink.Find(out string? why);
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
        if (why != null) Log(why);
        else Log(devices.Count == 0
            ? "No ST-LINK found. Is it plugged in, and does Device Manager show it under \"Universal Serial Bus devices\"?"
            : $"Found {devices.Count} ST-LINK{(devices.Count == 1 ? "" : "s")}: {string.Join(", ", devices)}");
        UpdateButtons();
    }

    private async void OnConnect(object? sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not UsbDeviceInfo device) return;
        await Work("Connect", () =>
        {
            var opened = ThingLink.Open(device);
            try
            {
                Log(opened.Version.ToString());
                double? volts = opened.StLink.ReadTargetVoltage(opened.Version);
                Log(volts is double v ? $"Target voltage: {v:F2} V" : "Target voltage: this ST-LINK cannot measure it");
                opened.Target.Connect();
                Log("SWIM connected; the Thing's CPU is stalled.");
                ShowStatus(opened);
                link = opened;
            }
            catch
            {
                opened.Dispose();
                throw;
            }
        });
    }

    private void ShowStatus(ThingLink l)
    {
        var s = l.Programmer.ReadStatus();
        string text = s.ReadOutProtected
            ? "Read-out protection is on: the Thing's contents cannot be read. Installing the kernel needs it removed, which erases the chip."
            : $"Kernel: {(s.HasKernel ? "present" : "none")}.  Locked blocks (UBC): {s.ProtectedBlocks}.  " +
              $"Program: {(s.ProgramLength > 0 ? $"{s.ProgramLength} bytes" : "none")}.  Uploads: {s.Uploads}.  " +
              $"Most writes to one store block: {s.WriteCounts.Max()}.";
        Log(text);
        Dispatcher.UIThread.Post(() => StatusText.Text = text);
    }

    private void OnDisconnect(object? sender, RoutedEventArgs e)
    {
        link?.Dispose();
        link = null;
        kernelConfirmed = false;
        Log("Disconnected.");
        UpdateButtons();
    }

    private async void OnInstallKernel(object? sender, RoutedEventArgs e)
    {
        if (link == null || kernel == null) return;
        if (!kernelConfirmed)
        {
            kernelConfirmed = true;
            Log("Installing the kernel writes the flash blocks that differ from what the Thing holds. " +
                "On an OLED Thing each block is rated for about 100 writes. Click again to go ahead.");
            UpdateButtons();
            return;
        }
        kernelConfirmed = false;
        var l = link;
        await Work("Install kernel", () =>
        {
            l.Programmer.Progress += Log;
            try
            {
                l.Programmer.InstallKernel(kernel.Image);
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
    }

    private async void OnUpload(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        var program = currentProgram();
        if (program == null || !program.Success)
        {
            Log("Open a program that compiles in Wren first.");
            return;
        }
        var l = link;
        await Work("Upload program", () =>
        {
            l.Programmer.Progress += Log;
            try
            {
                l.Programmer.UploadProgram(program.Image);
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        var l = link;
        await Work("Run", () =>
        {
            l.Programmer.Run();
            Log("The Thing is running. Connect again to talk to it.");
        });
        link?.Dispose();
        link = null;
        UpdateButtons();
    }
}
