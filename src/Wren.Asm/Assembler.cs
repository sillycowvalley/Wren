using System.Text;

namespace Wren.Asm;

/// <summary>One line of the listing: where it went and what it produced.</summary>
public sealed record ListingLine(string File, int Line, int Address, byte[] Bytes, string Source);

public sealed class AssemblyResult
{
    public List<Diagnostic> Errors { get; } = new();
    public MemoryImage Image { get; } = new();
    public Dictionary<string, long> Symbols { get; } = new();
    public List<ListingLine> Listing { get; } = new();
    public bool Success => Errors.Count == 0;

    /// <summary>A plain-text listing: address, bytes, source.</summary>
    public string ListingText()
    {
        var sb = new StringBuilder();
        foreach (var l in Listing)
        {
            string hex = string.Join(" ", l.Bytes.Select(b => b.ToString("X2")));
            sb.Append(l.Bytes.Length > 0 ? l.Address.ToString("X6") : "      ")
              .Append("  ").Append(hex.PadRight(15)).Append("  ").AppendLine(l.Source);
        }
        return sb.ToString();
    }
}

/// <summary>
/// An STM8 assembler using ST's instruction syntax (PM0044) with Wren's own directives.
///
/// Lines:      [label[:]] [instruction or directive] [; comment]
///             A label starts in column 1; the colon is optional there. An indented label needs the colon.
/// Constants:  NAME EQU expr   or   NAME = expr
/// Directives: ORG expr                       set the address
///             DC.B / DB / BYTE  list         bytes; strings in "double quotes" give one byte per character
///             DC.W / DW / WORD  list         16-bit words, high byte first
///             DS.B / DS  count, DS.W count   reserve space without writing it
///             INCLUDE "file"                 insert another source file
///             RELAX [ON|OFF]                 from here on, JP and CALL to an address become
///                                            JRA and CALLR (2 bytes) where it is in reach
///             END                            stop reading
/// Addresses 0-$FF use the short form where one exists, unless a forward reference is involved.
/// </summary>
public sealed class Assembler
{
    /// <summary>Returns the text of an included file, or null when it cannot be found.</summary>
    public Func<string, string?>? IncludeResolver { get; set; }

    private const int MaxPasses = 20;

    private sealed record SourceLine(string File, int Line, string Text);

    private readonly Dictionary<(int Line, int Operand), bool> forcedLong = new();

    public AssemblyResult Assemble(string source, string fileName = "main.asm")
    {
        var result = new AssemblyResult();
        var lines = new List<SourceLine>();
        try
        {
            Expand(source, fileName, lines, new Stack<string>(), result);
        }
        catch (AsmException ex)
        {
            result.Errors.Add(new Diagnostic(fileName, 0, ex.Message));
            return result;
        }
        if (!result.Success) return result;

        forcedLong.Clear();
        Dictionary<string, long> previous = new();
        for (int pass = 1; pass <= MaxPasses; pass++)
        {
            var symbols = RunPass(lines, previous, null);
            bool stable = pass > 1 && symbols.Count == previous.Count
                && symbols.All(kv => previous.TryGetValue(kv.Key, out long v) && v == kv.Value);
            previous = symbols;
            if (stable) break;
        }

        RunPass(lines, previous, result);
        return result;
    }

