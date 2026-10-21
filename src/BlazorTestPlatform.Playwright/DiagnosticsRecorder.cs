using System.Collections.Concurrent;
using System.Diagnostics;
using BlazorTestPlatform.Results;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Playwright;

/// <summary>
/// Records what happened in the page while a story ran — console output, uncaught errors, network requests and
/// WebSocket (Blazor Server circuit) activity — with timestamps relative to the story start. Attached to every
/// browser run so a flaky failure can be explained from its artifacts instead of re-run until it reproduces.
/// </summary>
public sealed class DiagnosticsRecorder
{
    private const int MaxEntries = 1000;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<ConsoleEntry> _console = new();
    private readonly ConcurrentQueue<NetworkEntry> _network = new();
    private readonly ConcurrentDictionary<IRequest, double> _pending = new();
    private readonly ConcurrentDictionary<IRequest, int> _statuses = new();
    private int _webSocketFrames;

    /// <summary>Attaches to a page.</summary>
    public DiagnosticsRecorder(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Console += (_, message) => Add(_console, new ConsoleEntry
        {
            Level = message.Type,
            Text = message.Text,
            Location = message.Location,
            AtMilliseconds = Now,
        });
        page.PageError += (_, error) => Add(_console, new ConsoleEntry { Level = "pageerror", Text = error, AtMilliseconds = Now });
        page.Request += (_, request) => _pending[request] = Now;
        page.Response += (_, response) => _statuses[response.Request] = response.Status;
        page.RequestFinished += (_, request) => Complete(request, failure: null);
        page.RequestFailed += (_, request) => Complete(request, request.Failure ?? "failed");
        page.WebSocket += (_, socket) =>
        {
            Add(_network, new NetworkEntry { Method = "WS", Url = socket.Url, ResourceType = "websocket", Status = 101, StartMilliseconds = Now });
            socket.FrameReceived += (_, _) => Interlocked.Increment(ref _webSocketFrames);
            socket.FrameSent += (_, _) => Interlocked.Increment(ref _webSocketFrames);
            socket.Close += (_, _) => Add(_console, new ConsoleEntry { Level = "info", Text = $"WebSocket closed: {socket.Url}", AtMilliseconds = Now });
        };
    }

    /// <summary>Console messages and page errors.</summary>
    public IReadOnlyList<ConsoleEntry> Console => _console.ToArray();

    /// <summary>Completed and failed requests.</summary>
    public IReadOnlyList<NetworkEntry> Network => _network.ToArray();

    /// <summary>WebSocket frames exchanged (Blazor Server circuit traffic).</summary>
    public int WebSocketFrames => Volatile.Read(ref _webSocketFrames);

    /// <summary>Console errors and page errors, excluding ignored patterns.</summary>
    public IReadOnlyList<ConsoleEntry> Errors(IEnumerable<string> ignoredPatterns)
    {
        var ignored = ignoredPatterns.ToArray();
        return Console
            .Where(c => c.Level is "error" or "pageerror")
            .Where(c => !ignored.Any(p => c.Text.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private double Now => _clock.Elapsed.TotalMilliseconds;

    private void Complete(IRequest request, string? failure)
    {
        var start = _pending.TryRemove(request, out var started) ? started : Now;
        var status = _statuses.TryRemove(request, out var code) ? code : 0;

        Add(_network, new NetworkEntry
        {
            Method = request.Method,
            Url = request.Url,
            ResourceType = request.ResourceType,
            Status = status,
            Failure = failure,
            StartMilliseconds = start,
            DurationMilliseconds = Now - start,
        });
    }

    private static void Add<T>(ConcurrentQueue<T> queue, T item)
    {
        queue.Enqueue(item);
        while (queue.Count > MaxEntries && queue.TryDequeue(out _))
        {
        }
    }
}
