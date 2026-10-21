using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform;

/// <summary>
/// Per-render state of a story: services, seeded randomness, action log, bound values and fake authentication.
/// Created by a driver (bUnit, workbench) and passed to <see cref="StoryRoot"/>.
/// </summary>
public sealed class StoryRenderContext
{
    private readonly Dictionary<string, object?> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);
    private IHandleEvent? _root;

    /// <summary>Creates a render context.</summary>
    public StoryRenderContext(Story story, IServiceProvider services, HostingModel hostingModel = HostingModel.InMemory)
    {
        Story = story ?? throw new ArgumentNullException(nameof(story));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        HostingModel = hostingModel;
        Random = new DeterministicRandom(story.Seed);
        Fake = new FakeData(Random.Fork("fake"));
        if (story.User is not null)
        {
            Authentication = new FakeAuthenticationStateProvider(story.User);
        }
    }

    /// <summary>The story being rendered.</summary>
    public Story Story { get; }

    /// <summary>Services of the renderer hosting the story.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The hosting model.</summary>
    public HostingModel HostingModel { get; }

    /// <summary>Recorded callback invocations.</summary>
    public ActionLog Actions { get; } = new();

    /// <summary>Seeded random source (from <see cref="Story.Seed"/>).</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Seeded fake-data generator.</summary>
    public FakeData Fake { get; }

    /// <summary>The active theme, if any.</summary>
    public ThemeDefinition? Theme { get; init; }

    /// <summary>The active viewport, if known.</summary>
    public Viewport? Viewport { get; init; }

    /// <summary>
    /// Parameter values that replace the story's own (workbench controls, <c>?arg.Name=value</c> links,
    /// <c>BunitStoryOptions.Args</c>). Converted with <see cref="StoryArgs.Resolve"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ArgOverrides
    {
        get => _argOverrides;
        init => _argOverrides = new Dictionary<string, object?>(value ?? throw new ArgumentNullException(nameof(value)), StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, object?> _argOverrides = new(StringComparer.OrdinalIgnoreCase);

    // Parameters whose override was removed: components keep the last value of a parameter that is no longer
    // passed, so the component's default is passed explicitly from then on.
    private readonly Dictionary<string, object?> _argResets = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, object?> ArgResets => _argResets;

    /// <summary>
    /// Replaces the overrides while the story is rendered (live controls) and re-renders it. Two-way bound values take
    /// the new value; overrides that are removed fall back to the story's values.
    /// </summary>
    public Task SetArgOverridesAsync(IReadOnlyDictionary<string, object?> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var previous = _argOverrides;
        _argOverrides = new Dictionary<string, object?>(overrides, StringComparer.OrdinalIgnoreCase);
        foreach (var name in previous.Keys.Where(n => !_argOverrides.ContainsKey(n)))
        {
            if (!Story.Args.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _argResets[name] = StoryArgs.ComponentDefault(Story, name);
            }
        }
        foreach (var arg in Story.Args.Where(a => a.Kind == StoryArgKind.Binding && a.HasConstant))
        {
            if (_cache.TryGetValue("binding:" + arg.Name, out var holder) && holder is IBindingHolder binding)
            {
                if (_argOverrides.TryGetValue(arg.Name, out var value))
                {
                    binding.Set(value);
                }
                else if (previous.ContainsKey(arg.Name))
                {
                    binding.Set(arg.ConstantValue);
                }
            }
        }

        return _root is StoryRoot root ? root.RefreshAsync() : Task.CompletedTask;
    }

    /// <summary>Fake authentication for stories that declare a <see cref="TestUser"/>.</summary>
    public FakeAuthenticationStateProvider? Authentication { get; }

    /// <summary>Values held by two-way bindings (<c>Bind(...)</c>), keyed by parameter name.</summary>
    public IReadOnlyDictionary<string, object?> BoundValues => _state;

    /// <summary>Receiver for action callbacks (does not trigger a re-render of the story root).</summary>
    internal object ActionReceiver { get; } = new();

    /// <summary>Receiver for binding callbacks (re-renders the story root so new values flow down).</summary>
    internal object BindingReceiver => (object?)_root ?? ActionReceiver;

    internal void AttachRoot(IHandleEvent root) => _root = root;

    internal T GetOrCreate<T>(string key, Func<T> factory)
    {
        if (_cache.TryGetValue(key, out var existing))
        {
            return (T)existing!;
        }

        var value = factory();
        _cache[key] = value;
        return value;
    }

    internal BindingHolder<T> GetBinding<T>(string name, T initialValue) =>
        GetOrCreate("binding:" + name, () =>
        {
            var start = _argOverrides.TryGetValue(name, out var overridden) && (overridden is T || (overridden is null && default(T) is null))
                ? (T)overridden!
                : initialValue;
            var holder = new BindingHolder<T>(start, value => _state[name] = value);
            _state[name] = start;
            return holder;
        });

    private interface IBindingHolder
    {
        void Set(object? value);
    }

    /// <summary>Holds a bound value; its <see cref="Value"/> property backs <c>ValueExpression</c>.</summary>
    internal sealed class BindingHolder<T>(T initialValue, Action<T> onChanged) : IBindingHolder
    {
        private T _value = initialValue;

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                onChanged(value);
            }
        }

        void IBindingHolder.Set(object? value)
        {
            if (value is T typed)
            {
                Value = typed;
            }
            else if (value is null && default(T) is null)
            {
                Value = default!;
            }
        }
    }
}
