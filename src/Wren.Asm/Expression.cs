using System.Globalization;

namespace Wren.Asm;

/// <summary>
/// Evaluates assembler expressions.
/// Numbers: $FF or 0xFF (hex), %1010 or 0b1010 (binary), 255 (decimal), 'A' (character).
/// Operators, lowest precedence first: |  ^  &amp;  &lt;&lt; &gt;&gt;  + -  * / %  and unary - ~ !
/// Functions: HIGH(x) is bits 8-15, LOW(x) is bits 0-7, PAGE(x) is bits 16-23.
/// </summary>
public static class Expression
{
    /// <summary>
    /// Returns the value, or null when it uses a symbol that is not defined yet.
    /// lookup returns null for an unknown symbol.
    /// </summary>
    public static long? Evaluate(string text, Func<string, long?> lookup)
    {
        var parser = new Parser(text, lookup);
        long? value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    public static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '?' || c == '@';
    public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '?' || c == '@';

    private sealed class Parser
    {
        private readonly string text;
        private readonly Func<string, long?> lookup;
        private int pos;

        public Parser(string text, Func<string, long?> lookup)
        {
            this.text = text;
            this.lookup = lookup;
        }

        public void ExpectEnd()
        {
            SkipSpace();
            if (pos < text.Length)
                throw new AsmException($"Unexpected '{text[pos..]}' in expression '{text}'");
        }

        public long? ParseExpression() => ParseOr();

        private long? ParseOr()
        {
            long? left = ParseXor();
            while (Accept("|")) left = Combine(left, ParseXor(), (a, b) => a | b);
            return left;
        }

        private long? ParseXor()
        {
            long? left = ParseAnd();
            while (Accept("^")) left = Combine(left, ParseAnd(), (a, b) => a ^ b);
            return left;
        }

        private long? ParseAnd()
        {
            long? left = ParseShift();
            while (Accept("&")) left = Combine(left, ParseShift(), (a, b) => a & b);
            return left;
        }

        private long? ParseShift()
        {
            long? left = ParseAdd();
            while (true)
            {
                if (Accept("<<")) left = Combine(left, ParseAdd(), (a, b) => a << (int)b);
                else if (Accept(">>")) left = Combine(left, ParseAdd(), (a, b) => a >> (int)b);
                else return left;
            }
        }

        private long? ParseAdd()
        {
            long? left = ParseMul();
            while (true)
            {
                if (Accept("+")) left = Combine(left, ParseMul(), (a, b) => a + b);
                else if (Accept("-")) left = Combine(left, ParseMul(), (a, b) => a - b);
                else return left;
            }
        }

        private long? ParseMul()
        {
            long? left = ParseUnary();
            while (true)
            {
                if (Accept("*")) left = Combine(left, ParseUnary(), (a, b) => a * b);
                else if (Accept("/")) left = Combine(left, ParseUnary(), Divide);
                else if (Accept("%")) left = Combine(left, ParseUnary(), Modulo);
                else return left;
            }
        }

        private static long Divide(long a, long b) =>
            b == 0 ? throw new AsmException("Division by zero") : a / b;

        private static long Modulo(long a, long b) =>
            b == 0 ? throw new AsmException("Division by zero") : a % b;

        private long? ParseUnary()
        {
            if (Accept("-")) return ParseUnary() is long v ? -v : null;
            if (Accept("~")) return ParseUnary() is long v ? ~v : null;
            if (Accept("!")) return ParseUnary() is long v ? (v == 0 ? 1 : 0) : null;
            if (Accept("+")) return ParseUnary();
            return ParsePrimary();
        }

        private long? ParsePrimary()
        {
            SkipSpace();
            if (pos >= text.Length) throw new AsmException($"Missing value in expression '{text}'");

            char c = text[pos];
            if (c == '(')
            {
                pos++;
                long? inner = ParseExpression();
                Expect(")");
                return inner;
            }
            if (c == '\'') return ParseChar();
            if (c == '$' || c == '%' || char.IsDigit(c)) return ParseNumber();
            if (IsIdentifierStart(c))
            {
                int start = pos;
                while (pos < text.Length && IsIdentifierChar(text[pos])) pos++;
                string name = text[start..pos];

                SkipSpace();
                if (pos < text.Length && text[pos] == '(')
                {
                    string upper = name.ToUpperInvariant();
                    if (upper is "HIGH" or "LOW" or "PAGE")
                    {
                        pos++;
                        long? arg = ParseExpression();
                        Expect(")");
                        if (arg is not long a) return null;
                        return upper switch
                        {
                            "HIGH" => (a >> 8) & 0xFF,
                            "LOW" => a & 0xFF,
                            _ => (a >> 16) & 0xFF,
                        };
                    }
                }
                return lookup(name);
            }
            throw new AsmException($"Unexpected '{c}' in expression '{text}'");
        }

        private long ParseChar()
        {
            // 'A'
            if (pos + 2 < text.Length && text[pos + 2] == '\'')
            {
                long value = text[pos + 1];
                pos += 3;
                return value;
            }
            throw new AsmException($"Bad character constant in '{text}'");
        }

        private long ParseNumber()
        {
            int start = pos;
            int radix = 10;
            if (text[pos] == '$') { radix = 16; pos++; }
            else if (text[pos] == '%') { radix = 2; pos++; }
            else if (text[pos] == '0' && pos + 1 < text.Length && (text[pos + 1] is 'x' or 'X')) { radix = 16; pos += 2; }
            else if (text[pos] == '0' && pos + 1 < text.Length && (text[pos + 1] is 'b' or 'B')
                     && pos + 2 < text.Length && text[pos + 2] is '0' or '1') { radix = 2; pos += 2; }

            int digitsStart = pos;
            while (pos < text.Length && char.IsLetterOrDigit(text[pos])) pos++;
            string digits = text[digitsStart..pos];
            if (digits.Length == 0) throw new AsmException($"Bad number '{text[start..pos]}'");

            try
            {
                return radix switch
                {
                    16 => long.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    2 => Convert.ToInt64(digits, 2),
                    _ => long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                };
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            {
                throw new AsmException($"Bad number '{text[start..pos]}'");
            }
        }

        private static long? Combine(long? a, long? b, Func<long, long, long> op) =>
            a is long x && b is long y ? op(x, y) : null;

        private void SkipSpace()
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        }

        private bool Accept(string token)
        {
            SkipSpace();
            if (string.CompareOrdinal(text, pos, token, 0, token.Length) != 0) return false;
            // Do not read "<" of "<<" as something else, and do not split "<<" into "<" "<".
            pos += token.Length;
            return true;
        }

        private void Expect(string token)
        {
            if (!Accept(token)) throw new AsmException($"Expected '{token}' in expression '{text}'");
        }
    }
}
