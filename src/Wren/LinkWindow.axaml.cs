using System.Diagnostics;
using Avalonia.Controls;
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
    private readonly Kernel? kernel;
    private IReadOnlyList<ProgrammerInfo> devices = Array.Empty<ProgrammerInfo>();
    private ThingLink? link;
    private bool kernelConfirmed;
    private bool unprotectConfirmed;
    private bool factoryConfirmed;
    private bool hasWrenKernel;
    private bool hasCurrentKernel;
    private WearWindow? wearWindow;
    private string? imageToWrite;

    /// <summary>The EEPROM size check's first half is done: power-cycle, connect, and click again.</summary>
    private EepromProbe? eepromProbe;
    private readonly WindowLayout layout;

    public LinkWindow() : this(null) { }

    /// <summary>
    /// For setting a Thing up and checking it: install the kernel, restore factory settings,
    /// write images, read the wear, run the checks. Programs are uploaded, run and debugged
    /// from the main window (Debug → Target → Thing on the programmer).
    /// </summary>
    public LinkWindow(Kernel? kernel)
    {
        InitializeComponent();
        this.kernel = kernel;
        layout = new WindowLayout(this, SavedSettings.Current.ProgrammerWindow);
        Closing += (_, _) =>
        {
            KeepLayout();
            SavedSettings.Save();
        };
        Closed += (_, _) => ReleaseThing();
        UpdateButtons();
        OnFind(null, new RoutedEventArgs());
    }

    /// <summary>Puts this window's placement in the settings (saved when a window closes).</summary>
    public void KeepLayout()
    {
        SavedSettings.Current.ProgrammerWindow = layout.Placement;
        wearWindow?.KeepLayout();
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
        KernelButton.IsEnabled = open && kernel != null;
        FactoryButton.IsEnabled = connected;
        ImageButton.IsEnabled = open;
        WearButton.IsEnabled = open && hasWrenKernel;
        DebugChecksButton.IsEnabled = open && hasCurrentKernel && kernel != null;
        EepromButton.IsEnabled = open;
        EepromButton.Content = eepromProbe != null ? "EEPROM check: read back" : "EEPROM size check";
        UnprotectButton.IsEnabled = connected && link!.Target.NoAccess;
        KernelButton.Content = kernelConfirmed ? "Click again to install" : "Install kernel";
        FactoryButton.Content = factoryConfirmed ? "Click again to restore" : "Restore factory settings";
        UnprotectButton.Content = unprotectConfirmed ? "Click again to erase and unprotect" : "Remove protection";
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

    private void OnFind(object? sender, RoutedEventArgs e)
    {
        devices = ThingLink.Find(out string? why);
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedIndex = ThingLink.Preferred(devices, SavedSettings.Current.LastProgrammer);
        if (why != null) Log(why);
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
                if (opened.Link is EspStLink esp)
                    Log($"Sync pulse: {esp.SyncMicroseconds:F1} µs (16 is right).");
                if (opened.Link is PicoLink pico)
                    Log($"Sync pulse: {pico.SyncMicroseconds:F1} µs (16 is right).");
                Log($"SWIM connected; the Thing's program is stopped while it is connected. SWIM_CSR = ${opened.Target.ConnectedCsr:X2}" +
                    (opened.Target.WaitedForAccess > 0 ? $", after waiting {opened.Target.WaitedForAccess} ms for the chip to allow memory access." : "."));
                if (opened.Target.NoAccess)
                {
                    const string text = "The Thing still refuses memory access after half a second (SWIM_CSR's NO_ACCESS bit stays set): " +
                        "most likely its read-out protection is on. Then its memory cannot be read or backed up, and installing the kernel " +
                        "needs the protection removed, which erases the chip.";
                    Log(text);
                    Dispatcher.UIThread.Post(() => StatusText.Text = text);
                    link = opened;
                    return;
                }
                ShowStatus(opened);
                link = opened;
                if (eepromProbe != null) Log("The EEPROM size check is waiting: click \"EEPROM check: read back\" now that the Thing has been powered off and on.");
                SavedSettings.Current.LastProgrammer = device.ToString();
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
        var s = l.Programmer.ReadStatus(kernel?.Image);
        hasWrenKernel = s.HasKernel;
        hasCurrentKernel = s.Info != null;
        string text = s.ReadOutProtected
            ? "Read-out protection is on: the Thing's contents cannot be read. Installing the kernel needs it removed, which erases the chip."
            : s.Flash switch
            {
                FlashContents.Empty => $"The flash is empty.  Locked blocks (UBC): {s.ProtectedBlocks}.",
                FlashContents.OtherFirmware => $"The flash holds other firmware, not Wren's kernel.  Locked blocks (UBC): {s.ProtectedBlocks}.",
                FlashContents.WrenKernelOlder =>
                    $"Wren's kernel, an older build: Install kernel updates it and keeps the program.  Locked blocks (UBC): {s.ProtectedBlocks}.  " +
                    $"Program: {(s.ProgramLength > 0 ? $"{s.ProgramLength} bytes" : "none")}.  Uploads: {s.Uploads}.",
                FlashContents.WrenKernelOtherBuild =>
                    $"Wren's kernel, another build: Install kernel would rewrite {s.KernelBlocksDiffering} block{(s.KernelBlocksDiffering == 1 ? "" : "s")}.  " +
                    $"Locked blocks (UBC): {s.ProtectedBlocks}.  Program: {(s.ProgramLength > 0 ? $"{s.ProgramLength} bytes" : "none")}.  " +
                    $"Uploads: {s.Uploads}.  Most writes to one store block: {s.WriteCounts.Max()}.",
                _ => $"Wren's kernel (this build).  Locked blocks (UBC): {s.ProtectedBlocks}.  " +
                    $"Program: {(s.ProgramLength > 0 ? $"{s.ProgramLength} bytes" : "none")}.  Uploads: {s.Uploads}.  " +
                    $"Most writes to one store block: {s.WriteCounts.Max()}.",
            };
        if (!s.ReadOutProtected)
        {
            var board = l.Programmer.DetectBoard();
            string boardText = board.Board switch
            {
                Board.OledThing => "An OLED Thing",
                Board.LedThing => "An LED Thing",
                _ => "The board is not known",
            } + $" ({board.How}).";
            text = boardText + "  " + text;
        }
        Log(text);
        Dispatcher.UIThread.Post(() => StatusText.Text = text);
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
            ? "Connected: the main window is running the program on the Thing with the debugger. Disconnect stops it there (the Thing runs on), so this window can connect."
            : "The main window has let go of the Thing: Connect to set it up here.";
        Log(text);
        StatusText.Text = text;
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
        catch (Exception ex) when (ex is LinkException or IOException or TimeoutException or InvalidOperationException)
        {
            Log($"Could not start the Thing as it was let go: {ex.Message}");
        }
        link.Dispose();
        link = null;
        kernelConfirmed = false;
        unprotectConfirmed = false;
        factoryConfirmed = false;
        hasWrenKernel = false;
        hasCurrentKernel = false;
        imageToWrite = null;
        Log("Disconnected: the Thing runs.");
        UpdateButtons();
    }

    private async void OnRemoveProtection(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        if (!unprotectConfirmed)
        {
            unprotectConfirmed = true;
            Log("Removing read-out protection makes the chip erase all its flash and EEPROM: the firmware on it is gone for good. " +
                "Afterwards it is blank and unprotected, ready for Install kernel or Write image. Click again to go ahead.");
            UpdateButtons();
            return;
        }
        unprotectConfirmed = false;
        var l = link;
        await Work("Remove protection", () =>
        {
            l.Programmer.Progress += Log;
            try
            {
                l.Programmer.RemoveReadOutProtection();
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
    }

    private async void OnWriteImage(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        if (imageToWrite == null)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a firmware image to write to the Thing",
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
            Log($"{Path.GetFileName(chosen)} will be written to the Thing's flash: only the blocks that differ, each costing one of an OLED Thing's ~100 flash writes. " +
                "Read-out protection stays off. Click again to go ahead.");
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
                l.Programmer.WriteImage(image, Path.GetFileName(path));
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
    }

    /// <summary>Digicool's factory firmware, copied beside Wren with the firmware folder.</summary>
    private static string? FactoryFirmwarePath()
    {
        string? folder = Kernel.FindFirmwareFolder(AppContext.BaseDirectory);
        string? path = folder == null ? null : Path.Combine(folder, "digicool", "8-bit_oled_thing_v1.1.s19");
        return path != null && File.Exists(path) ? path : null;
    }

    private async void OnWearReport(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        var l = link;
        StoreWear? wear = null;
        await Work("Wear report", () => wear = l.Programmer.ReadWear());
        if (wear == null) return;
        if (wearWindow == null)
        {
            wearWindow = new WearWindow(wear);
            wearWindow.Closed += (_, _) => wearWindow = null;
            wearWindow.Show(this);
        }
        else
        {
            wearWindow.Fill(wear);
            wearWindow.Activate();
        }
    }

    private async void OnEepromCheck(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        var l = link;
        var probe = eepromProbe;
        if (probe == null)
        {
            EepromProbe? written = null;
            await Work("EEPROM size check: writing the pattern", () =>
            {
                var check = new EepromSizeCheck(l.Target, Log);
                written = check.WritePattern();
                if (!written.PatternHeld) check.Restore(written);
            });
            eepromProbe = written is { PatternHeld: true } ? written : null;
        }
        else
        {
            await Work("EEPROM size check: reading back after the power cycle", () =>
            {
                new EepromSizeCheck(l.Target, Log).CheckAndRestore(probe);
            });
            eepromProbe = null;
        }
        UpdateButtons();
    }

    private void OnPicoWiring(object? sender, RoutedEventArgs e) => OpenSheet("Pico_Programmer_Wiring.png");

    private void OnStLinkWiring(object? sender, RoutedEventArgs e) => OpenSheet("StLink_Programmer_Wiring.png");

    private void OpenSheet(string name)
    {
        string? found = MainWindow.FindBeside($"docs/Infographics/{name}");
        if (found == null) Log($"The wiring sheet {name} was not found beside Wren.");
        else Process.Start(new ProcessStartInfo(found) { UseShellExecute = true });
    }

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

    private async void OnRestoreFactory(object? sender, RoutedEventArgs e)
    {
        if (link == null) return;
        string? path = FactoryFirmwarePath();
        if (path == null)
        {
            Log("Digicool's factory firmware (firmware/digicool/8-bit_oled_thing_v1.1.s19) was not found beside Wren.");
            return;
        }
        if (!factoryConfirmed)
        {
            factoryConfirmed = true;
            Log(link.Target.NoAccess
                ? "Restoring factory settings first removes read-out protection, which erases all the chip's flash and EEPROM, then writes Digicool's firmware back, unprotected. Click again to go ahead."
                : "Restoring factory settings writes Digicool's firmware over what the Thing holds (only blocks that differ), clears the EEPROM and leaves it unprotected. Click again to go ahead.");
            UpdateButtons();
            return;
        }
        factoryConfirmed = false;
        var l = link;
        await Work("Restore factory settings", () =>
        {
            var image = MemoryImage.Parse(File.ReadAllText(path));
            l.Programmer.Progress += Log;
            try
            {
                l.Programmer.RestoreFactory(image);
                ShowStatus(l);
            }
            finally
            {
                l.Programmer.Progress -= Log;
            }
        });
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

}
