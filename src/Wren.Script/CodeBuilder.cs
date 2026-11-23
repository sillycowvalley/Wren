namespace Wren.Script;

/// <summary>
/// Collects bytecode with symbolic labels, then lays it out: jumps start short (2 bytes)
/// and become long (3 bytes) where the target is out of reach, until nothing changes.
/// </summary>
public sealed class CodeBuilder
{
    private abstract record Item;
    private sealed record Bytes(byte[] Data) : Item;
    private sealed record LabelMark(int Label) : Item;
    private sealed record LineMark(int Line) : Item;
    private sealed record JumpTo(byte ShortOp, byte LongOp, int Label) : Item;
    private sealed record CallTo(int Label) : Item;
    private sealed record WordOf(int Label) : Item;

    private readonly List<Item> items = new();
    private int labelCount;

    public int NewLabel() => labelCount++;

    public void Mark(int label) => items.Add(new LabelMark(label));

    /// <summary>Records that the next byte starts a statement on this source line.</summary>
    public void MarkLine(int line) => items.Add(new LineMark(line));

    public void Emit(params byte[] data) => items.Add(new Bytes(data));

    /// <summary>Jump (or JZ / JNZ) to a label.</summary>
    public void Jump(byte shortOp, int label)
    {
        byte longOp = shortOp switch
        {
            Op.Jump => Op.JumpLong,
            Op.JumpZero => Op.JumpZeroLong,
            Op.JumpNotZero => Op.JumpNotZeroLong,
            _ => throw new ArgumentException("Not a jump", nameof(shortOp)),
        };
        items.Add(new JumpTo(shortOp, longOp, label));
    }

    public void Call(int label) => items.Add(new CallTo(label));

    /// <summary>A 16-bit address of a label (handler table entries).</summary>
    public void AddressOf(int label) => items.Add(new WordOf(label));

    public sealed record Layout(byte[] Code, int[] LabelAddresses, List<(int Address, int Line)> Lines);

    public Layout Build()
    {
        var isLong = new bool[items.Count];
        int[] labels = new int[labelCount];
        int[] addresses = new int[items.Count];

        for (int pass = 0; pass < 100; pass++)
        {
            // Place everything with the current jump sizes.
            int address = 0;
            for (int i = 0; i < items.Count; i++)
            {
                addresses[i] = address;
                switch (items[i])
                {
                    case Bytes b: address += b.Data.Length; break;
                    case LabelMark l: labels[l.Label] = address; break;
                    case JumpTo: address += isLong[i] ? 3 : 2; break;
                    case CallTo: address += 3; break;
                    case WordOf: address += 2; break;
                }
            }
            // Lengthen jumps that cannot reach.
            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is JumpTo j && !isLong[i])
                {
                    int offset = labels[j.Label] - (addresses[i] + 2);
                    if (offset < -128 || offset > 127)
                    {
                        isLong[i] = true;
                        changed = true;
                    }
                }
            }
            if (!changed) break;
        }

        var code = new List<byte>();
        var lines = new List<(int, int)>();
        for (int i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case Bytes b:
                    code.AddRange(b.Data);
                    break;
                case LineMark m:
                    if (lines.Count > 0 && lines[^1].Item1 == code.Count) lines[^1] = (code.Count, m.Line);
                    else lines.Add((code.Count, m.Line));
                    break;
                case JumpTo j:
                    if (isLong[i])
                    {
                        code.Add(j.LongOp);
                        code.Add((byte)(labels[j.Label] >> 8));
                        code.Add((byte)labels[j.Label]);
                    }
                    else
                    {
                        code.Add(j.ShortOp);
                        code.Add((byte)(labels[j.Label] - (addresses[i] + 2)));
                    }
                    break;
                case CallTo c:
                    code.Add(Op.Call);
                    code.Add((byte)(labels[c.Label] >> 8));
                    code.Add((byte)labels[c.Label]);
                    break;
                case WordOf w:
                    code.Add((byte)(labels[w.Label] >> 8));
                    code.Add((byte)labels[w.Label]);
                    break;
            }
        }
        return new Layout(code.ToArray(), labels, lines);
    }
}
