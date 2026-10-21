using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using BlazorTestPlatform.Results;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Coverage;

/// <summary>One value of a component parameter and the stories that render it.</summary>
/// <param name="Value">The value as control text (<see cref="StoryArgs.Format"/>), or <c>null</c>.</param>
/// <param name="Stories">Ids of the stories that render the component with this value.</param>
public sealed record StateCoverage(string Value, IReadOnlyList<string> Stories);

/// <summary>Which values of one component parameter the stories render.</summary>
public sealed record ParameterCoverage
{
    /// <summary>Parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Short type name, e.g. <c>ButtonSize?</c>.</summary>
    public required string Type { get; init; }

    /// <summary>
    /// Every value the parameter can take, for parameters with a finite set of states (<c>bool</c>, enums and their
    /// nullable forms, where <c>null</c> is a state). Empty for open-ended types such as strings and numbers.
    /// </summary>
    public IReadOnlyList<string> Domain { get; init; } = [];

    /// <summary>The values the stories render, in domain order (open-ended types: in first-seen order).</summary>
    public IReadOnlyList<StateCoverage> Values { get; init; } = [];

    /// <summary>Stories whose value is computed per render, so it cannot be known without rendering.</summary>
    public IReadOnlyList<string> ComputedIn { get; init; } = [];

    /// <summary>Domain values no story renders.</summary>
    [JsonIgnore]
    public IEnumerable<string> Missing => Domain.Where(d => !Values.Any(v => v.Value == d));

    /// <summary>True when the parameter has a finite set of states.</summary>
    [JsonIgnore]
    public bool IsFinite => Domain.Count > 0;
}

/// <summary>State coverage of one component (one story title).</summary>
public sealed record ComponentCoverage
{
    /// <summary>Story title, e.g. <c>Inputs/Button</c>.</summary>
    public required string Title { get; init; }

    /// <summary>Full name of the component type.</summary>
    public required string Component { get; init; }

    /// <summary>Number of stories (skipped stories excluded).</summary>
    public int Stories { get; init; }

    /// <summary>Editable parameters (see <see cref="StoryArgs.ControlsFor"/>) and the values the stories render.</summary>
    public IReadOnlyList<ParameterCoverage> Parameters { get; init; } = [];

    /// <summary>Finite states rendered by at least one story.</summary>
    [JsonIgnore]
    public int CoveredStates => Parameters.Where(p => p.IsFinite).Sum(p => p.Domain.Count - p.Missing.Count());

    /// <summary>Finite states of every parameter.</summary>
    [JsonIgnore]
    public int TotalStates => Parameters.Sum(p => p.Domain.Count);

    /// <summary><see cref="CoveredStates"/> / <see cref="TotalStates"/> (1 when the component has no finite states).</summary>
    [JsonIgnore]
    public double Ratio => TotalStates == 0 ? 1 : (double)CoveredStates / TotalStates;

    /// <summary>Uncovered states as <c>Parameter=value</c>.</summary>
    [JsonIgnore]
    public IEnumerable<string> MissingStates => Parameters.SelectMany(p => p.Missing.Select(v => $"{p.Name}={v}"));
}

/// <summary>
/// How much of each component's state space the stories exercise: for every <c>bool</c> and enum parameter, which values
/// at least one story renders (its own argument, else the component's default), plus the components of a library that
/// have no stories at all. Written to <c>{results}/story-coverage.json</c>, where <c>btp report</c> picks it up.
/// </summary>
public sealed record StoryCoverageReport
{
    /// <summary>File name inside the results directory.</summary>
    public const string FileName = "story-coverage.json";

    /// <summary>Coverage per component, ordered by title.</summary>
    public IReadOnlyList<ComponentCoverage> Components { get; init; } = [];

    /// <summary>Full names of components that no story renders (only when the analysis was given a component list).</summary>
    public IReadOnlyList<string> ComponentsWithoutStories { get; init; } = [];

    /// <summary>Finite states rendered by at least one story, over every component.</summary>
    [JsonIgnore]
    public int CoveredStates => Components.Sum(c => c.CoveredStates);

    /// <summary>Finite states over every component.</summary>
    [JsonIgnore]
    public int TotalStates => Components.Sum(c => c.TotalStates);

    /// <summary><see cref="CoveredStates"/> / <see cref="TotalStates"/> (1 when there are no finite states).</summary>
    [JsonIgnore]
    public double Ratio => TotalStates == 0 ? 1 : (double)CoveredStates / TotalStates;

