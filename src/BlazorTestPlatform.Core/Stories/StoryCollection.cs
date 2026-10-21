using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
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
    /// <inheritdoc />
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public sealed override Type ComponentType => typeof(TComponent);

    /// <summary>Declares the stories.</summary>
    protected abstract void Define(StoryCollectionBuilder<TComponent> stories);

    internal override IReadOnlyList<Story> Build(IReadOnlyList<IStoryConfiguration> globalConfigurations)
    {
        var builder = new StoryCollectionBuilder<TComponent>(Title ?? DefaultTitle(typeof(TComponent)), GetType(), globalConfigurations);
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
    /// <c>Variant=Primary, Size=Small</c>.
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
            stories.Add(builder.Build(Title, _collectionType, i));
        }

        return stories;
    }
}

/// <summary>Declares parameter dimensions for <see cref="StoryCollectionBuilder{TComponent}.AddVariants"/>.</summary>
public sealed class VariantBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>
    where TComponent : IComponent
{
    private readonly List<(string Parameter, Type Type, IReadOnlyList<object?> Values)> _dimensions = [];

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

        _dimensions.Add((ComponentParameters.GetName(parameter), typeof(TValue), values.Cast<object?>().ToArray()));
        return this;
    }

    internal IEnumerable<(string Name, Action<StoryBuilder<TComponent>> Apply)> Expand()
    {
        if (_dimensions.Count == 0)
        {
            yield break;
        }

        var indices = new int[_dimensions.Count];
        while (true)
        {
            var parts = new List<string>(_dimensions.Count);
            var assignments = new List<(string Name, object? Value, Type Type)>(_dimensions.Count);
            for (var d = 0; d < _dimensions.Count; d++)
            {
                var (parameter, type, values) = _dimensions[d];
                var value = values[indices[d]];
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

            var dimension = _dimensions.Count - 1;
            while (dimension >= 0)
            {
                indices[dimension]++;
                if (indices[dimension] < _dimensions[dimension].Values.Count)
                {
                    break;
                }

                indices[dimension] = 0;
                dimension--;
            }

            if (dimension < 0)
            {
                yield break;
            }
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
