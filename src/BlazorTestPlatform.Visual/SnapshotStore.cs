using System.Runtime.InteropServices;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Visual;

/// <summary>Builds stable snapshot names.</summary>
public static class SnapshotNaming
{
    /// <summary>The current platform token (<c>win32</c>, <c>darwin</c>, <c>linux</c>), matching Playwright conventions.</summary>
    public static string CurrentPlatform =>
        OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "other";

    /// <summary>
    /// Snapshot name for a matrix cell: <c>{story-id}/{viewport}.{theme}.{browser}-{platform}</c>.
    /// The hosting model is deliberately excluded: Server, WebAssembly and SSR renders must look identical,
    /// so all of them are compared against the same baseline — which is how cross-hosting regressions surface.
    /// </summary>
    public static string For(string storyId, string viewport, string theme, string browser, string? platform = null, string? variant = null) =>
        $"{storyId}/{viewport}.{theme}.{browser}-{platform ?? CurrentPlatform}{(variant is null ? string.Empty : "." + StoryId.Slugify(variant))}";
}

/// <summary>
/// Compares screenshots against committed baselines (<c>{snapshots}/{name}.png</c>) and writes actual/diff/expected
/// images into the results directory on mismatch, ready for the HTML report and <c>btp approve</c>.
/// </summary>
public sealed class SnapshotStore
{
    private readonly Lock _gate = new();
    private BaselineManifest? _manifest;

    /// <summary>Creates a store.</summary>
    public SnapshotStore(string baselineDirectory, string outputDirectory, SnapshotUpdateMode updateMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        BaselineDirectory = Path.GetFullPath(baselineDirectory);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        UpdateMode = updateMode;
    }

    /// <summary>Creates a store from settings.</summary>
    public static SnapshotStore FromSettings(BtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SnapshotStore(settings.SnapshotDirectoryPath, Path.Combine(settings.ResultsDirectoryPath, "visual"), settings.UpdateSnapshots);
    }

    /// <summary>Baseline directory.</summary>
    public string BaselineDirectory { get; }

    /// <summary>Where actual/diff/expected images go on mismatch.</summary>
    public string OutputDirectory { get; }

    /// <summary>Update mode.</summary>
    public SnapshotUpdateMode UpdateMode { get; }

    /// <summary>Path of a baseline.</summary>
    public string BaselinePathFor(string snapshotName) => Path.Combine(BaselineDirectory, Normalize(snapshotName) + ".png");

    /// <summary>Compares a PNG screenshot with its baseline.</summary>
    /// <param name="snapshotName">Snapshot name (see <see cref="SnapshotNaming"/>).</param>
    /// <param name="actualPng">The captured PNG.</param>
    /// <param name="options">Comparison options.</param>
    /// <param name="maxDiffPixels">Tolerated differing pixels.</param>
    /// <param name="maxDiffRatio">Tolerated differing ratio.</param>
    /// <param name="environment">
    /// The renderer that took the screenshot. When given, <see cref="BaselineManifest"/> records it for new or revalidated
    /// baselines, and mismatches against baselines from another renderer carry a <see cref="VisualResult.Note"/>.
    /// </param>
    public VisualResult Compare(string snapshotName, byte[] actualPng, ImageCompareOptions? options = null, int maxDiffPixels = 0, double maxDiffRatio = 0, RenderEnvironment? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        ArgumentNullException.ThrowIfNull(actualPng);
        var baselinePath = BaselinePathFor(snapshotName);
        var actual = RgbaImage.FromPng(actualPng);
        var output = Path.Combine(OutputDirectory, Normalize(snapshotName));

        lock (_gate)
        {
            if (!File.Exists(baselinePath))
            {
                if (UpdateMode == SnapshotUpdateMode.None)
                {
                    var actualPath = WriteFile(output + ".actual.png", actualPng);
                    return Result(snapshotName, VisualStatus.MissingBaseline, baselinePath, actual, actualPath: actualPath);
                }

                WriteFile(baselinePath, actualPng);
                Record(environment, revalidated: false);
                return Result(snapshotName, VisualStatus.NewBaseline, baselinePath, actual);
            }

            var baselineBytes = File.ReadAllBytes(baselinePath);
            if (baselineBytes.AsSpan().SequenceEqual(actualPng))
            {
                CleanOutputs(output);
                Record(environment, revalidated: UpdateMode is SnapshotUpdateMode.Changed or SnapshotUpdateMode.All);
                return Result(snapshotName, VisualStatus.Matched, baselinePath, actual);
            }

            var baseline = RgbaImage.FromPng(baselineBytes);
            var comparison = ImageComparer.Compare(baseline, actual, options);
            var withinTolerance = !comparison.SizeMismatch
                && (comparison.DiffPixels <= maxDiffPixels || (maxDiffRatio > 0 && comparison.DiffRatio <= maxDiffRatio));

            if (UpdateMode == SnapshotUpdateMode.All || (UpdateMode == SnapshotUpdateMode.Changed && !withinTolerance))
            {
                WriteFile(baselinePath, actualPng);
                CleanOutputs(output);
                Record(environment, revalidated: true);
                return Result(snapshotName, VisualStatus.Updated, baselinePath, actual, comparison);
            }

            if (withinTolerance)
            {
                CleanOutputs(output);
                Record(environment, revalidated: UpdateMode is SnapshotUpdateMode.Changed or SnapshotUpdateMode.All);
                return Result(snapshotName, VisualStatus.Matched, baselinePath, actual, comparison);
            }

            WriteFile(output + ".expected.png", baselineBytes);
            var actualFile = WriteFile(output + ".actual.png", actualPng);
            var diffPath = WriteFile(output + ".diff.png", comparison.DiffImage!.ToPng());
            return Result(
                snapshotName,
                comparison.SizeMismatch ? VisualStatus.SizeMismatch : VisualStatus.Mismatched,
                baselinePath,
                actual,
                comparison,
                actualFile,
                diffPath) with { Note = RendererNote(environment) };
        }
    }

