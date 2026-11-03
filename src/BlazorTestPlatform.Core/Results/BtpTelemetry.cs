using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BlazorTestPlatform.Results;

/// <summary>
/// OpenTelemetry-compatible instrumentation, built on <see cref="System.Diagnostics"/> so it needs no dependency: every
/// story run is an activity (<c>btp.story</c>) with its steps as child activities (<c>btp.step</c>), and runs are counted
/// and timed by the <c>BlazorTestPlatform</c> meter. Nothing is recorded unless a listener subscribes, for example
/// <c>AddSource(BtpTelemetry.Name)</c> and <c>AddMeter(BtpTelemetry.Name)</c> in an OpenTelemetry SDK setup that exports
/// to Jaeger, Zipkin, Azure Monitor or any OTLP collector.
/// </summary>
public static class BtpTelemetry
{
    /// <summary>The activity source and meter name.</summary>
    public const string Name = "BlazorTestPlatform";

    /// <summary>The source of <c>btp.story</c> and <c>btp.step</c> activities.</summary>
    public static ActivitySource Source { get; } = new(Name, typeof(BtpTelemetry).Assembly.GetName().Version?.ToString());

    /// <summary>The meter of the run metrics.</summary>
    public static Meter Meter { get; } = new(Name, typeof(BtpTelemetry).Assembly.GetName().Version?.ToString());

    /// <summary>Story runs, tagged with <c>btp.outcome</c> and <c>btp.driver</c>.</summary>
    public static Counter<long> StoryRuns { get; } = Meter.CreateCounter<long>("btp.story.runs", unit: "{run}", description: "Story runs, by outcome and driver.");

    /// <summary>Story run durations in milliseconds, tagged with <c>btp.outcome</c> and <c>btp.driver</c>.</summary>
    public static Histogram<double> StoryDuration { get; } = Meter.CreateHistogram<double>("btp.story.duration", unit: "ms", description: "Duration of story runs, retries included.");

    /// <summary>Starts the activity of a story run (null when nobody listens).</summary>
    public static Activity? StartStory(Story story, string driver, string? hostingModel = null, string? viewport = null, string? theme = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        var activity = Source.StartActivity("btp.story", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.DisplayName = $"{story.Id} [{driver}]";
        activity.SetTag("btp.story.id", story.Id);
        activity.SetTag("btp.story.component", story.ComponentType.FullName);
        activity.SetTag("btp.driver", driver);
        activity.SetTag("btp.hosting_model", hostingModel);
        activity.SetTag("btp.viewport", viewport);
        activity.SetTag("btp.theme", theme);
        return activity;
    }

    /// <summary>Records a finished run: activity status and tags, counter and duration histogram.</summary>
    public static void Complete(Activity? activity, StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var outcome = result.Outcome.ToString().ToLowerInvariant();
        StoryRuns.Add(1, new KeyValuePair<string, object?>("btp.outcome", outcome), new KeyValuePair<string, object?>("btp.driver", result.Driver));
        StoryDuration.Record(result.DurationMilliseconds, new KeyValuePair<string, object?>("btp.outcome", outcome), new KeyValuePair<string, object?>("btp.driver", result.Driver));
        if (activity is null)
        {
            return;
        }

        activity.SetTag("btp.outcome", outcome);
        activity.SetTag("btp.attempts", result.Attempts);
        if (result.Outcome == StoryOutcome.Failed)
        {
            activity.SetStatus(ActivityStatusCode.Error, result.Error);
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
