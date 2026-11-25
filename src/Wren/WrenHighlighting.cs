using System.Security;
using System.Text;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Wren.Script;

namespace Wren;

/// <summary>
/// Wren BASIC's colours in the editor, Visual Studio's classic ones: keywords blue, the
/// built-in statements and functions teal, constants purple, numbers dark green, strings
/// dark red, comments green. The other board's words are grey italic, so a program using
/// them looks wrong before a build says so. The words come from the compiler's own lists
/// (Names.Categories), so a new word is coloured without touching this.
/// </summary>
public static class WrenHighlighting
{
    private static readonly Dictionary<(Board, bool), IHighlightingDefinition> Made = new();

    /// <summary>The colours for a board, light or dark (Visual Studio's dark theme's colours).</summary>
    public static IHighlightingDefinition For(Board board, bool dark = false)
    {
        if (!Made.TryGetValue((board, dark), out var definition))
        {
            using var reader = XmlReader.Create(new StringReader(Xshd(board, dark)));
            definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            Made[(board, dark)] = definition;
        }
        return definition;
    }

    /// <summary>The definition in AvaloniaEdit's XML format.</summary>
    public static string Xshd(Board board, bool dark = false)
    {
        string C(string light, string night) => dark ? night : light;
        var words = Names.Categories(board);
        var sb = new StringBuilder();
        sb.AppendLine("<SyntaxDefinition name=\"Wren BASIC\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");
        string commentColour = C("#008000", "#6A9955");
        sb.AppendLine($"  <Color name=\"Comment\" foreground=\"{commentColour}\" />");
        string stringColour = C("#A31515", "#CE9178");
        sb.AppendLine($"  <Color name=\"String\" foreground=\"{stringColour}\" />");
        string numberColour = C("#098658", "#B5CEA8");
        sb.AppendLine($"  <Color name=\"Number\" foreground=\"{numberColour}\" />");
        string keywordColour = C("#0000FF", "#569CD6");
        sb.AppendLine($"  <Color name=\"Keyword\" foreground=\"{keywordColour}\" />");
        string builtinColour = C("#008080", "#4EC9B0");
        sb.AppendLine($"  <Color name=\"Builtin\" foreground=\"{builtinColour}\" />");
        string constantColour = C("#6F008A", "#C586C0");
        sb.AppendLine($"  <Color name=\"Constant\" foreground=\"{constantColour}\" />");
        string otherboardColour = C("#A0A0A0", "#6E6E6E");
        sb.AppendLine($"  <Color name=\"OtherBoard\" foreground=\"{otherboardColour}\" fontStyle=\"italic\" />");
        sb.AppendLine("  <RuleSet ignoreCase=\"true\">");
        sb.AppendLine("    <Span color=\"Comment\" begin=\"'\" />");
        sb.AppendLine("    <Span color=\"Comment\" begin=\"\\bREM\\b\" />");
        sb.AppendLine("    <Span color=\"String\" begin=\"&quot;\" end=\"&quot;\" />");
        Group(sb, "OtherBoard", words, WordKind.OtherBoard);
        Group(sb, "Keyword", words, WordKind.Keyword);
        Group(sb, "Builtin", words, WordKind.Statement, WordKind.Function);
        Group(sb, "Constant", words, WordKind.Constant);
        sb.AppendLine("    <Rule color=\"Number\">\\$[0-9A-Fa-f]+|&amp;[Hh][0-9A-Fa-f]+|&amp;[Bb][01]+|\\b[0-9]+\\b</Rule>");
        sb.AppendLine("  </RuleSet>");
        sb.AppendLine("</SyntaxDefinition>");
        return sb.ToString();
    }

    private static void Group(StringBuilder sb, string color, IReadOnlyDictionary<string, WordKind> words, params WordKind[] kinds)
    {
        var chosen = words.Where(w => kinds.Contains(w.Value)).Select(w => w.Key).OrderBy(w => w).ToList();
        if (chosen.Count == 0) return;
        sb.AppendLine($"    <Keywords color=\"{color}\">");
        foreach (string w in chosen) sb.AppendLine($"      <Word>{SecurityElement.Escape(w)}</Word>");
        sb.AppendLine("    </Keywords>");
    }
}
