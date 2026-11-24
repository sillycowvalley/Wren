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
    private readonly BreakpointMargin breakpointMargin = new();
    private readonly LineHighlighter highlighter = new();

    /// <summary>The program text as last saved or opened: the editor is "changed" when it differs.</summary>
    private string savedText = "";

    /// <summary>Set once the user has answered the unsaved-changes question, so closing goes ahead.</summary>
    private bool closeConfirmed;

    private bool Changed => (Editor.Text ?? "") != savedText;
    private readonly WindowLayout layout;

    /// <summary>The right-hand column, sized with the splitter.</summary>
    private ColumnDefinition SidePanel => RightGrid.ColumnDefinitions[3];

    /// <summary>The right-hand column's width as last chosen with the splitter (or the default).</summary>
    private double sideWidth = 320;

    /// <summary>
    /// Gives the right-hand column its chosen width, or less when the window is too narrow for
    /// that and the program's minimum width.
    /// </summary>
    private void FitSidePanel()
    {
        double room = RightGrid.Bounds.Width - RightGrid.ColumnDefinitions[0].MinWidth
            - RightGrid.ColumnDefinitions[1].ActualWidth - RightGrid.ColumnDefinitions[2].ActualWidth;
        double width = Math.Max(Math.Min(sideWidth, room), SidePanel.MinWidth);
        if (SidePanel.Width.Value != width) SidePanel.Width = new GridLength(width);
    }

    public MainWindow() : this(null) { }

    /// <summary>Opens startPath if given, otherwise the program open last time, otherwise programs/OledThing.bas.</summary>
    public MainWindow(string? startPath)
    {
        InitializeComponent();
        bits = new[] { B0, B1, B2, B3, B4, B5, B6, B7 };
        KeyDown += OnWindowKeyDown;
        SetUpEditor();

        var settings = SavedSettings.Current;
        layout = new WindowLayout(this, settings.MainWindow);
        Closing += (_, e) =>
        {
            if (Changed && !closeConfirmed)
            {
                e.Cancel = true;
                CloseAfterAsking();
                return;
            }
            settings.MainWindow = layout.Placement;
            settings.SidePanelWidth = sideWidth;
            linkWindow?.KeepLayout();
            SavedSettings.Save();
        };
        if (settings.SidePanelWidth is double side && double.IsFinite(side))
            sideWidth = Math.Clamp(side, SidePanel.MinWidth, SidePanel.MaxWidth);
        SidePanel.Width = new GridLength(sideWidth);
        RightGrid.SizeChanged += (_, _) => FitSidePanel();
        Splitter.DragCompleted += (_, _) => sideWidth = SidePanel.ActualWidth;
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
        if (!await MayDiscardChanges()) return;
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

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        if (path == null) return;
        if (Changed && await SaveChangesDialog.Ask(this, FileName, discardOnly: true) != SaveChangesAnswer.DontSave) return;
        Open(path);
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (!await MayDiscardChanges()) return;
        watcher?.Dispose();
        watcher = null;
        path = null;
        savedText = "";
        Editor.Text = "";
        Editor.Document.UndoStack.ClearAll();
        sourceLines = Array.Empty<string>();
        errorLine = 0;
        messages.Clear();
        messages.Add("A new program: save it (Ctrl+S) to compile and run it.");
        UpdateTitle();
        shownLine = -1;
        UpdateView();
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await Save();

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAs();

    /// <summary>Saves to the program's file (asking for one if it has none), then compiles and runs it.</summary>
    private async Task<bool> Save()
    {
        if (path == null) return await SaveAs();
        string text = Editor.Text ?? "";
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            messages.Add($"Could not save {FileName}: {ex.Message}");
            UpdateView();
            return false;
        }
        savedText = text;
        UpdateTitle();
        Compile(text);
        return true;
    }

    private async Task<bool> SaveAs()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the Wren BASIC program",
            SuggestedFileName = path != null ? Path.GetFileName(path) : "Program.bas",
            DefaultExtension = "bas",
            FileTypeChoices = new[] { new FilePickerFileType("Wren BASIC") { Patterns = new[] { "*.bas" } } },
        });
        string? chosen = file?.TryGetLocalPath();
        if (chosen == null) return false;
        path = chosen;
        SavedSettings.Current.LastFile = Path.GetFullPath(chosen);
        bool saved = await Save();
        Watch(chosen);
        return saved;
    }

    /// <summary>The file changed on disk: our own save is already compiled; anything else replaces the text unless it has unsaved edits.</summary>
    private void ReloadFromDisk()
    {
        if (path == null) return;
        string disk;
        try
        {
            disk = File.ReadAllText(path);
        }
        catch (IOException)
        {
            reloadPending = true; // still being written: try again on the next tick
            return;
        }
        if (disk == savedText) return;
        if (Changed)
        {
            messages.Add($"{FileName} changed on disk; your unsaved edits here are kept. Reload (Ctrl+R) to take the file's version.");
            UpdateView();
            return;
        }
        Open(path);
    }

    /// <summary>True when there are no unsaved edits, or they were saved or may be thrown away.</summary>
    private async Task<bool> MayDiscardChanges()
    {
        if (!Changed) return true;
        return await SaveChangesDialog.Ask(this, FileName) switch
        {
            SaveChangesAnswer.Save => await Save(),
            SaveChangesAnswer.DontSave => true,
            _ => false,
        };
    }

    private async void CloseAfterAsking()
    {
        if (!await MayDiscardChanges()) return;
        closeConfirmed = true;
        Close();
    }

    private string FileName => path != null ? Path.GetFileName(path) : "Untitled";

    private void UpdateTitle() => Title = $"{FileName}{(Changed ? " *" : "")} - Wren";

    // ---- The editor ----

    private void SetUpEditor()
    {
        Editor.TextArea.LeftMargins.Insert(0, breakpointMargin);
        Editor.TextArea.TextView.BackgroundRenderers.Add(highlighter);
        breakpointMargin.HasBreakpoint = line => session?.Breakpoints.Contains(line) ?? false;
        breakpointMargin.LineClicked += ToggleBreakpoint;
        Editor.TextChanged += (_, _) => UpdateTitle();
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
    }

    private void OnUndo(object? sender, RoutedEventArgs e) => Editor.Undo();
    private void OnRedo(object? sender, RoutedEventArgs e) => Editor.Redo();
    private void OnCut(object? sender, RoutedEventArgs e) => Editor.Cut();
    private void OnCopy(object? sender, RoutedEventArgs e) => Editor.Copy();
    private void OnPaste(object? sender, RoutedEventArgs e) => Editor.Paste();
    private void OnDelete(object? sender, RoutedEventArgs e) => Editor.Delete();
    private void OnSelectAll(object? sender, RoutedEventArgs e) => Editor.SelectAll();
    private void OnFind(object? sender, RoutedEventArgs e) => Editor.SearchPanel?.Open();

    private void Open(string file)
    {
        string source;
        try
        {
            source = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            messages.Add(ex.Message);
            UpdateView();
            return;
        }
        path = file;
        SavedSettings.Current.LastFile = Path.GetFullPath(file);
        Watch(file);
        savedText = source;
        Editor.Text = source;
        Editor.Document.UndoStack.ClearAll();
        UpdateTitle();
        Compile(source);
    }

    /// <summary>Compiles the program and uploads it to the emulated Thing; errors go to Messages.</summary>
    private void Compile(string source)
    {
        messages.Clear();
        errorLine = 0;
        sourceLines = source.Replace("\r\n", "\n").Split('\n');
        if (session != null)
        {
            var program = session.Upload(source);
            string name = path != null ? Path.GetFileName(path) : "the program";
            if (!program.Success)
            {
                foreach (var error in program.Errors) messages.Add(error.ToString());
                errorLine = program.Errors[0].Line;
                SetRunning(false);
            }
            else
            {
                messages.Add($"Uploaded {name}: {program.Image.Length} bytes in {program.Blocks} blocks.");
                SetRunning(true);
            }
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

    /// <summary>
    /// The menu shortcuts: F5 run or pause, F10 step, Ctrl+N new, Ctrl+O open, Ctrl+S save,
    /// Ctrl+Shift+S save as, Ctrl+R reload. The editor handles the Edit menu's own keys.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5) OnRun(sender, e);
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F10) { if (StepMenu.IsEnabled) OnStep(sender, e); }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N) OnNew(sender, e);
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O) OnOpen(sender, e);
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) OnSave(sender, e);
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S) OnSaveAs(sender, e);
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

    private void OnAbout(object? sender, RoutedEventArgs e) => new SplashWindow { IsAbout = true, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog(this);

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

    private void OnDebugChecks(object? sender, RoutedEventArgs e)
    {
        OnOpenLink(sender, e);
        linkWindow?.ExplainDebugChecks();
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
            ReloadFromDisk();
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

    private void ToggleBreakpoint(int line)
    {
        if (session == null) return;
        if (!session.Breakpoints.Remove(line)) session.Breakpoints.Add(line);
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
        highlighter.CurrentLine = current;
        highlighter.ErrorLine = errorLine;
        Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        breakpointMargin.InvalidateVisual();
        int show = current > 0 ? current : errorLine;
        if (show > 0 && show <= Editor.Document.LineCount) Editor.ScrollToLine(show);
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
