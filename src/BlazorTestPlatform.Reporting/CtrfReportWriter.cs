using System.Reflection;
using System.Text.Json;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>
/// Writes a CTRF report (Common Test Report Format, https://ctrf.io): one JSON document that CTRF tools (GitHub test
/// reporters, dashboards, AI summaries) read whatever framework produced it. Unlike JUnit XML it keeps retries and flaky
/// runs: a story that passed on a later attempt is <c>passed</c> with <c>flaky: true</c> and its <c>retries</c>.
/// Quarantined failures are reported as <c>skipped</c> (as in the JUnit report) with <c>rawStatus: "quarantined"</c>.
/// </summary>
public static class CtrfReportWriter
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    /// <summary>Writes the report to <paramref name="path"/> (directories are created).</summary>
    public static void Write(RunSummary summary, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, Create(summary));
    }

    /// <summary>The report as JSON.</summary>
    public static string Create(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var results = summary.Results;
        var start = results.Count == 0 ? DateTimeOffset.UtcNow : results.Min(r => r.StartedAt);
        var stop = results.Count == 0 ? start : results.Max(r => r.StartedAt.AddMilliseconds(r.DurationMilliseconds));

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Indented))
        {
            writer.WriteStartObject();
            writer.WriteString("reportFormat", "CTRF");
            writer.WriteString("specVersion", "0.0.0");
            writer.WriteString("generatedBy", "BlazorTestPlatform");
            writer.WriteStartObject("results");

            writer.WriteStartObject("tool");
            writer.WriteString("name", "BlazorTestPlatform");
            writer.WriteString("version", ToolVersion());
            writer.WriteEndObject();

            writer.WriteStartObject("summary");
            writer.WriteNumber("tests", results.Count);
            writer.WriteNumber("passed", results.Count(r => Status(r) == "passed"));
            writer.WriteNumber("failed", results.Count(r => Status(r) == "failed"));
            writer.WriteNumber("pending", 0);
            writer.WriteNumber("skipped", results.Count(r => Status(r) == "skipped"));
            writer.WriteNumber("other", 0);
            writer.WriteNumber("flaky", results.Count(r => r.Outcome == StoryOutcome.Flaky));
            writer.WriteNumber("start", start.ToUnixTimeMilliseconds());
            writer.WriteNumber("stop", stop.ToUnixTimeMilliseconds());
            writer.WriteEndObject();

            writer.WriteStartArray("tests");
            foreach (var result in results)
            {
                WriteTest(writer, result);
            }

            writer.WriteEndArray();

            writer.WriteStartObject("environment");
            writer.WriteString("appName", "BlazorTestPlatform");
            writer.WriteString("osPlatform", Environment.OSVersion.Platform.ToString());
            writer.WriteString("osRelease", Environment.OSVersion.VersionString);
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteTest(Utf8JsonWriter writer, StoryResult result)
    {
        writer.WriteStartObject();
        writer.WriteString("name", $"{result.Title}/{result.Name} [{Cell(result)}]");
        writer.WriteString("status", Status(result));
        writer.WriteNumber("duration", Math.Round(result.DurationMilliseconds));
        writer.WriteNumber("start", result.StartedAt.ToUnixTimeMilliseconds());
        writer.WriteNumber("stop", result.StartedAt.AddMilliseconds(result.DurationMilliseconds).ToUnixTimeMilliseconds());
        writer.WriteString("suite", result.Title);
        writer.WriteString("rawStatus", result.Outcome == StoryOutcome.Failed && result.Quarantine is not null ? "quarantined" : result.Outcome.ToString().ToLowerInvariant());
        writer.WriteString("type", result.HostingModel is null or "memory" ? "component" : "browser");

        var message = result.Outcome switch
        {
            StoryOutcome.Failed when result.Quarantine is { } quarantine => $"Quarantined ({quarantine.Reason}): {result.Error}",
            StoryOutcome.Failed => result.Error,
            StoryOutcome.Skipped => result.SkipReason,
            StoryOutcome.Flaky => $"Passed after {result.Attempts} attempts: {string.Join(" | ", result.AttemptErrors)}",
            _ => null,
        };
        if (message is not null)
        {
            writer.WriteString("message", message);
        }

        if (result.StackTrace is { } trace)
        {
            writer.WriteString("trace", trace);
        }

        if (result.SourceFile is { } file)
        {
            writer.WriteString("filePath", file);
        }

        if (result.SourceLine is { } line)
        {
            writer.WriteNumber("line", line);
        }

        writer.WriteNumber("retries", Math.Max(0, result.Attempts - 1));
        writer.WriteBoolean("flaky", result.Outcome == StoryOutcome.Flaky);
        if (result.Driver == "playwright" || result.Key.Contains("|chromium|", StringComparison.Ordinal) || result.Key.Contains("|firefox|", StringComparison.Ordinal) || result.Key.Contains("|webkit|", StringComparison.Ordinal))
        {
            var browser = result.Key.Split('|').FirstOrDefault(p => p is "chromium" or "firefox" or "webkit");
            if (browser is not null)
            {
                writer.WriteString("browser", browser);
            }
        }

        writer.WriteStartArray("tags");
        foreach (var tag in new[] { result.HostingModel, result.Viewport, result.Theme }.OfType<string>())
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();

        if (result.Artifacts.Count > 0 || result.Visual is { DiffPath: not null })
        {
            writer.WriteStartArray("attachments");
            foreach (var artifact in result.Artifacts)
            {
                writer.WriteStartObject();
                writer.WriteString("name", artifact.Description ?? artifact.Kind);
                writer.WriteString("contentType", StoryFile.ContentTypeOf(artifact.Path));
                writer.WriteString("path", artifact.Path);
                writer.WriteEndObject();
            }

            if (result.Visual is { DiffPath: { } diff } && !result.Artifacts.Any(a => string.Equals(a.Path, diff, StringComparison.OrdinalIgnoreCase)))
            {
                writer.WriteStartObject();
                writer.WriteString("name", "Visual difference");
                writer.WriteString("contentType", "image/png");
                writer.WriteString("path", diff);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (result.Steps.Count > 0)
        {
            writer.WriteStartArray("steps");
            foreach (var step in result.Steps)
            {
                writer.WriteStartObject();
                writer.WriteString("name", step.Title);
                writer.WriteString("status", step.Failed ? "failed" : "passed");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteStartObject("extra");
        writer.WriteString("key", result.Key);
        writer.WriteString("storyId", result.StoryId);
        writer.WriteString("driver", result.Driver);
        if (result.Visual is { } visual)
        {
            writer.WriteString("visual", visual.Status.ToString());
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string Status(StoryResult result) => result.Outcome switch
    {
        StoryOutcome.Passed or StoryOutcome.Flaky => "passed",
        StoryOutcome.Failed when result.Quarantine is not null => "skipped",
        StoryOutcome.Failed => "failed",
        _ => "skipped",
    };

    private static string Cell(StoryResult result) => string.Join(", ", new[] { result.Driver, result.HostingModel, result.Viewport, result.Theme }.OfType<string>());

    private static string ToolVersion()
    {
        var version = typeof(CtrfReportWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
