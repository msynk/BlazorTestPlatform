using System.Globalization;
using System.Text;
using System.Text.Json;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Options of <see cref="SarifReportWriter"/>.</summary>
public sealed class SarifOptions
{
    /// <summary>
    /// The repository root: story source files are written relative to it (with the <c>%SRCROOT%</c> base), which is
    /// what code-scanning services (GitHub, Azure DevOps) need to place results on files. Default: the current directory.
    /// </summary>
    public string? SourceRoot { get; set; }

    /// <summary>Also report accessibility violations that do not fail the policy (as warnings and notes). Default true.</summary>
    public bool IncludeWarnings { get; set; } = true;
}

/// <summary>
/// Writes SARIF 2.1.0, the static-analysis interchange format that GitHub code scanning, Azure DevOps and IDE viewers
/// read: accessibility violations (by axe rule id), failed stories, visual mismatches and render-performance regressions,
/// each located at the source of the story that found it and fingerprinted so re-runs update the same alert.
/// </summary>
public static class SarifReportWriter
{
    /// <summary>Rule id of a failed story run.</summary>
    public const string StoryFailedRule = "btp/story-failed";

    /// <summary>Rule id of a visual mismatch.</summary>
    public const string VisualRule = "btp/visual-mismatch";

    /// <summary>Rule id of a render-performance regression.</summary>
    public const string PerformanceRule = "btp/performance-regression";

