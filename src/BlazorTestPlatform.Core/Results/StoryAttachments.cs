namespace BlazorTestPlatform.Results;

/// <summary>A file of a story result worth attaching to the test framework's result (IDE, TRX, CI test views).</summary>
/// <param name="Name">Unique, readable name, e.g. <c>diff.png</c> or <c>trace.zip</c>.</param>
/// <param name="Path">Absolute path of an existing file.</param>
/// <param name="MediaType">MIME type, e.g. <c>image/png</c>.</param>
/// <param name="Description">What the file shows.</param>
public sealed record StoryAttachment(string Name, string Path, string MediaType, string? Description);

/// <summary>
/// The evidence of a story run as files for test frameworks: the xUnit, NUnit, MSTest and TUnit packages attach them to
/// the test result, so screenshots, visual diffs, DOM snapshots and Playwright traces appear in Test Explorer, TRX files
/// and Azure DevOps or GitHub test views next to the failure.
/// </summary>
public static class StoryAttachments
{
    /// <summary>Largest file attached (larger evidence stays in the results directory). Default 25 MB.</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>
    /// The existing files of <paramref name="result"/> that explain it, or none for a clean pass: attachments are made for
    /// failed and flaky runs, and for runs whose visual snapshot changed or has no baseline.
    /// </summary>
    public static IReadOnlyList<StoryAttachment> For(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var visualChanged = result.VisualComparisons().Any(v => v.Status is not (VisualStatus.Matched or VisualStatus.Updated));
        var files = new List<(string Kind, string Path, string? Description)>();

        // What the play script attached itself is wanted whatever the outcome.
        foreach (var artifact in result.Artifacts.Where(a => a.Kind == PlayAttachment.ArtifactKind))
        {
            Add(files, artifact.Kind, artifact.Path, artifact.Description);
        }

        if (result.Outcome is StoryOutcome.Passed or StoryOutcome.Skipped && !visualChanged)
        {
            return Attachments(files);
        }

        foreach (var visual in result.VisualComparisons().Where(v => v.Status is not (VisualStatus.Matched or VisualStatus.Updated)))
        {
            Add(files, "baseline", visual.BaselinePath, $"Visual baseline ({visual.SnapshotName})");
            Add(files, "actual", visual.ActualPath, $"Visual snapshot of this run ({visual.SnapshotName})");
            Add(files, "diff", visual.DiffPath, $"Visual difference ({visual.SnapshotName})");
        }

        foreach (var artifact in result.Artifacts)
        {
            Add(files, artifact.Kind, artifact.Path, artifact.Description);
        }

        return Attachments(files);
    }

    private static List<StoryAttachment> Attachments(List<(string Kind, string Path, string? Description)> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attachments = new List<StoryAttachment>();
        foreach (var (kind, path, description) in files)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes)
            {
                continue;
            }

            // Play attachments keep the name the script gave them.
            var name = Unique(names, kind == PlayAttachment.ArtifactKind ? info.Name : $"{kind}{info.Extension.ToLowerInvariant()}");
            attachments.Add(new StoryAttachment(name, info.FullName, StoryFile.ContentTypeOf(info.Name), description));
        }

        return attachments;
    }

    private static void Add(List<(string, string, string?)> files, string kind, string? path, string? description)
    {
        if (!string.IsNullOrEmpty(path) && !files.Exists(f => string.Equals(f.Item2, path, StringComparison.OrdinalIgnoreCase)))
        {
            files.Add((kind, path, description));
        }
    }

    private static string Unique(HashSet<string> names, string name)
    {
        var candidate = name;
        for (var i = 2; !names.Add(candidate); i++)
        {
            candidate = $"{System.IO.Path.GetFileNameWithoutExtension(name)}-{i}{System.IO.Path.GetExtension(name)}";
        }

        return candidate;
    }
}
