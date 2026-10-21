using System.Globalization;

namespace BlazorTestPlatform.Performance;

/// <summary>Absolute limits for a story's render behaviour. Exceeding a limit fails the story.</summary>
public sealed class PerformanceBudget
{
    /// <summary>Maximum renders of the story's root component during render + play.</summary>
    public int? MaxRootRenders { get; set; }

    /// <summary>Maximum renders across every component in the story's tree.</summary>
    public int? MaxTotalRenders { get; set; }

    /// <summary>Maximum managed bytes allocated by the initial render (median of iterations).</summary>
    public long? MaxAllocatedBytes { get; set; }

    /// <summary>Maximum median wall-clock duration of render + play.</summary>
    public TimeSpan? MaxMedianDuration { get; set; }

    /// <summary>Measured iterations (median is reported). Default 5.</summary>
    public int Iterations { get; set; } = 5;

    /// <summary>Warm-up iterations discarded before measuring (JIT, caches). Default 1.</summary>
    public int WarmupIterations { get; set; } = 1;

    /// <summary>Compare against (and record into) the performance baseline file. Default true.</summary>
    public bool TrackBaseline { get; set; } = true;

    internal PerformanceBudget Clone() => (PerformanceBudget)MemberwiseClone();
}

/// <summary>Measured render behaviour of one story.</summary>
public sealed record RenderMetrics
{
    /// <summary>Renders of the root component (the component under test).</summary>
    public int RootRenderCount { get; init; }

    /// <summary>Renders summed across all live components in the tree.</summary>
    public int TotalRenderCount { get; init; }

    /// <summary>Number of live components in the tree at the end of the story.</summary>
    public int ComponentCount { get; init; }

    /// <summary>Renders per component type (full name).</summary>
    public IReadOnlyDictionary<string, int> RendersByComponent { get; init; } = new Dictionary<string, int>();

    /// <summary>Median duration of render + play in milliseconds.</summary>
    public double MedianMilliseconds { get; init; }

    /// <summary>Fastest iteration in milliseconds.</summary>
    public double MinMilliseconds { get; init; }

    /// <summary>Slowest iteration in milliseconds.</summary>
    public double MaxMilliseconds { get; init; }

    /// <summary>Median managed allocations of the initial render, in bytes.</summary>
    public long AllocatedBytes { get; init; }

    /// <summary>Number of measured iterations.</summary>
    public int Iterations { get; init; }

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"root renders={RootRenderCount}, total renders={TotalRenderCount}, components={ComponentCount}, median={MedianMilliseconds:0.###}ms, allocated={AllocatedBytes:N0}B");
}

/// <summary>A budget limit that was exceeded.</summary>
public sealed record BudgetViolation(string Metric, double Limit, double Actual)
{
    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Metric}: {Actual:N0} exceeds budget {Limit:N0}");
}

/// <summary>Evaluates <see cref="RenderMetrics"/> against a <see cref="PerformanceBudget"/>.</summary>
public static class PerformanceBudgetEvaluator
{
    /// <summary>Returns every exceeded limit.</summary>
    public static IReadOnlyList<BudgetViolation> Evaluate(RenderMetrics metrics, PerformanceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(budget);

        var violations = new List<BudgetViolation>();
        if (budget.MaxRootRenders is { } maxRoot && metrics.RootRenderCount > maxRoot)
        {
            violations.Add(new("Root renders", maxRoot, metrics.RootRenderCount));
        }

        if (budget.MaxTotalRenders is { } maxTotal && metrics.TotalRenderCount > maxTotal)
        {
            violations.Add(new("Total renders", maxTotal, metrics.TotalRenderCount));
        }

        if (budget.MaxAllocatedBytes is { } maxBytes && metrics.AllocatedBytes > maxBytes)
        {
            violations.Add(new("Allocated bytes", maxBytes, metrics.AllocatedBytes));
        }

        if (budget.MaxMedianDuration is { } maxDuration && metrics.MedianMilliseconds > maxDuration.TotalMilliseconds)
        {
            violations.Add(new("Median duration (ms)", maxDuration.TotalMilliseconds, metrics.MedianMilliseconds));
        }

        return violations;
    }
}
