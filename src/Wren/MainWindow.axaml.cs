using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Wren.Emu;
using Wren.Link;
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
    private Kernel? oledKernel, ledKernel;
    private BoardChoice boardChoice = BoardChoice.Thing;

    /// <summary>The kernel for the board chosen in the Board menu.</summary>
    private Kernel? kernel => boardChoice == BoardChoice.LedThing ? ledKernel : oledKernel;

    /// <summary>The board programs are built for.</summary>
    private Board BuildBoard => boardChoice == BoardChoice.LedThing ? Board.LedThing : Board.OledThing;

    /// <summary>LED brightness from the emulator's last run, b0 first.</summary>
    private double[] ledLevels = new double[8];
    private ThingSession? session;
    private DeviceInfo chip = DeviceInfo.Stm8s003f3;
    private string? path;
    private string[] sourceLines = Array.Empty<string>();
    private int errorLine;
    private FileSystemWatcher? watcher;
    private bool reloadPending;

    /// <summary>
    /// Where debugging stands, as in Visual Studio: not started (the emulator does not run),
    /// running under the debugger, running without it, paused at a statement, or stepping
    /// (waiting for the statement the step stops at).
    /// </summary>
    private enum Mode { Design, Debugging, RunningFree, Paused, Stepping }

    private enum StepKind { Into, Over, Out, ToCursor }

    private Mode mode = Mode.Design;
    private StepKind stepKind;
    private int stepDepth;
    private int stepLine;

    /// <summary>The last good build, and the text the last build was made from.</summary>
    private CompiledProgram? built;
    private string builtText = "";


    private bool running => mode is Mode.Debugging or Mode.RunningFree;
    private bool stepping => mode == Mode.Stepping;

    /// <summary>The program was edited since the last build, or never built.</summary>
    private bool OutOfDate => built == null || (Editor.Text ?? "") != builtText;

    private RunTarget Target => SavedSettings.Current.Target;
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
    private ColumnDefinition SidePanel => RightGrid.ColumnDefinitions[2];

    /// <summary>The right-hand column's width as last chosen with the splitter (or the default).</summary>
    private double sideWidth = 320;

    /// <summary>
    /// Gives the right-hand column its chosen width, or less when the window is too narrow for
    /// that and the program's minimum width.
    /// </summary>
    private void FitSidePanel()
    {
        // The left column keeps room for the program (260) and, when it shows, the board.
        var left = RightGrid.ColumnDefinitions[0];
        left.MinWidth = 260 + (BoardColumn.IsVisible ? BoardColumn.DesiredSize.Width : 0);
        double room = RightGrid.Bounds.Width - left.MinWidth - RightGrid.ColumnDefinitions[1].ActualWidth;
        double width = Math.Max(Math.Min(sideWidth, room), SidePanel.MinWidth);
        if (SidePanel.Width.Value != width) SidePanel.Width = new GridLength(width);
    }

    public MainWindow() : this(null) { }

    /// <summary>Opens startPath if given, otherwise the program open last time, otherwise programs/OledThing/OledThing.bas.</summary>
    public MainWindow(string? startPath)
    {
        InitializeComponent();
        bits = new[] { B0, B1, B2, B3, B4, B5, B6, B7 };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        SetUpEditor();

        var settings = SavedSettings.Current;
        if (settings.DiagnosticLog && !DiagnosticLog.Enabled) DiagnosticLog.Start();
        DiagnosticLogMenu.IsChecked = DiagnosticLog.Enabled;
        AddHandler(MenuItem.ClickEvent, (_, e) =>
        {
            if (e.Source is MenuItem { Header: string header }) DiagnosticLog.Write($"Menu: {header.Replace("_", "")}");
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        layout = new WindowLayout(this, settings.MainWindow);
        Closing += (_, e) =>
        {
            if (Changed && !closeConfirmed)
            {
                e.Cancel = true;
                CloseAfterAsking();
                return;
            }
            if (thing != null)
            {
                try
                {
                    thing.Stop();
                }
                catch (Exception ex) when (Expected(ex)) { }
                finally
                {
                    thing.Dispose();
                }
                thing = null;
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
        ThingTargetMenu.IsChecked = settings.Target == RunTarget.Thing;
        EmulatorTargetMenu.IsChecked = settings.Target == RunTarget.Emulator;
        boardChoice = settings.Board;
        chip = boardChoice == BoardChoice.DevelopmentBoard ? DeviceInfo.Stm8s103f3 : DeviceInfo.Stm8s003f3;
        ThingMenu.IsChecked = boardChoice == BoardChoice.Thing;
        DevBoardMenu.IsChecked = boardChoice == BoardChoice.DevelopmentBoard;
        LedThingMenu.IsChecked = boardChoice == BoardChoice.LedThing;
        DarkModeMenu.IsChecked = settings.DarkMode;
        LargeBoardMenu.IsChecked = settings.LargeBoard;
        ApplyBoardSize();
        ApplyHighlighting();

        Pd2Button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty && session != null)
                if (session.Oled != null) session.Oled.Pd2Low = Pd2Button.IsPressed || Pd2Pin.IsChecked == true;
        };
        Pd3Button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty && session != null)
                if (session.Oled != null) session.Oled.Pd3Low = Pd3Button.IsPressed || Pd3Pin.IsChecked == true;
        };

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Background, OnTick);
        timer.Start();

        try
        {
            string? firmware = Kernel.FindFirmwareFolder(AppContext.BaseDirectory)
                ?? throw new InvalidOperationException("The firmware folder was not found next to Wren.");
            oledKernel = Kernel.Assemble(firmware, Board.OledThing);
            ledKernel = Kernel.Assemble(firmware, Board.LedThing);
            NewSession();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            messages.Add(ex.Message);
        }

        string? last = settings.LastFile is { } saved && File.Exists(saved) ? saved : null;
        string? first = startPath ?? last ?? FindProgram(Path.Combine("OledThing", "OledThing.bas"));
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
        byte inputs = session?.Inputs ?? 0;
        session = new ThingSession(kernel, chip);
        session.SetInputs(inputs);
        ApplyPins();
        if (session.Oled != null) Oled.Oled = session.Oled.Oled;
        ledLevels = new double[8];
        bool led = session.Leds != null;
        OledGroup.IsVisible = SpareGroup.IsVisible = AnalogGroup.IsVisible = !led;
        LedGroup.IsVisible = led;
        HexPin.Content = led ? "M2 (PA1)" : "HEX (PA1)";
        BarPin.Content = led ? "M1 (PA2)" : "BAR (PA2)";
        InvPin.Content = led ? "M0 (PA3)" : "INV (PA3)";
    }

    // ---- The program ----

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (!await MayDiscardChanges()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a Wren BASIC program",
            SuggestedStartLocation = await ProgramFolder(),
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

    /// <summary>
    /// Where Open and Save As start: the open program's folder, else the last program's. Given
    /// no start, Windows reuses the folder of the last file dialog, which may be the Firmware
    /// window's.
    /// </summary>
    private async Task<IStorageFolder?> ProgramFolder()
    {
        string? file = path ?? SavedSettings.Current.LastFile;
        string? folder = file != null ? Path.GetDirectoryName(Path.GetFullPath(file)) : null;
        return folder != null && Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;
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
        built = null;
        builtText = "";
        StopForAnotherProgram();
        messages.Add("A new program. Press F5 to build it and run it.");
        UpdateTitle();
        shownLine = -1;
        UpdateView();
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await Save();

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAs();

    /// <summary>Saves to the program's file (asking for one if it has none). Saving builds nothing.</summary>
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
        UpdateView();
        return true;
    }

    private async Task<bool> SaveAs()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the Wren BASIC program",
            SuggestedStartLocation = await ProgramFolder(),
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

    /// <summary>The file changed on disk: our own save is ignored; anything else replaces the text unless it has unsaved edits.</summary>
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
        Editor.TextArea.LeftMargins.Add(new Border { Width = 6 });   // a little room between the line numbers and the program
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
        sourceLines = source.Replace("\r\n", "\n").Split('\n');
        errorLine = 0;
        built = null;
        builtText = "";
        StopForAnotherProgram();
        messages.Clear();
        messages.Add($"Opened {FileName}. F5 builds it and runs it with the debugger, Ctrl+F5 without.");
        shownLine = -1;
        UpdateView();
    }

    /// <summary>
    /// Another program is in the editor: whatever ran stops, as Shift+F5 stops it. On the
    /// Thing that means the debugger lets go (the Thing runs on by itself), so F5 and Ctrl+F5
    /// can take it again for the new program.
    /// </summary>
    private void StopForAnotherProgram()
    {
        if (thing != null) ThingStop();
        SetMode(Mode.Design);
    }

    // ---- Building ----

    private void OnBuild(object? sender, RoutedEventArgs e) => Build();

    /// <summary>Compiles the editor's text; errors go to Messages. Runs nothing. True when it built.</summary>
    private bool Build()
    {
        string text = Editor.Text ?? "";
        UpdateView();                                  // logs the messages about to be cleared
        messages.Clear();
        if (DiagnosticLog.Enabled)
            DiagnosticLog.Write($"Build {FileName}, the source as built:\n" +
                string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select((l, i) => $"  {i + 1,3}| {l}")));
        errorLine = 0;
        sourceLines = text.Replace("\r\n", "\n").Split('\n');
        var program = Compiler.Compile(text, BuildBoard);
        builtText = text;
        if (!program.Success)
        {
            built = null;
            foreach (var error in program.Errors) messages.Add(error.ToString());
            errorLine = program.Errors[0].Line;
            messages.Add("Build failed.");
        }
        else
        {
            built = program;
            messages.Add($"Built {FileName}: {program.Image.Length} bytes in {program.Blocks} blocks.");
        }
        shownLine = -1;
        UpdateView();
        return built != null;
    }

    /// <summary>Builds when the program was edited since the last build, as Visual Studio does before it starts.</summary>
    private bool BuildIfNeeded() => !OutOfDate || Build();

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
    /// The menu shortcuts, Visual Studio's where it has them: F7 build; F5 start
    /// debugging or continue, Ctrl+F5 start without debugging, Shift+F5 stop, Ctrl+Shift+F5
    /// restart, Ctrl+Alt+Break break all; F11 step into, F10 step over, Shift+F11 step out,
    /// Ctrl+F10 run to cursor, F9 toggle breakpoint; and the File menu's. Caught before the
    /// editor sees them. The editor handles the Edit menu's own keys.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var m = e.KeyModifiers;
        if (DiagnosticLog.Enabled && (e.Key is >= Key.F1 and <= Key.F12 || (m & (KeyModifiers.Control | KeyModifiers.Alt)) != 0) &&
            e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt))
            DiagnosticLog.Write($"Key: {(m == KeyModifiers.None ? "" : m.ToString().Replace(", ", "+") + "+")}{e.Key} (line {Editor.TextArea.Caret.Line}, mode {mode})");
        const KeyModifiers None = KeyModifiers.None, Ctrl = KeyModifiers.Control, Shift = KeyModifiers.Shift;
        switch (e.Key)
        {
            case Key.F7 when m == None: OnBuild(sender, e); break;
            case Key.F5 when m == None: OnStartDebugging(sender, e); break;
            case Key.F5 when m == Ctrl: OnStartWithoutDebugging(sender, e); break;
            case Key.F5 when m == Shift: OnStop(sender, e); break;
            case Key.F5 when m == (Ctrl | Shift): OnRestart(sender, e); break;
            case Key.Pause or Key.Cancel when m == (Ctrl | KeyModifiers.Alt): OnBreakAll(sender, e); break;
            case Key.F11 when m == None: OnStepInto(sender, e); break;
            case Key.F11 when m == Shift: OnStepOut(sender, e); break;
            case Key.F10 when m == None: OnStepOver(sender, e); break;
            case Key.F10 when m == Ctrl: OnRunToCursor(sender, e); break;
            case Key.F9 when m == None: OnToggleBreakpoint(sender, e); break;
            case Key.N when m == Ctrl: OnNew(sender, e); break;
            case Key.O when m == Ctrl: OnOpen(sender, e); break;
            case Key.S when m == Ctrl: OnSave(sender, e); break;
            case Key.S when m == (Ctrl | Shift): OnSaveAs(sender, e); break;
            case Key.R when m == Ctrl: OnReload(sender, e); break;
            default: return;
        }
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

    private static bool Dark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>The editor's colours for the board being built for, in the current theme.</summary>
    private void ApplyHighlighting() => Editor.SyntaxHighlighting = WrenHighlighting.For(BuildBoard, Dark);

    /// <summary>View → Dark Mode: the whole application switches, and remembers it.</summary>
    private void OnDarkMode(object? sender, RoutedEventArgs e)
    {
        bool dark = DarkModeMenu.IsChecked;
        if (Application.Current != null) Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        SavedSettings.Current.DarkMode = dark;
        SavedSettings.Save();
        ApplyHighlighting();
        breakpointMargin.InvalidateVisual();
    }

    /// <summary>View → Large Emulator: the board's panel at its large or compact size, remembered.</summary>
    private void OnLargeBoard(object? sender, RoutedEventArgs e)
    {
        SavedSettings.Current.LargeBoard = LargeBoardMenu.IsChecked;
        SavedSettings.Save();
        ApplyBoardSize();
    }

    /// <summary>
    /// Sizes the emulated board: large, the OLED at 7 screen pixels to a pixel; compact, at 5,
    /// with the LEDs, the bit buttons and the groups below narrowed to match. Text keeps its size.
    /// </summary>
    private void ApplyBoardSize()
    {
        bool large = SavedSettings.Current.LargeBoard;
        int scale = large ? 7 : 5;
        Oled.Width = 64 * scale;
        Oled.Height = 32 * scale;
        ModeNote.MaxWidth = 64 * scale;                    // the notes wrap to the OLED's width, so the panel is as wide as its frame
        SpareNote.MaxWidth = 64 * scale;
        foreach (var bit in bits) bit.Width = large ? 42 : 28;
        double led = large ? 30 : 21;
        foreach (var ellipse in LedRow.Children.OfType<Avalonia.Controls.Shapes.Ellipse>())
        {
            ellipse.Width = led;
            ellipse.Height = led;
        }
        LedRow.Spacing = large ? 14 : 10;
    }

    private void OnDiagnosticLog(object? sender, RoutedEventArgs e)
    {
        if (DiagnosticLog.Enabled) DiagnosticLog.Stop();
        else DiagnosticLog.Start();
        DiagnosticLogMenu.IsChecked = DiagnosticLog.Enabled;
        SavedSettings.Current.DiagnosticLog = DiagnosticLog.Enabled;
        SavedSettings.Save();
        messages.Add(DiagnosticLog.Enabled
            ? $"Diagnostic log on: {DiagnosticLog.FilePath}. Tools → Copy diagnostic log copies it."
            : "Diagnostic log off.");
        UpdateView();
    }

    private async void OnCopyDiagnosticLog(object? sender, RoutedEventArgs e)
    {
        string text = DiagnosticLog.Text;
        if (text.Length == 0)
        {
            messages.Add("The diagnostic log is off: turn it on with Tools → Diagnostic log, then do what went wrong again.");
            UpdateView();
            return;
        }
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        messages.Add($"Copied the diagnostic log ({text.Count(c => c == '\n')} lines) to the clipboard.");
        UpdateView();
    }

    private void OnOpenLogFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DiagnosticLog.DefaultFolder);
        Process.Start(new ProcessStartInfo(DiagnosticLog.DefaultFolder) { UseShellExecute = true });
    }

    private void OnAbout(object? sender, RoutedEventArgs e) => new SplashWindow { IsAbout = true, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog(this);

    /// <summary>A file in the repository (looking upward from Wren) or beside Wren.</summary>
    internal static string? FindBeside(string relative)
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
        linkWindow = new LinkWindow(oledKernel, ledKernel);
        linkWindow.Closed += (_, _) => linkWindow = null;
        linkWindow.Show(this);
        TellProgrammerWindow();
    }


    // ---- Starting, stopping and stepping (Visual Studio's keys) ----

    private void SetMode(Mode next)
    {
        if (next != mode) DiagnosticLog.Write($"Mode: {mode} → {next}");
        mode = next;
        clock.Restart();
    }

    /// <summary>Builds if needed and loads the program into the emulator, powered on. False when the build failed.</summary>
    private bool StartEmulator()
    {
        if (session == null || !BuildIfNeeded()) return false;
        session.Load(built!);
        ApplyPins();
        shownLine = -1;
        return true;
    }

    private void OnStartDebugging(object? sender, RoutedEventArgs e)
    {
        if (OnThingTarget)
        {
            if (mode == Mode.Design) ThingStart(debug: true);
            else if (mode == Mode.Paused)
            {
                SetMode(Mode.Debugging);
                ThingDo(d => d.Continue());
            }
            UpdateView();
            return;
        }
        if (mode == Mode.Paused) SetMode(Mode.Debugging);
        else if (mode == Mode.Design && StartEmulator()) SetMode(Mode.Debugging);
        UpdateView();
    }

    private void OnStartWithoutDebugging(object? sender, RoutedEventArgs e)
    {
        if (OnThingTarget)
        {
            if (mode == Mode.Design && !thingBusy) ThingStart(debug: false);
            UpdateView();
            return;
        }
        if (mode == Mode.Design && StartEmulator()) SetMode(Mode.RunningFree);
        UpdateView();
    }

    private void OnStop(object? sender, RoutedEventArgs e)
    {
        if (mode == Mode.Design) return;
        if (OnThingTarget)
        {
            ThingStop();
            UpdateView();
            return;
        }
        SetMode(Mode.Design);
        shownLine = -1;
        UpdateView();
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if (mode == Mode.Design) return;
        if (OnThingTarget)
        {
            ThingStop(() => ThingStart(debug: true));
            UpdateView();
            return;
        }
        bool free = mode == Mode.RunningFree;
        SetMode(Mode.Design);
        if (StartEmulator()) SetMode(free ? Mode.RunningFree : Mode.Debugging);
        UpdateView();
    }

    private void OnBreakAll(object? sender, RoutedEventArgs e)
    {
        if (mode != Mode.Debugging && mode != Mode.Stepping) return;
        if (OnThingTarget)
        {
            SetMode(Mode.Stepping);
            ThingDo(d => d.Pause());
            UpdateView();
            return;
        }
        // Stop at the next statement if one comes soon, else wait there for events.
        SetMode(Mode.Paused);
        session?.Step(0.05);
        shownLine = -1;
        UpdateView();
    }

    private void OnStepInto(object? sender, RoutedEventArgs e) => BeginStep(StepKind.Into);
    private void OnStepOver(object? sender, RoutedEventArgs e) => BeginStep(StepKind.Over);
    private void OnStepOut(object? sender, RoutedEventArgs e) => BeginStep(StepKind.Out);

    private void OnRunToCursor(object? sender, RoutedEventArgs e)
    {
        int line = Editor.TextArea.Caret.Line;
        var program = OnThingTarget ? built : session?.Program;
        if (program != null && mode != Mode.Design && !program.Lines.Any(l => l.Line == line))
        {
            messages.Add($"Line {line} has no statement to stop at.");
            UpdateView();
            return;
        }
        stepLine = line;
        BeginStep(StepKind.ToCursor);
    }

    /// <summary>A step (or Run To Cursor) from a pause: the stepping commands work only while the program is paused.</summary>
    private void BeginStep(StepKind kind)
    {
        if (mode != Mode.Paused || thingBusy) return;
        if (OnThingTarget)
        {
            {
                SetMode(Mode.Stepping);
                int line = stepLine;
                ThingDo(kind switch
                {
                    StepKind.Over => d => d.StepOver(),
                    StepKind.Out => d => d.StepOut(),
                    StepKind.ToCursor => d => d.RunToCursor(line),
                    _ => d => d.StepInto(),
                });
            }
            UpdateView();
            return;
        }
        if (session == null) return;
        stepKind = kind;
        stepDepth = session.CallDepth;
        SetMode(Mode.Stepping);
        UpdateView();
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (OnThingTarget || session?.Program == null || mode == Mode.Design) return;
        session.Mcu.Reset();
        if (mode != Mode.RunningFree) SetMode(Mode.Debugging);
    }

    private void OnPowerCycle(object? sender, RoutedEventArgs e)
    {
        if (OnThingTarget || session?.Program == null || mode == Mode.Design) return;
        session.PowerOn();
        ApplyPins();
        if (mode != Mode.RunningFree) SetMode(Mode.Debugging);
    }

    private void OnTargetChanged(object? sender, RoutedEventArgs e)
    {
        var chosen = ThingTargetMenu.IsChecked ? RunTarget.Thing : RunTarget.Emulator;
        if (chosen == Target) return;
        if (thing != null) ThingStop();
        SavedSettings.Current.Target = chosen;
        SetMode(Mode.Design);
        messages.Add(chosen == RunTarget.Thing
            ? "Target: the Thing on the programmer. F5 runs the program there with the debugger, Ctrl+F5 without; it is uploaded only when it changed."
            : "Target: the emulator.");
        UpdateView();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (reloadPending)
        {
            reloadPending = false;
            ReloadFromDisk();
        }
        if (session == null) return;
        if (OnThingTarget)
        {
            ThingTick();
            clock.Restart();
            return;
        }

        // Run as much emulated time as real time has passed, at most 50 ms per tick.
        double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.05);
        clock.Restart();

        switch (mode)
        {
            case Mode.Debugging:
                if (session.Run(seconds) == SessionStop.Breakpoint) SetMode(Mode.Paused);
                break;
            case Mode.RunningFree:
                session.RunFree(seconds);
                break;
            case Mode.Stepping:
                // A step can wait: the next statement may only come with an event.
                var stop = stepKind switch
                {
                    StepKind.Over => session.StepOver(seconds, stepDepth),
                    StepKind.Out => session.StepOut(seconds, stepDepth),
                    StepKind.ToCursor => session.RunTo(seconds, stepLine),
                    _ => session.Step(seconds),
                };
                if (stop == SessionStop.HandlerEnded) SetMode(Mode.Debugging);   // off the end of a handler: run on
                else if (stop != SessionStop.TimeUp) SetMode(Mode.Paused);
                break;
        }

        if (session.Fault != 0 && (running || stepping)) SetMode(Mode.Paused);
        if (session.Leds != null && mode is Mode.Debugging or Mode.RunningFree or Mode.Stepping) ledLevels = session.Leds.TakeBrightness();
        UpdateView();
    }

    // ---- Board ----

    private void OnBoardChanged(object? sender, RoutedEventArgs e)
    {
        var chosen = LedThingMenu.IsChecked ? BoardChoice.LedThing : DevBoardMenu.IsChecked ? BoardChoice.DevelopmentBoard : BoardChoice.Thing;
        if (chosen == boardChoice) return;
        if (thing != null) ThingStop();
        boardChoice = chosen;
        chip = chosen == BoardChoice.DevelopmentBoard ? DeviceInfo.Stm8s103f3 : DeviceInfo.Stm8s003f3;
        SavedSettings.Current.Board = chosen;
        NewSession();
        SetMode(Mode.Design);
        built = null;                                  // build again, for this board
        ApplyHighlighting();
        messages.Add($"Board: {chosen switch { BoardChoice.DevelopmentBoard => "the development board", BoardChoice.LedThing => "the LED Thing", _ => "the OLED Thing" }}. Press F5 to run the program on it.");
        UpdateView();
    }

    private void OnBitClicked(object? sender, RoutedEventArgs e)
    {
        int value = 0;
        for (int i = 0; i < 8; i++)
        {
            if (bits[i].IsChecked == true) value |= 1 << i;
        }
        session?.SetInputs((byte)value);
        UpdateView();
    }

    private void OnHexKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string text = (HexBox.Text ?? "").Trim().TrimStart('$');
        if (byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out byte value))
        {
            session?.SetInputs(value);
            UpdateView();
        }
    }

    private void OnPinClicked(object? sender, RoutedEventArgs e) => ApplyPins();

    private void OnAnalogLevelChanged(object? sender, RangeBaseValueChangedEventArgs e) => ApplyPins();

    private void ApplyPins()
    {
        if (session == null) return;
        if (session.Leds is { } leds)
        {
            leds.M2Low = HexPin.IsChecked == true;
            leds.M1Low = BarPin.IsChecked == true;
            leds.M0Low = InvPin.IsChecked == true;
            return;
        }
        var board = session.Oled!;
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

    private void OnToggleBreakpoint(object? sender, RoutedEventArgs e) => ToggleBreakpoint(Editor.TextArea.Caret.Line);

    private void ToggleBreakpoint(int line)
    {
        if (session == null) return;
        if (!session.Breakpoints.Remove(line)) session.Breakpoints.Add(line);
        DiagnosticLog.Write($"Breakpoint {(session.Breakpoints.Contains(line) ? "set" : "removed")} at line {line}: now [{string.Join(", ", session.Breakpoints.OrderBy(l => l))}]");
        session.UpdateBreakpoints();
        ThingBreakpointsChanged();
        shownLine = -1;
        UpdateView();
    }

    // ---- View ----

    private int messagesLogged;

    private void UpdateView()
    {
        if (messagesLogged > messages.Count) messagesLogged = 0;       // the list was cleared
        for (; messagesLogged < messages.Count; messagesLogged++) DiagnosticLog.Write($"Message: {messages[messagesLogged]}");
        var board = session;
        if (board != null)
        {
            for (int i = 0; i < 8; i++) bits[i].IsChecked = (board.Inputs & (1 << i)) != 0;
            if (!HexBox.IsFocused) HexBox.Text = board.Inputs.ToString("X2");
            if (board.Leds != null) ShowLeds();
        }

        int current = mode is not (Mode.Paused or Mode.Stepping) ? 0
            : OnThingTarget ? thingView?.Line ?? 0
            : session?.CurrentLine ?? 0;
        UpdateSource(current);
        NowText.Text = NowDescription(current);
        VariablesText.Text = VariablesDescription();
        StoreText.Text = StoreDescription();

        if (!MessagesList.ItemsSource?.Cast<string>().SequenceEqual(messages) ?? true)
            MessagesList.ItemsSource = messages.ToList();

        string state = mode switch
        {
            Mode.Design => "Not running: F5 starts debugging, Ctrl+F5 runs without",
            Mode.Debugging => "Running (debugging)",
            Mode.RunningFree => "Running without debugging",
            Mode.Stepping => "Stepping: waiting for the next statement",
            _ => "Paused",
        };
        string target = Target == RunTarget.Thing ? (thingBusy ? "Target: Thing (busy)" : "Target: Thing") : "Target: emulator";
        StatusText.Text = board == null ? "" :
            $"{state}   {target}" + (OutOfDate && (Editor.Text ?? "").Length > 0 ? "   Edited since the last build" : "") +
            (mode == Mode.Design || OnThingTarget ? "" : $"   {board.Mcu.Seconds:F2} s since reset" + (board.Oled is { } o ? $"   OLED {(o.Oled.Lit ? "on" : "off")}" : ""));
        if (BoardColumn.IsVisible == OnThingTarget)
        {
            BoardColumn.IsVisible = !OnThingTarget;           // the Thing's own OLED and pins show what it does
            Dispatcher.UIThread.Post(FitSidePanel, DispatcherPriority.Background);
        }

        bool emulator = Target == RunTarget.Emulator;
        StartMenu.Header = mode == Mode.Paused ? "_Continue" : "_Start Debugging";
        StartMenu.IsEnabled = mode is Mode.Design or Mode.Paused;
        StartFreeMenu.IsEnabled = mode == Mode.Design && !thingBusy;
        StopMenu.IsEnabled = mode != Mode.Design;
        RestartMenu.IsEnabled = mode != Mode.Design;
        BreakMenu.IsEnabled = mode is Mode.Debugging or Mode.Stepping;
        StepIntoMenu.IsEnabled = StepOverMenu.IsEnabled = StepOutMenu.IsEnabled = RunToCursorMenu.IsEnabled = !thingBusy && mode == Mode.Paused;
        ResetMenu.IsEnabled = PowerMenu.IsEnabled = emulator && mode != Mode.Design;
        Oled.Refresh();
    }

    /// <summary>The emulated LED Thing's LEDs: a scan lights each LED an eighth of the time, which is full brightness.</summary>
    private void ShowLeds()
    {
        var shapes = new[] { Led0, Led1, Led2, Led3, Led4, Led5, Led6, Led7 };
        bool running = mode is Mode.Debugging or Mode.RunningFree or Mode.Stepping or Mode.Paused;
        for (int i = 0; i < 8; i++)
        {
            double level = running ? Math.Clamp(ledLevels[i] * 8, 0, 1) : 0;
            shapes[i].Fill = new SolidColorBrush(Color.FromRgb((byte)(70 + 185 * level), (byte)(20 + 30 * level), (byte)(20 + 30 * level)));
        }
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
        // Stopping at a line puts the caret on it, as Visual Studio does, so F9 there takes the
        // breakpoint off. Only when the stop moves, so the caret is not pulled back otherwise.
        if (current > 0 && current != caretStop && current <= Editor.Document.LineCount)
        {
            Editor.TextArea.Caret.Line = current;
            Editor.TextArea.Caret.Column = 1;
            Editor.TextArea.Focus();
        }
        caretStop = current;
    }

    private int caretStop;

    private string NowDescription(int current)
    {
        if (session == null) return "";
        if (mode == Mode.Design)
            return errorLine > 0 ? "The program has errors: see Messages." :
                OnThingTarget ? (thingBusy ? "Talking to the Thing…" : "Not running on the Thing. F5 runs it there with the debugger (set breakpoints with F9), Ctrl+F5 runs it without.") :
                "Not running. F5 starts debugging (set breakpoints with F9), Ctrl+F5 runs without debugging.";
        if (OnThingTarget) return ThingNow(current);
        if (session.Program == null) return "No program.";
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
        if (OnThingTarget) return mode == Mode.Design ? "" : ThingVariables();
        if (session?.Program == null || mode == Mode.Design) return "";
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
        if (OnThingTarget)
            return (built == null ? "" : $"Program: {built.Image.Length} bytes in {built.Blocks} of {ProgramStore.Blocks} blocks.\n") +
                "The Thing's own wear: Tools → Firmware…, Wear report.";
        var fat = session.ReadFat();
        var counts = ProgramStore.WriteCounts(fat);
        int uploads = ProgramStore.IsFormatted(fat) ? (fat[6] << 8) | fat[7] : 0;
        string program = session.Program == null ? "" :
            $"Program: {session.Program.Image.Length} bytes in {session.Program.Blocks} of {ProgramStore.Blocks} blocks; " +
            $"variables {session.Program.DataBytes} of {Compiler.VariableArea - session.Program.Blocks} bytes.\n";
        return program +
            $"Uploads to this emulated Thing: {uploads}. Most writes to one block: {counts.Max()} " +
            $"(rated {chip.FlashEndurance:N0}); fewest: {counts.Min()}.";
    }

    // ---- Copying the messages ----

    private void OnCopyMessages(object? sender, RoutedEventArgs e) => CopyMessages(selectedOnly: true);
    private void OnCopyAllMessages(object? sender, RoutedEventArgs e) => CopyMessages(selectedOnly: false);

    private void OnMessagesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            CopyMessages(selectedOnly: true);
            e.Handled = true;
        }
    }

    /// <summary>The selected messages (or, with none selected, all of them) to the clipboard, one a line.</summary>
    private async void CopyMessages(bool selectedOnly)
    {
        var lines = selectedOnly && MessagesList.SelectedItems is { Count: > 0 } selected
            ? messages.Where(m => selected.Contains(m)).ToList()
            : messages.ToList();
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
    }
}
