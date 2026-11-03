using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform;

/// <summary>Base type of every story collection. Derive from <see cref="Stories{TComponent}"/>.</summary>
public abstract class StoryCollection
{
    /// <summary>
    /// Hierarchical title (e.g. <c>Inputs/Button</c>). Defaults to the component's name.
    /// The title is part of every story id, so renaming it renames baselines too.
    /// </summary>
    public virtual string? Title => null;

    /// <summary>The component documented by this collection.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public abstract Type ComponentType { get; }

    /// <summary>
    /// The source file that declares the collection, as the compiler saw it (an absolute path, or a mapped one such as
    /// <c>/_/src/...</c> in deterministic CI builds). Null for collections created without caller information.
    /// </summary>
    public string? SourceFile { get; private protected set; }

    /// <summary>The line of the declaration in <see cref="SourceFile"/> (0 when unknown).</summary>
    public int SourceLine { get; private protected set; }

    /// <summary>
    /// Records where a collection is declared. Called by the generated story catalog, which knows every collection's
    /// source; not meant for direct use.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static TCollection WithSource<TCollection>(TCollection collection, string? sourceFile, int sourceLine)
        where TCollection : StoryCollection
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (collection.SourceFile is null && !string.IsNullOrEmpty(sourceFile))
        {
            collection.SourceFile = sourceFile;
            collection.SourceLine = sourceLine;
        }

        return collection;
    }

    internal abstract IReadOnlyList<Story> Build(IReadOnlyList<IStoryConfiguration> globalConfigurations);

    internal static string DefaultTitle(Type componentType)
    {
        var name = componentType.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick > 0 ? name[..tick] : name;
    }
}

