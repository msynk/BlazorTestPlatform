using System.Collections.Concurrent;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Bunit;

/// <summary>Parses stylesheets once per distinct content (stories share the same design-system CSS).</summary>
internal static class StylesheetCache
{
    private static readonly ConcurrentDictionary<string, CssVisibility> Cache = new(StringComparer.Ordinal);

    public static CssVisibility? Resolve(IReadOnlyList<string> inline, IReadOnlyList<string> files, MediaEnvironment environment)
    {
        if (inline.Count == 0 && files.Count == 0)
        {
            return null;
        }

        // A configured stylesheet that does not exist is a configuration error: skipping it would silently show
        // elements its rules hide.
        var missing = files.FirstOrDefault(f => !File.Exists(f));
        if (missing is not null)
        {
            throw new FileNotFoundException($"The stylesheet '{missing}' configured for bUnit (bunit.stylesheets in btp.json, or BunitStoryOptions.AddStylesheetFile) does not exist. Build the project that produces it, or fix the path.", missing);
        }

        var sheets = inline.Concat(files.Select(File.ReadAllText)).ToArray();
        var key = string.Join('\u0001', sheets.Select(s => DeterministicRandom.StableHash(s).ToString("x16", System.Globalization.CultureInfo.InvariantCulture)));
        return Cache.GetOrAdd(key, _ => CssVisibility.Parse(sheets)).For(environment);
    }
}
