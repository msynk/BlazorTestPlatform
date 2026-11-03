using System.Security.Claims;
using BlazorTestPlatform.Workbench;
using BlazorTestPlatform.Workbench.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BlazorTestPlatform.Workbench.Tests;

public sealed class WorkbenchSecurityTests
{
    private sealed class HostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "App";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FixedAuthentication(bool authenticated) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(authenticated ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "ada")], "test") : new ClaimsIdentity())));
    }

    private sealed class DisposableGreeting : IGreetingService, IDisposable
    {
        public bool Disposed { get; private set; }

        public string Greet() => Disposed ? throw new ObjectDisposedException(nameof(DisposableGreeting)) : "real";

        public void Dispose() => Disposed = true;
    }

    private static WorkbenchOptions Register(string? environment, Action<WorkbenchOptions>? configure = null)
    {
        var services = new ServiceCollection();
        if (environment is not null)
        {
            services.AddSingleton<IHostEnvironment>(new HostEnvironment(environment));
        }

        services.AddBlazorTestWorkbench(o =>
        {
            o.AddStoriesFromAssemblyOf<WidgetStories>();
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider().GetRequiredService<WorkbenchOptions>();
    }

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Development", null, true)]
    [InlineData("Staging", null, true)]
    [InlineData(null, null, true)]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public void The_workbench_is_off_in_production_unless_enabled(string? environment, string? variable, bool expected)
    {
        var services = new ServiceCollection();
        if (environment is not null)
        {
            services.AddSingleton<IHostEnvironment>(new HostEnvironment(environment));
        }

        Assert.Equal(expected, WorkbenchServiceCollectionExtensions.EnabledByEnvironment(services, variable));
    }

    [Fact]
    public void Explicit_activation_wins_over_the_environment()
    {
        Assert.False(Register("Production").IsEnabled);
        Assert.True(Register("Production", o => o.Enabled = true).IsEnabled);
        Assert.False(Register("Development", o => o.Enabled = false).IsEnabled);
    }

    [Fact]
    public async Task Access_requires_the_policy_and_fails_closed()
    {
        var disabled = new WorkbenchOptions { IsEnabled = false };
        Assert.False(await WorkbenchAccess.IsAllowedAsync(disabled, new ServiceCollection().BuildServiceProvider()));

        var open = new WorkbenchOptions();
        Assert.True(await WorkbenchAccess.IsAllowedAsync(open, new ServiceCollection().BuildServiceProvider()));

        var guarded = new WorkbenchOptions { AuthorizationPolicy = "workbench" };
        Assert.False(await WorkbenchAccess.IsAllowedAsync(guarded, new ServiceCollection().BuildServiceProvider()));

        static IServiceProvider WithUser(bool authenticated) => new ServiceCollection()
            .AddAuthorizationCore(o => o.AddPolicy("workbench", p => p.RequireAuthenticatedUser()))
            .AddLogging()
            .AddSingleton<AuthenticationStateProvider>(new FixedAuthentication(authenticated))
            .BuildServiceProvider();

        Assert.False(await WorkbenchAccess.IsAllowedAsync(guarded, WithUser(authenticated: false)));
        Assert.True(await WorkbenchAccess.IsAllowedAsync(guarded, WithUser(authenticated: true)));
        Assert.False(await WorkbenchAccess.IsAllowedAsync(new WorkbenchOptions { AuthorizationPolicy = "unknown" }, WithUser(authenticated: true)));
    }

    [Fact]
    public async Task Disposable_application_singletons_are_never_disposed_by_story_scopes()
    {
        var greeting = new DisposableGreeting();
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService>(greeting);
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var story = scope.ServiceProvider.GetRequiredService<StoryCatalog>().Get("tests-widget--plain");
            await scope.ServiceProvider.GetRequiredService<StoryServiceScope>().ActivateAsync(story, HostingModel.InteractiveServer, authentication: null);
            Assert.Equal("real", scope.ServiceProvider.GetRequiredService<IGreetingService>().Greet());
        }

        Assert.False(greeting.Disposed);

        // The story that overrides it is flagged instead of silently breaking the application.
        var options = provider.GetRequiredService<WorkbenchOptions>();
        Assert.Contains(typeof(IGreetingService), options.UnwrappableOverrides);
        Assert.Contains(typeof(IGreetingService), options.UnsupportedOverridesOf(provider.GetRequiredService<StoryCatalog>().Get("tests-widget--overridden")));
    }

    [Fact]
    public async Task Factory_singletons_declared_disposable_are_flagged_like_other_disposable_singletons()
    {
        var greeting = new DisposableGreeting();
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, DisposableGreeting>(_ => greeting);
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var story = scope.ServiceProvider.GetRequiredService<StoryCatalog>().Get("tests-widget--plain");
            await scope.ServiceProvider.GetRequiredService<StoryServiceScope>().ActivateAsync(story, HostingModel.InteractiveServer, authentication: null);
            Assert.Equal("real", scope.ServiceProvider.GetRequiredService<IGreetingService>().Greet());
        }

        Assert.False(greeting.Disposed);
        Assert.Contains(typeof(IGreetingService), provider.GetRequiredService<WorkbenchOptions>().UnwrappableOverrides);
    }

    [Fact]
    public async Task Factory_singletons_that_turn_out_disposable_fail_clearly_instead_of_being_disposed_by_story_scopes()
    {
        var greeting = new DisposableGreeting();
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService>(_ => greeting);
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var story = scope.ServiceProvider.GetRequiredService<StoryCatalog>().Get("tests-widget--plain");
            await scope.ServiceProvider.GetRequiredService<StoryServiceScope>().ActivateAsync(story, HostingModel.InteractiveServer, authentication: null);
            var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IGreetingService>());
            Assert.Contains("AddSingleton<IGreetingService, DisposableGreeting>()", error.Message);
        }

        Assert.False(greeting.Disposed);
    }

    [Fact]
    public void Factory_singletons_whose_return_type_cannot_be_disposable_stay_overridable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, RealGreetingService>(_ => new RealGreetingService());

        Assert.True(WorkbenchServiceCollectionExtensions.CanWrap(services, typeof(IGreetingService)));
    }

    [Fact]
    public async Task Services_only_stories_register_stay_optional_elsewhere()
    {
        var services = new ServiceCollection();
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var story = scope.ServiceProvider.GetRequiredService<StoryCatalog>().Get("tests-widget--plain");
        await scope.ServiceProvider.GetRequiredService<StoryServiceScope>().ActivateAsync(story, HostingModel.InteractiveServer, authentication: null);

        Assert.Null(scope.ServiceProvider.GetService<IGreetingService>());
    }

    [Fact]
    public async Task The_application_options_monitor_survives_story_scopes()
    {
        var services = new ServiceCollection();
        services.Configure<WidgetSettings>(o => o.Applied.Add("app"));
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var monitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<WidgetSettings>>();
            Assert.IsNotAssignableFrom<IDisposable>(monitor);
            Assert.Contains("app", monitor.CurrentValue.Applied);
        }

        Assert.Contains("app", provider.GetRequiredService<IOptionsMonitor<WidgetSettings>>().CurrentValue.Applied);
    }
}
