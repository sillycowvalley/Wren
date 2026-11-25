using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wren.Asm;
using Wren.Link;
using Wren.Script;

namespace Wren;

/// <summary>Tools → Firmware: sets a real Thing up through an ST-LINK or a Pico, and checks it.</summary>
public partial class LinkWindow : Window
{
    private readonly Kernel? oledKernel, ledKernel;

    /// <summary>The kernel for the connected Thing's board (the OLED Thing's when the board is not known).</summary>
    private Kernel? kernel;
    private IReadOnlyList<ProgrammerInfo> devices = Array.Empty<ProgrammerInfo>();
    private ThingLink? link;
    private bool kernelConfirmed;
    private bool hasWrenKernel;
    private bool hasCurrentKernel;

    /// <summary>The Thing holds exactly the kernel this Wren installs: Install kernel has nothing to do.</summary>
    private bool kernelUpToDate;
    private string? imageToWrite;

    /// <summary>Watches the serial ports: the list follows them, and an unplugged programmer is let go.</summary>
    private readonly DispatcherTimer portWatch = new() { Interval = TimeSpan.FromSeconds(1) };
    private HashSet<string> portNames = new();
    private readonly WindowLayout layout;

    public LinkWindow() : this(null, null) { }

    /// <summary>
    /// For setting a Thing up and checking it: install the kernel, restore factory settings,
    /// write images, read the wear, run the checks. Programs are uploaded, run and debugged
    /// from the main window (Debug → Target → Thing on the programmer).
    /// </summary>
    public LinkWindow(Kernel? oledKernel, Kernel? ledKernel)
    {
        InitializeComponent();
        this.oledKernel = oledKernel;
        this.ledKernel = ledKernel;
        kernel = oledKernel;
        layout = new WindowLayout(this, SavedSettings.Current.ProgrammerWindow);
        Closing += (_, _) =>
        {
            KeepLayout();
            SavedSettings.Save();
        };
        Closed += (_, _) =>
        {
            portWatch.Stop();
            ReleaseThing();
        };
        UpdateButtons();
        OnFind(null, new RoutedEventArgs());
        portNames = SerialPorts.Names();
        portWatch.Tick += (_, _) => WatchPorts();
        portWatch.Start();
        CheckDigicoolFirmware();
    }

    private static readonly Lazy<HttpClient> GitHub = new(DigicoolFirmware.CreateClient);

    /// <summary>
    /// Wren carries none of Digicool's firmware: each time this window opens it fetches any new
    /// or changed .s19 file from Digicool's GitHub into the per-user folder Write image… opens.
    /// Quiet in the background; offline, what was fetched before is still there.
    /// </summary>
    private async void CheckDigicoolFirmware()
    {
        var firmware = new DigicoolFirmware(GitHub.Value, DigicoolFirmware.DefaultFolder);
        firmware.LearnFolder();
        var check = await firmware.UpdateAsync();
        foreach (string name in check.Downloaded) Log($"Digicool firmware: fetched {name} from Digicool's GitHub.");
        foreach (string failure in check.Failures) DiagnosticLog.Write($"Firmware: Digicool firmware: {failure}");
        if (check.Downloaded.Count == 0 && check.Failures.Count == 0)
            Log($"Digicool firmware: up to date ({check.UpToDate.Count} files).");
        else if (check.Failures.Count > 0)
            Log(check.UpToDate.Count + check.Downloaded.Count > 0
                ? $"Digicool firmware: not all checked ({check.Failures[0]})."
                : $"Digicool firmware: could not check Digicool's GitHub ({check.Failures[0]}).");
    }

    /// <summary>Puts this window's placement in the settings (saved when a window closes).</summary>
    public void KeepLayout()
    {
        SavedSettings.Current.ProgrammerWindow = layout.Placement;
    }

