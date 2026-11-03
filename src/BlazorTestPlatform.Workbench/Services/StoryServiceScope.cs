using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Workbench.Services;

/// <summary>
/// Per-scope (circuit, WebAssembly app or SSR request) holder of the active story's service overrides.
/// Frames are full page loads, so each story gets a fresh scope and its own overrides.
/// </summary>
/// <remarks>
/// The story's registrations live in their own container, but their dependencies resolve as in bUnit: from the story
/// first, then from the application (a fake service can take an <c>ILogger&lt;T&gt;</c>, <c>NavigationManager</c> or
/// an application service). Application instances are only borrowed: the story container never owns, and so never
/// disposes, them. A story service that depends on the type it is registered as (a decorator, such as
/// <c>SlowWeather(IWeather inner)</c> registered as <c>IWeather</c>) receives the application's service.
/// </remarks>
public sealed class StoryServiceScope : IAsyncDisposable
{
    // Story registrations being constructed on this thread: while one is built, its own type resolves from the
    // application, so a decorator wraps the application's service instead of recursing into itself (factories are
    // opaque to the container's cycle detection, and the recursion would end in a stack overflow).
    [ThreadStatic]
    private static HashSet<(StoryServiceScope Scope, Type Service)>? t_building;

    private readonly Dictionary<object, object> _instances = [];
    private readonly Lock _gate = new();
    private readonly IServiceProvider? _application;
    private ServiceProvider? _overrides;
    private IServiceProvider? _resolver;
    private bool _disposed;

    /// <summary>Creates a scope whose story services can only depend on each other.</summary>
    public StoryServiceScope()
    {
    }

    /// <summary>Creates a scope whose story services can also depend on the application's services.</summary>
    /// <param name="application">The application's provider for this scope (circuit, WebAssembly app or request).</param>
    [ActivatorUtilitiesConstructor]
    public StoryServiceScope(IServiceProvider application) => _application = application;

    /// <summary>The active story, if any.</summary>
    public Story? Story { get; private set; }

    /// <summary>Provider holding the story's overrides.</summary>
    public IServiceProvider? Overrides => _overrides;

    /// <summary>
    /// Resolves the story's services first and the application's otherwise (what fixtures and story services see).
    /// Null before a story is activated.
    /// </summary>
    public IServiceProvider? Services => _resolver;

    /// <summary>JavaScript-interop calls of the active story (recorded when it declares JS mocks).</summary>
    public JSCallLog JSCalls { get; private set; } = new();

    /// <summary>Activates a story: builds its override container and seeds fixtures.</summary>
    public async Task ActivateAsync(Story story, HostingModel hostingModel, FakeAuthenticationStateProvider? authentication)
    {
        ArgumentNullException.ThrowIfNull(story);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await ReleaseAsync().ConfigureAwait(false);

        var services = new ServiceCollection();
        var fixtureContext = new FixtureContext(story, hostingModel);
        story.ConfigureServices(services, fixtureContext);
        if (authentication is not null)
        {
            services.AddSingleton<AuthenticationStateProvider>(authentication);
            services.AddSingleton(authentication);
        }

        var resolver = new FallbackServiceProvider(this, _application);
        IServiceCollection container = new ServiceCollection();
        foreach (var descriptor in services)
        {
            container.Add(Rewrite(descriptor, resolver));
        }

        _overrides = container.BuildServiceProvider();
        resolver.Story = _overrides;
        _resolver = resolver;
        JSCalls = new JSCallLog();
        Story = story;
        await story.InitializeFixturesAsync(resolver, fixtureContext).ConfigureAwait(false);
    }

