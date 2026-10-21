using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Workbench.Services;

/// <summary>
/// Per-scope (circuit, WebAssembly app or SSR request) holder of the active story's service overrides.
/// Frames are full page loads, so each story gets a fresh scope and its own overrides.
/// </summary>
public sealed class StoryServiceScope : IAsyncDisposable
{
    private readonly Dictionary<object, object> _instances = [];
    private readonly Lock _gate = new();
    private ServiceProvider? _overrides;

    /// <summary>The active story, if any.</summary>
    public Story? Story { get; private set; }

    /// <summary>Provider holding the story's overrides.</summary>
    public IServiceProvider? Overrides => _overrides;

    /// <summary>JavaScript-interop calls of the active story (recorded when it declares JS mocks).</summary>
    public JSCallLog JSCalls { get; private set; } = new();

    /// <summary>Activates a story: builds its override container and seeds fixtures.</summary>
    public async Task ActivateAsync(Story story, HostingModel hostingModel, FakeAuthenticationStateProvider? authentication)
    {
        ArgumentNullException.ThrowIfNull(story);
        await ReleaseAsync().ConfigureAwait(false);

        var services = new ServiceCollection();
        var fixtureContext = new FixtureContext(story, hostingModel);
        story.ConfigureServices(services, fixtureContext);
        if (authentication is not null)
        {
            services.AddSingleton<AuthenticationStateProvider>(authentication);
            services.AddSingleton(authentication);
        }

        _overrides = services.BuildServiceProvider();
        JSCalls = new JSCallLog();
        Story = story;
        await story.InitializeFixturesAsync(_overrides, fixtureContext).ConfigureAwait(false);
    }

    /// <summary>Resolves an override, or null when the story does not override the type.</summary>
    public object? Resolve(Type serviceType) => _overrides?.GetService(serviceType);

    /// <summary>Every registration the story makes for <paramref name="serviceType"/>, in registration order.</summary>
    public IEnumerable<object> ResolveAll(Type serviceType) =>
        _overrides is null ? [] : _overrides.GetServices(serviceType).OfType<object>();

    /// <summary>
    /// A per-story instance built on first use (story-scoped options, the decorated JS runtime...). Disposed with the
    /// scope or when another story is activated.
    /// </summary>
    internal T GetOrAdd<T>(object key, Func<T> factory)
        where T : class
    {
        lock (_gate)
        {
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
    public ValueTask DisposeAsync() => ReleaseAsync();

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
        }
    }
}
