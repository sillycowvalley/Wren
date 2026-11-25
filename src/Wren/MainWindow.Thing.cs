using Avalonia.Threading;
using Wren.Link;
using Wren.Script;

namespace Wren;

/// <summary>
/// The Thing target (Debug → Target → Thing on the programmer): the same keys as the emulator,
/// carried out on a real Thing by a <see cref="ThingDebugger"/>. Its calls talk to the chip
/// over SWIM and take milliseconds, so they run one after another on a background thread;
/// what the debugger last read is kept in a <see cref="ThingView"/> for the window to show.
/// </summary>
public partial class MainWindow
{
    private ThingDebugger? thing;
    private Task thingWork = Task.CompletedTask;
    private int thingPending;
    private ThingView? thingView;
    private DateTime thingRefreshed;

    private sealed record ThingView(ThingRunState State, int Line, RoutineInfo? Routine, int Depth, int Fault, int EventValue,
        IReadOnlyList<Variable> Globals, IReadOnlyList<Variable> Locals, IReadOnlyList<int> Armed);

    private bool thingBusy => thingPending > 0;

    /// <summary>
    /// How the window reaches the Thing, all on its own: the programmer that worked last if it
    /// is there, else the first one found that answers. A headless check of the window
    /// replaces it with one on the emulated Thing.
    /// </summary>
    internal static Func<Kernel, string?, ThingDebugger> OpenThing = (k, last) =>
    {
        var d = ThingDebugger.Find(k, last, out var found);
        string name = found.ToString()!;
        Dispatcher.UIThread.Post(() => SavedSettings.Current.LastProgrammer = name);
        return d;
    };
    private bool OnThingTarget => Target == RunTarget.Thing;

    private static ThingView Snapshot(ThingDebugger d) => new(d.State, d.CurrentLine, d.CurrentRoutine, d.CallDepth, d.Fault,
        d.EventValue, d.Globals().ToList(), d.Locals().ToList(), d.ArmedBreakpoints.ToList());

    private static bool Expected(Exception ex) =>
        ex is LinkException or IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException;

