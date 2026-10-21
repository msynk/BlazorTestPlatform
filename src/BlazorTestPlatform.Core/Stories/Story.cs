using System.Diagnostics.CodeAnalysis;
using BlazorTestPlatform.Performance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform;

/// <summary>
/// One named state of a component: its arguments, decorators, test data, interaction script (<see cref="Play"/>)
/// and quality gates (visual, accessibility, performance). A story is the single source of truth that the
/// workbench, bUnit, real-browser runs and the cross-hosting matrix all execute.
/// </summary>
public sealed class Story
{
    internal Story(StoryData data)
    {
        Title = data.Title;
        Name = data.Name;
        Id = StoryId.Create(data.Title, data.Name);
        ComponentType = data.ComponentType;
        Description = data.Description;
        Args = data.Args;
        Decorators = data.Decorators;
        Play = data.Play;
        Tags = data.Tags;
        Seed = data.Seed;
        User = data.User;
        ServiceConfigurations = data.ServiceConfigurations;
        Fixtures = data.Fixtures;
        HttpMocks = data.HttpMocks;
        JSInterop = data.JSInterop;
        FakeTimeStart = data.FakeTimeStart;
        Visual = data.Visual;
        Accessibility = data.Accessibility;
        Performance = data.Performance;
        Matrix = data.Matrix;
        Timeout = data.Timeout;
        SkipReason = data.SkipReason;
        Metadata = data.Metadata;
        CollectionType = data.CollectionType;
        Order = data.Order;
    }

    /// <summary>Stable identifier, e.g. <c>inputs-button--primary</c>.</summary>
    public string Id { get; }

    /// <summary>Hierarchical title, e.g. <c>Inputs/Button</c>.</summary>
    public string Title { get; }

    /// <summary>Story name within its title, e.g. <c>Primary</c>.</summary>
    public string Name { get; }

    /// <summary>The component rendered by the story.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type ComponentType { get; }

    /// <summary>Optional documentation shown in the workbench.</summary>
    public string? Description { get; }

    /// <summary>Component parameters.</summary>
    public IReadOnlyList<StoryArg> Args { get; }

    /// <summary>Decorators, innermost first.</summary>
    public IReadOnlyList<StoryDecorator> Decorators { get; }

    /// <summary>Interaction script executed after the first render by any driver.</summary>
    public Func<StoryContext, Task>? Play { get; }

    /// <summary>Tags used for filtering (e.g. <c>visual</c>, <c>a11y</c>, <c>smoke</c>, <c>!visual</c>).</summary>
    public IReadOnlySet<string> Tags { get; }

    /// <summary>Seed for every random source of the story. Defaults to a hash of <see cref="Id"/>.</summary>
    public int Seed { get; }

    /// <summary>Fake signed-in user, or null for no authentication state.</summary>
    public TestUser? User { get; }

    /// <summary>Service registrations that override the host's services.</summary>
    public IReadOnlyList<Action<IServiceCollection, FixtureContext>> ServiceConfigurations { get; }

    /// <summary>Data fixtures.</summary>
    public IReadOnlyList<StoryFixture> Fixtures { get; }

    /// <summary>JavaScript-interop mocks (null when none); see <see cref="JSInteropMocks"/>.</summary>
    public JSInteropMocks? JSInterop { get; }

    /// <summary>Configurations of the story's fake HTTP backend (see <see cref="HttpMock"/>).</summary>
    public IReadOnlyList<Action<HttpMock, FixtureContext>> HttpMocks { get; }

    /// <summary>When set, a <c>FakeTimeProvider</c> frozen at this instant replaces <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset? FakeTimeStart { get; }

    /// <summary>Visual-regression options.</summary>
    public VisualOptions Visual { get; }

    /// <summary>Accessibility options.</summary>
    public StoryAccessibilityOptions Accessibility { get; }

    /// <summary>Render-performance budget, or null when not measured.</summary>
    public PerformanceBudget? Performance { get; }

    /// <summary>Matrix restrictions.</summary>
    public MatrixFilter Matrix { get; }

    /// <summary>Overall timeout for render + play.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>When set, the story is skipped by test runners.</summary>
    public string? SkipReason { get; }

    /// <summary>Free-form metadata for extensions.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; }

    /// <summary>The collection type that declared the story.</summary>
    public Type? CollectionType { get; }

    /// <summary>Declaration order within the collection.</summary>
    public int Order { get; }

    /// <summary>Workbench layout (from <c>Layout(...)</c>; default <see cref="StoryLayout.Padded"/>).</summary>
    public StoryLayout Layout => Metadata.TryGetValue(StoryBuilderMetadata.Layout, out var layout) && layout is StoryLayout value ? value : StoryLayout.Padded;

    /// <summary>
    /// Whether browser runs prerender interactive modes (default true, like production). Stories whose initial work never
    /// completes (pending loads, authorizing users) must disable it: Blazor only hydrates after prerendering finishes.
    /// </summary>
    public bool Prerender => !Metadata.TryGetValue(StoryBuilderMetadata.Prerender, out var value) || value is not false;

    /// <summary><c>Title/Name</c>.</summary>
    public string Path => $"{Title}/{Name}";

