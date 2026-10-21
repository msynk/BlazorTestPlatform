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

        var sheets = inline.Concat(files.Where(File.Exists).Select(File.ReadAllText)).ToArray();
        var key = string.Join('\u0001', sheets.Select(s => DeterministicRandom.StableHash(s).ToString("x16", System.Globalization.CultureInfo.InvariantCulture)));
        return Cache.GetOrAdd(key, _ => CssVisibility.Parse(sheets)).For(environment);
    }
}
