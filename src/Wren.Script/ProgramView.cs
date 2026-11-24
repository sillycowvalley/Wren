namespace Wren.Script;

/// <summary>A variable as the debugger shows it.</summary>
public sealed record Variable(string Name, string Value);

/// <summary>
/// Reads a running program's state from the Thing's memory, through the kernel's symbols:
/// where the VM is, its call depth, the program's fault, the event's value, the globals and
/// the current routine's locals. Shared by the emulated Thing's session and the real
/// Thing's debugger, which reads the same RAM over SWIM.
/// </summary>
public sealed class ProgramView
{
    private readonly int vpcAddress, fpAddress, cspAddress, cstackTop, varsAddress, faultAddress, eventValueAddress;

    public ProgramView(Kernel kernel)
    {
        vpcAddress = kernel.Symbol("vpc");
        fpAddress = kernel.Symbol("fp");
        cspAddress = kernel.Symbol("csp");
        cstackTop = kernel.Symbol("CSTACK_TOP");
        varsAddress = kernel.Symbol("VARS");
        faultAddress = kernel.Symbol("vm_fault");
        eventValueAddress = kernel.Symbol("evt_value");
    }

    public int Vpc(Func<int, byte> read) => (ushort)Word(read, vpcAddress);

    /// <summary>Calls in progress below the current routine.</summary>
    public int CallDepth(Func<int, byte> read) => (cstackTop - (ushort)Word(read, cspAddress)) / 4;

    /// <summary>The program's fault number, 0 while it runs.</summary>
    public int Fault(Func<int, byte> read) => read(faultAddress);

    public int EventValue(Func<int, byte> read) => read(eventValueAddress);

    public IEnumerable<Variable> Globals(CompiledProgram program, Func<int, byte> read)
    {
        foreach (var g in program.Globals)
        {
            switch (g.Kind)
            {
                case VariableKind.Integer:
                    yield return new Variable(g.Name, Format(Word(read, varsAddress + g.Offset)));
                    break;
                default:
                    int shown = Math.Min(g.Length, 8);
                    var items = Enumerable.Range(0, shown).Select(i => g.Kind == VariableKind.ByteArray
                        ? read(varsAddress + g.Offset + i).ToString()
                        : Word(read, varsAddress + g.Offset + 2 * i).ToString());
                    yield return new Variable($"{g.Name}({g.Length})", string.Join(", ", items) + (g.Length > shown ? ", ..." : ""));
                    break;
            }
        }
    }

    /// <summary>Parameters and locals of a routine, stopped at one of its statements.</summary>
    public IEnumerable<Variable> Locals(RoutineInfo routine, Func<int, byte> read)
    {
        int fp = (ushort)Word(read, fpAddress);
        for (int i = 0; i < routine.Locals.Count; i++)
        {
            if (routine.Locals[i].StartsWith('(')) continue; // hidden FOR limits
            yield return new Variable(routine.Locals[i], Format(Word(read, fp - 2 * i)));
        }
    }

    private static string Format(short v) => v is >= 0 and <= 9 ? v.ToString() : $"{v}  (${(ushort)v:X4})";

    private static short Word(Func<int, byte> read, int address) => (short)((read(address) << 8) | read(address + 1));
}
