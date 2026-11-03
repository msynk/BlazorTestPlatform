using System.Text;
using System.Text.RegularExpressions;

namespace BlazorTestPlatform.Accessibility.Dom;

/// <summary>
/// Splits stylesheets by cascade layer before they are parsed, because the CSS parser keeps neither <c>@layer</c> nor
/// <c>@container</c> blocks. Each layer's rules become one stylesheet (keeping the <c>@media</c>/<c>@supports</c> blocks
/// around them), unlayered rules another, and the layer order is recorded as declared (<c>@layer a, b;</c> or first
/// use). <c>@container</c> conditions are rewritten as <c>@media</c> conditions on the viewport: without layout the
/// container's size is unknown, and the viewport is its upper bound.
/// </summary>
internal sealed partial class CssCascadeLayers
{
    private readonly Dictionary<string, StringBuilder> _layers = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly StringBuilder _unlayered = new();
    private int _anonymous;

    /// <summary>Layer names in precedence order (later wins for normal declarations).</summary>
    public IReadOnlyList<string> Order => _order;

    /// <summary>CSS of each layer, plus the unlayered CSS under a null name.</summary>
    public IEnumerable<(string? Layer, string Css)> Sheets()
    {
        foreach (var name in _order)
        {
            if (_layers.TryGetValue(name, out var css) && css.Length > 0)
            {
                yield return (name, css.ToString());
            }
        }

        if (_unlayered.Length > 0)
        {
            yield return (null, _unlayered.ToString());
        }
    }

    /// <summary>Adds a stylesheet.</summary>
    public void Add(string css) => Split(css, layer: null, wrappers: []);

    private void Split(string css, string? layer, string[] wrappers)
    {
        foreach (var (prelude, body) in Items(css))
        {
            var trimmed = prelude.Trim();
            if (trimmed.StartsWith("@layer", StringComparison.OrdinalIgnoreCase))
            {
                var names = trimmed[6..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (body is null)
                {
                    // Statement form: declares the order of layers that may be filled later.
                    foreach (var name in names)
                    {
                        Declare(Combine(layer, name));
                    }

                    continue;
                }

                var blockName = Combine(layer, names.Length > 0 ? names[0] : $"<anonymous-{_anonymous++}>");
                Declare(blockName);
                Split(body, blockName, wrappers);
            }
            else if (body is not null && (trimmed.StartsWith("@media", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("@supports", StringComparison.OrdinalIgnoreCase)))
            {
                Split(body, layer, [.. wrappers, trimmed]);
            }
            else if (body is not null && trimmed.StartsWith("@container", StringComparison.OrdinalIgnoreCase))
            {
                Split(body, layer, [.. wrappers, ContainerAsMedia(trimmed)]);
            }
            else if (body is not null)
            {
                var target = layer is null ? _unlayered : Bucket(layer);
                foreach (var wrapper in wrappers)
                {
                    target.Append(wrapper).Append('{');
                }

                target.Append(trimmed).Append('{').Append(body).Append('}');
                target.Append('}', wrappers.Length).Append('\n');
            }
        }
    }

    private static string Combine(string? parent, string name) => parent is null ? name : parent + "." + name;

    private void Declare(string name)
    {
        if (!_order.Contains(name))
        {
            // A sub-layer is ordered with its parent: just before the parent's own (unnested) rules take effect.
            var parent = name.LastIndexOf('.') is > 0 and var dot ? name[..dot] : null;
            var index = parent is null ? -1 : _order.IndexOf(parent);
            if (index >= 0)
            {
                _order.Insert(index, name);
            }
            else
            {
                _order.Add(name);
            }
        }
    }

    private StringBuilder Bucket(string layer)
    {
        Declare(layer);
        if (!_layers.TryGetValue(layer, out var bucket))
        {
            _layers[layer] = bucket = new StringBuilder();
        }

        return bucket;
    }

    /// <summary>
    /// <c>@container card (min-width: 400px)</c> becomes <c>@media (min-width: 400px)</c>; logical sizes map to the
    /// viewport's width and height. Style and state queries cannot be evaluated and never apply.
    /// </summary>
    private static string ContainerAsMedia(string prelude)
    {
        var condition = ContainerName().Replace(prelude[10..].Trim(), string.Empty).Trim();
        if (condition.Length == 0 || condition.Contains("style(", StringComparison.OrdinalIgnoreCase) || condition.Contains("scroll-state(", StringComparison.OrdinalIgnoreCase))
        {
            return "@media not all";
        }

        condition = condition
            .Replace("inline-size", "width", StringComparison.OrdinalIgnoreCase)
            .Replace("block-size", "height", StringComparison.OrdinalIgnoreCase);
        return "@media " + condition;
    }

    [GeneratedRegex(@"^(?!not\b|\()[a-zA-Z_][\w-]*\s*")]
    private static partial Regex ContainerName();

    /// <summary>Top-level items of a CSS text: (prelude, block body) or (statement, null), skipping comments and strings.</summary>
    private static IEnumerable<(string Prelude, string? Body)> Items(string css)
    {
        var start = 0;
        var i = 0;
        while (i < css.Length)
        {
            var ch = css[i];
            if (ch == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 2;
                continue;
            }

            if (ch is '"' or '\'')
            {
                i = SkipString(css, i);
                continue;
            }

            if (ch == ';')
            {
                var statement = StripComments(css[start..i]).Trim();
                if (statement.Length > 0)
                {
                    yield return (statement, null);
                }

                start = ++i;
                continue;
            }

            if (ch == '{')
            {
                var close = MatchingBrace(css, i);
                yield return (StripComments(css[start..i]), css[(i + 1)..close]);
                start = i = Math.Min(close + 1, css.Length);
                continue;
            }

            i++;
        }
    }

    private static int SkipString(string css, int i)
    {
        var quote = css[i++];
        while (i < css.Length && css[i] != quote)
        {
            i += css[i] == '\\' ? 2 : 1;
        }

        return i + 1;
    }

    private static int MatchingBrace(string css, int open)
    {
        var depth = 0;
        var i = open;
        while (i < css.Length)
        {
            var ch = css[i];
            if (ch == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 2;
                continue;
            }

            if (ch is '"' or '\'')
            {
                i = SkipString(css, i);
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}' && --depth == 0)
            {
                return i;
            }

            i++;
        }

        return css.Length;
    }

    private static string StripComments(string text) => Comment().Replace(text, " ");

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();
}
