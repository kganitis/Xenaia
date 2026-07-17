using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Xenaia.Modules.Triage.Rules;

/// <summary>
/// Converts a ticket body from HTML to normalized plain text: tags stripped,
/// entities decoded, whitespace runs collapsed, line breaks preserved between
/// block elements, table cells joined by spaces on one line. Rule patterns
/// match against this text, never against raw HTML.
/// </summary>
public static class TextNormalizer
{
    private static readonly HashSet<string> LineBreakElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "h1", "h2", "h3", "h4", "h5", "h6",
        "li", "tr", "table", "ul", "ol", "hr", "blockquote",
    };

    private static readonly HashSet<string> CellElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "td", "th",
    };

    private static readonly Regex WhitespaceRun = new(
        @"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();

        var sb = new StringBuilder();
        AppendText(doc.DocumentNode, sb);

        var lines = sb.ToString()
            .Split('\n')
            .Select(line => WhitespaceRun.Replace(line, " ").Trim())
            .Where(line => line.Length > 0);

        return string.Join("\n", lines);
    }

    private static void AppendText(HtmlNode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                sb.Append(HtmlEntity.DeEntitize(child.InnerText));
            }
            else if (child.NodeType == HtmlNodeType.Element)
            {
                var breaksLine = LineBreakElements.Contains(child.Name);
                var isCell = CellElements.Contains(child.Name);
                if (breaksLine) sb.Append('\n');
                if (isCell) sb.Append(' ');
                AppendText(child, sb);
                if (isCell) sb.Append(' ');
                if (breaksLine) sb.Append('\n');
            }
        }
    }
}
