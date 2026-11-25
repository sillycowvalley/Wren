namespace Wren.Script;

/// <summary>A built-in statement or function: its opcode, how many arguments it takes, and defaults.</summary>
public sealed record Builtin(string Name, byte Opcode, int MinArgs, int MaxArgs, long[] Defaults, bool ReturnsValue);

/// <summary>How the editor colours a word.</summary>
public enum WordKind { Keyword, Statement, Function, Constant, OtherBoard }

/// <summary>The words of Wren BASIC.</summary>
public static class Names
{
    /// <summary>The OLED Thing's pins for events and PIN, by the kernel's pin number.</summary>
    public static readonly Dictionary<string, int> Pins = new()
    {
        ["PA1"] = 0, ["PA2"] = 1, ["PA3"] = 2, ["PD2"] = 3, ["PD3"] = 4,
    };

    /// <summary>The LED Thing's mode pins, named as on its silk: M0 is PA3, M1 PA2, M2 PA1.</summary>
    public static readonly Dictionary<string, int> LedPins = new()
    {
        ["M0"] = 2, ["M1"] = 1, ["M2"] = 0,
    };

    public static Dictionary<string, int> PinsFor(Board board) => board == Board.LedThing ? LedPins : Pins;

    public static readonly Dictionary<string, long> Constants = new()
    {
        ["TRUE"] = -1, ["FALSE"] = 0, ["ON"] = 1, ["OFF"] = 0,
        ["PA1"] = 0, ["PA2"] = 1, ["PA3"] = 2, ["PD2"] = 3, ["PD3"] = 4,
        ["M0"] = 2, ["M1"] = 1, ["M2"] = 0,
        ["SMALL"] = 0, ["NORMAL"] = 1, ["LARGE"] = 2, ["BIG"] = 3,
    };

    /// <summary>
    /// Words that belong to one board: the OLED Thing's display, fonts, analog inputs and pin
    /// names, the LED Thing's LEDs and mode pins. Every word is reserved on both boards.
    /// </summary>
    public static readonly Dictionary<string, Board> OnlyOn = new[]
    {
        "CLS", "SHOW", "PIXEL", "LINE", "BOX", "FILL", "INVERT", "CONTRAST", "INVERSE", "FLIP", "TEXT",
        "HEX", "DEC", "BIN", "BITS", "BAR", "ANALOG", "SMALL", "NORMAL", "LARGE", "BIG",
        "PA1", "PA2", "PA3", "PD2", "PD3",
    }.Select(w => (w, Board.OledThing)).Concat(new[] { "LEDS", "M0", "M1", "M2" }.Select(w => (w, Board.LedThing)))
     .ToDictionary(p => p.w, p => p.Item2);

    /// <summary>Null when the word may be used on the board; otherwise why not.</summary>
    public static string? NotOn(string word, Board board)
    {
        string key = word.ToUpperInvariant();
        if (!OnlyOn.TryGetValue(key, out var only) || only == board) return null;
        return $"{key} is for the {(only == Board.LedThing ? "LED Thing" : "OLED Thing")}, not the {(board == Board.LedThing ? "LED Thing" : "OLED Thing")}";
    }