    /// <summary>Writes the SARIF log to <paramref name="path"/>.</summary>
    public static void Write(RunSummary summary, string path, SarifOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, Create(summary, options));
    }

    /// <summary>Builds the SARIF log.</summary>
    public static string Create(RunSummary summary, SarifOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        options ??= new SarifOptions();
        var root = Path.GetFullPath(options.SourceRoot ?? Environment.CurrentDirectory);
        var rules = new SortedDictionary<string, (string Name, string Description, string? HelpUri, IReadOnlyList<string> Tags)>(StringComparer.Ordinal)
        {
            [StoryFailedRule] = ("StoryFailed", "A story failed: an interaction, assertion, render or runtime check did not pass.", null, ["reliability"]),
            [VisualRule] = ("VisualMismatch", "A screenshot no longer matches its baseline.", null, ["visual"]),
            [PerformanceRule] = ("PerformanceRegression", "A render-performance budget was exceeded or the baseline regressed.", null, ["performance"]),
        };

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            writer.WriteString("version", "2.1.0");
            writer.WriteStartArray("runs");
            writer.WriteStartObject();

            // Results first into a side buffer, so the rules they use are known when the tool section is written.
            using var resultsBuffer = new MemoryStream();
            using (var results = new Utf8JsonWriter(resultsBuffer, new JsonWriterOptions { Indented = true }))
            {
                results.WriteStartArray();
                foreach (var result in summary.Results)
                {
                    WriteResults(results, result, root, options, rules);
                }

                results.WriteEndArray();
            }

            writer.WriteStartObject("tool");
            writer.WriteStartObject("driver");
            writer.WriteString("name", "BlazorTestPlatform");
            writer.WriteString("informationUri", "https://github.com/blazor-test-platform/BlazorTestPlatform");
            writer.WriteStartArray("rules");
            foreach (var (id, rule) in rules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteString("name", rule.Name);
                writer.WriteStartObject("shortDescription");
                writer.WriteString("text", rule.Description);
                writer.WriteEndObject();
                if (rule.HelpUri is { } help && Uri.TryCreate(help, UriKind.Absolute, out var helpUri) && helpUri.Scheme is "https" or "http")
                {
                    writer.WriteString("helpUri", helpUri.AbsoluteUri);
                }

                writer.WriteStartObject("properties");
                writer.WriteStartArray("tags");
                foreach (var tag in rule.Tags)
                {
                    writer.WriteStringValue(tag);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartObject("originalUriBaseIds");
            writer.WriteStartObject("%SRCROOT%");
            writer.WriteString("uri", new Uri(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar).AbsoluteUri);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WritePropertyName("results");
            writer.WriteRawValue(resultsBuffer.ToArray());
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteResults(Utf8JsonWriter writer, StoryResult result, string root, SarifOptions options, SortedDictionary<string, (string Name, string Description, string? HelpUri, IReadOnlyList<string> Tags)> rules)
    {
        var cell = JUnitReportWriter.Cell(result);
        if (result.Accessibility is { } a11y)
        {
            foreach (var violation in a11y.Violations)
            {
                var failing = result.AccessibilityFailures.Contains(violation.RuleId, StringComparer.OrdinalIgnoreCase);
                if (!failing && !options.IncludeWarnings)
                {
                    continue;
                }

                rules.TryAdd(violation.RuleId, (violation.RuleId, string.IsNullOrWhiteSpace(violation.Help) ? violation.RuleId : violation.Help, violation.HelpUrl, ["accessibility", .. violation.Tags]));
                foreach (var node in violation.Nodes.DefaultIfEmpty(new AccessibilityNode(string.Empty, string.Empty)))
                {
                    var level = failing ? "error" : violation.Impact >= AccessibilityImpact.Serious ? "warning" : "note";
                    var message = $"{violation.Help} ({violation.Impact.ToString().ToLowerInvariant()}) in story '{result.StoryId}' [{cell}]"
                        + (node.Target.Length > 0 ? $": {node.Target}" : string.Empty)
                        + (node.FailureSummary is { Length: > 0 } summary ? $". {summary}" : string.Empty);
                    WriteResult(writer, violation.RuleId, level, message, result, root, $"{violation.RuleId}|{result.Key}|{node.Target}");
                }
            }
        }

        if (result.Outcome == StoryOutcome.Failed && result.AccessibilityFailures.Count == 0 && result.Visual?.IsFailure != true)
        {
            WriteResult(writer, StoryFailedRule, "error", $"Story '{result.StoryId}' failed [{cell}]: {FirstLine(result.Error)}", result, root, $"{StoryFailedRule}|{result.Key}");
        }

        if (result.Visual is { IsFailure: true } visual)
        {
            var detail = visual.Status switch
            {
                VisualStatus.MissingBaseline => "has no baseline",
                VisualStatus.SizeMismatch => $"changed size ({visual.Width}x{visual.Height})",
                _ => $"differs by {visual.DiffPixels.ToString(CultureInfo.InvariantCulture)} px ({visual.DiffRatio.ToString("P2", CultureInfo.InvariantCulture)})",
            };
            WriteResult(writer, VisualRule, "error", $"Snapshot '{visual.SnapshotName}' of story '{result.StoryId}' {detail}.", result, root, $"{VisualRule}|{visual.SnapshotName}");
        }

        var regressions = result.BudgetViolations.Select(v => v.ToString())
            .Concat(result.PerformanceComparison?.Failures.Select(f => f.ToString()) ?? [])
            .ToArray();
        if (regressions.Length > 0)
        {
            WriteResult(writer, PerformanceRule, "error", $"Story '{result.StoryId}' [{cell}]: {string.Join("; ", regressions)}", result, root, $"{PerformanceRule}|{result.Key}");
        }
    }

    private static void WriteResult(Utf8JsonWriter writer, string ruleId, string level, string message, StoryResult result, string root, string identity)
    {
        writer.WriteStartObject();
        writer.WriteString("ruleId", ruleId);
        writer.WriteString("level", level);
        writer.WriteStartObject("message");
        writer.WriteString("text", message);
        writer.WriteEndObject();
        writer.WriteStartArray("locations");
        writer.WriteStartObject();
        if (RelativeSource(result.SourceFile, root) is { } source)
        {
            writer.WriteStartObject("physicalLocation");
            writer.WriteStartObject("artifactLocation");
            writer.WriteString("uri", source);
            writer.WriteString("uriBaseId", "%SRCROOT%");
            writer.WriteEndObject();
            writer.WriteStartObject("region");
            writer.WriteNumber("startLine", Math.Max(1, result.SourceLine ?? 1));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteStartArray("logicalLocations");
        writer.WriteStartObject();
        writer.WriteString("fullyQualifiedName", result.StoryId);
        writer.WriteString("kind", "member");
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();

        // A stable fingerprint lets code scanning track one alert across runs instead of opening a new one each time.
        writer.WriteStartObject("partialFingerprints");
        writer.WriteString("btpIdentity/v1", DeterministicRandom.StableHash(identity).ToString("x16", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// The story's source relative to <paramref name="root"/>, with forward slashes; null when unknown or outside it.
    /// Deterministic builds map sources to <c>/_/</c> (the repository root), which is resolved the same way.
    /// </summary>
    internal static string? RelativeSource(string? sourceFile, string root)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            return null;
        }

        var normalized = sourceFile.Replace('\\', '/');
        if (normalized.StartsWith("/_/", StringComparison.Ordinal))
        {
            return normalized[3..];
        }

        if (!Path.IsPathRooted(sourceFile))
        {
            return normalized.TrimStart('/');
        }

        var relative = Path.GetRelativePath(root, sourceFile).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative;
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(no message)";
        }

        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? text : text[..newline].TrimEnd('\r');
    }
}
