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
    /// <summary>Computes a 16-hex-character fingerprint over one or more markup snapshots (e.g. all stories of a component).</summary>
    public static string Compute(IEnumerable<string> markups)
    {
        ArgumentNullException.ThrowIfNull(markups);
        var builder = new StringBuilder();
        foreach (var markup in markups)
        {
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
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>Computes a fingerprint for a single markup snapshot.</summary>
    public static string Compute(string markup) => Compute([markup]);
}
