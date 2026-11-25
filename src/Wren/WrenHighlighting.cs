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
    private static readonly Dictionary<Board, IHighlightingDefinition> Made = new();

    public static IHighlightingDefinition For(Board board)
    {
        if (!Made.TryGetValue(board, out var definition))
        {
            using var reader = XmlReader.Create(new StringReader(Xshd(board)));
            definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            Made[board] = definition;
        }
        return definition;
    }

    /// <summary>The definition in AvaloniaEdit's XML format.</summary>
    public static string Xshd(Board board)
    {
        var words = Names.Categories(board);
        var sb = new StringBuilder();
        sb.AppendLine("<SyntaxDefinition name=\"Wren BASIC\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");
        sb.AppendLine("  <Color name=\"Comment\" foreground=\"#008000\" />");
        sb.AppendLine("  <Color name=\"String\" foreground=\"#A31515\" />");
        sb.AppendLine("  <Color name=\"Number\" foreground=\"#098658\" />");
        sb.AppendLine("  <Color name=\"Keyword\" foreground=\"#0000FF\" />");
        sb.AppendLine("  <Color name=\"Builtin\" foreground=\"#008080\" />");
        sb.AppendLine("  <Color name=\"Constant\" foreground=\"#6F008A\" />");
        sb.AppendLine("  <Color name=\"OtherBoard\" foreground=\"#A0A0A0\" fontStyle=\"italic\" />");
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