    private void Log(string message)
    {
        DiagnosticLog.Write($"Firmware: {message}");
        Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += message + Environment.NewLine;
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });
    }

    private void UpdateButtons()
    {
        bool connected = link != null;
        bool open = connected && !link!.Target.NoAccess;
        ConnectButton.IsEnabled = !connected && !heldByMainWindow && devices.Count > 0;
        DisconnectButton.IsEnabled = connected || heldByMainWindow;
        KernelButton.IsEnabled = open && kernel != null && !kernelUpToDate;
        ImageButton.IsEnabled = connected;                     // on a protected Thing too: it removes the protection
        WearButton.IsEnabled = open && hasWrenKernel;
        DebugChecksButton.IsEnabled = open && hasCurrentKernel && kernel != null;
        KernelButton.Content = kernelConfirmed ? "Click again to install" : "Install kernel";
        ImageButton.Content = imageToWrite != null ? $"Click again to write {Path.GetFileName(imageToWrite)}" : "Write image…";
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
        catch (Exception ex) when (ex is LinkException or InvalidOperationException or ArgumentException or IOException
            or UnauthorizedAccessException or TimeoutException)
        {
            Log($"Failed: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
            UpdateButtons();
        }
    }

    private void OnFind(object? sender, RoutedEventArgs e) => Refresh(quiet: false);

    private void Refresh(bool quiet)
    {
        devices = ThingLink.Find(out string? why);
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedIndex = ThingLink.Preferred(devices, SavedSettings.Current.LastProgrammer);
        if (why != null && !quiet) Log(why);
        Log(devices.Count == 0
            ? "No programmer found. An ST-LINK shows in Device Manager under \"Universal Serial Bus devices\"; a Pico under \"Ports (COM & LPT)\"."
            : $"Found: {string.Join(", ", devices)}");
        UpdateButtons();
    }

    private async void OnConnect(object? sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not ProgrammerInfo device) return;
        await Work("Connect", () =>
        {
            var opened = ThingLink.Open(device);
            try
            {
                foreach (string line in opened.About) Log(line);
                opened.Target.Connect();
                if (opened.Link is PicoLink pico)
                    Log($"Sync pulse: {pico.SyncMicroseconds:F1} µs (16 is right).");
                Log($"SWIM connected; the Thing's program is stopped while it is connected. SWIM_CSR = ${opened.Target.ConnectedCsr:X2}" +
                    (opened.Target.WaitedForAccess > 0 ? $", after waiting {opened.Target.WaitedForAccess} ms for the chip to allow memory access." : "."));
                if (opened.Target.NoAccess)
                {
                    const string text = "Read-out protection is on: the Thing cannot be read.\nWrite image… removes it (erasing the chip).";
                    Log(text);
                    Dispatcher.UIThread.Post(() => SetStatus(text));
                    link = opened;
                    return;
                }
                ShowStatus(opened);
                link = opened;
                SavedSettings.Current.LastProgrammer = device.ToString();
            }
            catch
            {
                opened.Dispose();
                throw;
            }
        });
    }

    /// <summary>The status under the buttons, one statement a line: each label (before its colon) in bold.</summary>
    private void SetStatus(string text)
    {
        var inlines = new InlineCollection();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            int colon = lines[i].IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && colon <= 30)
            {
                inlines.Add(new Run(lines[i][..(colon + 1)]) { FontWeight = FontWeight.Bold });
                inlines.Add(new Run(lines[i][(colon + 1)..]));
            }
            else inlines.Add(new Run(lines[i]));
        }
        StatusText.Inlines = inlines;
    }

    /// <summary>What the Thing holds, as a few short lines under the buttons.</summary>
    private void ShowStatus(ThingLink l)
    {
        var found = l.Target.NoAccess ? Board.Unknown : l.Programmer.DetectBoard().Board;
        kernel = found == Board.LedThing ? ledKernel : oledKernel;
        var s = l.Programmer.ReadStatus(kernel?.Image);
        hasWrenKernel = s.HasKernel;
        hasCurrentKernel = s.Info != null;
        kernelUpToDate = s.Flash == FlashContents.WrenKernel;
        string text;
        if (s.ReadOutProtected)
            text = "Read-out protection is on: the Thing cannot be read.\nWrite image… removes it (erasing the chip).";
        else
        {
            string board = found switch
            {
                Board.OledThing => "OLED Thing",
                Board.LedThing => "LED Thing",
                _ => "not known",
            };
            if (s.Info is { } info) board += $", {info.EepromSize} bytes of EEPROM";
            string firmware = s.Flash switch
            {
                FlashContents.Empty => "empty",
                FlashContents.OtherFirmware => "not Wren's (Install kernel replaces it)",
                FlashContents.WrenKernelOlder => "Wren's kernel, out of date (Install kernel updates it)",
                FlashContents.WrenKernelOtherBuild => $"Wren's kernel, out of date (Install kernel updates {s.KernelBlocksDiffering} block{(s.KernelBlocksDiffering == 1 ? "" : "s")})",
                _ => "Wren's kernel, up to date",
            };
            text = $"Board: {board}\nFirmware: {firmware}";
            if (s.HasKernel) text += $"\nProgram: {(s.ProgramLength > 0 ? $"{s.ProgramLength} bytes" : "none")}";
        }
        Log(text);
        Dispatcher.UIThread.Post(() => SetStatus(text));
    }

    private void OnDisconnect(object? sender, RoutedEventArgs e)
    {
        if (heldByMainWindow) releaseMainWindow?.Invoke();
        else ReleaseThing();
    }

    private bool heldByMainWindow;

    /// <summary>Asks the main window to stop debugging on the Thing, so this window can connect.</summary>
    private Action? releaseMainWindow;

    /// <summary>
    /// The main window holds the programmer while it runs a program on the Thing with the
    /// debugger: this window shows it connected there, and Disconnect stops that session.
    /// </summary>
    public void HeldByMainWindow(bool held, Action release)
    {
        releaseMainWindow = release;
        if (held == heldByMainWindow) return;
        heldByMainWindow = held;
        string text = held
            ? "Connected: the main window is debugging on the Thing.\nDisconnect stops that."
            : "The main window has let go of the Thing.";
        Log(text);
        SetStatus(text);
        UpdateButtons();
    }

    /// <summary>
    /// Lets go of the Thing, leaving it running (connected, its CPU is stalled): Disconnect,
    /// closing the window, and the main window taking the programmer to run or debug a program.
    /// </summary>
    public void ReleaseThing()
    {
        if (link == null) return;
        try
        {
            if (!link.Target.NoAccess) link.Programmer.Run();
        }
        catch (Exception ex) when (ex is LinkException or IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log($"Could not start the Thing as it was let go: {ex.Message}");
        }
        Forget();
        Log("Disconnected: the Thing runs.");
        UpdateButtons();
    }

    /// <summary>Closes the link (which never throws) and resets what this window knew about the Thing.</summary>
    private void Forget()
    {
        link?.Dispose();
        link = null;
        kernelConfirmed = false;
        hasWrenKernel = false;
        hasCurrentKernel = false;
        kernelUpToDate = false;
        imageToWrite = null;
    }

    /// <summary>
    /// Every second: when the serial ports change, the list follows (a Pico plugged in shows at
    /// once), and if the connected programmer's port has gone it was unplugged, so it is let go
    /// here rather than failing at the next click.
    /// </summary>
    private void WatchPorts()
    {
        var now = SerialPorts.Names();
        if (now.SetEquals(portNames)) return;
        portNames = now;
        if (link?.PortName is string port && !now.Contains(port))
        {
            Forget();
            const string text = "The programmer was unplugged. Plug it in again and click Connect.";
            Log($"{port} has gone: {text}");
            SetStatus(text);
        }
        if (IsEnabled) Refresh(quiet: true);
        UpdateButtons();
    }

    private async void OnWriteImage(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        if (imageToWrite == null)
        {
            string digicool = DigicoolFirmware.DefaultFolder;
            var start = Directory.Exists(digicool) ? await StorageProvider.TryGetFolderFromPathAsync(digicool) : null;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a firmware image to write to the Thing",
                SuggestedStartLocation = start,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Firmware images") { Patterns = new[] { "*.s19", "*.hex", "*.ihx" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                },
            });
            string? chosen = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (chosen == null) return;
            imageToWrite = chosen;
            Log(link.Target.NoAccess
                ? $"Writing {Path.GetFileName(chosen)} first removes read-out protection, which erases all the chip's flash and EEPROM, then writes the image, unprotected. Click again to go ahead."
                : $"{Path.GetFileName(chosen)} will be written to the Thing's flash: only the blocks that differ, each costing one of a Thing's ~100 flash writes. " +
                  "The EEPROM is cleared (Wren's program table goes with Wren's kernel) and read-out protection stays off. Click again to go ahead.");
            UpdateButtons();
            return;
        }
        string path = imageToWrite;
        imageToWrite = null;
        var l = link;
        await Work("Write image", () =>
        {
            var image = MemoryImage.Parse(File.ReadAllText(path));
            if (image.Count == 0) throw new InvalidOperationException($"{Path.GetFileName(path)} holds no S-records or Intel HEX data");
            l.Programmer.Progress += Log;
            try
            {
                l.Programmer.ReplaceFirmware(image, Path.GetFileName(path));
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
    }

    private async void OnWearReport(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        var l = link;
        StoreWear? wear = null;
        await Work("Wear report", () => wear = l.Programmer.ReadWear());
        if (wear == null) return;
        await new WearWindow(wear).ShowDialog(this);            // modal: it keeps the focus until Close
    }

    private void OnPicoWiring(object? sender, RoutedEventArgs e) => OpenSheet("Pico_Programmer_Wiring.png");

    private void OnStLinkWiring(object? sender, RoutedEventArgs e) => OpenSheet("StLink_Programmer_Wiring.png");

    private void OpenSheet(string name)
    {
        string? found = MainWindow.FindBeside($"docs/Infographics/{name}");
        if (found == null) Log($"The wiring sheet {name} was not found beside Wren.");
        else Process.Start(new ProcessStartInfo(found) { UseShellExecute = true });
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = "";

    private async void OnCopyLog(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(LogBox.Text ?? "");
    }

    private async void OnDebugChecks(object? sender, RoutedEventArgs e)
    {
        if (link == null || kernel == null) return;
        var l = link;
        var k = kernel;
        DebugModuleFindings? found = null;
        await Work("Debug module checks", () =>
        {
            bool hasProgram = l.Programmer.ReadStatus(k.Image).ProgramLength > 0;
            found = new DebugModuleChecks(l.Target, k, Log).Run(hasProgram);
        });
        if (found == null) return;
        static string Say(bool? r) => r switch { true => "yes", false => "no", null => "unknown" };
        Log($"Summary: breakpoint on reading flash {Say(found.ReadBreakpointOnFlash)}; reading RAM while idle {Say(found.LiveReadsWhileIdle)}; " +
            $"STALL stops the CPU {Say(found.StallStopsTheCpu)}; TIM4 freezes {Say(found.Tim4Freezes)}.");
    }

    private async void OnInstallKernel(object? sender, RoutedEventArgs e)
    {
        if (link == null || kernel == null) return;
        if (!kernelConfirmed)
        {
            kernelConfirmed = true;
            Log("Installing the kernel writes the flash blocks that differ from what the Thing holds. " +
                "A Thing's flash is rated for about 100 writes a block. Click again to go ahead.");
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
                l.Programmer.InstallKernel(new[] { oledKernel, ledKernel }.OfType<Kernel>().Select(k => k.Image));
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
    }

}
