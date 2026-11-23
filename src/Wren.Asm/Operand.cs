namespace Wren.Asm;

public enum Register { None, A, X, Y, XL, XH, YL, YH, SP, CC }

/// <summary>The addressing form of one operand, in ST's syntax.</summary>
public enum OperandKind
{
    Register,   // A, X, XL, SP, CC ...
    Immediate,  // #expr
    Direct,     // expr (short or long address, or a branch target)
    IndexX,     // (X)
    IndexY,     // (Y)
    OffsetX,    // (expr,X)
    OffsetY,    // (expr,Y)
    OffsetSP,   // (expr,SP)
    Pointer,    // [expr.w]  [expr.e]
    PointerX,   // ([expr.w],X)  ([expr.e],X)
    PointerY,   // ([expr.w],Y)  ([expr.e],Y)
}

public sealed class Operand
{
    public OperandKind Kind { get; init; }
    public Register Register { get; init; }

    /// <summary>The expression's value, or null while a symbol in it is still undefined.</summary>
    public long? Value { get; init; }

    /// <summary>For pointers: true for [ptr.e] (a 24-bit pointer, LDF/JPF/CALLF only).</summary>
    public bool ExtendedPointer { get; init; }

    /// <summary>True when the short (one byte) form is to be used. Set by the assembler.</summary>
    public bool Short { get; set; }

    public string Text { get; init; } = "";

    public bool IsRegister(Register r) => Kind == OperandKind.Register && Register == r;

    /// <summary>The value, which must be known by the time bytes are produced.</summary>
    public long Number => Value ?? 0;

    public bool CanBeShort => Kind is OperandKind.Direct or OperandKind.OffsetX or OperandKind.OffsetY
        or OperandKind.Pointer or OperandKind.PointerX or OperandKind.PointerY;

    public override string ToString() => Text;

    public static Register ParseRegister(string text) => text.Trim().ToUpperInvariant() switch
    {
        "A" => Register.A,
        "X" => Register.X,
        "Y" => Register.Y,
        "XL" => Register.XL,
        "XH" => Register.XH,
        "YL" => Register.YL,
        "YH" => Register.YH,
        "SP" => Register.SP,
        "CC" => Register.CC,
        _ => Register.None,
    };

    /// <summary>Parses one operand. lookup returns null for a symbol not defined yet.</summary>
    public static Operand Parse(string text, Func<string, long?> lookup)
    {
        string t = text.Trim();
        if (t.Length == 0) throw new AsmException("Missing operand");

        Register reg = ParseRegister(t);
        if (reg != Register.None)
            return new Operand { Kind = OperandKind.Register, Register = reg, Text = t };

        if (t[0] == '#')
            return new Operand { Kind = OperandKind.Immediate, Value = Expression.Evaluate(t[1..], lookup), Text = t };

        if (t[0] == '[' && t[^1] == ']')
            return ParsePointer(t[1..^1], OperandKind.Pointer, t, lookup);

        if (t[0] == '(' && t[^1] == ')' && MatchingClose(t, 0) == t.Length - 1)
        {
            string inner = t[1..^1].Trim();
            Register only = ParseRegister(inner);
            if (only == Register.X) return new Operand { Kind = OperandKind.IndexX, Text = t };
            if (only == Register.Y) return new Operand { Kind = OperandKind.IndexY, Text = t };

            int comma = LastTopLevelComma(inner);
            if (comma >= 0)
            {
                string baseText = inner[..comma].Trim();
                Register index = ParseRegister(inner[(comma + 1)..]);
                if (baseText.StartsWith('[') && baseText.EndsWith(']'))
                {
                    var kind = index switch
                    {
                        Register.X => OperandKind.PointerX,
                        Register.Y => OperandKind.PointerY,
                        _ => throw new AsmException($"Bad index register in '{t}'"),
                    };
                    return ParsePointer(baseText[1..^1], kind, t, lookup);
                }
                var offsetKind = index switch
                {
                    Register.X => OperandKind.OffsetX,
                    Register.Y => OperandKind.OffsetY,
                    Register.SP => OperandKind.OffsetSP,
                    _ => throw new AsmException($"Bad index register in '{t}'"),
                };
                return new Operand { Kind = offsetKind, Value = Expression.Evaluate(baseText, lookup), Text = t };
            }
            // A bracketed expression such as ($10+2) is an ordinary address.
        }

        return new Operand { Kind = OperandKind.Direct, Value = Expression.Evaluate(t, lookup), Text = t };
    }

    private static Operand ParsePointer(string inner, OperandKind kind, string text, Func<string, long?> lookup)
    {
        string e = inner.Trim();
        bool extended = false;
        if (e.EndsWith(".w", StringComparison.OrdinalIgnoreCase)) e = e[..^2];
        else if (e.EndsWith(".e", StringComparison.OrdinalIgnoreCase)) { e = e[..^2]; extended = true; }
        return new Operand { Kind = kind, Value = Expression.Evaluate(e, lookup), ExtendedPointer = extended, Text = text };
    }

    private static int MatchingClose(string t, int open)
    {
        int depth = 0;
        for (int i = open; i < t.Length; i++)
        {
            if (t[i] is '(' or '[') depth++;
            else if (t[i] is ')' or ']') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int LastTopLevelComma(string t)
    {
        int depth = 0;
        int found = -1;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] is '(' or '[') depth++;
            else if (t[i] is ')' or ']') depth--;
            else if (t[i] == ',' && depth == 0) found = i;
        }
        return found;
    }

    /// <summary>Splits an operand list at top-level commas, leaving those inside brackets and quotes.</summary>
    public static List<string> SplitOperands(string text)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        bool inString = false;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == quote) inString = false;
                continue;
            }
            if (c is '"') { inString = true; quote = c; }
            else if (c == '\'' && i + 2 < text.Length && text[i + 2] == '\'') i += 2;
            else if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        string last = text[start..].Trim();
        if (last.Length > 0 || parts.Count > 0) parts.Add(last);
        return parts;
    }
}
