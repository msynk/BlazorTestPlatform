using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Aria;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility;

/// <summary>
/// The accessibility tree of rendered markup as text: one line per element with a role (generic containers are
/// flattened), its accessible name and states, children indented under it. bUnit computes it with this engine and the
/// browser drivers with its port in <c>btp-driver.js</c>, so <c>ctx.ExpectAriaSnapshotAsync</c> passes or fails alike
/// everywhere.
/// </summary>
/// <example>
/// <code>
/// - banner:
///   - heading "Driver parity" [level=1]
/// - region "Account":
///   - checkbox "Remember me" [checked=false]
///   - button "Save" [disabled]
/// </code>
/// </example>
public static class AriaSnapshot
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "script", "style", "template", "head", "noscript", "meta", "link", "title" };

    /// <summary>The snapshot of a markup fragment (CSS visibility applies when <paramref name="styles"/> is given).</summary>
    public static string Of(string markup, CssVisibility? styles = null) => Of(DomScope.Parse(markup ?? throw new ArgumentNullException(nameof(markup)), styles));

    /// <summary>The snapshot of a parsed scope.</summary>
    public static string Of(DomScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var lines = new List<string>();
        Walk(scope.Roots.OfType<IElement>(), scope, depth: 0, lines);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Normalizes a snapshot written by hand for comparison: line endings, blank lines, trailing spaces and the common
    /// indentation (so indented raw string literals compare equal).
    /// </summary>
    public static string Normalize(string snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var lines = snapshot.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToArray();
        var indent = lines.Length == 0 ? 0 : lines.Min(l => l.Length - l.TrimStart().Length);
        return string.Join('\n', lines.Select(l => l[indent..]));
    }

    private static void Walk(IEnumerable<IElement> elements, DomScope scope, int depth, List<string> lines)
    {
        foreach (var element in elements)
        {
            if (Skipped.Contains(element.LocalName) || DomFacts.IsExcludedFromAccessibilityTree(element, scope))
            {
                continue;
            }

            var role = AriaRoles.GetRole(element);
            if (role is null or "generic" or "none" or "presentation")
            {
                Walk(element.Children, scope, depth, lines);
                continue;
            }

            var line = new StringBuilder().Append(' ', depth * 2).Append("- ").Append(role);
            var name = AccessibleName.Compute(element, scope);
            if (name.Length > 0)
            {
                line.Append(" \"").Append(name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }

            var states = States(element, role);
            if (states.Count > 0)
            {
                line.Append(" [").AppendJoin("] [", states).Append(']');
            }

            var children = new List<string>();
            Walk(element.Children, scope, depth + 1, children);
            lines.Add(children.Count > 0 ? line.Append(':').ToString() : line.ToString());
            lines.AddRange(children);
        }
    }

    private static List<string> States(IElement element, string role)
    {
        var states = new List<string>();
        if (DomQueries.CheckedState(element) is { } isChecked)
        {
            states.Add(isChecked ? "checked" : "checked=false");
        }

        if (DomFacts.IsDisabled(element))
        {
            states.Add("disabled");
        }

        switch (element.GetAttribute("aria-expanded")?.Trim().ToLowerInvariant())
        {
            case "true":
                states.Add("expanded");
                break;
            case "false":
                states.Add("expanded=false");
                break;
        }

        if (role == "heading")
        {
            states.Add("level=" + HeadingLevel(element).ToString(CultureInfo.InvariantCulture));
        }

        return states;
    }

    private static int HeadingLevel(IElement element)
    {
        if (int.TryParse(element.GetAttribute("aria-level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level >= 1)
        {
            return level;
        }

        return element.LocalName is ['h', >= '1' and <= '6'] ? element.LocalName[1] - '0' : 2;
    }
}
