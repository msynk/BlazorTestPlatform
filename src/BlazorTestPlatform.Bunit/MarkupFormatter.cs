using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace BlazorTestPlatform.Bunit;

/// <summary>
/// Writes rendered markup in the stable form markup snapshots compare: one element per line with two-space indentation,
/// attributes sorted by name (class tokens sorted too), HTML whitespace collapsed, adjacent text merged and kept inline when
/// short, comments dropped, and the attributes the framework adds removed (<c>blazor:onclick</c>,
/// <c>blazor:elementReference</c>, <c>_bl_{id}</c> element references, CSS-isolation scopes such as <c>b-3xxtam6d07</c>).
/// The text of <c>pre</c>, <c>textarea</c>, <c>script</c> and <c>style</c> is kept as it is, and a non-breaking space shows
/// as <c>&amp;nbsp;</c>. Elements carrying <c>data-btp-ignore</c> keep their tag and attributes, and their content
/// (whatever it is, even none) becomes <c>…</c>.
/// </summary>
/// <remarks>
/// Whitespace between inline elements is not represented (each node is on its own line), so <c>Hello <b>x</b></c> and
/// <c>Hello<b>x</b></c> compare equal; a visual snapshot catches spacing.
/// </remarks>
internal static partial class MarkupFormatter
{
    private const int InlineTextLength = 80;

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    // Elements whose text is significant as written.
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase) { "pre", "textarea", "script", "style" };

    [GeneratedRegex("^b-[a-z0-9]{10}$")]
    private static partial Regex ScopeAttribute();

    // HTML whitespace only: a non-breaking space is content.
    [GeneratedRegex("[ \t\n\f\r]+")]
    private static partial Regex Whitespace();

    /// <summary>Formats <paramref name="nodes"/> (the story's root nodes, or one element).</summary>
    public static string Format(IEnumerable<INode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var builder = new StringBuilder();
        WriteChildren(builder, Children(nodes), 0);
        return builder.ToString();
    }

    private static void WriteChildren(StringBuilder builder, List<object> children, int depth)
    {
        foreach (var child in children)
        {
            if (child is IElement element)
            {
                WriteElement(builder, element, depth);
            }
            else
            {
                Indent(builder, depth).Append(EscapeText((string)child)).Append('\n');
            }
        }
    }

    private static void WriteElement(StringBuilder builder, IElement element, int depth)
    {
        var name = element.LocalName;
        Indent(builder, depth).Append('<').Append(name);
        foreach (var attribute in element.Attributes
            .Where(a => !a.Name.StartsWith("blazor:", StringComparison.OrdinalIgnoreCase)
                && !a.Name.StartsWith("_bl_", StringComparison.Ordinal)
                && !ScopeAttribute().IsMatch(a.Name))
            .OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            var value = string.Equals(attribute.Name, "class", StringComparison.Ordinal)
                ? string.Join(' ', attribute.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
                : attribute.Value;
            builder.Append(' ').Append(attribute.Name);
            if (value.Length > 0)
            {
                builder.Append("=\"").Append(EscapeAttribute(value)).Append('"');
            }
        }

        builder.Append('>');
        if (VoidElements.Contains(name))
        {
            builder.Append('\n');
            return;
        }

        if (element.HasAttribute(VisualOptions.IgnoreAttribute))
        {
            builder.Append("…</").Append(name).Append(">\n");
            return;
        }

        if (RawTextElements.Contains(name))
        {
            builder.Append(EscapeText(element.TextContent.Replace("\r\n", "\n", StringComparison.Ordinal))).Append("</").Append(name).Append(">\n");
            return;
        }

        var children = Children(element.ChildNodes);
        if (children.Count == 0)
        {
            builder.Append("</").Append(name).Append(">\n");
            return;
        }

        if (children is [string inline] && inline.Length <= InlineTextLength)
        {
            builder.Append(EscapeText(inline)).Append("</").Append(name).Append(">\n");
            return;
        }

        builder.Append('\n');
        WriteChildren(builder, children, depth + 1);
        Indent(builder, depth).Append("</").Append(name).Append(">\n");
    }

    // Elements, and runs of adjacent text nodes merged into one collapsed string (Blazor renders "Count: @count" as two
    // text nodes). Comments and whitespace-only text are dropped.
    private static List<object> Children(IEnumerable<INode> nodes)
    {
        var children = new List<object>();
        StringBuilder? text = null;
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IElement element:
                    Flush();
                    children.Add(element);
                    break;
                case IText run:
                    (text ??= new StringBuilder()).Append(run.Data);
                    break;
            }
        }

        Flush();
        return children;

        void Flush()
        {
            if (text is not null && Collapse(text.ToString()) is { Length: > 0 } content)
            {
                children.Add(content);
            }

            text = null;
        }
    }

    private static StringBuilder Indent(StringBuilder builder, int depth) => builder.Append(' ', depth * 2);

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim(' ');

    private static string EscapeText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace(" ", "&nbsp;", StringComparison.Ordinal);

    private static string EscapeAttribute(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
