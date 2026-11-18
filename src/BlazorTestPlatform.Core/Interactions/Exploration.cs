using System.Globalization;
using System.Text;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform;

/// <summary>
/// Describes a stateful exploration (<see cref="StoryContext.ExploreAsync"/>): named actions a user could take, when each
/// is possible, and invariants that must hold after every step, as Hypothesis' rule-based state machines and fast-check's
/// model-based commands.
/// </summary>
public sealed class ExplorationBuilder
{
    internal List<(string Name, Func<StoryContext, Task> Run, Func<StoryContext, Task<bool>>? When, string? Code)> Actions { get; } = [];

    internal List<(string Name, Func<StoryContext, Task> Check)> Invariants { get; } = [];

    internal int StepCount { get; private set; } = 20;

    internal int? SeedValue { get; private set; }

    /// <summary>
    /// Adds an action. <paramref name="when"/> says whether it is possible now (a disabled button is not); <paramref name="code"/>
    /// is the C# the failure report prints for it in the reproduction script (default: a comment with the name).
    /// </summary>
    public ExplorationBuilder Action(string name, Func<StoryContext, Task> run, Func<StoryContext, Task<bool>>? when = null, string? code = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);
        if (Actions.Exists(a => string.Equals(a.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"An action named '{name}' already exists.", nameof(name));
        }

        Actions.Add((name, run, when, code));
        return this;
    }

    /// <summary>Adds an invariant: an assertion (it throws when broken) checked before the first step and after every step.</summary>
    public ExplorationBuilder Invariant(string name, Func<StoryContext, Task> check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(check);
        Invariants.Add((name, check));
        return this;
    }

    /// <summary>How many actions to take (default 20).</summary>
    public ExplorationBuilder Steps(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        StepCount = count;
        return this;
    }

    /// <summary>
    /// The seed that picks the actions. By default it derives from the story's seed, so a run is repeatable; set
    /// <c>BTP_EXPLORE_SEED</c> (or this) to try other sequences, and use the seed a failure reports to replay it.
    /// </summary>
    public ExplorationBuilder Seed(int seed)
    {
        SeedValue = seed;
        return this;
    }
}

/// <summary>
/// An exploration (<see cref="StoryContext.ExploreAsync"/>) found a sequence of actions that breaks an invariant or makes an
/// action fail. The message lists the steps, the seed and a regression play script; the bUnit runner shrinks the steps to
/// a shortest failing sequence it can find.
/// </summary>
public sealed class ExplorationFailedException : StoryAssertionException
{
    internal ExplorationFailedException(string exploration, string failure, int seed, IReadOnlyList<string> steps, IReadOnlyDictionary<string, string?> code, Exception inner, IReadOnlyList<string>? original = null)
        : base(Describe(exploration, failure, seed, steps, code, inner, original), inner)
    {
        Exploration = exploration;
        Failure = failure;
        Seed = seed;
        Steps = steps;
        Code = code;
        OriginalSteps = original;
    }

    /// <summary>The exploration's name.</summary>
    public string Exploration { get; }

    /// <summary>What went wrong: <c>invariant 'name' broke</c> or <c>action 'name' failed</c>.</summary>
    public string Failure { get; }

    /// <summary>The seed that picked the actions.</summary>
    public int Seed { get; }

    /// <summary>The actions taken, in order, up to the failure.</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>The steps of the exploration before they were shrunk, or null when not shrunk.</summary>
    public IReadOnlyList<string>? OriginalSteps { get; }

    internal IReadOnlyDictionary<string, string?> Code { get; }

    /// <summary>The same failure, reported with <paramref name="shrunk"/> steps found by shrinking.</summary>
    public ExplorationFailedException WithShrunkSteps(IReadOnlyList<string> shrunk, Exception inner)
    {
        ArgumentNullException.ThrowIfNull(shrunk);
        return new ExplorationFailedException(Exploration, Failure, Seed, shrunk, Code, inner ?? InnerException!, OriginalSteps ?? Steps);
    }

    private static string Describe(string name, string what, int seed, IReadOnlyList<string> taken, IReadOnlyDictionary<string, string?> code, Exception inner, IReadOnlyList<string>? original)
    {
        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Exploration '{name}': {what} after {taken.Count} step(s)")
            .Append(original is null ? "." : string.Create(CultureInfo.InvariantCulture, $" (shrunk from {original.Count})."))
            .AppendLine()
            .Append("  Steps: ").Append(taken.Count == 0 ? "(none: the initial state)" : string.Join(" -> ", taken))
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Replay: .Seed({seed}) on the exploration, or BTP_EXPLORE_SEED={seed}")
            .Append(original is null ? string.Empty : " (replays the original steps)")
            .AppendLine()
            .Append("  Error: ").Append(inner.Message.Replace("\n", "\n    ", StringComparison.Ordinal))
            .AppendLine()
            .AppendLine("  As a regression story:")
            .AppendLine("    .Play(async ctx =>")
            .AppendLine("    {");
        foreach (var step in taken)
        {
            message.Append("        ").AppendLine(code.GetValueOrDefault(step) ?? $"// {step}");
        }

        return message.Append("    })").ToString();
    }
}

/// <summary>
/// Makes <see cref="StoryContext.ExploreAsync"/> replay a fixed sequence of actions instead of picking them (runners use it
/// to shrink a failure: each candidate runs on a fresh render). Applies to the current async flow only.
/// </summary>
public static class ExplorationReplay
{
    private static readonly AsyncLocal<(string Name, IReadOnlyList<string> Steps)?> Current = new();

