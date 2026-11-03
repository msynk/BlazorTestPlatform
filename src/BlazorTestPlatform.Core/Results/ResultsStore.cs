using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BlazorTestPlatform.Results;

/// <summary>Shared JSON settings for every file BlazorTestPlatform reads or writes.</summary>
public static class BtpJson
{
    /// <summary>camelCase, indented, enums as strings, nulls omitted.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes with source-generated metadata where available (trimming safe for BlazorTestPlatform's own types).</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>());

    /// <summary>Deserializes with source-generated metadata where available.</summary>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, TypeInfo<T>());

    /// <summary>Deserializes a stream with source-generated metadata where available.</summary>
    public static T? Deserialize<T>(Stream json) => JsonSerializer.Deserialize(json, TypeInfo<T>());

    /// <summary>Type metadata from <see cref="Options"/>.</summary>
    public static JsonTypeInfo<T> TypeInfo<T>() => Options.TypeInfo<T>();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            TypeInfoResolver = JsonResolvers.Create(BtpJsonContext.Default),
        };
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>
/// Writes one JSON file per <see cref="StoryResult"/> into <c>{results}/results/</c>. Files are independent,
/// so parallel tests (and parallel test projects) never contend; the CLI and HTML report aggregate them.
/// </summary>
public sealed class ResultsStore
{
    /// <summary>Creates a store rooted at <paramref name="directory"/>.</summary>
    public ResultsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    /// <summary>Root directory.</summary>
    public string Directory { get; }

    /// <summary>Directory holding per-result JSON files.</summary>
    public string ResultsDirectory => Path.Combine(Directory, "results");

    /// <summary>Directory for artifacts of a result key.</summary>
    public string ArtifactsDirectoryFor(string key) => Path.Combine(Directory, "artifacts", SafeFileName(key));

    /// <summary>Writes (or overwrites) a result.</summary>
    public string Write(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        System.IO.Directory.CreateDirectory(ResultsDirectory);
        var path = Path.Combine(ResultsDirectory, SafeFileName(result.Key) + ".json");
        var temp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
        File.WriteAllText(temp, BtpJson.Serialize(result));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Reads every result. Files that cannot be read are skipped; use the other overload to learn which.</summary>
    public IReadOnlyList<StoryResult> ReadAll() => ReadAll(out _);

    /// <summary>
    /// Reads every result, and lists the files that could not be read (a crashed run's partial file, a result written by
    /// an incompatible version). Callers that gate on results should treat unreadable files as failures: a failed story
    /// whose result cannot be read must not let the run pass.
    /// </summary>
    public IReadOnlyList<StoryResult> ReadAll(out IReadOnlyList<string> unreadable)
    {
        var problems = new List<string>();
        unreadable = problems;
        if (!System.IO.Directory.Exists(ResultsDirectory))
        {
            return [];
        }

        var results = new List<StoryResult>();
        foreach (var file in System.IO.Directory.EnumerateFiles(ResultsDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (BtpJson.Deserialize<StoryResult>(File.ReadAllText(file)) is { } result)
                {
                    if (result.SchemaVersion > StoryResult.CurrentSchemaVersion)
                    {
                        problems.Add($"{file}: written by a newer version (result schema {result.SchemaVersion}; this version reads up to {StoryResult.CurrentSchemaVersion})");
                        continue;
                    }

                    results.Add(result);
                }
                else
                {
                    problems.Add($"{file}: empty result");
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                problems.Add($"{file}: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>Deletes previous results and artifacts.</summary>
    /// <exception cref="InvalidOperationException">
    /// The directory does not look like a results directory: it is a drive root or a git repository, contains the
    /// current directory, or holds project, solution or <c>btp.json</c> files. A misconfigured results path must never
    /// delete sources.
    /// </exception>
    public void Clear()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        if (ProtectedReason(Directory) is { } reason)
        {
            throw new InvalidOperationException(
                $"Refusing to delete '{Directory}': {reason}. Point the results directory (BTP_RESULTS_DIR or btp.json resultsDirectory) at a folder that only holds test output.");
        }

        System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static readonly string[] SourceMarkers = ["*.csproj", "*.fsproj", "*.vbproj", "*.sln", "*.slnx", "btp.json", "Directory.Build.props", "global.json"];

    /// <summary>Why <paramref name="directory"/> must not be deleted recursively, or null when it may.</summary>
    internal static string? ProtectedReason(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Path.GetPathRoot(full) is { Length: > 0 } root && string.Equals(full, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            return "it is a drive root";
        }

        if (System.IO.Directory.Exists(Path.Combine(full, ".git")) || File.Exists(Path.Combine(full, ".git")))
        {
            return "it is a git repository";
        }

        foreach (var marker in SourceMarkers)
        {
            if (System.IO.Directory.EnumerateFiles(full, marker, SearchOption.TopDirectoryOnly).FirstOrDefault() is { } file)
            {
                return $"it contains {Path.GetFileName(file)}";
            }
        }

        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.CurrentDirectory));
        if (string.Equals(current, full, StringComparison.OrdinalIgnoreCase)
            || current.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "it contains the current directory";
        }

        return null;
    }

    /// <summary>Converts a key into a portable file name.</summary>
    public static string SafeFileName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(key.Length);
        foreach (var ch in key)
        {
            builder.Append(ch == '|' ? '~' : invalid.Contains(ch) || ch is '/' or '\\' or ':' ? '_' : ch);
        }

        var name = builder.ToString();
        if (name.Trim('.').Length == 0)
        {
            // "." and ".." are not file names: they would address the directory or its parent.
            name = name.Replace('.', '_');
        }

        if (name.Length > 150)
        {
            name = name[..120] + "~" + DeterministicRandom.StableHash(key).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }
}
