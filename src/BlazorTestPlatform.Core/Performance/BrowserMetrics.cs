using System.Globalization;

namespace BlazorTestPlatform.Performance;

/// <summary>
/// Runtime metrics a real browser reports for a story run (render, play and captures), from the Performance Timeline
/// API. Browsers support different entry types: a null metric was not reported by this browser (layout shifts and long
/// tasks are Chromium-only; Firefox reports the largest contentful paint; heap size is Chromium-only).
/// </summary>
public sealed record BrowserMetrics
{
    /// <summary>Cumulative layout shift (shifts without recent input), or null when the browser does not report it.</summary>
    public double? CumulativeLayoutShift { get; init; }

    /// <summary>Time of the largest contentful paint since navigation, in milliseconds.</summary>
    public double? LargestContentfulPaintMilliseconds { get; init; }

    /// <summary>Tasks that blocked the main thread for 50 ms or more.</summary>
    public int? LongTasks { get; init; }

    /// <summary>Total duration of <see cref="LongTasks"/>, in milliseconds.</summary>
    public double? LongTaskMilliseconds { get; init; }

    /// <summary>Elements in the story root after the run.</summary>
    public int DomNodes { get; init; }

    /// <summary>Used JavaScript heap, in bytes.</summary>
    public long? JSHeapBytes { get; init; }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"CLS {Show(CumulativeLayoutShift, "0.###")}, LCP {Show(LargestContentfulPaintMilliseconds, "0")} ms, long tasks {Show(LongTasks, "0")} ({Show(LongTaskMilliseconds, "0")} ms), {DomNodes} DOM nodes");

    private static string Show(double? value, string format) => value?.ToString(format, CultureInfo.InvariantCulture) ?? "n/a";

    private static string Show(int? value, string format) => value?.ToString(format, CultureInfo.InvariantCulture) ?? "n/a";
}

/// <summary>
/// Limits for <see cref="BrowserMetrics"/>, checked by the browser runner after the play function. A limit on a metric
/// the browser does not report is not checked (and the run says so in its timeline).
/// </summary>
public sealed class BrowserBudget : StoryOptionsBase
{
    /// <summary>Maximum cumulative layout shift (Core Web Vitals: 0.1 is "good").</summary>
    public double? MaxLayoutShift { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Maximum largest contentful paint (Core Web Vitals: 2.5 s is "good").</summary>
    public TimeSpan? MaxLargestContentfulPaint { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Maximum total time of long tasks (main-thread blocking).</summary>
    public TimeSpan? MaxLongTaskDuration { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Maximum elements in the story root.</summary>
    public int? MaxDomNodes { get; set { ThrowIfReadOnly(); field = value; } }

    internal BrowserBudget Clone() => new()
    {
        MaxLayoutShift = MaxLayoutShift,
        MaxLargestContentfulPaint = MaxLargestContentfulPaint,
        MaxLongTaskDuration = MaxLongTaskDuration,
        MaxDomNodes = MaxDomNodes,
    };

    /// <summary>Exceeded limits, and the limited metrics the browser did not report.</summary>
    public (IReadOnlyList<BudgetViolation> Violations, IReadOnlyList<string> NotReported) Evaluate(BrowserMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var violations = new List<BudgetViolation>();
        var notReported = new List<string>();
        Check("Cumulative layout shift", MaxLayoutShift, metrics.CumulativeLayoutShift);
        Check("Largest contentful paint (ms)", MaxLargestContentfulPaint?.TotalMilliseconds, metrics.LargestContentfulPaintMilliseconds);
        Check("Long tasks (ms)", MaxLongTaskDuration?.TotalMilliseconds, metrics.LongTaskMilliseconds);
        Check("DOM nodes", MaxDomNodes, metrics.DomNodes);
        return (violations, notReported);

        void Check(string metric, double? limit, double? actual)
        {
            if (limit is not { } max)
            {
                return;
            }

            if (actual is not { } value)
            {
                notReported.Add(metric);
            }
            else if (value > max)
            {
                violations.Add(new BudgetViolation(metric, max, value));
            }
        }
    }
}