    private void Expand(string text, string file, List<SourceLine> lines, Stack<string> including, AssemblyResult result)
    {
        if (including.Contains(file, StringComparer.OrdinalIgnoreCase))
            throw new AsmException($"'{file}' includes itself");
        including.Push(file);

        string[] raw = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            string code = StripComment(raw[i]).Trim();
            string[] words = code.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && words[0].TrimStart('.', '#').Equals("INCLUDE", StringComparison.OrdinalIgnoreCase))
            {
                string name = words[1].Trim().Trim('"');
                string? content = IncludeResolver?.Invoke(name);
                if (content == null)
                {
                    result.Errors.Add(new Diagnostic(file, i + 1, $"Cannot find include file '{name}'"));
                    continue;
                }
                lines.Add(new SourceLine(file, i + 1, raw[i]));
                Expand(content, name, lines, including, result);
                continue;
            }
            lines.Add(new SourceLine(file, i + 1, raw[i]));
        }
        including.Pop();
    }

    /// <summary>
    /// One pass over the source. Returns the symbols it defined. When result is given this is
    /// the final pass: bytes, listing and errors go into it.
    /// </summary>
    private Dictionary<string, long> RunPass(List<SourceLine> lines, Dictionary<string, long> previous, AssemblyResult? result)
    {
        bool final = result != null;
        var symbols = new Dictionary<string, long>();
        long address = 0;
        bool relax = false;

        long? Lookup(string name)
        {
            if (symbols.TryGetValue(name, out long v)) return v;
            if (previous.TryGetValue(name, out long p)) return p;
            if (final) throw new AsmException($"Undefined symbol '{name}'");
            return null;
        }

        void Define(string name, long value, SourceLine line)
        {
            if (Encoder.IsMnemonic(name) || Operand.ParseRegister(name) != Register.None)
                throw new AsmException($"'{name}' is a reserved word and cannot be a label");
            if (symbols.ContainsKey(name)) throw new AsmException($"'{name}' is already defined");
            symbols[name] = value;
        }

        for (int index = 0; index < lines.Count; index++)
        {
            SourceLine line = lines[index];
            long lineAddress = address;
            byte[] produced = Array.Empty<byte>();
            try
            {
                var parsed = ParseLine(line.Text);
                string? op = parsed.Op?.ToUpperInvariant().TrimStart('.');

                if (op is "EQU" or "=")
                {
                    if (parsed.Label == null) throw new AsmException("EQU needs a name");
                    long? value = Expression.Evaluate(parsed.Operands, Lookup);
                    if (value is long v) Define(parsed.Label, v, line);
                    result?.Listing.Add(new ListingLine(line.File, line.Line, (int)(value ?? 0), produced, line.Text));
                    continue;
                }

                if (parsed.Label != null) Define(parsed.Label, address, line);

                switch (op)
                {
                    case null:
                    case "INCLUDE":
                    case "#INCLUDE":
                        break;
                    case "END":
                        result?.Listing.Add(new ListingLine(line.File, line.Line, (int)lineAddress, produced, line.Text));
                        index = lines.Count;
                        continue;
                    case "RELAX":
                        relax = !parsed.Operands.Trim().Equals("OFF", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "ORG":
                        address = Expression.Evaluate(parsed.Operands, Lookup)
                            ?? throw new AsmException("ORG needs a value known at this point");
                        lineAddress = address;
                        break;
                    case "DC.B":
                    case "DB":
                    case "BYTE":
                        produced = DataBytes(parsed.Operands, Lookup, final);
                        break;
                    case "DC.W":
                    case "DW":
                    case "WORD":
                        produced = DataWords(parsed.Operands, Lookup, final);
                        break;
                    case "DS.B":
                    case "DS":
                    case "DS.W":
                        long count = Expression.Evaluate(parsed.Operands, Lookup)
                            ?? throw new AsmException($"{op} needs a value known at this point");
                        if (count < 0) throw new AsmException($"{op} count cannot be negative");
                        address += op == "DS.W" ? count * 2 : count;
                        break;
                    default:
                        if (!Encoder.IsMnemonic(op)) throw new AsmException($"Unknown instruction or directive '{parsed.Op}'");
                        var operandTexts = parsed.Operands.Length == 0 ? new List<string>() : Operand.SplitOperands(parsed.Operands);
                        var ops = new List<Operand>();
                        for (int i = 0; i < operandTexts.Count; i++)
                        {
                            var o = Operand.Parse(operandTexts[i], Lookup);
                            if (o.CanBeShort)
                            {
                                bool fits = o.Value is long v && v >= 0 && v <= 0xFF;
                                if (!fits) forcedLong[(index, i)] = true;
                                o.Short = fits && !forcedLong.ContainsKey((index, i));
                            }
                            ops.Add(o);
                        }
                        if (relax && op is "JP" or "CALL" && ops.Count == 1 && ops[0].Kind == OperandKind.Direct)
                            op = Relax(op, ops[0], address, index);
                        produced = Encoder.Encode(op, ops, address, final);
                        break;
                }

                if (result != null && produced.Length > 0)
                {
                    if (address < 0 || address + produced.Length > 0x1000000)
                        throw new AsmException($"Address out of range: ${address:X}");
                    result.Image.Write((int)address, produced);
                }
                address += produced.Length;
            }
            catch (AsmException ex)
            {
                result?.Errors.Add(new Diagnostic(line.File, line.Line, ex.Message));
            }
            result?.Listing.Add(new ListingLine(line.File, line.Line, (int)lineAddress, produced, line.Text));
        }

        if (result != null)
        {
            foreach (var kv in symbols) result.Symbols[kv.Key] = kv.Value;
        }
        return symbols;
    }

    /// <summary>
    /// JP or CALL to a target within reach of a 2-byte relative jump becomes JRA or CALLR.
    /// Once a jump is found out of reach it stays long, so the passes settle.
    /// </summary>
    private string Relax(string op, Operand target, long address, int line)
    {
        if (forcedLong.ContainsKey((line, -1)) || target.Value is not long value) return op;
        long offset = value - (address + 2);
        if (offset is >= -128 and <= 127) return op == "JP" ? "JRA" : "CALLR";
        forcedLong[(line, -1)] = true;
        return op;
    }

    private static byte[] DataBytes(string text, Func<string, long?> lookup, bool final)
    {
        var data = new List<byte>();
        foreach (string item in Operand.SplitOperands(text))
        {
            if (item.Length >= 2 && item[0] == '"' && item[^1] == '"')
            {
                data.AddRange(Encoding.ASCII.GetBytes(item[1..^1]));
                continue;
            }
            long v = Expression.Evaluate(item, lookup) ?? 0;
            if (final && (v < -128 || v > 255)) throw new AsmException($"Byte out of range: {v}");
            data.Add((byte)v);
        }
        return data.ToArray();
    }

    private static byte[] DataWords(string text, Func<string, long?> lookup, bool final)
    {
        var data = new List<byte>();
        foreach (string item in Operand.SplitOperands(text))
        {
            long v = Expression.Evaluate(item, lookup) ?? 0;
            if (final && (v < -32768 || v > 0xFFFF)) throw new AsmException($"Word out of range: {v}");
            data.Add((byte)(v >> 8));
            data.Add((byte)v);
        }
        return data.ToArray();
    }

    private sealed record ParsedLine(string? Label, string? Op, string Operands);

    private static ParsedLine ParseLine(string raw)
    {
        string text = StripComment(raw).TrimEnd();
        if (text.Trim().Length == 0) return new ParsedLine(null, null, "");

        string? label = null;
        int pos = 0;
        bool startsInColumnOne = !char.IsWhiteSpace(text[0]);

        // A label: in column one, or anywhere when followed by a colon.
        string firstWord = ReadWord(text.TrimStart(), out int wordEnd);
        int lead = text.Length - text.TrimStart().Length;
        string afterWord = text[(lead + wordEnd)..];

        if (firstWord.Length > 0 && afterWord.StartsWith(':'))
        {
            label = firstWord;
            pos = lead + wordEnd + 1;
        }
        else if (startsInColumnOne && firstWord.Length > 0 && Expression.IsIdentifierStart(firstWord[0]))
        {
            label = firstWord;
            pos = lead + wordEnd;
        }
        else if (firstWord.Length > 0 && IsEqu(afterWord))
        {
            label = firstWord;
            pos = lead + wordEnd;
        }

        string rest = text[pos..].Trim();
        if (rest.Length == 0) return new ParsedLine(label, null, "");

        if (rest.StartsWith('='))
            return new ParsedLine(label, "=", rest[1..].Trim());

        string op = ReadWord(rest, out int opEnd);
        if (op.Length == 0) throw new AsmException($"Cannot read '{rest}'");
        return new ParsedLine(label, op, rest[opEnd..].Trim());
    }

    private static bool IsEqu(string afterWord)
    {
        string t = afterWord.TrimStart();
        if (t.StartsWith('=')) return true;
        string w = ReadWord(t, out _);
        return w.Equals("EQU", StringComparison.OrdinalIgnoreCase) || w.Equals(".EQU", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A word of identifier characters, dots and '#'.</summary>
    private static string ReadWord(string text, out int end)
    {
        int i = 0;
        while (i < text.Length && (Expression.IsIdentifierChar(text[i]) || text[i] is '.' or '#')) i++;
        end = i;
        return text[..i];
    }

    private static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') inString = !inString;
            else if (!inString && c == '\'' && i + 2 < line.Length && line[i + 2] == '\'') i += 2;
            else if (!inString && c == ';') return line[..i];
        }
        return line;
    }
}