    /// <summary>The renderer recorded for <paramref name="key"/> (<c>{browser}-{platform}</c>), or null.</summary>
    public RenderEnvironment? RecordedRenderer(string key)
    {
        lock (_gate)
        {
            return Manifest.Get(key);
        }
    }

    private BaselineManifest Manifest => _manifest ??= BaselineManifest.Load(BaselineDirectory);

    // A full regeneration (changed/all) proves every baseline it touched renders this way; "missing" only claims an
    // empty slot, so adding one new story after an upgrade does not hide that the other baselines are older.
    private void Record(RenderEnvironment? environment, bool revalidated)
    {
        if (environment is null || UpdateMode == SnapshotUpdateMode.None || (!revalidated && Manifest.Get(environment.Key) is not null))
        {
            return;
        }

        if (Manifest.Set(environment))
        {
            Manifest.Save(BaselineDirectory);
        }
    }

    private string? RendererNote(RenderEnvironment? environment)
    {
        if (environment is null || Manifest.Get(environment.Key) is not { } recorded || recorded.SameRendererAs(environment))
        {
            return null;
        }

        return $"Baselines for {environment.Key} were recorded with {recorded}; this run uses {environment}. Browser upgrades change "
            + "text rendering and anti-aliasing, so regenerate them (scripts/visual-tests.sh --update=changed, or BTP_UPDATE_SNAPSHOTS=changed) and review the diffs.";
    }

    /// <summary>Accepts an actual image as the new baseline (used by <c>btp approve</c>).</summary>
    public static void Approve(VisualResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ActualPath is null || result.BaselinePath is null || !File.Exists(result.ActualPath))
        {
            throw new InvalidOperationException($"Snapshot '{result.SnapshotName}' has no actual image to approve.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(result.BaselinePath)!);
        File.Copy(result.ActualPath, result.BaselinePath, overwrite: true);
    }

    private static VisualResult Result(
        string name,
        VisualStatus status,
        string baselinePath,
        RgbaImage actual,
        ImageCompareResult? comparison = null,
        string? actualPath = null,
        string? diffPath = null) => new()
        {
            SnapshotName = name,
            Status = status,
            BaselinePath = baselinePath,
            ActualPath = actualPath,
            DiffPath = diffPath,
            DiffPixels = comparison?.DiffPixels ?? 0,
            DiffRatio = comparison?.DiffRatio ?? 0,
            Width = actual.Width,
            Height = actual.Height,
        };

    private static string WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void CleanOutputs(string outputPrefix)
    {
        foreach (var suffix in new[] { ".actual.png", ".diff.png", ".expected.png" })
        {
            var path = outputPrefix + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string Normalize(string name) => name.Replace('\\', '/').Trim('/').Replace('/', Path.DirectorySeparatorChar);
}