    /// <summary>Statements. Defaults fill in missing trailing arguments.</summary>
    public static readonly Dictionary<string, Builtin> Statements = new[]
    {
        new Builtin("CLS", Op.Cls, 0, 0, Array.Empty<long>(), false),
        new Builtin("SHOW", Op.Show, 0, 0, Array.Empty<long>(), false),
        new Builtin("PIXEL", Op.Pixel, 2, 3, new long[] { 1 }, false),
        new Builtin("LINE", Op.Line, 4, 5, new long[] { 1 }, false),
        new Builtin("BOX", Op.Box, 4, 5, new long[] { 1 }, false),
        new Builtin("FILL", Op.Fill, 4, 5, new long[] { 1 }, false),
        new Builtin("INVERT", Op.Invert, 4, 4, Array.Empty<long>(), false),
        new Builtin("CONTRAST", Op.Contrast, 1, 1, Array.Empty<long>(), false),
        new Builtin("INVERSE", Op.Inverse, 1, 1, Array.Empty<long>(), false),
        new Builtin("FLIP", Op.Flip, 1, 1, Array.Empty<long>(), false),
        new Builtin("PULLUPS", Op.Pullups, 1, 1, Array.Empty<long>(), false),
        new Builtin("TEXT", Op.Text, 3, 4, new long[] { 1 }, false),
        new Builtin("HEX", Op.Hex, 4, 5, new long[] { 1 }, false),
        new Builtin("DEC", Op.Dec, 3, 4, new long[] { 1 }, false),
        new Builtin("BIN", Op.Bin, 4, 5, new long[] { 1 }, false),
        new Builtin("BITS", Op.Bits, 5, 5, Array.Empty<long>(), false),
        new Builtin("BAR", Op.Bar, 6, 6, Array.Empty<long>(), false),
        new Builtin("WAIT", Op.Wait, 1, 1, Array.Empty<long>(), false),
        new Builtin("EEWRITE", Op.EeWrite, 2, 2, Array.Empty<long>(), false),
        new Builtin("LEDS", Op.Leds, 1, 1, Array.Empty<long>(), false),
    }.ToDictionary(b => b.Name);

    /// <summary>Functions. Those with no arguments can be written without brackets.</summary>
    public static readonly Dictionary<string, Builtin> Functions = new[]
    {
        new Builtin("VALUE", Op.Value, 0, 0, Array.Empty<long>(), true),
        new Builtin("INPUTS", Op.Inputs, 0, 0, Array.Empty<long>(), true),
        new Builtin("PIN", Op.Pin, 1, 1, Array.Empty<long>(), true),
        new Builtin("ANALOG", Op.Analog, 1, 1, Array.Empty<long>(), true),
        new Builtin("MILLIS", Op.Millis, 0, 0, Array.Empty<long>(), true),
        new Builtin("EEREAD", Op.EeRead, 1, 1, Array.Empty<long>(), true),
        new Builtin("RND", Op.Rnd, 1, 1, Array.Empty<long>(), true),
        new Builtin("ABS", Op.Abs, 1, 1, Array.Empty<long>(), true),
        new Builtin("MIN", Op.Min, 2, 2, Array.Empty<long>(), true),
        new Builtin("MAX", Op.Max, 2, 2, Array.Empty<long>(), true),
        new Builtin("LOST", Op.Lost, 0, 0, Array.Empty<long>(), true),
    }.ToDictionary(b => b.Name);

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DIM", "AS", "BYTE", "INTEGER", "CONST", "SUB", "FUNCTION", "END", "ON", "IF", "THEN", "ELSE",
        "ELSEIF", "WHILE", "WEND", "FOR", "TO", "STEP", "NEXT", "EXIT", "RETURN", "CALL", "LET",
        "AND", "OR", "XOR", "NOT", "MOD", "SHL", "SHR", "START", "CHANGE", "PINS", "LATCH", "PRESS",
        "RELEASE", "EVERY", "MS", "FALLING", "RISING", "BOTH", "LOW", "HIGH", "REM",
    };

    /// <summary>
    /// Every word of the language with how the editor colours it on a board: the other
    /// board's words as OtherBoard, so they look wrong before a build says so.
    /// </summary>
    public static IReadOnlyDictionary<string, WordKind> Categories(Board board)
    {
        var words = new Dictionary<string, WordKind>(StringComparer.OrdinalIgnoreCase);
        foreach (string w in Constants.Keys) words[w] = WordKind.Constant;
        foreach (string w in Functions.Keys) words[w] = WordKind.Function;
        foreach (string w in Statements.Keys) words[w] = WordKind.Statement;
        foreach (string w in Keywords) words[w] = WordKind.Keyword;     // ON is a keyword before it is a constant
        foreach (var (w, only) in OnlyOn) if (only != board) words[w] = WordKind.OtherBoard;
        return words;
    }

    public static bool IsReserved(string name)
    {
        string upper = name.ToUpperInvariant();
        return Keywords.Contains(upper) || Statements.ContainsKey(upper) || Functions.ContainsKey(upper)
            || Constants.ContainsKey(upper);
    }
}
