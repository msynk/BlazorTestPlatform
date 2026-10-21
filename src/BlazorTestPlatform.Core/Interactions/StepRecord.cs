using System.Diagnostics;

namespace BlazorTestPlatform.Results;

/// <summary>Category of a timeline step.</summary>
public enum StepKind
{
    /// <summary>A user-defined step (<c>ctx.StepAsync</c>).</summary>
    Step,

    /// <summary>An element interaction.</summary>
    Action,

    /// <summary>A keyboard interaction.</summary>
    Keyboard,

    /// <summary>An auto-waiting assertion.</summary>
    Assertion,

    /// <summary>Waiting (idle, time advance).</summary>
    Wait,

    /// <summary>Navigation / rendering.</summary>
    Render,

    /// <summary>Snapshot, accessibility scan or measurement.</summary>
    Check,

    /// <summary>A log message.</summary>
    Log,
}

/// <summary>One entry of a story's execution timeline. Timelines make flaky failures explainable.</summary>
public sealed class StepRecord
{
    /// <summary>Description, e.g. <c>click role=button[name="Save"]</c>.</summary>
    public required string Title { get; init; }

    /// <summary>Category.</summary>
    public StepKind Kind { get; init; }

    /// <summary>Start offset from the beginning of the story, in milliseconds.</summary>
    public double StartMilliseconds { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public double DurationMilliseconds { get; set; }

    /// <summary>Error message when the step failed.</summary>
    public string? Error { get; set; }

    /// <summary>Nested steps.</summary>
    public List<StepRecord> Children { get; init; } = [];

    /// <summary>True when the step failed.</summary>
    public bool Failed => Error is not null;
}

/// <summary>Records a nested timeline of steps.</summary>
public sealed class StepRecorder
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<StepRecord> _roots = [];
    private readonly AsyncLocal<StepRecord?> _current = new();
    private readonly Lock _gate = new();

    /// <summary>Top-level steps.</summary>
    public IReadOnlyList<StepRecord> Steps
    {
        get
        {
            lock (_gate)
            {
                return _roots.ToArray();
            }
        }
    }

    /// <summary>Elapsed time since the recorder started.</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>Records an instantaneous entry.</summary>
    public void Log(string title, StepKind kind = StepKind.Log)
    {
        lock (_gate)
        {
            Add(new StepRecord { Title = title, Kind = kind, StartMilliseconds = _clock.Elapsed.TotalMilliseconds });
        }
    }

    /// <summary>Runs <paramref name="body"/> as a timed step; failures are recorded and rethrown.</summary>
    public async Task<T> RunAsync<T>(string title, StepKind kind, Func<Task<T>> body)
    {
        var parent = _current.Value;
        StepRecord step;
        lock (_gate)
        {
            step = new StepRecord { Title = title, Kind = kind, StartMilliseconds = _clock.Elapsed.TotalMilliseconds };
            Add(step);
        }

        _current.Value = step;

        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            step.Error = FirstLine(ex.Message);
            throw;
        }
        finally
        {
            step.DurationMilliseconds = _clock.Elapsed.TotalMilliseconds - step.StartMilliseconds;
            _current.Value = parent;
        }
    }

    /// <summary>Runs <paramref name="body"/> as a timed step.</summary>
    public Task RunAsync(string title, StepKind kind, Func<Task> body) =>
        RunAsync<bool>(title, kind, async () =>
        {
            await body().ConfigureAwait(false);
            return true;
        });

    private void Add(StepRecord step)
    {
        if (_current.Value is { } parent)
        {
            parent.Children.Add(step);
        }
        else
        {
            _roots.Add(step);
        }
    }

    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? message : message[..index].TrimEnd('\r');
    }
}
