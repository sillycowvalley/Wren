namespace Wren.Emu;

/// <summary>A block of peripheral registers in the 0x5000-0x57FF (or 0x7Fxx) area.</summary>
public abstract class Peripheral
{
    protected Peripheral(string name, int baseAddress, int size)
    {
        Name = name;
        BaseAddress = baseAddress;
        Size = size;
    }

    public string Name { get; }
    public int BaseAddress { get; }
    public int Size { get; }

    /// <summary>Reads the register at offset from the base address.</summary>
    public abstract byte Read(int offset);

    public abstract void Write(int offset, byte value);

    public virtual void Reset() { }

    /// <summary>Advances the peripheral by a number of master clock cycles.</summary>
    public virtual void Tick(int masterCycles) { }
}

/// <summary>Registers that only hold what is written to them. Used for peripherals that are not modelled.</summary>
public sealed class PlainRegisters : Peripheral
{
    private readonly byte[] resetValues;
    private readonly byte[] values;

    public PlainRegisters(string name, int baseAddress, byte[] resetValues) : base(name, baseAddress, resetValues.Length)
    {
        this.resetValues = resetValues;
        values = (byte[])resetValues.Clone();
    }

    public override byte Read(int offset) => values[offset];
    public override void Write(int offset, byte value) => values[offset] = value;
    public override void Reset() => Array.Copy(resetValues, values, values.Length);
}
