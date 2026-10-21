using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using BlazorTestPlatform.Performance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform;

/// <summary>Component-agnostic story configuration, used by global and shared defaults.</summary>
public interface IStoryBuilder
{
    /// <summary>The story name.</summary>
    string Name { get; }

    /// <summary>The component type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    Type ComponentType { get; }

    /// <summary>Wraps the story in a decorator (decorators added first wrap closest to the component).</summary>
    IStoryBuilder Decorate(StoryDecorator decorator);

    /// <summary>Adds tags.</summary>
    IStoryBuilder Tags(params string[] tags);

    /// <summary>Sets the seed of every random source in the story.</summary>
    IStoryBuilder Seed(int seed);

    /// <summary>Signs a fake user in.</summary>
    IStoryBuilder User(TestUser user);

    /// <summary>Registers service overrides.</summary>
    IStoryBuilder Services(Action<IServiceCollection, FixtureContext> configure);

    /// <summary>Attaches a data fixture.</summary>
    IStoryBuilder UseFixture(StoryFixture fixture);

    /// <summary>Freezes <see cref="TimeProvider"/> at the given instant.</summary>
    IStoryBuilder UseFakeTime(DateTimeOffset start);

    /// <summary>Answers the story's HTTP requests from a fake backend.</summary>
    IStoryBuilder MockHttp(Action<HttpMock, FixtureContext> configure);

    /// <summary>Configures visual-regression options.</summary>
    IStoryBuilder Visual(Action<VisualOptions> configure);

    /// <summary>Configures accessibility options.</summary>
    IStoryBuilder Accessibility(Action<StoryAccessibilityOptions> configure);

    /// <summary>Sets a render-performance budget.</summary>
    IStoryBuilder Performance(Action<PerformanceBudget> configure);

    /// <summary>Restricts matrix cells.</summary>
    IStoryBuilder Matrix(Action<MatrixFilter> configure);

    /// <summary>Sets the render + play timeout.</summary>
    IStoryBuilder Timeout(TimeSpan timeout);

    /// <summary>Enables or disables automatic action logging for unset <c>EventCallback</c> parameters.</summary>
    IStoryBuilder AutoActions(bool enabled = true);

    /// <summary>Attaches metadata for extensions.</summary>
    IStoryBuilder Metadata(string key, object? value);
}

