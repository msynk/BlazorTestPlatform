using System.Collections.Concurrent;
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Hub;

/// <summary>What to run.</summary>
public sealed record HubRunRequest
{
    /// <summary>Stories to run, by id.</summary>
    public required IReadOnlyList<string> StoryIds { get; init; }

    /// <summary>Label shown in the run list.</summary>
    public string Label { get; init; } = "Run from the hub";

    /// <summary>Trigger recorded in the archive.</summary>
    public string Trigger { get; init; } = "hub";

    /// <summary>Records the run as finished at this time instead of now (used to seed history).</summary>
    public DateTimeOffset? RecordAt { get; init; }

    /// <summary>Free-form values the running stories can read through <see cref="HubRunContext.Current"/>.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

/// <summary>The run a story executes in, for stories that need to know (demo stories that vary between runs).</summary>
public sealed class HubRunContext
{
    private static readonly AsyncLocal<HubRunContext?> Ambient = new();

    internal HubRunContext(HubRunRequest request, int sequence)
    {
        Request = request;
        Sequence = sequence;
    }

    /// <summary>The context of the run on this async flow, or null outside hub runs.</summary>
    public static HubRunContext? Current => Ambient.Value;

    /// <summary>The request.</summary>
    public HubRunRequest Request { get; }

    /// <summary>Position of this run in the archive (1 for the first run).</summary>
    public int Sequence { get; }

    internal static IDisposable Enter(HubRunContext context)
    {
        var previous = Ambient.Value;
        Ambient.Value = context;
        return new Restore(previous);
    }

    private sealed class Restore(HubRunContext? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

/// <summary>Live state of a hub run.</summary>
public sealed class HubRunState
{
    private readonly ConcurrentQueue<StoryResult> _results = new();

    internal HubRunState(string id, HubRunRequest request, int total)
    {
        Id = id;
        Request = request;
        Total = total;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Run id.</summary>
    public string Id { get; }

    /// <summary>The request.</summary>
    public HubRunRequest Request { get; }

    /// <summary>Stories to run.</summary>
    public int Total { get; }

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset? FinishedAt { get; internal set; }

    /// <summary><c>running</c>, <c>passed</c>, <c>failed</c>, <c>cancelled</c> or <c>error</c>.</summary>
    public string Status { get; internal set; } = "running";

    /// <summary>Error that stopped the run.</summary>
    public string? Error { get; internal set; }

    /// <summary>Stories running now.</summary>
    public ConcurrentDictionary<string, DateTimeOffset> Running { get; } = new(StringComparer.Ordinal);

    /// <summary>Finished results, in completion order.</summary>
    public IReadOnlyList<StoryResult> Results => _results.ToArray();

    /// <summary>Finished count.</summary>
    public int Completed => _results.Count;

    internal void Add(StoryResult result) => _results.Enqueue(result);
}

/// <summary>
/// Runs stories in memory (bUnit) from the hub, one run at a time, writing results to the project's results directory
/// and the run to the archive, so the dashboards update as soon as it finishes.
/// </summary>
public sealed class HubRunner : IDisposable
{
    private readonly HubOptions _options;
    private readonly HubStore _store;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates the runner.</summary>
    public HubRunner(HubOptions options, HubStore store)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>The current or last run.</summary>
    public HubRunState? Current { get; private set; }

    /// <summary>Whether a run is in progress.</summary>
    public bool IsRunning => Current is { Status: "running" };

    /// <summary>Whether the hub can run stories (a catalog is configured).</summary>
    public bool CanRun => _store.Catalog.Count > 0;

    /// <summary>Starts a run in the background. Throws when one is already running.</summary>
    public HubRunState Start(HubRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("A run is already in progress.");
            }

            var stories = request.StoryIds.Distinct(StringComparer.Ordinal)
                .Select(id => _store.Catalog.TryGet(id, out var story) ? story : null)
                .OfType<Story>()
                .ToArray();
            var state = new HubRunState($"hub-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}", request, stories.Length);
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            Current = state;
            var token = _cancellation.Token;
            _ = Task.Run(() => RunAsync(state, stories, token), CancellationToken.None);
            return state;
        }
    }

    /// <summary>Runs synchronously to completion (seeding, tests).</summary>
    public async Task<HubRunState> RunAsync(HubRunRequest request, CancellationToken cancellationToken = default)
    {
        var state = Start(request);
        using var registration = cancellationToken.Register(Cancel);
        while (state.Status == "running")
        {
            await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
        }

        return state;
    }

    /// <summary>Cancels the run in progress.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    private async Task RunAsync(HubRunState state, IReadOnlyList<Story> stories, CancellationToken cancellationToken)
    {
        var sequence = _store.Archive.Load().Count + 1;
        using var scope = HubRunContext.Enter(new HubRunContext(state.Request, sequence));
        try
        {
            var settings = _options.LoadSettings();
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(stories, parallel, async (story, token) =>
            {
                state.Running[story.Id] = DateTimeOffset.UtcNow;
                try
                {
                    var result = await BunitStoryRunner.RunAsync(story, new BunitStoryOptions { Settings = settings }, token).ConfigureAwait(false);
                    state.Add(result);
                }
                finally
                {
                    state.Running.TryRemove(story.Id, out _);
                }
            }).ConfigureAwait(false);

            var results = state.Results;
            var info = RunInfo.Detect(state.Id, state.Request.Trigger, _options.ResolvedProjectDirectory) with { Label = state.Request.Label };
            var record = RunRecord.Create(info, results);
            if (state.Request.RecordAt is { } at)
            {
                var length = record.Run.FinishedAt - record.Run.StartedAt;
                record = record with { Run = record.Run with { StartedAt = at - length, FinishedAt = at } };
            }

            _store.Archive.Save(record, _options.ArchiveCapacity);
            state.Status = results.Any(r => r.IsBlockingFailure) ? "failed" : "passed";
        }
        catch (OperationCanceledException)
        {
            state.Status = "cancelled";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            state.Status = "error";
            state.Error = ex.Message;
        }
        finally
        {
            state.FinishedAt = DateTimeOffset.UtcNow;
            _store.Invalidate();
        }
    }
}
