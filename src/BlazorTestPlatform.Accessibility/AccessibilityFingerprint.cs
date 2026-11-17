using System.Security.Cryptography;
using System.Text;
using BlazorTestPlatform.Accessibility.Aria;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility;

/// <summary>
/// Hashes the accessibility-relevant structure of rendered markup: element roles, ARIA attribute names, focusability,
/// input types and whether each node is named — but not text, classes or styles. Copy edits keep the fingerprint;
/// structural changes (a new control, a removed label, a changed role) invalidate manual reviews.
/// </summary>
public static class AccessibilityFingerprint
{
    /// <summary>
    /// Computes a 16-hex-character fingerprint over one or more markup snapshots, in order (the fingerprint of earlier
    /// versions: two stories that render the same structure count twice, and their order matters). Manual reviews use
    /// <see cref="ComputeSet"/>; this one is kept so reviews recorded with it still match.
    /// </summary>
    public static string Compute(IEnumerable<string> markups)
    {
        ArgumentNullException.ThrowIfNull(markups);
        return Hash(string.Concat(markups.Select(Structure)));
    }

    /// <summary>
    /// The fingerprint of the distinct structures <paramref name="markups"/> render (e.g. all stories of a component): it
    /// changes when a state with a new accessible structure appears or one goes away, not when a story repeats a state
    /// another story already renders (a story that differs only in its play script) or stories are reordered. Equal to
    /// <see cref="Compute(string)"/> for a single snapshot.
    /// </summary>
    public static string ComputeSet(IEnumerable<string> markups)
    {
        ArgumentNullException.ThrowIfNull(markups);
        return Hash(string.Concat(markups.Select(Structure).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
    }

    private static string Hash(string structure) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(structure)).AsSpan(0, 8));

    private static string Structure(string markup)
    {
        var builder = new StringBuilder();
        var scope = DomScope.Parse(markup);
        foreach (var element in scope.Elements())
        {
            if (DomFacts.IsHidden(element))
            {
                continue;
            }

            var role = AriaRoles.GetRole(element);
            var focusable = DomFacts.IsTabbable(element);
            var aria = element.Attributes
                .Where(a => a.Name.StartsWith("aria-", StringComparison.Ordinal) && a.Name is not ("aria-label" or "aria-valuenow" or "aria-valuetext" or "aria-checked" or "aria-pressed" or "aria-selected" or "aria-expanded" or "aria-activedescendant"))
                .Select(a => a.Name)
                .Order(StringComparer.Ordinal);

            if (role is null or "generic" && !focusable && !aria.Any())
            {
                continue;
            }

            builder.Append(element.LocalName).Append('|').Append(role).Append('|')
                .Append(element.LocalName == "input" ? element.GetAttribute("type") ?? "text" : string.Empty).Append('|')
                .Append(focusable ? 'f' : '-')
                .Append(string.IsNullOrWhiteSpace(AccessibleName.Compute(element, scope)) ? '-' : 'n').Append('|')
                .AppendJoin(',', aria).Append('\n');
        }

        builder.Append("--\n");

        return builder.ToString();
    }

    /// <summary>Computes a fingerprint for a single markup snapshot.</summary>
    public static string Compute(string markup) => Compute([markup]);
}