/// <summary>Fluent builder for one story of <typeparamref name="TComponent"/>.</summary>
public sealed class StoryBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> : IStoryBuilder
    where TComponent : IComponent
{
    private readonly List<StoryArg> _args = [];
    private readonly List<StoryDecorator> _decorators = [];
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<IServiceCollection, FixtureContext>> _services = [];
    private readonly List<StoryFixture> _fixtures = [];
    private readonly List<Action<HttpMock, FixtureContext>> _httpMocks = [];
    private JSInteropMocks? _jsInterop;
    private readonly Dictionary<string, object?> _metadata = new(StringComparer.Ordinal);
    private readonly VisualOptions _visual = new();
    private readonly StoryAccessibilityOptions _accessibility = new();
    private readonly MatrixFilter _matrix = new();
    private PerformanceBudget? _performance;
    private Func<StoryContext, Task>? _play;
    private string? _description;
    private int? _seed;
    private TestUser? _user;
    private DateTimeOffset? _fakeTime;
    private TimeSpan? _timeout;
    private string? _skipReason;
    private bool _autoActions = true;

    internal StoryBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type ComponentType => typeof(TComponent);

    /// <summary>Sets a parameter value.</summary>
    public StoryBuilder<TComponent> Arg<TValue>(Expression<Func<TComponent, TValue>> parameter, TValue value)
    {
        var name = ComponentParameters.GetName(parameter);
        return SetArg(new StoryArg(name, KindOf(typeof(TValue)), typeof(TValue), value, _ => value, StoryArg.Describe(value)));
    }

    /// <summary>Sets a parameter value computed per render context (e.g. from seeded fake data or services).</summary>
    public StoryBuilder<TComponent> Arg<TValue>(Expression<Func<TComponent, TValue>> parameter, Func<StoryRenderContext, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var name = ComponentParameters.GetName(parameter);
        return SetArg(new StoryArg(name, KindOf(typeof(TValue)), typeof(TValue), ctx => factory(ctx), "(computed)"));
    }

    /// <summary>Passes an unmatched attribute (captured by <c>CaptureUnmatchedValues</c>), e.g. <c>class</c> or <c>aria-label</c>.</summary>
    public StoryBuilder<TComponent> Attribute(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SetArg(new StoryArg(name, StoryArgKind.Value, value?.GetType(), value, _ => value, StoryArg.Describe(value)));
    }

    /// <summary>Sets <c>ChildContent</c>.</summary>
    public StoryBuilder<TComponent> ChildContent(RenderFragment content) =>
        SetArg(new StoryArg("ChildContent", StoryArgKind.Fragment, typeof(RenderFragment), _ => content, "(fragment)"));

    /// <summary>Sets <c>ChildContent</c> to raw markup.</summary>
    public StoryBuilder<TComponent> ChildContent(string markup)
    {
        RenderFragment fragment = b => b.AddMarkupContent(0, markup);
        return SetArg(new StoryArg("ChildContent", StoryArgKind.Fragment, typeof(RenderFragment), _ => fragment, markup));
    }

    /// <summary>Sets a <see cref="RenderFragment"/> parameter.</summary>
    public StoryBuilder<TComponent> Fragment(Expression<Func<TComponent, RenderFragment?>> parameter, RenderFragment fragment)
    {
        var name = ComponentParameters.GetName(parameter);
        return SetArg(new StoryArg(name, StoryArgKind.Fragment, typeof(RenderFragment), _ => fragment, "(fragment)"));
    }

    /// <summary>Sets a <see cref="RenderFragment{TValue}"/> template parameter.</summary>
    public StoryBuilder<TComponent> Template<TItem>(Expression<Func<TComponent, RenderFragment<TItem>?>> parameter, RenderFragment<TItem> template)
    {
        var name = ComponentParameters.GetName(parameter);
        return SetArg(new StoryArg(name, StoryArgKind.Fragment, typeof(RenderFragment<TItem>), _ => template, "(template)"));
    }

    /// <summary>Logs invocations of an <see cref="EventCallback"/> parameter into the action log.</summary>
    public StoryBuilder<TComponent> Action(Expression<Func<TComponent, EventCallback>> parameter)
    {
        var name = ComponentParameters.GetName(parameter);
        return SetArg(new StoryArg(name, StoryArgKind.Action, typeof(EventCallback), ctx => ComponentParameters.CreateActionCallback(typeof(EventCallback), name, ctx), "(action)"));
    }

    /// <summary>Logs invocations of an <see cref="EventCallback{TValue}"/> parameter into the action log.</summary>
    public StoryBuilder<TComponent> Action<TArg>(Expression<Func<TComponent, EventCallback<TArg>>> parameter)
    {
        var name = ComponentParameters.GetName(parameter);
        return SetArg(new StoryArg(name, StoryArgKind.Action, typeof(EventCallback<TArg>), ctx => ComponentParameters.CreateActionCallback(typeof(EventCallback<TArg>), name, ctx), "(action)"));
    }

    /// <summary>
    /// Two-way binds a parameter: supplies <c>{Name}</c>, handles <c>{Name}Changed</c> (re-rendering with the new value
    /// and logging an action) and supplies <c>{Name}Expression</c> when the component declares it (e.g. <c>InputBase</c>).
    /// </summary>
    public StoryBuilder<TComponent> Bind<TValue>(Expression<Func<TComponent, TValue>> parameter, TValue initialValue)
    {
        var name = ComponentParameters.GetName(parameter);
        var changedName = name + "Changed";
        var changed = ComponentParameters.Find(typeof(TComponent), changedName)
            ?? throw new ArgumentException($"Cannot bind '{name}': '{typeof(TComponent).Name}' has no '{changedName}' parameter.", nameof(parameter));

        if (changed.PropertyType != typeof(EventCallback<TValue>))
        {
            throw new ArgumentException($"Cannot bind '{name}': '{changedName}' must be EventCallback<{typeof(TValue).Name}>.", nameof(parameter));
        }

        SetArg(new StoryArg(name, StoryArgKind.Binding, typeof(TValue), initialValue, ctx => ctx.GetBinding(name, initialValue).Value, StoryArg.Describe(initialValue)));
        SetArg(new StoryArg(changedName, StoryArgKind.Binding, typeof(EventCallback<TValue>), ctx => ctx.GetOrCreate("bind-changed:" + name, () =>
            EventCallback.Factory.Create<TValue>(ctx.BindingReceiver, (TValue value) =>
            {
                ctx.GetBinding(name, initialValue).Value = value;
                ctx.Actions.Record(changedName, value);
            })), "(binding)"));

        if (ComponentParameters.Find(typeof(TComponent), name + "Expression") is { } expressionParameter
            && expressionParameter.PropertyType == typeof(Expression<Func<TValue>>))
        {
            SetArg(new StoryArg(name + "Expression", StoryArgKind.Binding, expressionParameter.PropertyType, ctx => ctx.GetOrCreate("bind-expression:" + name, () =>
            {
                var holder = ctx.GetBinding(name, initialValue);
                var valueProperty = typeof(StoryRenderContext.BindingHolder<TValue>).GetProperty(nameof(StoryRenderContext.BindingHolder<TValue>.Value))!;
                return Expression.Lambda<Func<TValue>>(Expression.Property(Expression.Constant(holder), valueProperty));
            }), null));
        }

        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.AutoActions" />
    public StoryBuilder<TComponent> AutoActions(bool enabled = true)
    {
        _autoActions = enabled;
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Decorate" />
    public StoryBuilder<TComponent> Decorate(StoryDecorator decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);
        _decorators.Add(decorator);
        return this;
    }

    /// <summary>Wraps the story with a fragment transform.</summary>
    public StoryBuilder<TComponent> Decorate(Func<RenderFragment, RenderFragment> decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);
        return Decorate((story, _) => decorator(story));
    }

    /// <summary>Wraps the story in a component that renders <c>ChildContent</c> (providers, layouts...).</summary>
    public StoryBuilder<TComponent> Wrap<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TWrapper>(IReadOnlyDictionary<string, object?>? parameters = null)
        where TWrapper : IComponent =>
        Decorate((story, _) => builder =>
        {
            builder.OpenComponent<TWrapper>(0);
            if (parameters is not null)
            {
                foreach (var pair in parameters)
                {
                    builder.AddComponentParameter(1, pair.Key, pair.Value);
                }
            }

            builder.AddComponentParameter(2, "ChildContent", story);
            builder.CloseComponent();
        });

    /// <summary>Wraps the story in a <c>div</c> with an inline style and/or class (e.g. to constrain width).</summary>
    public StoryBuilder<TComponent> Container(string? style = null, string? cssClass = null) =>
        Decorate((story, _) => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "style", style);
            builder.AddAttribute(2, "class", cssClass);
            builder.AddContent(3, story);
            builder.CloseElement();
        });

    /// <summary>Supplies a cascading value to the component.</summary>
    public StoryBuilder<TComponent> CascadingValue<TValue>(TValue value, string? name = null) =>
        Decorate((story, _) => builder =>
        {
            builder.OpenComponent<CascadingValue<TValue>>(0);
            builder.AddComponentParameter(1, "Value", value);
            if (name is not null)
            {
                builder.AddComponentParameter(2, "Name", name);
            }

            builder.AddComponentParameter(3, "IsFixed", true);
            builder.AddComponentParameter(4, "ChildContent", story);
            builder.CloseComponent();
        });

    /// <summary>Provides a cascading <see cref="EditContext"/> so form inputs (<c>InputText</c>...) render outside an <c>EditForm</c>.</summary>
    public StoryBuilder<TComponent> WithEditContext(object? model = null) =>
        Decorate((story, ctx) => builder =>
        {
            var editContext = ctx.GetOrCreate("edit-context", () => new EditContext(model ?? new object()));
            builder.OpenComponent<CascadingValue<EditContext>>(0);
            builder.AddComponentParameter(1, "Value", editContext);
            builder.AddComponentParameter(2, "IsFixed", true);
            builder.AddComponentParameter(3, "ChildContent", story);
            builder.CloseComponent();
        });

    /// <summary>Sets the interaction script, executed after the first render by every driver.</summary>
    public StoryBuilder<TComponent> Play(Func<StoryContext, Task> play)
    {
        _play = play ?? throw new ArgumentNullException(nameof(play));
        return this;
    }

    /// <summary>Sets how the workbench lays the story out.</summary>
    public StoryBuilder<TComponent> Layout(StoryLayout layout) => Metadata(StoryBuilderMetadata.Layout, layout);

    /// <summary>Enables or disables prerendering of interactive modes in browser runs (see <see cref="Story.Prerender"/>).</summary>
    public StoryBuilder<TComponent> Prerender(bool enabled) => Metadata(StoryBuilderMetadata.Prerender, enabled);

    /// <summary>Sets documentation shown in the workbench.</summary>
    public StoryBuilder<TComponent> Description(string description)
    {
        _description = description;
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Tags" />
    public StoryBuilder<TComponent> Tags(params string[] tags)
    {
        foreach (var tag in tags)
        {
            if (tag.StartsWith('!'))
            {
                _tags.Remove(tag[1..]);
            }
            else
            {
                _tags.Add(tag);
            }
        }

        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Seed" />
    public StoryBuilder<TComponent> Seed(int seed)
    {
        _seed = seed;
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.User" />
    public StoryBuilder<TComponent> User(TestUser user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        return this;
    }

    /// <summary>Signs in a fake user with roles.</summary>
    public StoryBuilder<TComponent> AuthenticatedAs(string name, params string[] roles) => User(TestUser.Named(name).WithRoles(roles));

    /// <summary>Provides an anonymous authentication state (for <c>AuthorizeView</c>).</summary>
    public StoryBuilder<TComponent> Anonymous() => User(TestUser.Anonymous);

    /// <summary>Registers service overrides.</summary>
    public StoryBuilder<TComponent> Services(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _services.Add((services, _) => configure(services));
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Services" />
    public StoryBuilder<TComponent> Services(Action<IServiceCollection, FixtureContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _services.Add(configure);
        return this;
    }

    /// <summary>Attaches a data fixture.</summary>
    public StoryBuilder<TComponent> UseFixture<TFixture>()
        where TFixture : StoryFixture, new() => UseFixture(new TFixture());

    /// <inheritdoc cref="IStoryBuilder.UseFixture" />
    public StoryBuilder<TComponent> UseFixture(StoryFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixtures.Add(fixture);
        return this;
    }

    /// <summary>
    /// Answers HTTP requests made through <see cref="HttpClient"/> or <see cref="IHttpClientFactory"/> (named and typed
    /// clients) from a fake backend, in bUnit, Interactive Server and WebAssembly alike. Calls accumulate; rules added
    /// later win, so stories can override collection defaults.
    /// </summary>
    public StoryBuilder<TComponent> MockHttp(Action<HttpMock> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _httpMocks.Add((mock, _) => configure(mock));
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.MockHttp" />
    public StoryBuilder<TComponent> MockHttp(Action<HttpMock, FixtureContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _httpMocks.Add(configure);
        return this;
    }

    /// <summary>
    /// Canned JavaScript-interop results. They always apply in bUnit; in browsers only with
    /// <see cref="JSInteropMocks.InBrowsers"/> (otherwise the real JavaScript runs). Stories with mocks record their JS
    /// calls under every driver (<c>ExpectJSCall</c>). Calls accumulate.
    /// </summary>
    public StoryBuilder<TComponent> MockJS(Action<JSInteropMocks> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var mocks = new JSInteropMocks();
        configure(mocks);
        (_jsInterop ??= new JSInteropMocks()).MergeFrom(mocks);
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.UseFakeTime" />
    public StoryBuilder<TComponent> UseFakeTime(DateTimeOffset start)
    {
        _fakeTime = start;
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Visual" />
    public StoryBuilder<TComponent> Visual(Action<VisualOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_visual);
        return this;
    }

    /// <summary>Excludes the story from visual regression.</summary>
    public StoryBuilder<TComponent> NoVisual() => Visual(v => v.Skip = true);

    /// <inheritdoc cref="IStoryBuilder.Accessibility" />
    public StoryBuilder<TComponent> Accessibility(Action<StoryAccessibilityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_accessibility);
        return this;
    }

    /// <summary>Disables an accessibility rule for this story. A justification is mandatory and appears in reports.</summary>
    public StoryBuilder<TComponent> DisableAccessibilityRule(string ruleId, string justification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        _accessibility.DisabledRules.Add(ruleId);
        _accessibility.Justifications[ruleId] = justification;
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Performance" />
    public StoryBuilder<TComponent> Performance(Action<PerformanceBudget> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _performance ??= new PerformanceBudget();
        configure(_performance);
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Matrix" />
    public StoryBuilder<TComponent> Matrix(Action<MatrixFilter> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_matrix);
        return this;
    }

    /// <summary>Runs the story only under the given hosting models.</summary>
    public StoryBuilder<TComponent> OnlyHostingModels(params HostingModel[] models) => Matrix(m => m.HostingModels.UnionWith(models));

    /// <summary>Never runs the story under the given hosting models.</summary>
    public StoryBuilder<TComponent> ExcludeHostingModels(params HostingModel[] models) => Matrix(m => m.ExcludedHostingModels.UnionWith(models));

    /// <inheritdoc cref="IStoryBuilder.Timeout" />
    public StoryBuilder<TComponent> Timeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
        return this;
    }

    /// <summary>Marks the story as skipped by test runners (it still appears in the workbench).</summary>
    public StoryBuilder<TComponent> Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _skipReason = reason;
        return this;
    }

    /// <inheritdoc cref="IStoryBuilder.Metadata" />
    public StoryBuilder<TComponent> Metadata(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _metadata[key] = value;
        return this;
    }

    internal StoryBuilder<TComponent> SetValueArg(string name, object? value, Type valueType) =>
        SetArg(new StoryArg(name, KindOf(valueType), valueType, value, _ => value, StoryArg.Describe(value)));

    internal Story Build(string title, Type? collectionType, int order)
    {
        var args = _args.ToList();
        if (_autoActions)
        {
            foreach (var parameter in ComponentParameters.Of(typeof(TComponent)).Values)
            {
                if (ComponentParameters.IsEventCallback(parameter.PropertyType)
                    && !args.Any(a => string.Equals(a.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    && parameter.IsDefined(typeof(ParameterAttribute), inherit: true))
                {
                    var name = parameter.Name;
                    var type = parameter.PropertyType;
                    args.Add(new StoryArg(name, StoryArgKind.Action, type, ctx => ComponentParameters.CreateActionCallback(type, name, ctx), "(auto action)"));
                }
            }
        }

        var id = StoryId.Create(title, Name);
        return new Story(new StoryData
        {
            Title = title,
            Name = Name,
            ComponentType = typeof(TComponent),
            Description = _description,
            Args = args,
            Decorators = _decorators.ToArray(),
            Play = _play,
            Tags = _tags.ToHashSet(StringComparer.OrdinalIgnoreCase),
            Seed = _seed ?? (int)(DeterministicRandom.StableHash(id) & 0x7FFFFFFF),
            User = _user,
            ServiceConfigurations = _services.ToArray(),
            Fixtures = _fixtures.ToArray(),
            HttpMocks = _httpMocks.ToArray(),
            JSInterop = _jsInterop,
            FakeTimeStart = _fakeTime,
            Visual = _visual.Clone(),
            Accessibility = _accessibility.Clone(),
            Performance = _performance?.Clone(),
            Matrix = _matrix.Clone(),
            Timeout = _timeout,
            SkipReason = _skipReason,
            Metadata = new Dictionary<string, object?>(_metadata),
            CollectionType = collectionType,
            Order = order,
        });
    }

    private StoryBuilder<TComponent> SetArg(StoryArg arg)
    {
        _args.RemoveAll(a => string.Equals(a.Name, arg.Name, StringComparison.OrdinalIgnoreCase));
        _args.Add(arg);
        return this;
    }

    private static StoryArgKind KindOf(Type type) =>
        type == typeof(RenderFragment) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RenderFragment<>))
            ? StoryArgKind.Fragment
            : StoryArgKind.Value;

    IStoryBuilder IStoryBuilder.Decorate(StoryDecorator decorator) => Decorate(decorator);

    IStoryBuilder IStoryBuilder.Tags(params string[] tags) => Tags(tags);

    IStoryBuilder IStoryBuilder.Seed(int seed) => Seed(seed);

    IStoryBuilder IStoryBuilder.User(TestUser user) => User(user);

    IStoryBuilder IStoryBuilder.Services(Action<IServiceCollection, FixtureContext> configure) => Services(configure);

    IStoryBuilder IStoryBuilder.UseFixture(StoryFixture fixture) => UseFixture(fixture);

    IStoryBuilder IStoryBuilder.UseFakeTime(DateTimeOffset start) => UseFakeTime(start);

    IStoryBuilder IStoryBuilder.MockHttp(Action<HttpMock, FixtureContext> configure) => MockHttp(configure);

    IStoryBuilder IStoryBuilder.Visual(Action<VisualOptions> configure) => Visual(configure);

    IStoryBuilder IStoryBuilder.Accessibility(Action<StoryAccessibilityOptions> configure) => Accessibility(configure);

    IStoryBuilder IStoryBuilder.Performance(Action<PerformanceBudget> configure) => Performance(configure);

    IStoryBuilder IStoryBuilder.Matrix(Action<MatrixFilter> configure) => Matrix(configure);

    IStoryBuilder IStoryBuilder.Timeout(TimeSpan timeout) => Timeout(timeout);

    IStoryBuilder IStoryBuilder.AutoActions(bool enabled) => AutoActions(enabled);

    IStoryBuilder IStoryBuilder.Metadata(string key, object? value) => Metadata(key, value);
}