/// <summary>
/// Declares the stories of a component. Stories are discovered by <see cref="StoryCatalog"/> from assemblies.
/// </summary>
/// <example>
/// <code>
/// public sealed class ButtonStories : Stories&lt;Button&gt;
/// {
///     public override string Title => "Inputs/Button";
///
///     protected override void Define(StoryCollectionBuilder&lt;Button&gt; stories)
///     {
///         stories.Defaults(s => s.ChildContent("Save"));
///         stories.Add("Primary", s => s.Arg(b => b.Variant, ButtonVariant.Primary));
///         stories.Add("Disabled", s => s.Arg(b => b.Disabled, true));
///         stories.Add("Click", s => s.Play(async ctx =>
///         {
///             await ctx.GetByRole("button", "Save").ClickAsync();
///             await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 1);
///         }));
///     }
/// }
/// </code>
/// </example>
public abstract class Stories<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> : StoryCollection
    where TComponent : IComponent
{
    /// <summary>
    /// Creates the collection. The compiler supplies where the derived class is declared, so reports, SARIF results and
    /// the workbench can point at the story's source.
    /// </summary>
    protected Stories([CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
    {
        SourceFile = string.IsNullOrEmpty(sourceFile) ? null : sourceFile;
        SourceLine = sourceLine;
    }

    /// <inheritdoc />
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public sealed override Type ComponentType => typeof(TComponent);

    /// <summary>Declares the stories.</summary>
    protected abstract void Define(StoryCollectionBuilder<TComponent> stories);

    internal override IReadOnlyList<Story> Build(IReadOnlyList<IStoryConfiguration> globalConfigurations)
    {
        var builder = new StoryCollectionBuilder<TComponent>(Title ?? DefaultTitle(typeof(TComponent)), GetType(), globalConfigurations) { Source = (SourceFile, SourceLine) };
        Define(builder);
        return builder.Build();
    }
}

/// <summary>
/// Assembly-wide story defaults (global decorators, tags, seeds...). Implementations with a public parameterless
/// constructor are discovered in the same assemblies as the stories and applied to every story first.
/// </summary>
public interface IStoryConfiguration
{
    /// <summary>Configures a story before its collection defaults and its own configuration.</summary>
    void Configure(IStoryBuilder story);
}

/// <summary>Collects the stories of one component.</summary>
public sealed class StoryCollectionBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
    where TComponent : IComponent
{
    private readonly List<Action<StoryBuilder<TComponent>>> _defaults = [];
    private readonly List<(string Name, Action<StoryBuilder<TComponent>>? Configure)> _stories = [];
    private readonly Type? _collectionType;
    private readonly IReadOnlyList<IStoryConfiguration> _global;

    internal StoryCollectionBuilder(string title, Type? collectionType, IReadOnlyList<IStoryConfiguration> global)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        _collectionType = collectionType;
        _global = global;
    }

    /// <summary>The collection title.</summary>
    public string Title { get; }

    internal (string? File, int Line) Source { get; init; }

    /// <summary>Configuration applied to every story of the collection (before the story's own configuration).</summary>
    public StoryCollectionBuilder<TComponent> Defaults(Action<StoryBuilder<TComponent>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _defaults.Add(configure);
        return this;
    }

    /// <summary>Adds a story.</summary>
    public StoryCollectionBuilder<TComponent> Add(string name, Action<StoryBuilder<TComponent>>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _stories.Add((name, configure));
        return this;
    }

    /// <summary>
    /// Adds one story per combination of parameter values (a visual state matrix), named like
    /// <c>Variant=Primary, Size=Small</c>. Use <see cref="VariantBuilder{TComponent}.Pairwise"/> to cover every pair of
    /// values instead of every combination, and <see cref="VariantBuilder{TComponent}.Where"/> to leave out
    /// combinations the component does not support.
    /// </summary>
    public StoryCollectionBuilder<TComponent> AddVariants(Action<VariantBuilder<TComponent>> vary, Action<StoryBuilder<TComponent>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(vary);
        var variants = new VariantBuilder<TComponent>();
        vary(variants);
        foreach (var (name, apply) in variants.Expand())
        {
            _stories.Add((name, story =>
            {
                apply(story);
                configure?.Invoke(story);
            }));
        }

        return this;
    }

    internal IReadOnlyList<Story> Build()
    {
        var stories = new List<Story>(_stories.Count);
        for (var i = 0; i < _stories.Count; i++)
        {
            var (name, configure) = _stories[i];
            var builder = new StoryBuilder<TComponent>(name);
            foreach (var global in _global)
            {
                global.Configure(builder);
            }

            foreach (var defaults in _defaults)
            {
                defaults(builder);
            }

            configure?.Invoke(builder);
            stories.Add(builder.Build(Title, _collectionType, i, Source));
        }

        return stories;
    }
}

/// <summary>Declares parameter dimensions for <see cref="StoryCollectionBuilder{TComponent}.AddVariants"/>.</summary>
public sealed class VariantBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
    where TComponent : IComponent
{
    private readonly List<(string Parameter, Type Type, IReadOnlyList<object?> Values)> _dimensions = [];
    private readonly List<Func<VariantCombination<TComponent>, bool>> _constraints = [];
    private bool _pairwise;

    internal VariantBuilder()
    {
    }

    /// <summary>Adds a dimension: one story per value (combined with the other dimensions).</summary>
    public VariantBuilder<TComponent> Vary<TValue>(Expression<Func<TComponent, TValue>> parameter, params TValue[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var name = ComponentParameters.GetName(parameter);
        if (_dimensions.Any(d => string.Equals(d.Parameter, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"'{name}' is already varied; give all of its values in one Vary call.", nameof(parameter));
        }

        _dimensions.Add((name, typeof(TValue), values.Cast<object?>().ToArray()));
        return this;
    }

    /// <summary>
    /// Covers every pair of values of any two parameters at least once instead of every combination (all-pairs
    /// testing). Most interaction defects involve one or two parameters, so this keeps most of the coverage with far
    /// fewer stories: 4 parameters with 4 values each give 16 to 20 stories instead of 256. With fewer than three
    /// parameters it changes nothing. The selection is deterministic, so story ids and baselines are stable.
    /// </summary>
    public VariantBuilder<TComponent> Pairwise()
    {
        _pairwise = true;
        return this;
    }

    /// <summary>
    /// Leaves out combinations the predicate rejects (for example <c>c =&gt; !(c.Get(b =&gt; b.Variant) == Variant.Link
    /// &amp;&amp; c.Get(b =&gt; b.Size) == Size.Large)</c>). Constraints combine with <see cref="Pairwise"/>: pairs that
    /// only occur in rejected combinations are not covered.
    /// </summary>
    public VariantBuilder<TComponent> Where(Func<VariantCombination<TComponent>, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _constraints.Add(predicate);
        return this;
    }

    internal IEnumerable<(string Name, Action<StoryBuilder<TComponent>> Apply)> Expand()
    {
        if (_dimensions.Count == 0)
        {
            yield break;
        }

        var sizes = _dimensions.Select(d => d.Values.Count).ToArray();
        Func<IReadOnlyList<int>, bool>? isValid = _constraints.Count == 0 ? null : row =>
        {
            var combination = new VariantCombination<TComponent>(_dimensions.Select((d, i) => (d.Parameter, d.Values[row[i]])).ToArray());
            return _constraints.All(c => c(combination));
        };
        var rows = _pairwise ? Combinatorics.Pairwise(sizes, isValid) : Combinatorics.CartesianProduct(sizes, isValid);

        foreach (var row in rows)
        {
            var parts = new List<string>(_dimensions.Count);
            var assignments = new List<(string Name, object? Value, Type Type)>(_dimensions.Count);
            for (var d = 0; d < _dimensions.Count; d++)
            {
                var (parameter, type, values) = _dimensions[d];
                var value = values[row[d]];
                parts.Add($"{parameter}={Format(value)}");
                assignments.Add((parameter, value, type));
            }

            yield return (string.Join(", ", parts), builder =>
            {
                foreach (var (name, value, type) in assignments)
                {
                    builder.SetValueArg(name, value, type);
                }
            });
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

/// <summary>One combination of varied parameter values, passed to <see cref="VariantBuilder{TComponent}.Where"/>.</summary>
public sealed class VariantCombination<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
    where TComponent : IComponent
{
    private readonly IReadOnlyList<(string Parameter, object? Value)> _values;

    internal VariantCombination(IReadOnlyList<(string Parameter, object? Value)> values) => _values = values;

    /// <summary>The value of a varied parameter by name.</summary>
    /// <exception cref="KeyNotFoundException">The parameter is not varied.</exception>
    public object? this[string parameter] =>
        _values.FirstOrDefault(v => string.Equals(v.Parameter, parameter, StringComparison.OrdinalIgnoreCase)) is { Parameter: not null } match
            ? match.Value
            : throw new KeyNotFoundException($"'{parameter}' is not varied. Varied: {string.Join(", ", _values.Select(v => v.Parameter))}.");

    /// <summary>The value of a varied parameter.</summary>
    /// <exception cref="KeyNotFoundException">The parameter is not varied.</exception>
    public TValue Get<TValue>(Expression<Func<TComponent, TValue>> parameter) => (TValue)this[ComponentParameters.GetName(parameter)]!;

    /// <inheritdoc />
    public override string ToString() => string.Join(", ", _values.Select(v => $"{v.Parameter}={v.Value}"));
}
