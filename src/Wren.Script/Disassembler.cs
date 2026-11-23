using System.Text;

namespace Wren.Script;

/// <summary>Turns a program image back into readable bytecode, for listings and tests.</summary>
public static class Disassembler
{
    private static readonly Dictionary<byte, string> Simple = new()
    {
        [Op.Value] = "VALUE",
        [Op.Add] = "ADD", [Op.Sub] = "SUB", [Op.Mul] = "MUL", [Op.Div] = "DIV", [Op.Mod] = "MOD",
        [Op.And] = "AND", [Op.Or] = "OR", [Op.Xor] = "XOR", [Op.Not] = "NOT", [Op.Neg] = "NEG",
        [Op.Shl] = "SHL", [Op.Shr] = "SHR", [Op.Eq] = "EQ", [Op.Ne] = "NE", [Op.Lt] = "LT",
        [Op.Le] = "LE", [Op.Gt] = "GT", [Op.Ge] = "GE", [Op.Drop] = "DROP",
        [Op.Return] = "RET", [Op.ReturnValue] = "RETV",
        [Op.Cls] = "CLS", [Op.Show] = "SHOW", [Op.Pixel] = "PIXEL", [Op.Line] = "LINE", [Op.Box] = "BOX",
        [Op.Fill] = "FILL", [Op.Invert] = "INVERT", [Op.Contrast] = "CONTRAST", [Op.Inverse] = "INVERSE",
        [Op.Hex] = "HEX", [Op.Dec] = "DEC", [Op.Bin] = "BIN", [Op.Bits] = "BITS", [Op.Bar] = "BAR",
        [Op.Wait] = "WAIT", [Op.EeWrite] = "EEWRITE", [Op.Inputs] = "INPUTS", [Op.Pin] = "PIN",
        [Op.Analog] = "ANALOG", [Op.Millis] = "MILLIS", [Op.EeRead] = "EEREAD", [Op.Rnd] = "RND",
        [Op.Abs] = "ABS", [Op.Min] = "MIN", [Op.Max] = "MAX", [Op.Lost] = "LOST", [Op.Flip] = "FLIP",
    };

    /// <summary>One instruction at address: its text and length.</summary>
    public static (string Text, int Length) Instruction(byte[] code, int address)
    {
        byte op = code[address];
        byte Arg(int i) => address + i < code.Length ? code[address + i] : (byte)0;
        int Word(int i) => (Arg(i) << 8) | Arg(i + 1);

        if (op < 0x10) return ($"PUSH {op}", 1);
        if (op is >= 0x20 and < 0x30) return ($"LOADG {op - 0x20}", 1);
        if (op is >= 0x30 and < 0x40) return ($"STOREG {op - 0x30}", 1);
        if (Simple.TryGetValue(op, out string? name)) return (name, 1);
        return op switch
        {
            Op.PushByte => ($"PUSH {Arg(1)}", 2),
            Op.PushNegative => ($"PUSH {Arg(1) - 256}", 2),
            Op.PushWord => ($"PUSH {(short)Word(1)}", 3),
            Op.LoadGlobalByte => ($"LOADG {Arg(1)}", 2),
            Op.StoreGlobalByte => ($"STOREG {Arg(1)}", 2),
            Op.LoadLocal => ($"LOADL {Arg(1)}", 2),
            Op.StoreLocal => ($"STOREL {Arg(1)}", 2),
            Op.LoadArrayWord => ($"LOADAW {Arg(1)},{Arg(2)}", 3),
            Op.StoreArrayWord => ($"STOREAW {Arg(1)},{Arg(2)}", 3),
            Op.LoadArrayByte => ($"LOADAB {Arg(1)},{Arg(2)}", 3),
            Op.StoreArrayByte => ($"STOREAB {Arg(1)},{Arg(2)}", 3),
            Op.Jump or Op.JumpZero or Op.JumpNotZero =>
                ($"{(op == Op.Jump ? "JMP" : op == Op.JumpZero ? "JZ" : "JNZ")} {address + 2 + (sbyte)Arg(1):X4}", 2),
            Op.JumpLong => ($"JMP {Word(1):X4}", 3),
            Op.JumpZeroLong => ($"JZ {Word(1):X4}", 3),
            Op.JumpNotZeroLong => ($"JNZ {Word(1):X4}", 3),
            Op.Call => ($"CALL {Word(1):X4}", 3),
            Op.Enter => ($"ENTER {Arg(1)},{Arg(2)}", 3),
            Op.Text => ($"TEXT \"{Encoding.ASCII.GetString(code, address + 2, Math.Min(Arg(1), code.Length - address - 2))}\"", 2 + Arg(1)),
            _ => ($"?? {op:X2}", 1),
        };
    }

    /// <summary>The whole program: header, handler table and code, with source lines.</summary>
    public static string Listing(CompiledProgram program)
    {
        byte[] code = program.Image;
        var sb = new StringBuilder();
        sb.AppendLine($"Version {code[0]}, {code[1]} bytes of variables, {code[2]} handlers");
        int pos = 3;
        for (int i = 0; i < code[2]; i++)
        {
            int ev = code[pos];
            int address = (code[pos + 1] << 8) | code[pos + 2];
            pos += 3;
            string extra = "";
            if (ev is >= Op.EventLatch and < Op.EventPress) extra = $" settings {code[pos++]:X2}";
            else if (ev >= Op.EventEvery) { extra = $" every {(code[pos] << 8) | code[pos + 1]} ms"; pos += 2; }
            sb.AppendLine($"  event {ev} at {address:X4}{extra}");
        }
        int lastLine = -1;
        while (pos < code.Length)
        {
            var routine = program.Routines.FirstOrDefault(r => r.Start == pos);
            if (routine != null) sb.AppendLine().AppendLine(routine.Name + ":");
            int line = program.LineAt(pos);
            var (text, length) = Instruction(code, pos);
            string lineText = line != lastLine && program.Lines.Any(l => l.Address == pos) ? $"  ; line {line}" : "";
            if (lineText.Length > 0) lastLine = line;
            sb.AppendLine($"{pos:X4}  {text}{lineText}");
            pos += length;
        }
        return sb.ToString();
    }
}