    /// <summary>
    /// Story registrations are created against <paramref name="resolver"/>, so their dependencies can come from the
    /// application. Instances and open generics stay as registered (open generics then only see the story's services).
    /// </summary>
    private ServiceDescriptor Rewrite(ServiceDescriptor descriptor, IServiceProvider resolver)
    {
        if (descriptor.IsKeyedService || descriptor.ImplementationInstance is not null || descriptor.ServiceType.IsGenericTypeDefinition)
        {
            return descriptor;
        }

        var serviceType = descriptor.ServiceType;
        if (descriptor.ImplementationFactory is { } factory)
        {
            return new ServiceDescriptor(serviceType, _ => Build(serviceType, () => factory(resolver)), descriptor.Lifetime);
        }

        return descriptor.ImplementationType is null
            ? descriptor
            : new ServiceDescriptor(serviceType, _ => Build(serviceType, () => ActivatorUtilities.CreateInstance(resolver, descriptor.ImplementationType)), descriptor.Lifetime);
    }

    private object Build(Type serviceType, Func<object> create)
    {
        var building = t_building ??= [];
        if (!building.Add((this, serviceType)))
        {
            throw new InvalidOperationException($"A circular dependency was detected while building the story service '{serviceType}'.");
        }

        try
        {
            return create();
        }
        finally
        {
            building.Remove((this, serviceType));
        }
    }

    private bool IsBuilding(Type serviceType) => t_building?.Contains((this, serviceType)) == true;

    /// <summary>The story's container first, then the application's provider for this scope.</summary>
    private sealed class FallbackServiceProvider(StoryServiceScope owner, IServiceProvider? application) : IServiceProvider, IServiceProviderIsService
    {
        public IServiceProvider? Story { get; set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceProviderIsService))
            {
                return this;
            }

            // The story's registration of this type is being built: the dependency is the service it decorates.
            var own = owner.IsBuilding(serviceType) ? null : Story?.GetService(serviceType);

            // An enumerable the story has nothing for comes back empty, not null: then the application's apply.
            if (own is not null && !(own is System.Collections.IEnumerable items && serviceType.IsGenericType
                && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>) && !items.GetEnumerator().MoveNext()))
            {
                return own;
            }

            return application?.GetService(serviceType) ?? own;
        }

        public bool IsService(Type serviceType) =>
            serviceType == typeof(IServiceProvider)
            || Story?.GetService(typeof(IServiceProviderIsService)) is IServiceProviderIsService story && story.IsService(serviceType)
            || application?.GetService(typeof(IServiceProviderIsService)) is IServiceProviderIsService app && app.IsService(serviceType);
    }

    /// <summary>
    /// Resolves an override, or null when the story does not override the type (or its override is being built and
    /// depends on the application's service).
    /// </summary>
    public object? Resolve(Type serviceType) => IsBuilding(serviceType) ? null : _overrides?.GetService(serviceType);

    /// <summary>Every registration the story makes for <paramref name="serviceType"/>, in registration order.</summary>
    public IEnumerable<object> ResolveAll(Type serviceType) =>
        _overrides is null || IsBuilding(serviceType) ? [] : _overrides.GetServices(serviceType).OfType<object>();

    /// <summary>
    /// A per-story instance built on first use (story-scoped options, the decorated JS runtime...). Disposed with the
    /// scope or when another story is activated.
    /// </summary>
    internal T GetOrAdd<T>(object key, Func<T> factory)
        where T : class
    {
        lock (_gate)
        {
            // An instance created after disposal would never be disposed.
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_instances.TryGetValue(key, out var existing))
            {
                return (T)existing;
            }

            var created = factory();
            _instances[key] = created;
            return created;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        await ReleaseAsync().ConfigureAwait(false);
    }

    private async ValueTask ReleaseAsync()
    {
        object[] instances;
        lock (_gate)
        {
            instances = [.. _instances.Values];
            _instances.Clear();
        }

        foreach (var instance in instances)
        {
            if (instance is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        if (_overrides is not null)
        {
            await _overrides.DisposeAsync().ConfigureAwait(false);
            _overrides = null;
            _resolver = null;
        }

        // Nothing of the released story may leak into the next one (or into resolutions after disposal).
        Story = null;
        JSCalls = new JSCallLog();
    }
}
