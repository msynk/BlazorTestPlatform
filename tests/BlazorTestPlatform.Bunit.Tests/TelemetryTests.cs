using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class TelemetryTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<TelemetryTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Story_runs_are_traced_with_their_steps_and_counted()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BtpTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var runs = new ConcurrentBag<(long Value, string? Outcome)>();
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BtpTelemetry.Name && instrument.Name == "btp.story.runs")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<long>((_, value, tags, _) => runs.Add((value, tags.ToArray().FirstOrDefault(t => t.Key == "btp.outcome").Value as string)));
        meters.Start();

        // Other tests may run the same story in parallel: only spans under this test's own root count.
        using var root = new Activity("test").Start();
        await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--clicks-twice"), Options);
        root.Stop();

        var story = Assert.Single(activities, a => a.OperationName == "btp.story" && a.RootId == root.RootId);
        Assert.Equal("tests-counter--clicks-twice", story.GetTagItem("btp.story.id"));
        Assert.Equal("passed", story.GetTagItem("btp.outcome"));
        Assert.Equal(ActivityStatusCode.Ok, story.Status);
        var steps = activities.Where(a => a.OperationName == "btp.step" && a.RootId == story.RootId).ToArray();
        Assert.Contains(steps, s => s.DisplayName == "play" && s.ParentSpanId == story.SpanId);
        Assert.Contains(steps, s => s.DisplayName.StartsWith("click", StringComparison.Ordinal));
        Assert.Contains(runs, r => r.Value == 1 && r.Outcome == "passed");
    }

    [Fact]
    public async Task Failed_runs_are_traced_as_errors()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BtpTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using var root = new Activity("test").Start();
        await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--failing-play"), Options);
        root.Stop();

        var story = Assert.Single(activities, a => a.OperationName == "btp.story" && a.RootId == root.RootId);
        Assert.Equal(ActivityStatusCode.Error, story.Status);
        Assert.Contains(activities, a => a.OperationName == "btp.step" && a.RootId == story.RootId && a.Status == ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Stories_and_results_know_where_their_collection_is_declared()
    {
        var story = Catalog.Get("tests-counter--default");

        Assert.EndsWith("TestStories.cs", story.SourceFile, StringComparison.Ordinal);
        Assert.True(story.SourceLine > 0);

        var result = await BunitStoryRunner.RunAsync(story, Options);
        Assert.Equal(story.SourceFile, result.SourceFile);
        Assert.Equal(story.SourceLine, result.SourceLine);
    }
}
