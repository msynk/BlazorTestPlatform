namespace BlazorTestPlatform;

/// <summary>
/// Combinatorial test design over dimensions of values. <see cref="Pairwise(IReadOnlyList{int}, Func{IReadOnlyList{int}, bool}?)"/>
/// returns a small set of rows in which every pair of values of any two dimensions appears at least once (all-pairs
/// coverage): typically a few dozen rows where the full Cartesian product has thousands. Results are deterministic:
/// the same dimensions always produce the same rows in the same order, on every platform.
/// </summary>
/// <remarks>
/// Rows are arrays of value indices, one per dimension, in the order the dimensions were given. Pairwise rows are
/// generated with the In-Parameter-Order (IPOG) strategy, then completed against an optional constraint.
/// </remarks>
public static class Combinatorics
{
    /// <summary>Most leaves a constraint search visits for one row before it gives up on that row.</summary>
    private const int SearchBudget = 10_000;

    /// <summary>Every combination of values (the full Cartesian product), in odometer order (last dimension fastest).</summary>
    /// <param name="sizes">Number of values of each dimension (each at least 1).</param>
    /// <param name="isValid">Optional constraint: rows for which it returns false are left out.</param>
    public static IReadOnlyList<int[]> CartesianProduct(IReadOnlyList<int> sizes, Func<IReadOnlyList<int>, bool>? isValid = null)
    {
        Validate(sizes);
        var rows = new List<int[]>();
        if (sizes.Count == 0)
        {
            return rows;
        }

        var indices = new int[sizes.Count];
        while (true)
        {
            if (isValid is null || isValid(indices))
            {
                rows.Add((int[])indices.Clone());
            }

            var dimension = sizes.Count - 1;
            while (dimension >= 0 && ++indices[dimension] == sizes[dimension])
            {
                indices[dimension] = 0;
                dimension--;
            }

            if (dimension < 0)
            {
                return rows;
            }
        }
    }

    /// <summary>Number of rows of the full Cartesian product (saturates at <see cref="long.MaxValue"/>).</summary>
    public static long CartesianCount(IReadOnlyList<int> sizes)
    {
        Validate(sizes);
        if (sizes.Count == 0)
        {
            return 0;
        }

        long count = 1;
        foreach (var size in sizes)
        {
            if (count > long.MaxValue / size)
            {
                return long.MaxValue;
            }

            count *= size;
        }

        return count;
    }

    /// <summary>
    /// Rows that cover every pair of values of every two dimensions at least once (2-way / all-pairs coverage).
    /// With fewer than three dimensions this is the full Cartesian product.
    /// </summary>
    /// <param name="sizes">Number of values of each dimension (each at least 1).</param>
    /// <param name="isValid">
    /// Optional constraint on complete rows. Rows it rejects are never returned, and pairs that only occur in rejected
    /// rows are left uncovered (they cannot be rendered anyway).
    /// </param>
    public static IReadOnlyList<int[]> Pairwise(IReadOnlyList<int> sizes, Func<IReadOnlyList<int>, bool>? isValid = null)
    {
        Validate(sizes);
        if (sizes.Count < 3)
        {
            return CartesianProduct(sizes, isValid);
        }

        var rows = Ipog(sizes);
        if (isValid is null)
        {
            foreach (var row in rows)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    if (row[i] < 0)
                    {
                        row[i] = 0;
                    }
                }
            }

