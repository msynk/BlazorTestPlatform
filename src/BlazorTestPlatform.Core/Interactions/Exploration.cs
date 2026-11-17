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

        return context.Recorder.RunAsync($"explore {name} (seed {seed}, {exploration.StepCount} steps)", StepKind.Step, async () =>
        {
            await CheckInvariantsAsync(context, name, exploration, seed, taken).ConfigureAwait(false);
            for (var step = 0; step < exploration.StepCount; step++)
            {
                var possible = new List<int>();
                for (var i = 0; i < exploration.Actions.Count; i++)
                {
                    if (exploration.Actions[i].When is not { } when || await when(context).ConfigureAwait(false))
                    {
                        possible.Add(i);
                    }
                }

                if (possible.Count == 0)
                {
                    context.Recorder.Log($"exploration {name}: no action is possible after {taken.Count} step(s); stopping");
                    return;
                }

                var action = exploration.Actions[possible[random.Next(possible.Count)]];
                taken.Add(action.Name);
                try
                {
                    await context.StepAsync($"{step + 1}. {action.Name}", () => action.Run(context)).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not StorySkippedException)
                {
                    throw Failure(name, exploration, seed, taken, $"action '{action.Name}' failed", ex);
                }

                await CheckInvariantsAsync(context, name, exploration, seed, taken).ConfigureAwait(false);
            }
        });
    }

    private static async Task CheckInvariantsAsync(StoryContext context, string name, ExplorationBuilder exploration, int seed, List<string> taken)
    {
        foreach (var (invariant, check) in exploration.Invariants)
        {
            try
            {
                await check(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not StorySkippedException)
            {
                throw Failure(name, exploration, seed, taken, $"invariant '{invariant}' broke", ex);
            }
        }
    }

    // The failing sequence, the seed that replays it, and a play script to paste as a regression story.
    private static StoryAssertionException Failure(string name, ExplorationBuilder exploration, int seed, IReadOnlyList<string> taken, string what, Exception inner)
    {
        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Exploration '{name}': {what} after {taken.Count} step(s).")
            .AppendLine()
            .Append("  Steps: ").Append(taken.Count == 0 ? "(none: the initial state)" : string.Join(" -> ", taken))
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Replay: .Seed({seed}) on the exploration, or BTP_EXPLORE_SEED={seed}")
            .AppendLine()
            .Append("  Error: ").Append(inner.Message.Replace("\n", "\n    ", StringComparison.Ordinal))
            .AppendLine()
            .AppendLine("  As a regression story:")
            .AppendLine("    .Play(async ctx =>")
            .AppendLine("    {");
        foreach (var step in taken)
        {
            var code = exploration.Actions.First(a => a.Name == step).Code;
            message.Append("        ").AppendLine(code ?? $"// {step}");
        }

        message.Append("    })");
        return new StoryAssertionException(message.ToString(), inner);
    }
}