    /// <summary>True when the story has a play function.</summary>
    public bool HasPlay => Play is not null;

    /// <summary>True when the story carries the tag.</summary>
    public bool HasTag(string tag) => Tags.Contains(tag);

    /// <summary>True when the story registers services or fixtures that must override the host's services.</summary>
    public bool HasServiceOverrides => ServiceConfigurations.Count > 0 || Fixtures.Count > 0 || FakeTimeStart is not null || HttpMocks.Count > 0;

    /// <summary>Builds the render fragment for the story (decorators + theme decorator + component).</summary>
    public RenderFragment Render(StoryRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderFragment fragment = builder => BuildComponent(builder, context);
        foreach (var decorator in Decorators)
        {
            fragment = decorator(fragment, context);
        }

        if (context.Theme?.Decorator is { } themeDecorator)
        {
            fragment = themeDecorator(fragment, context);
        }

        return fragment;
    }

    /// <summary>Applies the story's service overrides, fixtures and fake time to a service collection.</summary>
    public void ConfigureServices(IServiceCollection services, FixtureContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        if (FakeTimeStart is { } start)
        {
            var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(start);
            services.AddSingleton(fakeTime);
            services.AddSingleton<TimeProvider>(fakeTime);
        }

        foreach (var fixture in Fixtures)
        {
            fixture.ConfigureServices(services, context);
        }

        if (HttpMocks.Count > 0)
        {
            var mock = new HttpMock();
            foreach (var configure in HttpMocks)
            {
                configure(mock, context);
            }

            HttpMock Bound(IServiceProvider provider)
            {
                mock.Time = provider.GetService<TimeProvider>() ?? TimeProvider.System;
                return mock;
            }

            services.AddSingleton(Bound);
            services.AddSingleton<IHttpClientFactory>(provider => new HttpMockClientFactory(Bound(provider)));
            services.AddSingleton(provider => Bound(provider).CreateClient());
        }

        foreach (var configure in ServiceConfigurations)
        {
            configure(services, context);
        }
    }

    /// <summary>Runs fixture initialization (data seeding) against the built provider.</summary>
    public async Task InitializeFixturesAsync(IServiceProvider services, FixtureContext context)
    {
        foreach (var fixture in Fixtures)
        {
            await fixture.InitializeAsync(services, context).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Id;

    private void BuildComponent(RenderTreeBuilder builder, StoryRenderContext context)
    {
        builder.OpenComponent(0, ComponentType);
        var overrides = context.ArgOverrides;
        foreach (var arg in Args)
        {
            // Bindings read their holder, which applies overrides itself so user edits through the component survive renders.
            object? value;
            if (arg.Kind == StoryArgKind.Binding)
            {
                value = arg.CreateValue(context);
            }
            else if (arg.Kind == StoryArgKind.Value && overrides.TryGetValue(arg.Name, out var overridden))
            {
                value = overridden;
            }
            else
            {
                value = context.GetOrCreate("arg:" + arg.Name, () => arg.CreateValue(context));
            }

            builder.AddComponentParameter(1, arg.Name, value);
        }

        // Overrides of parameters the story leaves at the component's default, and defaults of removed overrides.
        foreach (var (name, value) in overrides)
        {
            if (!Args.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                builder.AddComponentParameter(2, name, value);
            }
        }

        foreach (var (name, value) in context.ArgResets)
        {
            if (!overrides.ContainsKey(name))
            {
                builder.AddComponentParameter(3, name, value);
            }
        }

        builder.CloseComponent();
    }
}

/// <summary>Well-known metadata keys.</summary>
public static class StoryBuilderMetadata
{
    /// <summary>Key of the <see cref="StoryLayout"/> metadata.</summary>
    public const string Layout = "btp:layout";

    /// <summary>Key of the prerender flag metadata.</summary>
    public const string Prerender = "btp:prerender";
}

/// <summary>Mutable data collected by builders.</summary>
internal sealed class StoryData
{
    public required string Title { get; init; }

    public required string Name { get; init; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public required Type ComponentType { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<StoryArg> Args { get; init; } = [];

    public IReadOnlyList<StoryDecorator> Decorators { get; init; } = [];

    public Func<StoryContext, Task>? Play { get; init; }

    public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();

    public int Seed { get; init; }

    public TestUser? User { get; init; }

    public IReadOnlyList<Action<IServiceCollection, FixtureContext>> ServiceConfigurations { get; init; } = [];

    public IReadOnlyList<StoryFixture> Fixtures { get; init; } = [];

    public IReadOnlyList<Action<HttpMock, FixtureContext>> HttpMocks { get; init; } = [];

    public JSInteropMocks? JSInterop { get; init; }

    public DateTimeOffset? FakeTimeStart { get; init; }

    public VisualOptions Visual { get; init; } = new();

    public StoryAccessibilityOptions Accessibility { get; init; } = new();

    public PerformanceBudget? Performance { get; init; }

    public MatrixFilter Matrix { get; init; } = new();

    public TimeSpan? Timeout { get; init; }

    public string? SkipReason { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    public Type? CollectionType { get; init; }

    public int Order { get; init; }
}
