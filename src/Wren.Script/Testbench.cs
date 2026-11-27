using System.Globalization;
using Wren.Emu;

namespace Wren.Script;

/// <summary>One change a testbench makes: at a time after power-on, a pin set.</summary>
public sealed record TestbenchStep(double Ms, string Pin, int Value, int Line);

/// <summary>
/// A testbench (docs/Testbench.md): what another circuit does to a Thing's pins, and when,
/// played into the emulator as the program runs. A text file, `.bench`:
///
///   # a comment
///   THING OledThing          the board it is for: OledThing or LedThing
///   0.000 B=5A               at 0 ms after power-on, b0-b7 = 5Ah
///   250.024 B=00 PD3=0       several pins on one line change together
///   END 1848                 the time it ends (optional; else its last change)
///
/// B takes a hex byte. HEX, BAR, INV, PD2 and PD3 (OLED Thing) or M0, M1 and M2 (LED Thing)
/// take 0 (pulled to GND) or 1 (let go). Times are milliseconds after power-on, in order.
/// Free of UI code.
/// </summary>
public sealed class Testbench
{
    private static readonly string[] OledPins = { "B", "HEX", "BAR", "INV", "PD2", "PD3" };
    private static readonly string[] LedPins = { "B", "M0", "M1", "M2" };

    private Testbench(Board board, List<TestbenchStep> steps, double endMs)
    {
        Board = board;
        Steps = steps;
        EndMs = endMs;
    }

    /// <summary>The board the testbench is for.</summary>
    public Board Board { get; }

    /// <summary>The changes, in time order.</summary>
    public IReadOnlyList<TestbenchStep> Steps { get; }

    /// <summary>When it ends, in ms after power-on.</summary>
    public double EndMs { get; }

    public static Testbench Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads a testbench; a FormatException names the line that is wrong.</summary>
    public static Testbench Parse(string text)
    {
        var board = Board.OledThing;
        var steps = new List<TestbenchStep>();
        double? end = null;
        double last = 0;
        int number = 0;
        foreach (string raw in text.Split('\n'))
        {
            number++;
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            string first = words[0].ToUpperInvariant();
            if (first == "THING")
            {
                if (words.Length != 2 || !Enum.TryParse(words[1], true, out board) || board == Board.Unknown)
                    throw Wrong(number, "THING takes OledThing or LedThing");
                continue;
            }
            if (first == "END")
            {
                if (words.Length != 2 || !TryMs(words[1], out double e)) throw Wrong(number, "END takes a time in ms");
                end = e;
                continue;
            }
            if (!TryMs(words[0], out double ms)) throw Wrong(number, $"'{words[0]}' is not a time in ms");
            if (ms < last) throw Wrong(number, "times must not go back");
            if (words.Length < 2) throw Wrong(number, "a time with no pin");
            last = ms;
            for (int i = 1; i < words.Length; i++)
            {
                string[] pair = words[i].Split('=');
                string pin = pair[0].ToUpperInvariant();
                var allowed = board == Board.LedThing ? LedPins : OledPins;
                if (pair.Length != 2 || !allowed.Contains(pin))
                    throw Wrong(number, $"'{words[i]}': pins are {string.Join(", ", allowed)}, as PIN=value");
                int value;
                if (pin == "B")
                {
                    if (!int.TryParse(pair[1].TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || value > 0xFF)
                        throw Wrong(number, $"'{words[i]}': B takes a hex byte");
                }
                else if (pair[1] is "0" or "1") value = pair[1] == "1" ? 1 : 0;
                else throw Wrong(number, $"'{words[i]}': {pin} takes 0 or 1");
                steps.Add(new TestbenchStep(ms, pin, value, number));
            }
        }
        return new Testbench(board, steps, end ?? last);
    }

    private static bool TryMs(string text, out double ms) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out ms) && ms >= 0;

    private static FormatException Wrong(int line, string why) => new($"Line {line}: {why}.");

    /// <summary>Sets one step's pin on the session's board.</summary>
    public static void Apply(ThingSession session, TestbenchStep step)
    {
        if (step.Pin == "B")
        {
            session.SetInputs((byte)step.Value);
            return;
        }
        bool low = step.Value == 0;
        if (session.Leds is { } leds)
        {
            if (step.Pin == "M0") leds.M0Low = low;
            else if (step.Pin == "M1") leds.M1Low = low;
            else if (step.Pin == "M2") leds.M2Low = low;
            return;
        }
        var oled = session.Oled!;
        switch (step.Pin)
        {
            case "HEX": oled.HexLow = low; break;
            case "BAR": oled.BarLow = low; break;
            case "INV": oled.InvLow = low; break;
            case "PD2": oled.Pd2Low = low; break;
            case "PD3": oled.Pd3Low = low; break;
        }
    }
}

/// <summary>
/// Plays a testbench into a session on the Thing's own clock (the time since its last reset,
/// so Restart and Power cycle start it again): each change is made at its time, the run split
/// there, however long the steps the caller runs.
/// </summary>
public sealed class TestbenchPlayer
{
    private int next;
    private double lastMs = -1;

    public TestbenchPlayer(Testbench bench) => Bench = bench;

    public Testbench Bench { get; }

    /// <summary>The Thing's time since its last reset, in ms.</summary>
    public static double NowMs(ThingSession session) => session.Mcu.Seconds * 1000;

    /// <summary>True once the Thing's clock has passed the testbench's end.</summary>
    public bool Finished(ThingSession session) => NowMs(session) >= Bench.EndMs;

    /// <summary>Makes every change due by now; after a reset, from the start again.</summary>
    public void Apply(ThingSession session)
    {
        double now = NowMs(session);
        if (now < lastMs) next = 0;
        lastMs = now;
        while (next < Bench.Steps.Count && Bench.Steps[next].Ms <= now) Testbench.Apply(session, Bench.Steps[next++]);
    }

    /// <summary>
    /// Runs the session for some emulated time with run (Run, RunFree, Step …), split at each
    /// change so it is made on time. Returns the first stop that is not TimeUp, or TimeUp.
    /// </summary>
    public SessionStop Run(ThingSession session, double seconds, Func<double, SessionStop> run)
    {
        double end = NowMs(session) + seconds * 1000;
        while (true)
        {
            Apply(session);
            double now = NowMs(session);
            if (now >= end) return SessionStop.TimeUp;
            double until = next < Bench.Steps.Count ? Math.Min(end, Bench.Steps[next].Ms) : end;
            var stop = run(Math.Max(until - now, 0.001) / 1000);    // at least 1 µs: Mcu.Run steps whole ticks
            if (stop != SessionStop.TimeUp)
            {
                Apply(session);
                return stop;
            }
        }
    }
}
