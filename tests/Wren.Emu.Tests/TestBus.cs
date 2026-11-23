using Wren.Asm;
using Wren.Emu;

namespace Wren.Emu.Tests;

/// <summary>64 KB of plain memory, for testing the CPU on its own.</summary>
public sealed class TestBus : IBus
{
    public byte[] Memory { get; } = new byte[0x10000];

    public byte Read(int address) => Memory[address & 0xFFFF];
    public void Write(int address, byte value) => Memory[address & 0xFFFF] = value;

    /// <summary>Assembles source and loads it.</summary>
    public void Load(string source)
    {
        var result = new Assembler().Assemble(source);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Errors));
        foreach (var (address, value) in result.Image.Bytes) Memory[address] = value;
    }
}
