using System.Text;
using System.Text.Json;

namespace BlazorTestPlatform.Visual;

/// <summary>The renderer that produced (or is comparing) screenshots: browser, platform and versions.</summary>
/// <param name="Browser">Browser name (<c>chromium</c>, <c>firefox</c>, <c>webkit</c>).</param>
/// <param name="Platform">Platform token (<c>linux</c>, <c>win32</c>, <c>darwin</c>).</param>
/// <param name="Playwright">Microsoft.Playwright version.</param>
/// <param name="BrowserVersion">Browser build, when known.</param>
public sealed record RenderEnvironment(string Browser, string Platform, string Playwright, string? BrowserVersion = null)
{
    /// <summary>Key shared with snapshot file names: <c>{browser}-{platform}</c>.</summary>
    public string Key => $"{Browser}-{Platform}";

    /// <summary>True when both environments render with the same Playwright and browser builds.</summary>
    public bool SameRendererAs(RenderEnvironment other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Playwright, other.Playwright, StringComparison.Ordinal)
            && (BrowserVersion is null || other.BrowserVersion is null || string.Equals(BrowserVersion, other.BrowserVersion, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public override string ToString() => $"Playwright {Playwright}" + (BrowserVersion is null ? string.Empty : $" ({Browser} {BrowserVersion})");
}

/// <summary>
/// <c>{snapshots}/baselines.json</c>: which renderer the committed baselines of each <c>{browser}-{platform}</c> come
/// from. Written when baselines are created or revalidated (<c>BTP_UPDATE_SNAPSHOTS=changed|all</c>), so a mismatch
/// after a Playwright upgrade says so instead of looking like a regression, and CI can tell that the pinned
/// Playwright version and the committed baselines disagree before running a single browser.
/// </summary>
public sealed class BaselineManifest
{
    /// <summary>Manifest file name inside the snapshot directory.</summary>
    public const string FileName = "baselines.json";

    private readonly SortedDictionary<string, RenderEnvironment> _entries = new(StringComparer.Ordinal);

    /// <summary>Entries by <see cref="RenderEnvironment.Key"/>.</summary>
    public IReadOnlyDictionary<string, RenderEnvironment> Entries => _entries;

    /// <summary>The renderer recorded for a key, or null.</summary>
    public RenderEnvironment? Get(string key) => _entries.GetValueOrDefault(key);

    /// <summary>Records <paramref name="environment"/>; returns false when it was already recorded.</summary>
    public bool Set(RenderEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (_entries.TryGetValue(environment.Key, out var existing) && existing == environment)
        {
            return false;
        }

        _entries[environment.Key] = environment;
        return true;
    }

    /// <summary>Loads the manifest of a snapshot directory (empty when absent).</summary>
    public static BaselineManifest Load(string snapshotDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotDirectory);
        var path = Path.Combine(snapshotDirectory, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new BaselineManifest();
    }

    /// <summary>Parses manifest JSON.</summary>
    public static BaselineManifest Parse(string json)
    {
        var manifest = new BaselineManifest();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("renderers", out var renderers) && renderers.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in renderers.EnumerateObject())
            {
                var dash = entry.Name.LastIndexOf('-');
                if (dash <= 0)
                {
                    continue;
                }

                var value = entry.Value;
                manifest._entries[entry.Name] = new RenderEnvironment(
                    entry.Name[..dash],
                    entry.Name[(dash + 1)..],
                    value.TryGetProperty("playwright", out var playwright) ? playwright.GetString() ?? string.Empty : string.Empty,
                    value.TryGetProperty("browserVersion", out var browser) ? browser.GetString() : null);
            }
        }

        return manifest;
    }

    /// <summary>Writes the manifest into a snapshot directory.</summary>
    public void Save(string snapshotDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotDirectory);
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(Path.Combine(snapshotDirectory, FileName), ToJson());
    }

    /// <summary>Serializes the manifest (stable key order, LF line endings).</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteString("$comment", "Renderer that produced the committed baselines, per browser-platform. Maintained by BlazorTestPlatform; regenerate baselines instead of editing it.");
            writer.WriteStartObject("renderers");
            foreach (var (key, environment) in _entries)
            {
                writer.WriteStartObject(key);
                writer.WriteString("playwright", environment.Playwright);
                if (environment.BrowserVersion is not null)
                {
                    writer.WriteString("browserVersion", environment.BrowserVersion);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
