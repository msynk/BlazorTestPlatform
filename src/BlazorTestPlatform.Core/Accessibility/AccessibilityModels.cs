using System.Globalization;
using System.Text;

namespace BlazorTestPlatform.Accessibility;

/// <summary>Severity of an accessibility violation (axe-core scale).</summary>
public enum AccessibilityImpact
{
    /// <summary>Annoyance.</summary>
    Minor = 1,

    /// <summary>Some difficulty.</summary>
    Moderate = 2,

    /// <summary>Serious barrier.</summary>
    Serious = 3,

    /// <summary>Blocks access entirely.</summary>
    Critical = 4,
}

/// <summary>One offending element.</summary>
/// <param name="Target">CSS selector of the element.</param>
/// <param name="Html">HTML snippet.</param>
/// <param name="FailureSummary">How to fix.</param>
public sealed record AccessibilityNode(string Target, string Html, string? FailureSummary = null);

/// <summary>A failed accessibility rule.</summary>
public sealed record AccessibilityViolation
{
    /// <summary>Rule id (axe-core ids are used by every engine, e.g. <c>button-name</c>).</summary>
    public required string RuleId { get; init; }

    /// <summary>Severity.</summary>
    public AccessibilityImpact Impact { get; init; }

    /// <summary>What the rule checks.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Short help text.</summary>
    public string Help { get; init; } = string.Empty;

    /// <summary>Documentation link.</summary>
    public string? HelpUrl { get; init; }

    /// <summary>Tags such as <c>wcag2aa</c>, <c>wcag143</c>, <c>best-practice</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Offending elements.</summary>
    public IReadOnlyList<AccessibilityNode> Nodes { get; init; } = [];

    /// <summary>Engine that produced the violation (<c>btp-markup</c>, <c>axe-core 4.x</c>).</summary>
    public string Engine { get; init; } = string.Empty;

    /// <summary>WCAG success criteria derived from tags (e.g. <c>1.4.3</c>).</summary>
    public IEnumerable<string> WcagCriteria => Tags
        .Where(t => t.Length > 4 && t.StartsWith("wcag", StringComparison.Ordinal) && t[4..].All(char.IsAsciiDigit))
        .Select(t => t[4..])
        .Select(d => d.Length >= 3 ? $"{d[0]}.{d[1]}.{d[2..]}" : d);
}

/// <summary>Accessibility scan output for one story/cell.</summary>
public sealed record AccessibilityReport
{
    /// <summary>Engine name and version.</summary>
    public required string Engine { get; init; }

    /// <summary>Violations found.</summary>
    public IReadOnlyList<AccessibilityViolation> Violations { get; init; } = [];

    /// <summary>Checks the engine could not decide (need manual review).</summary>
    public IReadOnlyList<AccessibilityViolation> Incomplete { get; init; } = [];

    /// <summary>Number of rules that passed.</summary>
    public int PassedRules { get; init; }

    /// <summary>Rules skipped because they were disabled.</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = [];
}

/// <summary>An accepted, time-boxed known issue. Suppressions are explicit and expire, so debt stays visible.</summary>
public sealed record AccessibilitySuppression
{
    /// <summary>Rule id.</summary>
    public required string RuleId { get; init; }

    /// <summary>Glob of story ids (default all).</summary>
    public string StoryPattern { get; init; } = "*";

    /// <summary>Only suppress nodes whose selector contains this text.</summary>
    public string? TargetContains { get; init; }

    /// <summary>Why the issue is accepted (ticket link...).</summary>
    public required string Justification { get; init; }

    /// <summary>After this date the suppression stops applying and the build fails again.</summary>
    public DateOnly? Expires { get; init; }
}

/// <summary>Outcome of applying a policy to a report.</summary>
public sealed record AccessibilityEvaluation
{
    /// <summary>Violations that fail the gate.</summary>
    public IReadOnlyList<AccessibilityViolation> Failures { get; init; } = [];

    /// <summary>Violations below the impact threshold (reported as warnings).</summary>
    public IReadOnlyList<AccessibilityViolation> Warnings { get; init; } = [];

    /// <summary>Violations covered by a suppression.</summary>
    public IReadOnlyList<AccessibilityViolation> Suppressed { get; init; } = [];

    /// <summary>Suppressions that matched but have expired.</summary>
    public IReadOnlyList<AccessibilitySuppression> ExpiredSuppressions { get; init; } = [];

    /// <summary>True when nothing fails the gate.</summary>
    public bool Passed => Failures.Count == 0;

