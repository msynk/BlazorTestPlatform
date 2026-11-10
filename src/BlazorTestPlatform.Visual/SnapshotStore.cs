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
    // One lock per baseline directory, shared by every store instance in the process: runners, tests and the CLI can
    // each create their own store for the same directory.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lock> Gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate;
    private BaselineManifest? _manifest;

    // The last baseline write per path, with what was there before it (null: nothing), so a run that fails afterwards
    // can undo it.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]?> _written = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a store.</summary>
    public SnapshotStore(string baselineDirectory, string outputDirectory, SnapshotUpdateMode updateMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        BaselineDirectory = Path.GetFullPath(baselineDirectory);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        UpdateMode = updateMode;
        _gate = Gates.GetOrAdd(BaselineDirectory, _ => new Lock());
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
    public VisualResult Compare(string snapshotName, byte[] actualPng, ImageCompareOptions? options = null, int maxDiffPixels = 0, double maxDiffRatio = 0, RenderEnvironment? environment = null) =>
        Compare(snapshotName, actualPng, options, maxDiffPixels, maxDiffRatio, environment, outputQualifier: null);

    /// <summary>Compares a PNG screenshot with its baseline, keeping the evidence of each run separate.</summary>
    /// <param name="snapshotName">Snapshot name (see <see cref="SnapshotNaming"/>).</param>
    /// <param name="actualPng">The captured PNG.</param>
    /// <param name="options">Comparison options.</param>
    /// <param name="maxDiffPixels">Tolerated differing pixels.</param>
    /// <param name="maxDiffRatio">Tolerated differing ratio.</param>
    /// <param name="environment">The renderer that took the screenshot (see the other overload).</param>
    /// <param name="outputQualifier">
    /// Distinguishes runs that share a baseline (for example <c>wasm.attempt-1</c>): their actual, expected and diff images
    /// get separate names, so a later matching render mode or a passing retry never deletes the evidence of an earlier
    /// mismatch. Null keeps one set of outputs per snapshot.
    /// </param>
    public VisualResult Compare(string snapshotName, byte[] actualPng, ImageCompareOptions? options, int maxDiffPixels, double maxDiffRatio, RenderEnvironment? environment, string? outputQualifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        ArgumentNullException.ThrowIfNull(actualPng);
        var baselinePath = BaselinePathFor(snapshotName);
        var actual = RgbaImage.FromPng(actualPng);
        var output = Path.Combine(OutputDirectory, Normalize(snapshotName)) + (string.IsNullOrWhiteSpace(outputQualifier) ? string.Empty : "." + StoryId.Slugify(outputQualifier));
        var result = Compare(snapshotName, actualPng, actual, baselinePath, output, options, maxDiffPixels, maxDiffRatio, environment);
        return environment is null ? result : result with { RendererKey = environment.Key, PlaywrightVersion = environment.Playwright, BrowserVersion = environment.BrowserVersion };
    }

    private VisualResult Compare(string snapshotName, byte[] actualPng, RgbaImage actual, string baselinePath, string output, ImageCompareOptions? options, int maxDiffPixels, double maxDiffRatio, RenderEnvironment? environment)
    {
        lock (_gate)
        {
            if (!File.Exists(baselinePath))
            {
                if (UpdateMode == SnapshotUpdateMode.None)
                {
                    var actualPath = WriteFile(output + ".actual.png", actualPng);
                    return Result(snapshotName, VisualStatus.MissingBaseline, baselinePath, actual, actualPath: actualPath);
                }

                _written[baselinePath] = null;
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
                _written[baselinePath] = baselineBytes;
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

    /// <summary>
    /// Undoes the baseline this store wrote for <paramref name="result"/> (a new or updated baseline): the run that
    /// produced it failed another check (accessibility, console errors...), so its screenshot must not become the
    /// reference. Returns true when something was undone.
    /// </summary>
    public bool Revert(VisualResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (VisualStatus.NewBaseline or VisualStatus.Updated) || result.BaselinePath is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_written.TryRemove(result.BaselinePath, out var previous))
            {
                return false;
            }

            if (previous is null)
            {
                File.Delete(result.BaselinePath);
            }
            else
            {
                WriteFile(result.BaselinePath, previous);
            }

            return true;
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
        if (environment is null || UpdateMode == SnapshotUpdateMode.None)
        {
            return;
        }

        // Re-read under the directory lock: another store instance may have recorded a renderer since this one loaded.
        var manifest = _manifest = BaselineManifest.Load(BaselineDirectory);
        if ((revalidated || manifest.Get(environment.Key) is null) && manifest.Set(environment))
        {
            manifest.Save(BaselineDirectory);
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

    /// <summary>
    /// Accepts an actual image as the new baseline (used by <c>btp approve</c>). Approving a changed screenshot also
    /// records its renderer in <see cref="BaselineManifest"/>, so baselines approved after a browser upgrade are
    /// attributed to the new build.
    /// </summary>
    public static void Approve(VisualResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ActualPath is null || result.BaselinePath is null || !File.Exists(result.ActualPath))
        {
            throw new InvalidOperationException($"Snapshot '{result.SnapshotName}' has no actual image to approve.");
        }

        Promote(result, result.ActualPath, result.BaselinePath);
    }

    /// <summary>
    /// Accepts an actual image as the new baseline, trusting nothing in <paramref name="result"/> but the snapshot name:
    /// the baseline is written to <c>{baselineDirectory}/{snapshot}.png</c>, and the actual image must be a PNG inside
    /// <paramref name="resultsDirectory"/>. Results copied from another machine (a CI artifact) work: when the recorded
    /// actual path does not exist, the image is looked up in <c>{resultsDirectory}/visual</c>. Used by <c>btp approve</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No actual image inside the results directory, or it is not a PNG.</exception>
    public static void Approve(VisualResult result, string baselineDirectory, string resultsDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        var results = Path.GetFullPath(resultsDirectory);
        var baseline = Path.Combine(Path.GetFullPath(baselineDirectory), Normalize(result.SnapshotName) + ".png");

        string? actual = null;
        if (result.ActualPath is { } recorded)
        {
            var candidates = new[]
            {
                Path.GetFullPath(recorded),
                Path.Combine(results, "visual", Path.GetDirectoryName(Normalize(result.SnapshotName)) ?? string.Empty, Path.GetFileName(recorded.Replace('\\', '/'))),
            };
            actual = candidates.FirstOrDefault(c => IsInside(c, results) && File.Exists(c));
        }

        if (actual is null)
        {
            throw new InvalidOperationException($"Snapshot '{result.SnapshotName}' has no actual image inside '{results}' to approve.");
        }

        try
        {
            _ = PngCodec.Decode(File.ReadAllBytes(actual));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"'{actual}' is not a valid PNG image: {ex.Message}", ex);
        }

        Promote(result, actual, baseline);
    }

    private static bool IsInside(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void Promote(VisualResult result, string actualPath, string baselinePath)
    {
        WriteFile(baselinePath, File.ReadAllBytes(actualPath));
        result = result with { BaselinePath = baselinePath };

        var relative = Normalize(result.SnapshotName) + ".png";
        if (result is not { RendererKey: { } key, PlaywrightVersion: { } playwright } || key.LastIndexOf('-') is not (> 0 and var dash)
            || !Path.GetFullPath(result.BaselinePath).EndsWith(relative, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Like Record: a reviewed change proves the new renderer, a filled-in missing baseline only claims an empty slot.
        var baselineDirectory = Path.GetFullPath(result.BaselinePath)[..^relative.Length];
        var manifest = BaselineManifest.Load(baselineDirectory);
        if ((result.Status is VisualStatus.Mismatched or VisualStatus.SizeMismatch || manifest.Get(key) is null)
            && manifest.Set(new RenderEnvironment(key[..dash], key[(dash + 1)..], playwright, result.BrowserVersion)))
        {
            manifest.Save(baselineDirectory);
        }
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
            DiffFingerprint = comparison is { DiffPixels: > 0 } ? comparison.Fingerprint : null,
            DiffBounds = comparison is { DiffPixels: > 0, DiffBounds: { } b } ? [b.X, b.Y, b.Width, b.Height] : null,
            Width = actual.Width,
            Height = actual.Height,
        };

    /// <summary>Writes through a temporary file, so a crash never leaves a truncated baseline or output.</summary>
    private static string WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
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

    /// <summary>The relative path of a snapshot name. Rejects rooted names and <c>.</c>/<c>..</c> segments.</summary>
    private static string Normalize(string name)
    {
        var segments = name.Replace('\\', '/').Trim('/').Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains(':', StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invalid snapshot name '{name}': it must be a relative path without '.', '..', ':' or empty segments.", nameof(name));
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }
}
