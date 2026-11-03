using System.Globalization;

namespace BlazorTestPlatform;

/// <summary>
/// One slice of a test run split across machines (CI matrix jobs, parallel agents). Every item lands in exactly one
/// shard, chosen from a stable hash of its key, so the split is the same on every machine and does not shift when
/// unrelated stories are added or removed.
/// </summary>
/// <param name="Index">1-based shard number.</param>
/// <param name="Count">Number of shards.</param>
public sealed record Shard(int Index, int Count)
{
    /// <summary>The whole run (shard 1 of 1).</summary>
    public static Shard All { get; } = new(1, 1);

    /// <summary>The shard named by <c>BTP_SHARD</c> (<c>2/4</c>), or <see cref="All"/> when it is not set.</summary>
    public static Shard FromEnvironment() => Parse(Environment.GetEnvironmentVariable("BTP_SHARD")) ?? All;

    /// <summary>Parses <c>index/count</c> (for example <c>2/4</c>); null for empty input.</summary>
    /// <exception cref="FormatException">The text is not <c>index/count</c> with <c>1 &lt;= index &lt;= count</c>.</exception>
    public static Shard? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split('/');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count >= 1 && index >= 1 && index <= count)
        {
            return new Shard(index, count);
        }

        throw new FormatException($"Invalid shard '{text}'. Use index/count with 1 <= index <= count, for example 2/4.");
    }

    /// <summary>True when the item with <paramref name="key"/> belongs to this shard.</summary>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Validate();
        return Count == 1 || (int)(Mix(DeterministicRandom.StableHash(key)) % (ulong)Count) == Index - 1;
    }

    // SplitMix64's finalizer: FNV-1a alone leaves keys that differ in a few trailing characters clustered modulo small counts.
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>The items of <paramref name="items"/> that belong to this shard, in their original order.</summary>
    public IEnumerable<T> Select<T>(IEnumerable<T> items, Func<T, string> key)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        Validate();
        return Count == 1 ? items : items.Where(item => Contains(key(item)));
    }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Index}/{Count}");

    private void Validate()
    {
        if (Count < 1 || Index < 1 || Index > Count)
        {
            throw new InvalidOperationException($"Invalid shard {Index}/{Count}: use 1 <= index <= count.");
        }
    }
}

/// <summary>Sharding helpers for catalogs and matrix cells.</summary>
public static class ShardExtensions
{
    /// <summary>The matrix cells of <paramref name="shard"/> (by <see cref="MatrixCase.Key"/>).</summary>
    public static IEnumerable<MatrixCase> InShard(this IEnumerable<MatrixCase> cases, Shard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        return shard.Select(cases, c => c.Key);
    }

    /// <summary>The stories of <paramref name="shard"/> (by story id).</summary>
    public static StoryCatalog InShard(this StoryCatalog catalog, Shard shard)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(shard);
        return catalog.Where(story => shard.Contains(story.Id));
    }
}