    /// <summary>Human-readable failure report.</summary>
    public string Format(string? subject = null)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{Failures.Count} accessibility violation(s){(subject is null ? string.Empty : " in " + subject)}:");
        foreach (var violation in Failures)
        {
            builder.AppendLine().Append(CultureInfo.InvariantCulture, $"  [{violation.Impact.ToString().ToLowerInvariant()}] {violation.RuleId}: {violation.Help}");
            if (violation.HelpUrl is not null)
            {
                builder.Append(CultureInfo.InvariantCulture, $" ({violation.HelpUrl})");
            }

            foreach (var node in violation.Nodes.Take(5))
            {
                builder.AppendLine().Append(CultureInfo.InvariantCulture, $"      {node.Target}: {Truncate(node.Html, 160)}");
                if (!string.IsNullOrWhiteSpace(node.FailureSummary))
                {
                    builder.AppendLine().Append(CultureInfo.InvariantCulture, $"        -> {node.FailureSummary.ReplaceLineEndings(" ")}");
                }
            }
        }

        foreach (var expired in ExpiredSuppressions)
        {
            builder.AppendLine().Append(CultureInfo.InvariantCulture, $"  Expired suppression for {expired.RuleId} ({expired.Expires:yyyy-MM-dd}): {expired.Justification}");
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

/// <summary>The accessibility gate applied to every story.</summary>
public sealed class AccessibilityPolicy
{
    /// <summary>Violations at or above this impact fail the build. Default <see cref="AccessibilityImpact.Serious"/>.</summary>
    public AccessibilityImpact FailOn { get; set; } = AccessibilityImpact.Serious;

    /// <summary>Rule tags to run (axe-core <c>runOnly</c>). Default WCAG 2.2 A/AA plus 2.0/2.1 A/AA.</summary>
    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase) { "wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa" };

    /// <summary>Also run best-practice rules (reported, gate still uses <see cref="FailOn"/>). Default true.</summary>
    public bool IncludeBestPractices { get; set; } = true;

    /// <summary>Rules disabled everywhere.</summary>
    public HashSet<string> DisabledRules { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Known, time-boxed issues.</summary>
    public List<AccessibilitySuppression> Suppressions { get; } = [];

    /// <summary>Applies the policy (and story-level disabled rules) to a report.</summary>
    public AccessibilityEvaluation Evaluate(AccessibilityReport report, string? storyId = null, StoryAccessibilityOptions? storyOptions = null, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var now = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var failures = new List<AccessibilityViolation>();
        var warnings = new List<AccessibilityViolation>();
        var suppressed = new List<AccessibilityViolation>();
        var expired = new List<AccessibilitySuppression>();

        foreach (var violation in report.Violations)
        {
            if (DisabledRules.Contains(violation.RuleId) || storyOptions?.DisabledRules.Contains(violation.RuleId) == true)
            {
                suppressed.Add(violation);
                continue;
            }

            var matching = Suppressions.Where(s =>
                string.Equals(s.RuleId, violation.RuleId, StringComparison.OrdinalIgnoreCase)
                && (storyId is null || Glob.IsMatch(storyId, s.StoryPattern))
                && (s.TargetContains is null || violation.Nodes.All(n => n.Target.Contains(s.TargetContains, StringComparison.OrdinalIgnoreCase))))
                .ToArray();

            var active = matching.FirstOrDefault(s => s.Expires is null || s.Expires >= now);
            if (active is not null)
            {
                suppressed.Add(violation);
                continue;
            }

            expired.AddRange(matching);
            if (violation.Impact >= FailOn || matching.Length > 0)
            {
                failures.Add(violation);
            }
            else
            {
                warnings.Add(violation);
            }
        }

        return new AccessibilityEvaluation { Failures = failures, Warnings = warnings, Suppressed = suppressed, ExpiredSuppressions = expired.Distinct().ToArray() };
    }
}

/// <summary>Thrown when the accessibility gate fails.</summary>
public sealed class AccessibilityViolationException : StoryAssertionException
{
    /// <summary>Creates the exception.</summary>
    public AccessibilityViolationException(AccessibilityEvaluation evaluation, string? subject = null)
        : base((evaluation ?? throw new ArgumentNullException(nameof(evaluation))).Format(subject))
    {
        Evaluation = evaluation;
    }

    /// <summary>The failing evaluation.</summary>
    public AccessibilityEvaluation Evaluation { get; }
}