    /// <summary>Queues work for the Thing; done runs on the UI thread with what the work returned.</summary>
    private void ThingQueue(Func<ThingDebugger?> work, Action<ThingDebugger?>? done = null)
    {
        thingPending++;
        thingWork = thingWork.ContinueWith(_ =>
        {
            ThingDebugger? result = null;
            ThingView? view = null;
            string? error = null;
            try
            {
                result = work();
                if (result != null) view = Snapshot(result);
            }
            catch (Exception ex) when (Expected(ex))
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                thingPending--;
                if (error != null) ThingFailed(error);
                else
                {
                    if (view != null) thingView = view;
                    done?.Invoke(result);
                    FollowThing();
                }
                shownLine = -1;
                UpdateView();
            });
        }, TaskScheduler.Default);
    }

    private void ThingDo(Action<ThingDebugger> action, Action? done = null)
    {
        var d = thing;
        if (d == null) return;
        ThingQueue(() =>
        {
            action(d);
            return d;
        }, _ => done?.Invoke());
    }

    private void Say(string text) => Dispatcher.UIThread.Post(() =>
    {
        messages.Add(text);
        UpdateView();
    });

    private void ThingFailed(string error)
    {
        messages.Add($"The Thing: {error}");
        var d = thing;
        thing = null;
        thingView = null;
        TellProgrammerWindow();
        if (d != null) Task.Run(() =>
        {
            try { d.Dispose(); }
            catch (Exception ex) when (Expected(ex)) { }
        });
        SetMode(Mode.Design);
    }

    /// <summary>Tells the Programmer window whether this window holds the Thing.</summary>
    private void TellProgrammerWindow() => linkWindow?.HeldByMainWindow(thing != null, () =>
    {
        ThingStop();
        UpdateView();
    });

    /// <summary>The window's mode follows the debugger's state after each call.</summary>
    private void FollowThing()
    {
        if (thing == null || thingView == null) return;
        if (thingView.State == ThingRunState.Paused) mode = Mode.Paused;
        else if (thingView.State == ThingRunState.Stepping) mode = Mode.Stepping;
        else if (mode != Mode.Design) mode = Mode.Debugging;
    }

    /// <summary>
    /// F5 or Ctrl+F5 from a stopped program: builds if needed, takes the
    /// programmer (from the Programmer window too), uploads the program only if the Thing holds
    /// something else, and starts it, under the debugger or on its own.
    /// </summary>
    private void ThingStart(bool debug)
    {
        if (kernel == null || thing != null) return;
        if (!BuildIfNeeded()) return;
        var program = built!;
        var k = kernel;
        string? last = SavedSettings.Current.LastProgrammer;
        var lines = session?.Breakpoints.ToList() ?? new List<int>();
        linkWindow?.ReleaseThing();
        messages.Add(debug ? "Starting the program on the Thing, with the debugger…" : "Starting the program on the Thing…");
        SetMode(debug ? Mode.Debugging : Mode.Design);
        thingView = null;
        ThingQueue(() =>
        {
            var d = OpenThing(k, last);
            try
            {
                d.Progress += Say;
                d.Breakpoints.UnionWith(lines);
                d.Start(program, debug);
            }
            catch
            {
                d.Dispose();
                throw;
            }
            if (debug) return d;
            d.Dispose();
            return null;
        }, d =>
        {
            thing = d;
            TellProgrammerWindow();
            if (SavedSettings.Current.LastProgrammer is string through) messages.Add($"Through {through}.");
            if (!debug) messages.Add("The Thing is running the program.");
            else WarnIfTooManyBreakpoints();
        });
    }

    /// <summary>Shift+F5: the debugger lets go, and the Thing runs the program on its own.</summary>
    private void ThingStop(Action? then = null)
    {
        var d = thing;
        if (d == null)
        {
            SetMode(Mode.Design);
            then?.Invoke();
            return;
        }
        thing = null;
        ThingQueue(() =>
        {
            try
            {
                d.Stop();
            }
            finally
            {
                d.Dispose();                           // the port is closed whatever Stop met
            }
            return null;
        }, _ =>
        {
            messages.Add("Debugging stopped: the Thing runs the program on its own.");
            thingView = null;
            TellProgrammerWindow();
            SetMode(Mode.Design);
            then?.Invoke();
        });
    }

    /// <summary>Every tick while a program runs or steps on the Thing: check for a stop, and read the variables a few times a second.</summary>
    private void ThingTick()
    {
        if (thing == null || thingBusy) return;
        if (mode == Mode.Debugging && DateTime.Now - thingRefreshed > TimeSpan.FromMilliseconds(250))
        {
            thingRefreshed = DateTime.Now;
            ThingDo(d => d.Refresh());
        }
        else if (mode is Mode.Debugging or Mode.Stepping) ThingDo(d => d.Poll());
    }

    private void ThingBreakpointsChanged()
    {
        if (thing == null || session == null) return;
        var lines = session.Breakpoints.ToList();
        ThingDo(d =>
        {
            d.Breakpoints.Clear();
            d.Breakpoints.UnionWith(lines);
            d.UpdateBreakpoints();
        }, WarnIfTooManyBreakpoints);
    }

    private void WarnIfTooManyBreakpoints()
    {
        var program = built;
        if (session == null || program == null || thingView == null) return;
        int withStatements = session.Breakpoints.Count(l => program.Lines.Any(p => p.Line == l));
        if (withStatements > 2)
            messages.Add($"The Thing has two hardware breakpoints: it stops at line{(thingView.Armed.Count == 1 ? "" : "s")} " +
                $"{string.Join(" and ", thingView.Armed)}. The emulator stops at them all.");
    }

    // ---- What the window shows on the Thing target ----

    private string ThingNow(int current)
    {
        var v = thingView;
        if (thingBusy && v == null) return "Talking to the Thing…";
        if (v == null) return "";
        if (v.Fault != 0)
        {
            string what = v.Fault < FaultNames.Length ? FaultNames[v.Fault] : $"fault {v.Fault}";
            return $"The program stopped: {what} (ERR {v.Fault}).";
        }
        if (mode == Mode.Debugging) return "Running on the Thing.";
        if (current == 0) return "Waiting for an event on the Thing.";
        string where = v.Routine == null ? "" : $"In {v.Routine.Name}";
        if (v.Routine != null && v.Routine.Name.StartsWith("ON ") && v.Depth == 0) where += $", VALUE = {v.EventValue} (${v.EventValue:X2})";
        if (v.Depth > 0) where += $", {v.Depth} call{(v.Depth == 1 ? "" : "s")} deep";
        return $"{where} (on the Thing).\nNext: line {current}.";
    }

    private string ThingVariables()
    {
        var v = thingView;
        if (v == null) return "";
        var lines = v.Globals.Select(g => $"{g.Name} = {g.Value}").ToList();
        if (lines.Count == 0) lines.Add("(no global variables)");
        if (mode == Mode.Paused && v.Locals.Count > 0 && v.Routine != null)
        {
            lines.Add("");
            lines.Add($"In {v.Routine.Name}:");
            lines.AddRange(v.Locals.Select(l => $"  {l.Name} = {l.Value}"));
        }
        return string.Join("\n", lines);
    }
}
