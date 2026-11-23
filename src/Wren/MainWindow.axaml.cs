using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wren.Emu;

namespace Wren;

public partial class MainWindow : Window
{
    private readonly Session session = new();
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private ToggleButton[] bits = Array.Empty<ToggleButton>();
    private FileSystemWatcher? watcher;
    private bool running;
    private bool reloadPending;

    public MainWindow() : this(null) { }

    /// <summary>Opens startPath if given, otherwise the repository's demo when it can be found.</summary>
    public MainWindow(string? startPath)
    {
        InitializeComponent();
        bits = new[] { B0, B1, B2, B3, B4, B5, B6, B7 };
        BoardBox.SelectedIndex = 0;

        Pd2Button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty) session.Board.Pd2Low = Pd2Button.IsPressed;
        };
        Pd3Button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty) session.Board.Pd3Low = Pd3Button.IsPressed;
        };

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Background, OnTick);
        timer.Start();

        string? first = startPath ?? Session.FindDemo();
        if (first != null) LoadProgram(first, run: true);
        else UpdateView();
    }

    // ---- Program ----

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a program",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("STM8 programs") { Patterns = new[] { "*.asm", "*.s19", "*.hex", "*.ihx" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });
        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path != null) LoadProgram(path, run: true);
    }

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        if (session.ProgramPath != null) LoadProgram(session.ProgramPath, run: running);
    }

    private void LoadProgram(string path, bool run)
    {
        bool ok = session.Load(path);
        Oled.Oled = session.Board.Oled;
        FileText.Text = Path.GetFileName(path);
        ToolTip.SetTip(FileText, path);
        Watch(path);
        SetRunning(ok && run);
        UpdateView();
    }

    /// <summary>Reloads when the file (or anything beside it) is saved.</summary>
    private void Watch(string path)
    {
        watcher?.Dispose();
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory == null) return;
        watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        watcher.Changed += (_, _) => reloadPending = true;
        watcher.Created += (_, _) => reloadPending = true;
        watcher.Renamed += (_, _) => reloadPending = true;
    }

    // ---- Running ----

    private void OnRun(object? sender, RoutedEventArgs e) => SetRunning(!running);

    private void SetRunning(bool run)
    {
        running = run;
        RunButton.Content = running ? "Pause" : "Run";
        StepButton.IsEnabled = !running;
        clock.Restart();
    }

    private void OnStep(object? sender, RoutedEventArgs e)
    {
        session.Board.Mcu.Step();
        ReportFault();
        UpdateView();
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        session.Board.Reset();
        UpdateView();
    }

    private void OnPowerCycle(object? sender, RoutedEventArgs e)
    {
        session.Board.PowerOn();
        UpdateView();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (reloadPending)
        {
            reloadPending = false;
            if (session.ProgramPath != null) LoadProgram(session.ProgramPath, run: running || session.Assembly?.Success == false);
        }

        if (running)
        {
            // Run as much emulated time as real time has passed, at most 50 ms per tick.
            double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.05);
            clock.Restart();
            var reason = session.Board.Mcu.Run(seconds);
            if (reason != StopReason.TimeUp)
            {
                SetRunning(false);
                if (reason == StopReason.Break) session.Messages.Add($"BREAK at {session.Board.Mcu.Cpu.PC:X6}");
                ReportFault();
            }
        }
        UpdateView();
    }

    private void ReportFault()
    {
        var cpu = session.Board.Mcu.Cpu;
        if (cpu.Fault == null) return;
        session.Messages.Add($"{cpu.Fault} at {cpu.PC:X6}");
        cpu.Fault = null;
        SetRunning(false);
    }

    // ---- Board ----

    private void OnBoardChanged(object? sender, SelectionChangedEventArgs e)
    {
        var chip = BoardBox.SelectedIndex == 1 ? DeviceInfo.Stm8s103f3 : DeviceInfo.Stm8s003f3;
        if (chip == session.Chip) return;
        session.SetChip(chip);
        Oled.Oled = session.Board.Oled;
        UpdateView();
    }

    private void OnBitClicked(object? sender, RoutedEventArgs e)
    {
        int value = 0;
        for (int i = 0; i < 8; i++)
        {
            if (bits[i].IsChecked == true) value |= 1 << i;
        }
        session.Board.SetInputs((byte)value);
        UpdateView();
    }

    private void OnHexKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string text = (HexBox.Text ?? "").Trim().TrimStart('$');
        if (byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out byte value))
        {
            session.Board.SetInputs(value);
            UpdateView();
        }
    }

    private void OnModePinClicked(object? sender, RoutedEventArgs e)
    {
        session.Board.HexLow = HexPin.IsChecked == true;
        session.Board.BarLow = BarPin.IsChecked == true;
        session.Board.InvLow = InvPin.IsChecked == true;
    }

    // ---- View ----

    private void UpdateView()
    {
        var board = session.Board;
        for (int i = 0; i < 8; i++) bits[i].IsChecked = (board.Inputs & (1 << i)) != 0;
        if (!HexBox.IsFocused) HexBox.Text = board.Inputs.ToString("X2");

        RegistersText.Text = session.RegisterText();
        WearText.Text = session.WearText();

        var messages = new List<string>(session.Messages);
        foreach (string name in board.Mcu.UnmodelledPeripherals.OrderBy(n => n))
            messages.Add($"Used {name}, which Wren does not model yet.");
        if (board.Oled.UnknownCommands.Count > 0)
            messages.Add($"OLED ignored unknown commands: {string.Join(" ", board.Oled.UnknownCommands.Distinct().Select(c => c.ToString("X2")))}");
        if (!MessagesList.ItemsSource?.Cast<string>().SequenceEqual(messages) ?? true)
            MessagesList.ItemsSource = messages;

        StatusText.Text = $"{(running ? "Running" : "Paused")}   {board.Mcu.Seconds:F3} s since reset   " +
            $"CPU {16.0 / (1 << ((board.Mcu.Clock.Ckdivr >> 3) & 3)) / board.Mcu.Clock.CpuDivider:0.###} MHz   " +
            $"OLED {(board.Oled.Lit ? "on" : "off")}";
        Oled.Refresh();
    }
}