    /// <summary>Replays <paramref name="steps"/> in the exploration named <paramref name="exploration"/> while the returned scope lives.</summary>
    public static IDisposable Begin(string exploration, IReadOnlyList<string> steps)
    {
        ArgumentException.ThrowIfNullOrEmpty(exploration);
        ArgumentNullException.ThrowIfNull(steps);
        var previous = Current.Value;
        Current.Value = (exploration, steps);
        return new Scope(previous);
    }

    internal static IReadOnlyList<string>? For(string exploration) =>
        Current.Value is { } replay && string.Equals(replay.Name, exploration, StringComparison.Ordinal) ? replay.Steps : null;

    private sealed class Scope((string Name, IReadOnlyList<string> Steps)? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>A replayed step was not possible at its point (its precondition was false): the candidate does not reproduce.</summary>
internal sealed class ExplorationReplayImpossibleException(string message) : Exception(message);

/// <summary>Runs explorations for <see cref="StoryContext.ExploreAsync"/>.</summary>
internal static class Exploration
{
    public static Task RunAsync(StoryContext context, string name, ExplorationBuilder exploration)
    {
        if (exploration.Actions.Count == 0)
        {
            throw new InvalidOperationException($"Exploration '{name}' has no actions.");
        }

        var seed = exploration.SeedValue
            ?? (int.TryParse(Environment.GetEnvironmentVariable("BTP_EXPLORE_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromEnvironment) ? fromEnvironment : (int?)null)
            ?? context.Random.Fork("explore:" + name).Next();
        var random = new DeterministicRandom(seed);
        var taken = new List<string>();
        var code = exploration.Actions.ToDictionary(a => a.Name, a => a.Code, StringComparer.Ordinal);
        var replay = ExplorationReplay.For(name);

        return context.Recorder.RunAsync($"explore {name} ({(replay is null ? $"seed {seed}, {exploration.StepCount} steps" : $"replaying {replay.Count} steps")})", StepKind.Step, async () =>
        {
            await CheckInvariantsAsync(context, name, exploration, seed, taken, code).ConfigureAwait(false);
            var count = replay?.Count ?? exploration.StepCount;
            for (var step = 0; step < count; step++)
            {
                var possible = new List<int>();
                for (var i = 0; i < exploration.Actions.Count; i++)
                {
                    if (exploration.Actions[i].When is not { } when || await when(context).ConfigureAwait(false))
                    {
                        possible.Add(i);
                    }
                }

                int chosen;
                if (replay is not null)
                {
                    chosen = exploration.Actions.FindIndex(a => string.Equals(a.Name, replay[step], StringComparison.Ordinal));
                    if (chosen < 0 || !possible.Contains(chosen))
                    {
                        throw new ExplorationReplayImpossibleException($"Replayed step {step + 1} ({replay[step]}) is not possible here.");
                    }
                }
                else if (possible.Count == 0)
                {
                    context.Recorder.Log($"exploration {name}: no action is possible after {taken.Count} step(s); stopping");
                    return;
                }
                else
                {
                    chosen = possible[random.Next(possible.Count)];
                }

                var action = exploration.Actions[chosen];
                taken.Add(action.Name);
                try
                {
                    await context.StepAsync($"{step + 1}. {action.Name}", () => action.Run(context)).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not StorySkippedException)
                {
                    throw new ExplorationFailedException(name, $"action '{action.Name}' failed", seed, taken, code, ex);
                }

                await CheckInvariantsAsync(context, name, exploration, seed, taken, code).ConfigureAwait(false);
            }
        });
    }

    private static async Task CheckInvariantsAsync(StoryContext context, string name, ExplorationBuilder exploration, int seed, List<string> taken, IReadOnlyDictionary<string, string?> code)
    {
        foreach (var (invariant, check) in exploration.Invariants)
        {
            try
            {
                await check(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not StorySkippedException)
            {
                throw new ExplorationFailedException(name, $"invariant '{invariant}' broke", seed, [.. taken], code, ex);
            }
        }
    }

    /// <summary>
    /// Delta debugging (ddmin) over <paramref name="steps"/>: the shortest subsequence it finds for which
    /// <paramref name="fails"/> still reports the same failure. Each probe should replay the candidate on a fresh render.
    /// </summary>
    /// <param name="steps">The failing steps.</param>
    /// <param name="fails">Replays a candidate; returns the steps that reproduced the failure (a prefix of the candidate when it fails early), or null.</param>
    /// <param name="maxProbes">The most candidates to try.</param>
    public static async Task<IReadOnlyList<string>> ShrinkAsync(IReadOnlyList<string> steps, Func<IReadOnlyList<string>, Task<IReadOnlyList<string>?>> fails, int maxProbes)
    {
        var current = steps.ToList();
        var probes = 0;
        var granularity = 2;
        while (current.Count >= 2 && probes < maxProbes)
        {
            var chunk = (int)Math.Ceiling(current.Count / (double)granularity);
            var reduced = false;
            for (var start = 0; start < current.Count && probes < maxProbes; start += chunk)
            {
                // Remove one chunk (the complement of it is the candidate).
                var candidate = current.Take(start).Concat(current.Skip(start + chunk)).ToList();
                probes++;
                if (await fails(candidate).ConfigureAwait(false) is { } reproduced)
                {
                    current = [.. reproduced];
                    granularity = Math.Max(granularity - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (!reduced)
            {
                if (chunk == 1)
                {
                    break;
                }

                granularity = Math.Min(granularity * 2, current.Count);
            }
        }

        // A single remaining step may still be removable (the failure can hold in the initial state after the prefix).
        if (current.Count == 1 && probes < maxProbes && await fails([]).ConfigureAwait(false) is not null)
        {
            current = [];
        }

        return current;
    }
}