            return Distinct(rows);
        }

        return Constrain(sizes, rows, isValid);
    }

    /// <summary>Pairwise rows of values (see <see cref="Pairwise(IReadOnlyList{int}, Func{IReadOnlyList{int}, bool}?)"/>).</summary>
    /// <param name="dimensions">The values of each dimension (each with at least one value).</param>
    /// <param name="isValid">Optional constraint on complete rows.</param>
    public static IReadOnlyList<T[]> Pairwise<T>(IReadOnlyList<IReadOnlyList<T>> dimensions, Func<IReadOnlyList<T>, bool>? isValid = null)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        var sizes = dimensions.Select(d => (d ?? throw new ArgumentException("Dimensions cannot be null.", nameof(dimensions))).Count).ToArray();
        Func<IReadOnlyList<int>, bool>? valid = isValid is null ? null : row => isValid(Map(dimensions, row));
        return Pairwise(sizes, valid).Select(row => Map(dimensions, row)).ToArray();
    }

    /// <summary>Every pair of values of two different dimensions that <paramref name="rows"/> do not contain.</summary>
    /// <returns>Tuples of (first dimension, its value, second dimension, its value), first dimension &lt; second.</returns>
    public static IReadOnlyList<(int Dimension1, int Value1, int Dimension2, int Value2)> MissingPairs(IReadOnlyList<int> sizes, IEnumerable<IReadOnlyList<int>> rows)
    {
        Validate(sizes);
        ArgumentNullException.ThrowIfNull(rows);
        var covered = new PairSet(sizes);
        foreach (var row in rows)
        {
            covered.Add(row);
        }

        var missing = new List<(int, int, int, int)>();
        for (var a = 0; a < sizes.Count; a++)
        {
            for (var b = a + 1; b < sizes.Count; b++)
            {
                for (var va = 0; va < sizes[a]; va++)
                {
                    for (var vb = 0; vb < sizes[b]; vb++)
                    {
                        if (!covered.Contains(a, va, b, vb))
                        {
                            missing.Add((a, va, b, vb));
                        }
                    }
                }
            }
        }

        return missing;
    }

    private static T[] Map<T>(IReadOnlyList<IReadOnlyList<T>> dimensions, IReadOnlyList<int> row)
    {
        var values = new T[row.Count];
        for (var i = 0; i < row.Count; i++)
        {
            values[i] = dimensions[i][row[i]];
        }

        return values;
    }

    private static void Validate(IReadOnlyList<int> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        for (var i = 0; i < sizes.Count; i++)
        {
            if (sizes[i] < 1)
            {
                throw new ArgumentException($"Dimension {i} has no values; every dimension needs at least one.", nameof(sizes));
            }
        }
    }

    /// <summary>IPOG for t = 2. Unassigned ("don't care") positions are -1.</summary>
    private static List<int[]> Ipog(IReadOnlyList<int> sizes)
    {
        var n = sizes.Count;

        // Largest dimensions first keeps the result small; ties keep the declared order, so the output is stable.
        var order = Enumerable.Range(0, n).OrderByDescending(i => sizes[i]).ThenBy(i => i).ToArray();
        var rows = new List<int[]>();
        for (var a = 0; a < sizes[order[0]]; a++)
        {
            for (var b = 0; b < sizes[order[1]]; b++)
            {
                var row = Enumerable.Repeat(-1, n).ToArray();
                row[order[0]] = a;
                row[order[1]] = b;
                rows.Add(row);
            }
        }

        for (var k = 2; k < n; k++)
        {
            var dimension = order[k];
            var size = sizes[dimension];

            // covered[j][vj * size + v]: the pair (order[j] = vj, dimension = v) occurs in some row.
            var covered = new bool[k][];
            for (var j = 0; j < k; j++)
            {
                covered[j] = new bool[sizes[order[j]] * size];
            }

            // Horizontal growth: give every existing row the value that covers the most new pairs.
            foreach (var row in rows)
            {
                var best = 0;
                var bestGain = -1;
                for (var v = 0; v < size; v++)
                {
                    var gain = 0;
                    for (var j = 0; j < k; j++)
                    {
                        var vj = row[order[j]];
                        if (vj >= 0 && !covered[j][(vj * size) + v])
                        {
                            gain++;
                        }
                    }

                    if (gain > bestGain)
                    {
                        best = v;
                        bestGain = gain;
                    }
                }

                row[dimension] = best;
                for (var j = 0; j < k; j++)
                {
                    var vj = row[order[j]];
                    if (vj >= 0)
                    {
                        covered[j][(vj * size) + best] = true;
                    }
                }
            }

            // Vertical growth: cover what is left, reusing don't-care positions before adding rows.
            for (var j = 0; j < k; j++)
            {
                var other = order[j];
                for (var vj = 0; vj < sizes[other]; vj++)
                {
                    for (var v = 0; v < size; v++)
                    {
                        if (covered[j][(vj * size) + v])
                        {
                            continue;
                        }

                        var target = rows.FirstOrDefault(r => r[dimension] == v && r[other] < 0);
                        if (target is null)
                        {
                            target = Enumerable.Repeat(-1, n).ToArray();
                            target[dimension] = v;
                            rows.Add(target);
                        }

                        target[other] = vj;
                        covered[j][(vj * size) + v] = true;
                    }
                }
            }
        }

        return rows;
    }

    /// <summary>Completes rows within the constraint, drops impossible ones, then covers the pairs they lost.</summary>
    private static List<int[]> Constrain(IReadOnlyList<int> sizes, List<int[]> candidates, Func<IReadOnlyList<int>, bool> isValid)
    {
        var covered = new PairSet(sizes);
        var result = new List<int[]>();
        foreach (var candidate in candidates)
        {
            if (Complete(sizes, candidate, covered, isValid) is { } row)
            {
                covered.Add(row);
                result.Add(row);
            }
        }

        for (var a = 0; a < sizes.Count; a++)
        {
            for (var b = a + 1; b < sizes.Count; b++)
            {
                for (var va = 0; va < sizes[a]; va++)
                {
                    for (var vb = 0; vb < sizes[b]; vb++)
                    {
                        if (covered.Contains(a, va, b, vb))
                        {
                            continue;
                        }

                        var seed = Enumerable.Repeat(-1, sizes.Count).ToArray();
                        seed[a] = va;
                        seed[b] = vb;
                        if (Complete(sizes, seed, covered, isValid) is { } row)
                        {
                            covered.Add(row);
                            result.Add(row);
                        }
                    }
                }
            }
        }

        return Distinct(result);
    }

    /// <summary>
    /// Fills the -1 positions of <paramref name="partial"/> with a valid assignment, preferring values that cover new
    /// pairs. Depth-first and deterministic; gives up (null) after <see cref="SearchBudget"/> complete rows.
    /// </summary>
    private static int[]? Complete(IReadOnlyList<int> sizes, int[] partial, PairSet covered, Func<IReadOnlyList<int>, bool> isValid)
    {
        var row = (int[])partial.Clone();
        var open = Enumerable.Range(0, row.Length).Where(i => row[i] < 0).ToArray();
        var budget = SearchBudget;
        return Search(0) ? row : null;

        bool Search(int position)
        {
            if (position == open.Length)
            {
                budget--;
                return isValid(row);
            }

            var dimension = open[position];
            var values = Enumerable.Range(0, sizes[dimension])
                .OrderByDescending(v => covered.Gain(row, dimension, v))
                .ThenBy(v => v)
                .ToArray();
            foreach (var value in values)
            {
                row[dimension] = value;
                if (Search(position + 1))
                {
                    return true;
                }

                if (budget <= 0)
                {
                    break;
                }
            }

            row[dimension] = -1;
            return false;
        }
    }

    private static List<int[]> Distinct(List<int[]> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return rows.Where(r => seen.Add(string.Join(',', r))).ToList();
    }

    /// <summary>The value pairs of every two dimensions covered so far.</summary>
    private sealed class PairSet
    {
        private readonly IReadOnlyList<int> _sizes;
        private readonly bool[][][] _covered;

        public PairSet(IReadOnlyList<int> sizes)
        {
            _sizes = sizes;
            _covered = new bool[sizes.Count][][];
            for (var a = 0; a < sizes.Count; a++)
            {
                _covered[a] = new bool[sizes.Count][];
                for (var b = a + 1; b < sizes.Count; b++)
                {
                    _covered[a][b] = new bool[sizes[a] * sizes[b]];
                }
            }
        }

        public bool Contains(int a, int va, int b, int vb) =>
            a < b ? _covered[a][b][(va * _sizes[b]) + vb] : _covered[b][a][(vb * _sizes[a]) + va];

        public void Add(IReadOnlyList<int> row)
        {
            for (var a = 0; a < row.Count; a++)
            {
                for (var b = a + 1; b < row.Count; b++)
                {
                    if (row[a] >= 0 && row[b] >= 0)
                    {
                        _covered[a][b][(row[a] * _sizes[b]) + row[b]] = true;
                    }
                }
            }
        }

        /// <summary>New pairs that setting <paramref name="dimension"/> to <paramref name="value"/> would cover.</summary>
        public int Gain(int[] row, int dimension, int value)
        {
            var gain = 0;
            for (var other = 0; other < row.Length; other++)
            {
                if (other != dimension && row[other] >= 0 && !Contains(dimension, value, other, row[other]))
                {
                    gain++;
                }
            }

            return gain;
        }
    }
}
