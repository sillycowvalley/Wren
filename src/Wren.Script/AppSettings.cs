using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wren.Script;

/// <summary>Which board the app simulates.</summary>
public enum BoardChoice
{
    Thing,
    DevelopmentBoard,
}

/// <summary>Where Start runs the program (Debug → Target).</summary>
public enum RunTarget
{
    Emulator,
    Thing,
}

/// <summary>Where a window was and how big, in its normal (not maximised) state.</summary>
public sealed class WindowPlacement
{
    /// <summary>Left edge in screen pixels; null when not known.</summary>
    public int? X { get; set; }

    /// <summary>Top edge in screen pixels; null when not known.</summary>
    public int? Y { get; set; }

    /// <summary>Client width in device-independent units.</summary>
    public double Width { get; set; }

    /// <summary>Client height in device-independent units.</summary>
    public double Height { get; set; }

    public bool Maximized { get; set; }
}

/// <summary>A rectangle in screen pixels, such as a monitor's working area.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>
/// The app's layout and small choices, kept from one run to the next in one JSON file.
/// A missing or unreadable file gives the defaults.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public WindowPlacement? MainWindow { get; set; }

    public WindowPlacement? ProgrammerWindow { get; set; }

    public WindowPlacement? WearWindow { get; set; }

    /// <summary>Width of the main window's right-hand column (Now, Variables, Messages); null for the default.</summary>
    public double? SidePanelWidth { get; set; }

    public BoardChoice Board { get; set; } = BoardChoice.Thing;

    /// <summary>Where Start runs the program: the emulator, or a real Thing through the programmer.</summary>
    public RunTarget Target { get; set; } = RunTarget.Emulator;

    /// <summary>The programmer last connected to, as the Firmware window names it.</summary>
    public string? LastProgrammer { get; set; }

    /// <summary>Tools → Diagnostic log: each run writes its session to a file in DiagnosticLog.DefaultFolder.</summary>
    public bool DiagnosticLog { get; set; }

    /// <summary>The program file last opened.</summary>
    public string? LastFile { get; set; }

    /// <summary>%AppData%\Wren\Settings.json (beside Wren if there is no app-data folder).</summary>
    public static string DefaultPath
    {
        get
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(folder)) folder = AppContext.BaseDirectory;
            return Path.Combine(folder, "Wren", "Settings.json");
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>The settings in json, or the defaults if it is not valid.</summary>
    public static AppSettings FromJson(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            if (!Enum.IsDefined(settings.Board)) settings.Board = BoardChoice.Thing;
            if (!Enum.IsDefined(settings.Target)) settings.Target = RunTarget.Emulator;
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>The settings in the file, or the defaults if it is missing or bad.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes the file (through a temporary file, so a failed write leaves the old one). Never throws.</summary>
    public bool Save(string path)
    {
        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, ToJson());
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when enough of a window's title bar, at (x, y) and width pixels wide, lies on one of
    /// the working areas for it to be seen and dragged.
    /// </summary>
    public static bool IsOnScreen(int x, int y, int width, IEnumerable<ScreenRect> workingAreas)
    {
        const int TitleHeight = 30, MinimumWide = 60, MinimumHigh = 20;
        foreach (var area in workingAreas)
        {
            int left = Math.Max(x, area.X);
            int right = Math.Min(x + width, area.X + area.Width);
            int top = Math.Max(y, area.Y);
            int bottom = Math.Min(y + TitleHeight, area.Y + area.Height);
            if (right - left >= MinimumWide && bottom - top >= MinimumHigh) return true;
        }
        return false;
    }
}
