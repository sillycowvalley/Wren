using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>Compiles BASIC, loads it with the kernel onto an emulated Thing and runs it.</summary>
public sealed class Run
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    public static Kernel Kernel => SharedKernel.Value;

    private Run(OledThing thing, CompiledProgram program)
    {
        Thing = thing;
        Program = program;
    }

    public OledThing Thing { get; }
    public CompiledProgram Program { get; }
    public Stm8Device Mcu => Thing.Mcu;

    public static CompiledProgram Compile(string source)
    {
        var program = Compiler.Compile(source);
        Assert.True(program.Success, string.Join("\n", program.Errors));
        return program;
    }

    public static string ProgramText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "programs", name));

    /// <summary>Starts a program; setup runs before power-on (inputs, mode pins).</summary>
    public static Run Start(string source, Action<OledThing>? setup = null, double seconds = 0.05)
    {
        var program = Compile(source);
        var thing = new OledThing();
        thing.Mcu.Load(Kernel.Image.Bytes);
        thing.Mcu.Load(ProgramStore.Plan(null, program.Image).Bytes());
        setup?.Invoke(thing);
        thing.PowerOn();
        var run = new Run(thing, program);
        run.For(seconds);
        return run;
    }

    /// <summary>Runs on for some time; fails if the CPU faults.</summary>
    public void For(double seconds)
    {
        Mcu.Run(seconds);
        Assert.Null(Mcu.Cpu.Fault);
    }

    /// <summary>The kernel's fault number for the program (0 = running).</summary>
    public int VmFault => Mcu.Read(Kernel.Symbol("vm_fault"));

    private int VarsAddress => Kernel.Symbol("VARS");

    public short Global(string name)
    {
        var v = Program.Globals.Single(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Word(VarsAddress + v.Offset);
    }

    public short Element(string name, int index)
    {
        var v = Program.Globals.Single(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return v.Kind == VariableKind.ByteArray
            ? Mcu.Read(VarsAddress + v.Offset + index)
            : Word(VarsAddress + v.Offset + 2 * index);
    }

    private short Word(int address) => (short)((Mcu.Read(address) << 8) | Mcu.Read(address + 1));

    public string Panel => Thing.Oled.PanelText();
}
