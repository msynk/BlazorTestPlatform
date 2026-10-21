using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BlazorTestPlatform;
using BlazorTestPlatform.Workbench;
using BlazorTestPlatform.Workbench.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the BlazorTestPlatform workbench.</summary>
public static class WorkbenchServiceCollectionExtensions
{
    internal const string OriginalKey = "btp:original";

    /// <summary>
    /// Registers the workbench, its story catalog and story-scoped service overrides.
    /// Call it in both the server and the WebAssembly client <c>Program.cs</c>, <b>after</b> the application's own
    /// registrations: services that stories override (via <c>Services(...)</c>, fixtures, fake users, fake time or JS
    /// mocks) are wrapped so each story frame resolves its own fakes while every other service stays real.
    /// </summary>
    // The server asks WebAssembly clients to render StoryHost by name; nothing in the client references it statically,
    // so without this the trimmer removes it from published (trimmed) WebAssembly apps.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BlazorTestPlatform.Workbench.Components.StoryHost))]
    public static IServiceCollection AddBlazorTestWorkbench(this IServiceCollection services, Action<WorkbenchOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(d => d.ServiceType == typeof(WorkbenchOptions)))
        {
            throw new InvalidOperationException("AddBlazorTestWorkbench was already called on this service collection.");
        }

        var options = new WorkbenchOptions();
        configure(options);
        var catalog = options.BuildCatalog();

        services.AddSingleton(options);
        services.AddSingleton(catalog);
        services.AddScoped<StoryServiceScope>();
        services.AddAuthorizationCore();

        var overridden = DiscoverOverriddenServices(catalog);
        foreach (var (serviceType, registrations) in overridden.Services)
        {
            if (serviceType == typeof(IJSRuntime) && overridden.UsesJSMocks)
            {
                // A story's own IJSRuntime becomes the inner runtime of the JS-mock decorator (see WrapJSRuntime).
                continue;
            }

            switch (Classify(services, serviceType, registrations))
            {
                case OverrideStrategy.Replace:
                    WrapForOverrides(services, serviceType);
                    break;
                case OverrideStrategy.Compose:
                    WrapForComposition(services, serviceType);
                    break;
                default:
                    options.UnwrappableOverrides.Add(serviceType);
                    break;
            }
        }

        foreach (var optionsType in overridden.Options)
        {
            ScopeOptions(services, optionsType);
        }

        if (overridden.UsesJSMocks && !WrapJSRuntime(services))
        {
            options.UnwrappableOverrides.Add(typeof(IJSRuntime));
        }

