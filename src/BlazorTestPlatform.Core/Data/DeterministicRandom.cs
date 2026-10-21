using System.Text;

namespace BlazorTestPlatform;

/// <summary>
/// A seeded pseudo-random generator (xoshiro256**) whose sequence is identical on every OS, runtime version
/// and process. Unlike <see cref="Random"/>, the algorithm is part of this library's contract, so seeded test
/// data never silently changes after a .NET upgrade.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Creates a generator for the given seed.</summary>
    public DeterministicRandom(long seed)
    {
        Seed = seed;
        var state = unchecked((ulong)seed);
        _s0 = SplitMix64(ref state);
        _s1 = SplitMix64(ref state);
        _s2 = SplitMix64(ref state);
        _s3 = SplitMix64(ref state);
    }

    /// <summary>The seed this generator was created with.</summary>
    public long Seed { get; }

    /// <summary>
    /// Derives an independent generator for a named purpose. Forking keeps unrelated data stable:
    /// adding a new <c>Fork("orders")</c> call does not shift the values produced by <c>Fork("users")</c>.
    /// </summary>
    public DeterministicRandom Fork(string purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        return new DeterministicRandom(unchecked((long)(StableHash(purpose) ^ (ulong)Seed * 0x9E3779B97F4A7C15UL)));
    }

    /// <summary>Returns the next raw 64-bit value.</summary>
    public ulong NextUInt64()
    {
        var result = RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Returns a non-negative integer.</summary>
    public int Next() => (int)(NextUInt64() >> 33);

    /// <summary>Returns an integer in <c>[0, maxExclusive)</c>.</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)NextBounded((ulong)maxExclusive);
    }

    /// <summary>Returns an integer in <c>[minInclusive, maxExclusive)</c>.</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
        }

        return (int)(minInclusive + (long)NextBounded((ulong)((long)maxExclusive - minInclusive)));
    }

    /// <summary>Returns a double in <c>[0, 1)</c>.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Returns a decimal in <c>[min, max)</c> rounded to <paramref name="decimals"/> places.</summary>
    public decimal NextDecimal(decimal min, decimal max, int decimals = 2) =>
        Math.Round(min + (max - min) * (decimal)NextDouble(), decimals, MidpointRounding.ToZero);

    /// <summary>Returns true with the given probability.</summary>
    public bool NextBool(double probabilityOfTrue = 0.5) => NextDouble() < probabilityOfTrue;

    /// <summary>Picks one element.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
        }

        return items[Next(items.Count)];
    }

    /// <summary>Picks one element.</summary>
    public T Pick<T>(params T[] items) => Pick((IReadOnlyList<T>)items);

    /// <summary>Returns a new list with the elements in a deterministic random order.</summary>
    public IReadOnlyList<T> Shuffle<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>Returns a deterministic (version 4 layout) GUID.</summary>
    public Guid NextGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, NextUInt64());
        BitConverter.TryWriteBytes(bytes[8..], NextUInt64());
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>Returns a point in time in <c>[start, end)</c>, truncated to whole seconds.</summary>
    public DateTimeOffset NextDateTime(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "end must be after start.");
        }

        var seconds = (long)((end - start).TotalSeconds * NextDouble());
        return start.AddSeconds(seconds);
    }

    /// <summary>A stable 64-bit FNV-1a hash (string.GetHashCode is randomized per process).</summary>
    public static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private ulong NextBounded(ulong bound)
    {
        // Lemire's nearly-divisionless method with rejection for an unbiased result.
        var threshold = (0UL - bound) % bound;
        while (true)
        {
            var r = NextUInt64();
            var high = Math.BigMul(r, bound, out var low);
            if (low >= threshold)
            {
                return high;
            }
        }
    }

    private static ulong SplitMix64(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
}
