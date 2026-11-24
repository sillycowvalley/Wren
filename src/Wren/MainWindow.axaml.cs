using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wren.Emu;
using Wren.Script;

namespace Wren;

public partial class MainWindow : Window
{
    private static readonly string[] FaultNames =
    {
        "", "the value stack is full", "the value stack is empty", "too many nested calls",
        "an array index is out of range", "division by zero", "a bad instruction", "a jump outside the program",
    };

    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private readonly List<string> messages = new();
    private ToggleButton[] bits = Array.Empty<ToggleButton>();
    private Kernel? kernel;
    private ThingSession? session;
    private DeviceInfo chip = DeviceInfo.Stm8s003f3;
    private string? path;
    private string[] sourceLines = Array.Empty<string>();
    private int errorLine;
    private FileSystemWatcher? watcher;
    private bool running;
    private bool stepping;
    private bool reloadPending;
    private int shownLine = -1;
    private readonly WindowLayout layout;

    public MainWindow() : this(null) { }

    /// <summary>Opens startPath if given, otherwise the program open last time, otherwise programs/OledThing.bas.</summary>
    public MainWindow(string? startPath)
    {
        InitializeComponent();
        bits = new[] { B0, B1, B2, B3, B4, B5, B6, B7 };
        KeyDown += OnWindowKeyDown;

        var settings = SavedSettings.Current;
        layout = new WindowLayout(this, settings.MainWindow);
        Closing += (_, _) =>
        {
            settings.MainWindow = layout.Placement;
            linkWindow?.KeepLayout();
            SavedSettings.Save();
        };
        if (settings.Board == BoardChoice.DevelopmentBoard)
        {
            chip = DeviceInfo.Stm8s103f3;
            DevBoardMenu.IsChecked = true;
            ThingMenu.IsChecked = false;
        }

        Pd2Button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty && session != null)
                session.Board.Pd2Low = Pd2Button.IsPressed || Pd2Pin.IsChecked == true;
        };
        Pd3Button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty && session != null)
                session.Board.Pd3Low = Pd3Button.IsPressed || Pd3Pin.IsChecked == true;
        };

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Background, OnTick);
        timer.Start();

        try
        {
            string? firmware = Kernel.FindFirmwareFolder(AppContext.BaseDirectory)
                ?? throw new InvalidOperationException("The firmware folder was not found next to Wren.");
            kernel = Kernel.Assemble(firmware);
            NewSession();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            messages.Add(ex.Message);
        }

        string? last = settings.LastFile is { } saved && File.Exists(saved) ? saved : null;
        string? first = startPath ?? last ?? FindProgram("OledThing.bas");
        if (first != null) Open(first);
        UpdateView();
    }

    /// <summary>A program in the repository's programs folder, else the copy beside Wren.</summary>
    private static string? FindProgram(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wren.sln")))
            {
                string inRepo = Path.Combine(dir.FullName, "programs", name);
                if (File.Exists(inRepo)) return inRepo;
            }
            dir = dir.Parent;
        }
        string beside = Path.Combine(AppContext.BaseDirectory, "programs", name);
        return File.Exists(beside) ? beside : null;
    }

    private void NewSession()
    {
        if (kernel == null) return;
        byte inputs = session?.Board.Inputs ?? 0;
        session = new ThingSession(kernel, chip);
        session.Board.SetInputs(inputs);
        ApplyPins();
        Oled.Oled = session.Board.Oled;
    }

    // ---- The program ----

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a Wren BASIC program",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Wren BASIC") { Patterns = new[] { "*.bas" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });
        string? chosen = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (chosen != null) Open(chosen);
    }

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        if (path != null) Open(path);
    }

    private void Open(string file)
    {
        path = file;
        SavedSettings.Current.LastFile = Path.GetFullPath(file);
        Title = $"{Path.GetFileName(file)} - Wren";
        Watch(file);
        messages.Clear();
        errorLine = 0;
        try
        {
            string source = File.ReadAllText(file);
            sourceLines = source.Replace("\r\n", "\n").Split('\n');
            if (session == null) return;
            var program = session.Upload(source);
            if (!program.Success)
            {
                foreach (var error in program.Errors) messages.Add(error.ToString());
                errorLine = program.Errors[0].Line;
                SetRunning(false);
            }
            else
            {
                messages.Add($"Uploaded {Path.GetFileName(file)}: {program.Image.Length} bytes in {program.Blocks} blocks.");
                SetRunning(true);
            }
        }
        catch (IOException ex)
        {
            messages.Add(ex.Message);
        }
        shownLine = -1;
        UpdateView();
    }

    /// <summary>Reloads when the file is saved.</summary>
    private void Watch(string file)
    {
        watcher?.Dispose();
        string? directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (directory == null) return;
        watcher = new FileSystemWatcher(directory, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        watcher.Changed += (_, _) => reloadPending = true;
        watcher.Created += (_, _) => reloadPending = true;
        watcher.Renamed += (_, _) => reloadPending = true;
    }

    // ---- Menus ----

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The menu shortcuts: F5 run or pause, F10 step, Ctrl+O open, Ctrl+R reload.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5) OnRun(sender, e);
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F10) { if (StepMenu.IsEnabled) OnStep(sender, e); }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O) OnOpen(sender, e);
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R) OnReload(sender, e);
        else return;
        e.Handled = true;
    }

    /// <summary>Opens the quick reference (docs/Infographics) in the viewer Windows has for PNGs.</summary>
    private void OnQuickReference(object? sender, RoutedEventArgs e)
    {
        string? found = FindBeside("docs/Infographics/Wren_BASIC_Quick_Reference.png");
        if (found == null)
        {
            messages.Add("The quick reference was not found beside Wren.");
            UpdateView();
            return;
        }
        Process.Start(new ProcessStartInfo(found) { UseShellExecute = true });
    }

    private void OnAbout(object? sender, RoutedEventArgs e) => new SplashWindow { IsAbout = true }.ShowDialog(this);

    /// <summary>A file in the repository (looking upward from Wren) or beside Wren.</summary>
    private static string? FindBeside(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ---- A real Thing ----

    private LinkWindow? linkWindow;

    private void OnOpenLink(object? sender, RoutedEventArgs e)
    {
        if (linkWindow != null)
        {
            linkWindow.Activate();
            return;
        }
        linkWindow = new LinkWindow(kernel, () => session?.Program);
        linkWindow.Closed += (_, _) => linkWindow = null;
        linkWindow.Show(this);
    }

    // ---- Running and stepping ----

    private void OnRun(object? sender, RoutedEventArgs e)
    {
        if (running)
        {
            // Pause: stop at the next statement if one comes soon, else wait for events.
            SetRunning(false);
            session?.Step(0.05);
        }
        else SetRunning(true);
        UpdateView();
    }

    private void SetRunning(bool run)
    {
        running = run;
        stepping = false;
        RunMenu.Header = running ? "_Pause" : "_Run";
        clock.Restart();
    }

    private void OnStep(object? sender, RoutedEventArgs e)
    {
        SetRunning(false);
        stepping = true;
        clock.Restart();
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        session?.Board.Reset();
        SetRunning(true);
    }

    private void OnPowerCycle(object? sender, RoutedEventArgs e)
    {
        session?.Board.PowerOn();
        SetRunning(true);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (reloadPending)
        {
            reloadPending = false;
            if (path != null) Open(path);
        }
        if (session == null) return;

        // Run as much emulated time as real time has passed, at most 50 ms per tick.
        double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.05);
        clock.Restart();

        if (running)
        {
            var stop = session.Run(seconds);
            if (stop == SessionStop.Breakpoint) SetRunning(false);
        }
        else if (stepping)
        {
            // A step can wait: the next statement may only come with an event.
            var stop = session.Step(seconds);
            if (stop != SessionStop.TimeUp) stepping = false;
        }

        if (session.Fault != 0 && (running || stepping)) SetRunning(false);
        UpdateView();
    }

    // ---- Board ----

    private void OnBoardChanged(object? sender, RoutedEventArgs e)
    {
        var chosen = DevBoardMenu.IsChecked ? DeviceInfo.Stm8s103f3 : DeviceInfo.Stm8s003f3;
        if (chosen == chip) return;
        chip = chosen;
        SavedSettings.Current.Board = chip == DeviceInfo.Stm8s103f3 ? BoardChoice.DevelopmentBoard : BoardChoice.Thing;
        NewSession();
        if (path != null) Open(path);
    }

    private void OnBitClicked(object? sender, RoutedEventArgs e)
    {
        int value = 0;
        for (int i = 0; i < 8; i++)
        {
            if (bits[i].IsChecked == true) value |= 1 << i;
        }
        session?.Board.SetInputs((byte)value);
        UpdateView();
    }

    private void OnHexKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string text = (HexBox.Text ?? "").Trim().TrimStart('$');
        if (byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out byte value))
        {
            session?.Board.SetInputs(value);
            UpdateView();
        }
    }

    private void OnPinClicked(object? sender, RoutedEventArgs e) => ApplyPins();

    private void OnAnalogLevelChanged(object? sender, RangeBaseValueChangedEventArgs e) => ApplyPins();

    private void ApplyPins()
    {
        if (session == null) return;
        var board = session.Board;
        board.HexLow = HexPin.IsChecked == true;
        board.BarLow = BarPin.IsChecked == true;
        board.InvLow = InvPin.IsChecked == true;
        board.Pd2Low = Pd2Pin.IsChecked == true || Pd2Button.IsPressed;
        board.Pd3Low = Pd3Pin.IsChecked == true || Pd3Button.IsPressed;
        board.AnalogPd2 = Pd2Analog.IsChecked == true ? (int)Pd2Level.Value : null;
        board.AnalogPd3 = Pd3Analog.IsChecked == true ? (int)Pd3Level.Value : null;
        Pd2Text.Text = board.AnalogPd2?.ToString() ?? "";
        Pd3Text.Text = board.AnalogPd3?.ToString() ?? "";
    }

    // ---- Breakpoints ----

    private void OnLineTapped(object? sender, TappedEventArgs e)
    {
        if (session == null || sender is not Control { DataContext: SourceLine line }) return;
        if (!session.Breakpoints.Remove(line.Number)) session.Breakpoints.Add(line.Number);
        session.UpdateBreakpoints();
        shownLine = -1;
        UpdateView();
    }

    // ---- View ----

    private void UpdateView()
    {
        var board = session?.Board;
        if (board != null)
        {
            for (int i = 0; i < 8; i++) bits[i].IsChecked = (board.Inputs & (1 << i)) != 0;
            if (!HexBox.IsFocused) HexBox.Text = board.Inputs.ToString("X2");
        }

        int current = session != null && !running ? session.CurrentLine : 0;
        UpdateSource(current);
        NowText.Text = NowDescription(current);
        VariablesText.Text = VariablesDescription();
        StoreText.Text = StoreDescription();

        if (!MessagesList.ItemsSource?.Cast<string>().SequenceEqual(messages) ?? true)
            MessagesList.ItemsSource = messages.ToList();

        StatusText.Text = board == null ? "" :
            $"{(running ? "Running" : stepping ? "Stepping: waiting for the next statement" : "Paused")}   " +
            $"{board.Mcu.Seconds:F2} s since reset   OLED {(board.Oled.Lit ? "on" : "off")}";
        StepMenu.IsEnabled = session?.Program != null;
        Oled.Refresh();
    }

    private void UpdateSource(int current)
    {
        int key = current * 7919 + (session?.Breakpoints.Sum() ?? 0) * 31 + errorLine * 13 + sourceLines.Length;
        if (key == shownLine) return;
        shownLine = key;
        SourceList.ItemsSource = sourceLines.Select((text, i) => new SourceLine
        {
            Number = i + 1,
            Text = text.Replace("\t", "    "),
            HasBreakpoint = session?.Breakpoints.Contains(i + 1) ?? false,
            IsCurrent = current == i + 1,
            HasError = errorLine == i + 1,
        }).ToList();
        if (current > 0) ScrollToLine(current);
        else if (errorLine > 0) ScrollToLine(errorLine);
    }

    private void ScrollToLine(int line)
    {
        // Each line is one text row; keep the line in view.
        double rowHeight = 18;
        double top = (line - 1) * rowHeight;
        var view = SourceScroll.Viewport.Height;
        if (top < SourceScroll.Offset.Y || top > SourceScroll.Offset.Y + view - rowHeight * 2)
            SourceScroll.Offset = new Avalonia.Vector(SourceScroll.Offset.X, Math.Max(0, top - view / 3));
    }

    private string NowDescription(int current)
    {
        if (session == null) return "";
        if (session.Program == null) return errorLine > 0 ? "The program has errors: see Messages." : "No program.";
        int fault = session.Fault;
        if (fault != 0)
        {
            int line = session.Program.LineAt(Math.Max(0, session.Vpc - 1));
            string what = fault < FaultNames.Length ? FaultNames[fault] : $"fault {fault}";
            return $"Stopped at line {line}: {what} (ERR {fault}).";
        }
        if (running) return "Running.";
        if (current == 0) return "Waiting for an event.";
        var routine = session.CurrentRoutine;
        string where = routine == null ? "" : $"In {routine.Name}";
        if (routine != null && routine.Name.StartsWith("ON ") && session.CallDepth == 0)
            where += $", VALUE = {session.EventValue} (${session.EventValue:X2})";
        if (session.CallDepth > 0) where += $", {session.CallDepth} call{(session.CallDepth == 1 ? "" : "s")} deep";
        return $"{where}.\nNext: line {current}.";
    }

    private string VariablesDescription()
    {
        if (session?.Program == null) return "";
        var lines = session.Globals().Select(v => $"{v.Name} = {v.Value}").ToList();
        if (lines.Count == 0) lines.Add("(no global variables)");
        if (!running)
        {
            var locals = session.Locals().ToList();
            var routine = session.CurrentRoutine;
            if (locals.Count > 0 && routine != null)
            {
                lines.Add("");
                lines.Add($"In {routine.Name}:");
                lines.AddRange(locals.Select(v => $"  {v.Name} = {v.Value}"));
            }
        }
        return string.Join("\n", lines);
    }

    private string StoreDescription()
    {
        if (session == null) return "";
        var fat = session.ReadFat();
        var counts = ProgramStore.WriteCounts(fat);
        int uploads = fat[0] == 1 ? (fat[6] << 8) | fat[7] : 0;
        string program = session.Program == null ? "" :
            $"Program: {session.Program.Image.Length} bytes in {session.Program.Blocks} of {ProgramStore.Blocks} blocks; " +
            $"variables {session.Program.DataBytes} of {Compiler.VariableArea - session.Program.Blocks} bytes.\n";
        return program +
            $"Uploads to this emulated Thing: {uploads}. Most writes to one block: {counts.Max()} " +
            $"(rated {chip.FlashEndurance:N0}); fewest: {counts.Min()}.";
    }
}