        return services;
    }

    // Contracts consumed once, when the application (or a circuit) starts, before any story frame exists: a story cannot
    // add to them, so story registrations of these never apply in browsers (the workbench flags the story instead).
    private static readonly HashSet<string> AppStartContracts = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Hosting.IHostedService",
        "Microsoft.Extensions.Logging.ILoggerProvider",
        "Microsoft.AspNetCore.Hosting.IStartupFilter",
        "Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler",
        "Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter",
    };

    // Contracts resolved as IEnumerable<T>: story registrations are added to the application's (as services.Add would)
    // instead of replacing them.
    private static readonly HashSet<string> EnumerableContracts = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Options.IConfigureOptions`1",
        "Microsoft.Extensions.Options.IConfigureNamedOptions`1",
        "Microsoft.Extensions.Options.IPostConfigureOptions`1",
        "Microsoft.Extensions.Options.IValidateOptions`1",
        "Microsoft.Extensions.Options.IOptionsChangeTokenSource`1",
        "Microsoft.AspNetCore.Authorization.IAuthorizationHandler",
    };

    private static readonly HashSet<string> OptionsContracts = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Options.IConfigureOptions`1",
        "Microsoft.Extensions.Options.IConfigureNamedOptions`1",
        "Microsoft.Extensions.Options.IPostConfigureOptions`1",
        "Microsoft.Extensions.Options.IValidateOptions`1",
    };

    internal enum OverrideStrategy
    {
        /// <summary>Single service: the story's registration replaces the application's for that frame.</summary>
        Replace,

        /// <summary>Multi-registration service: the story's registrations are appended to the application's.</summary>
        Compose,

        /// <summary>Consumed at application start: cannot be overridden per story.</summary>
        Unsupported,
    }

    internal static OverrideStrategy Classify(IServiceCollection services, Type serviceType, int storyRegistrations = 1)
    {
        var name = ContractName(serviceType);
        if (name is not null && AppStartContracts.Contains(name))
        {
            return OverrideStrategy.Unsupported;
        }

        if ((name is not null && EnumerableContracts.Contains(name))
            || storyRegistrations > 1
            || services.Count(d => d.ServiceType == serviceType && !d.IsKeyedService) > 1)
        {
            return OverrideStrategy.Compose;
        }

        return OverrideStrategy.Replace;
    }

    /// <summary>Kept for compatibility with earlier previews: true when the story's registration can replace the application's.</summary>
    internal static bool CanWrap(IServiceCollection services, Type serviceType) => Classify(services, serviceType) == OverrideStrategy.Replace;

    private static string? ContractName(Type serviceType) => (serviceType.IsGenericType ? serviceType.GetGenericTypeDefinition() : serviceType).FullName;

    private sealed record OverriddenServices(Dictionary<Type, int> Services, HashSet<Type> Options, bool UsesJSMocks);

    private static OverriddenServices DiscoverOverriddenServices(StoryCatalog catalog)
    {
        var types = new Dictionary<Type, int>();
        var optionsTypes = new HashSet<Type>();
        var usesJS = false;
        foreach (var story in catalog.Stories)
        {
            if (story.User is not null)
            {
                types.TryAdd(typeof(AuthenticationStateProvider), 1);
            }

            usesJS |= story.JSInterop is not null;
            if (!story.HasServiceOverrides)
            {
                continue;
            }

            var probe = new ServiceCollection();
            story.ConfigureServices(probe, new FixtureContext(story, HostingModel.InteractiveServer));
            foreach (var group in probe.Where(d => !d.IsKeyedService && !d.ServiceType.IsGenericTypeDefinition).GroupBy(d => d.ServiceType))
            {
                types[group.Key] = Math.Max(types.GetValueOrDefault(group.Key), group.Count());
                if (group.Key.IsGenericType && ContractName(group.Key) is { } name && OptionsContracts.Contains(name))
                {
                    optionsTypes.Add(group.Key.GetGenericArguments()[0]);
                }
            }
        }

        return new OverriddenServices(types, optionsTypes, usesJS);
    }

    private static StoryServiceScope? StoryScopeOf(IServiceProvider provider)
    {
        try
        {
            return provider.GetService<StoryServiceScope>();
        }
        catch (InvalidOperationException)
        {
            // Resolved from the root provider (e.g. by a singleton): no story scope, use the real service.
            return null;
        }
    }

    private static void WrapForOverrides(IServiceCollection services, Type serviceType)
    {
        var original = services.LastOrDefault(d => d.ServiceType == serviceType && !d.IsKeyedService);
        if (original is not null)
        {
            services.RemoveAll(serviceType);
            services.Add(ToKeyed(original));
        }

        services.Add(new ServiceDescriptor(serviceType, provider =>
        {
            if (StoryScopeOf(provider)?.Resolve(serviceType) is { } overridden)
            {
                return overridden;
            }

            if (original is not null)
            {
                return provider.GetRequiredKeyedService(serviceType, OriginalKey);
            }

            return serviceType == typeof(TimeProvider)
                ? TimeProvider.System
                : throw new InvalidOperationException($"No service for type '{serviceType}' has been registered and the current story does not provide one.");
        }, ServiceLifetime.Transient));
    }

    /// <summary>
    /// Multi-registration services: the application's registrations move to a key (keeping their lifetimes), single
    /// resolutions get the story's last registration or the application's last, and an explicit <c>IEnumerable&lt;T&gt;</c>
    /// registration yields the application's registrations followed by the story's — what <c>services.Add</c> after
    /// the application's own calls would give, without collapsing anything.
    /// </summary>
    private static void WrapForComposition(IServiceCollection services, Type serviceType)
    {
        var originals = services.Where(d => d.ServiceType == serviceType && !d.IsKeyedService).ToArray();
        services.RemoveAll(serviceType);
        foreach (var original in originals)
        {
            services.Add(ToKeyed(original));
        }

        services.Add(new ServiceDescriptor(serviceType, provider =>
            StoryScopeOf(provider)?.Resolve(serviceType)
            ?? (originals.Length > 0 ? provider.GetRequiredKeyedService(serviceType, OriginalKey) : null)!, ServiceLifetime.Transient));

        services.Add(new ServiceDescriptor(EnumerableOf(serviceType), provider =>
        {
            var all = provider.GetKeyedServices(serviceType, OriginalKey).OfType<object>()
                .Concat(StoryScopeOf(provider)?.ResolveAll(serviceType) ?? [])
                .ToArray();
            var typed = Array.CreateInstance(serviceType, all.Length);
            Array.Copy(all, typed, all.Length);
            return typed;
        }, ServiceLifetime.Transient));
    }

    // IEnumerable<T> has no annotated generic parameters, so constructing it for any reference type is trim safe.
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "IEnumerable<T> has no annotated generic parameters.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "IEnumerable<T> over reference types shares canonical code.")]
    private static Type EnumerableOf(Type serviceType) => typeof(IEnumerable<>).MakeGenericType(serviceType);

    /// <summary>
    /// <c>IOptions&lt;T&gt;</c> and <c>IOptionsMonitor&lt;T&gt;</c> are singletons, so a story's <c>Configure&lt;T&gt;</c>
    /// would never reach them; for options types that stories configure they resolve per story instead (scoped and
    /// transient consumers, i.e. components, see the story's values; application singletons keep the application's).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "TOptions comes from a story's own Configure<TOptions>() call, which roots its public parameterless constructor and the closed options types.")]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = "TOptions comes from a story's own Configure<TOptions>() call, which roots its public parameterless constructor.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Options types are reference types (shared canonical code).")]
    private static void ScopeOptions(IServiceCollection services, Type optionsType)
    {
        if (optionsType.IsValueType || optionsType.ContainsGenericParameters)
        {
            return;
        }

        typeof(WorkbenchServiceCollectionExtensions)
            .GetMethod(nameof(ScopeOptionsOf), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(optionsType)
            .Invoke(null, [services]);
    }

    internal static void ScopeOptionsOf<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(IServiceCollection services)
        where TOptions : class
    {
        services.AddOptions();
        services.Add(ServiceDescriptor.KeyedSingleton<IOptions<TOptions>>(OriginalKey, (provider, _) =>
            new OptionsManager<TOptions>(provider.GetRequiredService<IOptionsFactory<TOptions>>())));
        services.Add(ServiceDescriptor.KeyedSingleton<IOptionsMonitor<TOptions>>(OriginalKey, (provider, _) =>
            new OptionsMonitor<TOptions>(provider.GetRequiredService<IOptionsFactory<TOptions>>(), provider.GetServices<IOptionsChangeTokenSource<TOptions>>(), provider.GetRequiredService<IOptionsMonitorCache<TOptions>>())));

        services.AddTransient<IOptions<TOptions>>(provider =>
            StoryScopeOf(provider) is { } scope && ConfiguresOptions<TOptions>(scope)
                ? scope.GetOrAdd(typeof(IOptions<TOptions>), () => new OptionsManager<TOptions>(provider.GetRequiredService<IOptionsFactory<TOptions>>()))
                : provider.GetRequiredKeyedService<IOptions<TOptions>>(OriginalKey));
        services.AddTransient<IOptionsMonitor<TOptions>>(provider =>
            StoryScopeOf(provider) is { } scope && ConfiguresOptions<TOptions>(scope)
                ? scope.GetOrAdd(typeof(IOptionsMonitor<TOptions>), () => new OptionsMonitor<TOptions>(
                    provider.GetRequiredService<IOptionsFactory<TOptions>>(), provider.GetServices<IOptionsChangeTokenSource<TOptions>>(), new OptionsCache<TOptions>()))
                : provider.GetRequiredKeyedService<IOptionsMonitor<TOptions>>(OriginalKey));
    }

    private static bool ConfiguresOptions<TOptions>(StoryServiceScope scope)
        where TOptions : class =>
        scope.Overrides is { } overrides
        && (overrides.GetServices<IConfigureOptions<TOptions>>().Any()
            || overrides.GetServices<IPostConfigureOptions<TOptions>>().Any()
            || overrides.GetServices<IValidateOptions<TOptions>>().Any());

    /// <summary>
    /// Components of stories with JS mocks get a decorated <see cref="IJSRuntime"/> that records their calls and, with
    /// <see cref="JSInteropMocks.InBrowsers"/>, answers mocked identifiers. Every registration keeps resolving the same
    /// real runtime instance (Blazor itself casts it to its concrete type), and stories without mocks get it undecorated.
    /// </summary>
    private static bool WrapJSRuntime(IServiceCollection services)
    {
        var originals = services.Where(d => d.ServiceType == typeof(IJSRuntime) && !d.IsKeyedService).ToArray();
        if (originals.Length == 0)
        {
            return false;
        }

        services.RemoveAll<IJSRuntime>();
        foreach (var original in originals)
        {
            services.Add(ToKeyed(original));
        }

        services.Add(new ServiceDescriptor(typeof(IJSRuntime), provider =>
        {
            var scope = StoryScopeOf(provider);
            var real = scope?.Resolve(typeof(IJSRuntime)) as IJSRuntime ?? provider.GetRequiredKeyedService<IJSRuntime>(OriginalKey);
            return scope is { Story.JSInterop: { } mocks }
                ? scope.GetOrAdd(typeof(IJSRuntime), () => StoryJSRuntime.Create(real, mocks, scope.JSCalls, mocks.InBrowsers ? JSMockMode.Answer : JSMockMode.RecordOnly))
                : real;
        }, ServiceLifetime.Transient));
        return true;
    }

    /// <summary>The application's own JS runtime, bypassing story decoration (the workbench's internal calls use it).</summary>
    internal static IJSRuntime RealJSRuntime(IServiceProvider services) =>
        services.GetKeyedService<IJSRuntime>(OriginalKey) ?? services.GetRequiredService<IJSRuntime>();

    private static ServiceDescriptor ToKeyed(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is { } instance)
        {
            return new ServiceDescriptor(descriptor.ServiceType, OriginalKey, instance);
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return new ServiceDescriptor(descriptor.ServiceType, OriginalKey, (provider, _) => factory(provider), descriptor.Lifetime);
        }

        return new ServiceDescriptor(descriptor.ServiceType, OriginalKey, descriptor.ImplementationType!, descriptor.Lifetime);
    }
}
