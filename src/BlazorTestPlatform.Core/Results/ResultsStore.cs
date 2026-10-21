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
        var temp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, BtpJson.Serialize(result));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Reads every result.</summary>
    public IReadOnlyList<StoryResult> ReadAll()
    {
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
                    results.Add(result);
                }
            }
            catch (JsonException)
            {
                // A partially written file from a crashed run must not break reporting.
            }
        }

        return results;
    }

    /// <summary>Deletes previous results and artifacts.</summary>
    public void Clear()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
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
        if (name.Length > 150)
        {
            name = name[..120] + "~" + DeterministicRandom.StableHash(key).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }
}
