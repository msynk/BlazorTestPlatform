using System.Globalization;

namespace BlazorTestPlatform;

/// <summary>One shard of a <see cref="ShardPlan"/>: the keys it runs and how long they are expected to take.</summary>
/// <param name="Index">1-based shard number.</param>
/// <param name="Keys">Keys assigned to the shard, longest first.</param>
/// <param name="EstimatedMilliseconds">Sum of the keys' estimated durations.</param>
public sealed record PlannedShard(int Index, IReadOnlyList<string> Keys, double EstimatedMilliseconds);

/// <summary>
/// Duration-balanced sharding: splits work across shards so they finish together, from durations measured in earlier
/// runs. <see cref="Shard"/> splits by a stable hash, which keeps assignments fixed but lets one shard collect the slow
/// cells; a plan built from history evens out wall-clock time instead.
/// </summary>
public sealed class ShardPlan
{
    private ShardPlan(IReadOnlyList<PlannedShard> shards, IReadOnlyList<string> estimated)
    {
        Shards = shards;
        EstimatedWithoutHistory = estimated;
    }

    /// <summary>The shards, by index.</summary>
    public IReadOnlyList<PlannedShard> Shards { get; }

    /// <summary>Keys without a measured duration, estimated at the median of the others.</summary>
    public IReadOnlyList<string> EstimatedWithoutHistory { get; }

    /// <summary>Expected wall-clock time: the slowest shard.</summary>
    public double MakespanMilliseconds => Shards.Count == 0 ? 0 : Shards.Max(s => s.EstimatedMilliseconds);

    /// <summary>Total work across shards.</summary>
    public double TotalMilliseconds => Shards.Sum(s => s.EstimatedMilliseconds);

    /// <summary>How evenly the work is spread: 1 when every shard takes as long as the slowest.</summary>
    public double Balance => MakespanMilliseconds == 0 ? 1 : TotalMilliseconds / (Shards.Count * MakespanMilliseconds);

    /// <summary>
    /// Plans <paramref name="shardCount"/> shards with the longest-processing-time-first rule: keys sorted by duration,
    /// each assigned to the shard that is currently least loaded. Deterministic: ties break by key.
    /// </summary>
    /// <param name="keys">Keys to run.</param>
    /// <param name="durations">Measured duration per key, in milliseconds; missing keys get the median.</param>
    /// <param name="shardCount">Number of shards (at least 1).</param>
    public static ShardPlan Create(IEnumerable<string> keys, IReadOnlyDictionary<string, double> durations, int shardCount)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(durations);
        ArgumentOutOfRangeException.ThrowIfLessThan(shardCount, 1);

        var distinct = keys.Distinct(StringComparer.Ordinal).ToArray();
        var known = distinct.Where(durations.ContainsKey).Select(k => durations[k]).Order().ToArray();
        var median = known.Length == 0 ? 1000 : known[known.Length / 2];
        var unknown = distinct.Where(k => !durations.ContainsKey(k)).ToArray();

        var loads = new double[shardCount];
        var assigned = Enumerable.Range(0, shardCount).Select(_ => new List<string>()).ToArray();
        foreach (var (key, duration) in distinct
            .Select(k => (Key: k, Duration: durations.TryGetValue(k, out var d) ? d : median))
            .OrderByDescending(x => x.Duration)
            .ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            var target = 0;
            for (var i = 1; i < shardCount; i++)
            {
                if (loads[i] < loads[target])
                {
                    target = i;
                }
            }

            loads[target] += duration;
            assigned[target].Add(key);
        }

        return new ShardPlan(
            Enumerable.Range(0, shardCount).Select(i => new PlannedShard(i + 1, assigned[i], loads[i])).ToArray(),
            unknown);
    }

    /// <summary>The shard that runs <paramref name="key"/>, or null when the plan does not contain it.</summary>
    public PlannedShard? ShardOf(string key) => Shards.FirstOrDefault(s => s.Keys.Contains(key, StringComparer.Ordinal));

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Shards.Count} shards, makespan {MakespanMilliseconds / 1000:0.#} s, balance {Balance:P0}");
}
