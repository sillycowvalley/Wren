using System.Globalization;

namespace Wren.Script;

public enum TokenKind { Name, Number, String, Symbol, EndOfLine, EndOfFile }

public sealed record Token(TokenKind Kind, string Text, int Line, long Number = 0)
{
    /// <summary>True for a name equal to word, ignoring case.</summary>
    public bool Is(string word) => Kind == TokenKind.Name && Text.Equals(word, StringComparison.OrdinalIgnoreCase);

    public bool IsSymbol(string symbol) => Kind == TokenKind.Symbol && Text == symbol;

    public override string ToString() => Kind switch
    {
        TokenKind.EndOfLine => "end of line",
        TokenKind.EndOfFile => "end of file",
        TokenKind.String => $"\"{Text}\"",
        _ => $"'{Text}'",
    };
}

/// <summary>An error in a BASIC program, with its line.</summary>
public sealed class ScriptException : Exception
{
    public ScriptException(int line, string message) : base(message) => Line = line;

    public int Line { get; }
}

/// <summary>
/// Splits Wren BASIC into tokens. Comments start with ' or REM. Numbers: 123, &amp;H1F or $1F
/// (hex), &amp;B101 (binary). Strings are in double quotes. ':' separates statements on a line.
/// </summary>
public static class Lexer
{
    public static List<Token> Read(string source)
    {
        var tokens = new List<Token>();
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            int lineNumber = index + 1;
            string text = lines[index];
            int pos = 0;
            while (pos < text.Length)
            {
                char c = text[pos];
                if (char.IsWhiteSpace(c)) { pos++; continue; }
                if (c == '\'') break;

                if (char.IsLetter(c) || c == '_')
                {
                    int start = pos;
                    while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
                    string word = text[start..pos];
                    if (word.Equals("REM", StringComparison.OrdinalIgnoreCase)) { pos = text.Length; break; }
                    tokens.Add(new Token(TokenKind.Name, word, lineNumber));
                    continue;
                }

                if (char.IsDigit(c) || c == '$' || (c == '&' && pos + 1 < text.Length && char.ToUpperInvariant(text[pos + 1]) is 'H' or 'B'))
                {
                    tokens.Add(ReadNumber(text, ref pos, lineNumber));
                    continue;
                }

                if (c == '"')
                {
                    int end = text.IndexOf('"', pos + 1);
                    if (end < 0) throw new ScriptException(lineNumber, "A string has no closing quote");
                    tokens.Add(new Token(TokenKind.String, text[(pos + 1)..end], lineNumber));
                    pos = end + 1;
                    continue;
                }

                string two = pos + 1 < text.Length ? text.Substring(pos, 2) : "";
                if (two is "<>" or "<=" or ">=")
                {
                    tokens.Add(new Token(TokenKind.Symbol, two, lineNumber));
                    pos += 2;
                    continue;
                }
                if ("+-*/=<>(),:".Contains(c))
                {
                    if (c == ':') tokens.Add(new Token(TokenKind.EndOfLine, ":", lineNumber));
                    else tokens.Add(new Token(TokenKind.Symbol, c.ToString(), lineNumber));
                    pos++;
                    continue;
                }
                throw new ScriptException(lineNumber, $"Unexpected character '{c}'");
            }
            tokens.Add(new Token(TokenKind.EndOfLine, "", lineNumber));
        }
        tokens.Add(new Token(TokenKind.EndOfFile, "", lines.Length));
        return tokens;
    }

    private static Token ReadNumber(string text, ref int pos, int line)
    {
        int start = pos;
        int radix = 10;
        if (text[pos] == '$') { radix = 16; pos++; }
        else if (text[pos] == '&')
        {
            radix = char.ToUpperInvariant(text[pos + 1]) == 'H' ? 16 : 2;
            pos += 2;
        }
        int digitsStart = pos;
        while (pos < text.Length && char.IsLetterOrDigit(text[pos])) pos++;
        string digits = text[digitsStart..pos];
        try
        {
            long value = radix switch
            {
                16 => long.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                2 => Convert.ToInt64(digits, 2),
                _ => long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
            };
            return new Token(TokenKind.Number, text[start..pos], line, value);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new ScriptException(line, $"Bad number '{text[start..pos]}'");
        }
    }
}
