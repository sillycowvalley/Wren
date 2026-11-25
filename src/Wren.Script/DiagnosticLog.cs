namespace Wren.Script;

/// <summary>
/// Tools → Diagnostic log: when on, the app writes what happens in a session (menu commands,
/// keys, breakpoints, the program as started, mode changes, messages, and each decision the
/// Thing's debugger makes) to a timestamped file, so a session can be passed along whole.
/// Off, every call returns at once.
/// </summary>
public static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static StreamWriter? writer;

    public static bool Enabled { get; private set; }

    /// <summary>The file this session writes to; null while the log is off.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>%LocalAppData%\Wren\Logs (beside Wren if there is no local app-data folder).</summary>
    public static string DefaultFolder
    {
        get
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(folder)) folder = AppContext.BaseDirectory;
            return Path.Combine(folder, "Wren", "Logs");
        }
    }

    /// <summary>Starts a new log file in folder (the default folder if null).</summary>
    public static void Start(string? folder = null)
    {
        lock (Gate)
        {
            StopLocked();
            folder ??= DefaultFolder;
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, $"Wren-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };
            Enabled = true;
        }
        Write($"Diagnostic log started: Wren {typeof(DiagnosticLog).Assembly.GetName().Version}, {Environment.OSVersion}.");
    }

    public static void Stop()
    {
        Write("Diagnostic log stopped.");
        lock (Gate) StopLocked();
    }

    private static void StopLocked()
    {
        writer?.Dispose();
        writer = null;
        Enabled = false;
    }

    /// <summary>One line, with the time to the millisecond. Safe from any thread.</summary>
    public static void Write(string text)
    {
        if (!Enabled) return;
        lock (Gate)
        {
            if (writer == null) return;
            string stamp = $"{DateTime.Now:HH:mm:ss.fff} ";
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                writer.WriteLine(stamp + line);
        }
    }

    /// <summary>Everything this session's file holds so far; empty while the log is off.</summary>
    public static string Text
    {
        get
        {
            lock (Gate)
            {
                if (FilePath == null || !File.Exists(FilePath)) return "";
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new StreamReader(stream).ReadToEnd();
            }
        }
    }
}