    /// <summary>
    /// Throws when coverage is below <paramref name="minimumRatio"/>, or, with <paramref name="requireStoriesForEveryComponent"/>,
    /// when a component has no stories. The message lists what is missing.
    /// </summary>
    public void ThrowIfBelow(double minimumRatio, bool requireStoriesForEveryComponent = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumRatio, 1);
        var problems = new List<string>();
        if (Ratio < minimumRatio)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Story state coverage is {Ratio:P0} ({CoveredStates}/{TotalStates}), below the required {minimumRatio:P0}. Missing states:"));
            foreach (var component in Components.Where(c => c.CoveredStates < c.TotalStates))
            {
                problems.Add($"  {component.Title}: {string.Join(", ", component.MissingStates)}");
            }
        }

        if (requireStoriesForEveryComponent && ComponentsWithoutStories.Count > 0)
        {
            problems.Add("Components without stories: " + string.Join(", ", ComponentsWithoutStories));
        }

        if (problems.Count > 0)
        {
            throw new StoryAssertionException(string.Join(Environment.NewLine, problems));
        }
    }

    /// <summary>Writes the report into a results directory and returns its path.</summary>
    public string Save(string resultsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        Directory.CreateDirectory(resultsDirectory);
        var path = Path.Combine(resultsDirectory, FileName);
        File.WriteAllText(path, BtpJson.Serialize(this));
        return path;
    }

    /// <summary>Loads the report of a results directory, or null when none was written there.</summary>
    public static StoryCoverageReport? Load(string resultsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        var path = Path.Combine(resultsDirectory, FileName);
        return File.Exists(path) ? BtpJson.Deserialize<StoryCoverageReport>(File.ReadAllText(path)) : null;
    }
}

/// <summary>Computes <see cref="StoryCoverageReport"/>s.</summary>
public static class StoryCoverage
{
    /// <summary>Text used for a <c>null</c> state.</summary>
    public const string NullValue = "null";

    /// <summary>
    /// Analyzes a catalog. Pass the library's components (see <see cref="ComponentsIn"/>) to also list components that
    /// have no stories.
    /// </summary>
    public static StoryCoverageReport Analyze(StoryCatalog catalog, IEnumerable<Type>? components = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var coverage = catalog.Groups
            .Where(g => g.ComponentType != typeof(Composition))
            .Select(Analyze)
            .ToArray();

        var storied = catalog.Stories.Select(s => s.ComponentType).ToHashSet();
        var without = (components ?? [])
            .Where(t => !storied.Contains(t))
            .Select(t => t.FullName ?? t.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new StoryCoverageReport { Components = coverage, ComponentsWithoutStories = without };
    }

    /// <summary>Coverage of one component's stories.</summary>
    public static ComponentCoverage Analyze(StoryGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var stories = group.Stories.Where(s => s.SkipReason is null).ToArray();
        var parameters = new List<(ArgControl Control, List<(string Value, string Story)> Seen, List<string> Computed)>();
        foreach (var story in stories)
        {
            foreach (var control in StoryArgs.ControlsFor(story))
            {
                var index = parameters.FindIndex(p => string.Equals(p.Control.Name, control.Name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    parameters.Add((control, [], []));
                    index = parameters.Count - 1;
                }

                if (control.Computed)
                {
                    parameters[index].Computed.Add(story.Id);
                }
                else
                {
                    parameters[index].Seen.Add((control.DefaultText ?? NullValue, story.Id));
                }
            }
        }

        return new ComponentCoverage
        {
            Title = group.Title,
            Component = group.ComponentType.FullName ?? group.ComponentType.Name,
            Stories = stories.Length,
            Parameters = parameters.Select(p =>
            {
                var domain = DomainOf(p.Control);
                var order = domain.Count > 0 ? domain : p.Seen.Select(s => s.Value).Distinct(StringComparer.Ordinal).ToArray();
                return new ParameterCoverage
                {
                    Name = p.Control.Name,
                    Type = p.Control.TypeName,
                    Domain = domain,
                    Values = order
                        .Select(value => new StateCoverage(value, p.Seen.Where(s => s.Value == value).Select(s => s.Story).Distinct(StringComparer.Ordinal).ToArray()))
                        .Where(v => v.Stories.Count > 0)
                        .ToArray(),
                    ComputedIn = p.Computed,
                };
            }).ToArray(),
        };
    }

    /// <summary>
    /// The public, concrete components of an assembly: every <see cref="IComponent"/> except generic definitions,
    /// layouts and the <c>_Imports</c> class Razor generates.
    /// </summary>
    [RequiresUnreferencedCode("Enumerates the types of an assembly.")]
    public static IReadOnlyList<Type> ComponentsIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IComponent).IsAssignableFrom(t))
            .Where(t => !typeof(LayoutComponentBase).IsAssignableFrom(t) && t.Name != "_Imports")
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> DomainOf(ArgControl control) => control.Control switch
    {
        ArgControlType.Boolean => ["true", "false"],
        ArgControlType.Select => control.Options.Select(o => o.Length == 0 ? NullValue : o).ToArray(),
        _ => [],
    };
}
