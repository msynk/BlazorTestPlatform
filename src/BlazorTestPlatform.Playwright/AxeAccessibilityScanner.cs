using System.Text.Json;
using BlazorTestPlatform.Accessibility;
using Deque.AxeCore.Commons;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Playwright;

/// <summary>
/// Runs axe-core (bundled, offline) in the page and maps results to <see cref="AccessibilityReport"/>. Complements the
/// in-memory markup rules with checks that need a real rendering engine: color contrast, focusable scroll regions,
/// target sizes, visible-label mismatches and more.
/// </summary>
public static class AxeAccessibilityScanner
{
    private static readonly Lazy<string> Script = new(() => new BundledAxeScriptProvider().GetScript());

    private static readonly Dictionary<string, AccessibilityImpact> Impacts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minor"] = AccessibilityImpact.Minor,
        ["moderate"] = AccessibilityImpact.Moderate,
        ["serious"] = AccessibilityImpact.Serious,
        ["critical"] = AccessibilityImpact.Critical,
    };

    /// <summary>Scans the page (limited to <paramref name="include"/> when given).</summary>
    public static async Task<AccessibilityReport> ScanAsync(IPage page, AccessibilityPolicy policy, StoryAccessibilityOptions? storyOptions = null, string? include = "[data-btp-root]")
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(policy);

        var hasAxe = await page.EvaluateAsync<bool>("() => typeof window.axe !== 'undefined'").ConfigureAwait(false);
        if (!hasAxe)
        {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = Script.Value }).ConfigureAwait(false);
        }

        var tags = policy.Tags.ToList();
        if (policy.IncludeBestPractices)
        {
            tags.Add("best-practice");
        }

        var disabled = policy.DisabledRules.Concat(storyOptions?.DisabledRules ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(r => r, _ => new Dictionary<string, bool> { ["enabled"] = false });

        var context = new Dictionary<string, object?>
        {
            ["include"] = new[] { new[] { storyOptions?.Include ?? include ?? "body" } },
            ["exclude"] = (storyOptions?.Exclude ?? []).Select(e => new[] { e }).ToArray(),
        };

        var options = new Dictionary<string, object?>
        {
            ["runOnly"] = new Dictionary<string, object?> { ["type"] = "tag", ["values"] = tags },
            ["rules"] = disabled,
            ["resultTypes"] = new[] { "violations", "incomplete" },
        };

        var raw = await page.EvaluateAsync<JsonElement>(
            "async ([context, options]) => { const r = await window.axe.run(context, options); return { version: r.testEngine.version, violations: r.violations, incomplete: r.incomplete, passes: r.passes.length }; }",
            new object[] { context, options }).ConfigureAwait(false);

        var engine = "axe-core " + (raw.TryGetProperty("version", out var version) ? version.GetString() : "?");
        return new AccessibilityReport
        {
            Engine = engine,
            Violations = Map(raw.GetProperty("violations"), engine),
            Incomplete = Map(raw.GetProperty("incomplete"), engine),
            PassedRules = raw.TryGetProperty("passes", out var passes) ? passes.GetInt32() : 0,
            DisabledRules = disabled.Keys.ToArray(),
        };
    }

    private static List<AccessibilityViolation> Map(JsonElement results, string engine)
    {
        var list = new List<AccessibilityViolation>();
        foreach (var result in results.EnumerateArray())
        {
            var nodes = new List<AccessibilityNode>();
            foreach (var node in result.GetProperty("nodes").EnumerateArray())
            {
                var target = node.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Array
                    ? string.Join(" ", t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()))
                    : string.Empty;
                nodes.Add(new AccessibilityNode(
                    target,
                    node.TryGetProperty("html", out var html) ? html.GetString() ?? string.Empty : string.Empty,
                    node.TryGetProperty("failureSummary", out var summary) ? summary.GetString() : null));
            }

            var impact = result.TryGetProperty("impact", out var i) && i.ValueKind == JsonValueKind.String && Impacts.TryGetValue(i.GetString()!, out var parsed)
                ? parsed
                : AccessibilityImpact.Moderate;

            list.Add(new AccessibilityViolation
            {
                RuleId = result.GetProperty("id").GetString()!,
                Impact = impact,
                Description = result.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                Help = result.TryGetProperty("help", out var h) ? h.GetString() ?? string.Empty : string.Empty,
                HelpUrl = result.TryGetProperty("helpUrl", out var u) ? u.GetString() : null,
                Tags = result.TryGetProperty("tags", out var tags) ? tags.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
                Nodes = nodes,
                Engine = engine,
            });
        }

        return list;
    }
}
