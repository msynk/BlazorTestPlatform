using BlazorTestPlatform.Workbench;
using BlazorTestPlatform.Workbench.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace BlazorTestPlatform.Workbench.Tests;

public interface IGreetingService
{
    string Greet();
}

public sealed class RealGreetingService : IGreetingService
{
    public string Greet() => "real";
}

public sealed class FakeGreetingService : IGreetingService
{
    public string Greet() => "fake";
}

public sealed class Widget : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "widget");
}

public sealed class WidgetSettings
{
    public List<string> Applied { get; } = [];
}

public interface INotifier
{
    string Name { get; }
}

public sealed record Notifier(string Name) : INotifier;

public sealed class StoryRequirement : IAuthorizationRequirement;

public sealed class StoryRequirementHandler : AuthorizationHandler<StoryRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, StoryRequirement requirement)
    {
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Stand-in for the application's JS runtime: records what reaches it.</summary>
public sealed class RecordingJSRuntime : IJSRuntime
{
    public List<string> Calls { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add(identifier);
        if (identifier == "import")
        {
            throw new JSException("Failed to fetch dynamically imported module");
        }

        return ValueTask.FromResult(typeof(TValue) == typeof(string) ? (TValue)(object)"real" : default!);
    }
}

public sealed class WidgetStories : Stories<Widget>
{
    public override string Title => "Tests/Widget";

    protected override void Define(StoryCollectionBuilder<Widget> stories)
    {
        stories.Add("Plain");
        stories.Add("Overridden", s => s.Services(services => services.AddSingleton<IGreetingService, FakeGreetingService>()));
        stories.Add("Options", s => s.Services(services => services.Configure<WidgetSettings>(o => o.Applied.Add("story"))));
        stories.Add("Http", s => s.MockHttp(http => http.Get("api/ping").RespondText("pong")));
        stories.Add("Fake time", s => s.UseFakeTime(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        stories.Add("Notifiers", s => s.Services(services => services.AddSingleton<INotifier>(new Notifier("story"))));
        stories.Add("Authorization", s => s.Services(services => services.AddSingleton<IAuthorizationHandler, StoryRequirementHandler>()));
        stories.Add("Logging", s => s.Services(services => services.AddSingleton<ILoggerProvider>(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance)));
        stories.Add("JS in browsers", s => s.MockJS(js => js
            .ApplyInBrowsers()
            .Setup("localStorage.getItem", "dark", "theme")
            .SetupVoid("analytics.track")
            .Module("./media.js", media => media.Setup("prefersReducedMotion", true))));
        stories.Add("JS recorded", s => s.MockJS(js => js.Setup("localStorage.getItem", "dark", "theme")));
        stories.Add("JS strict", s => s.MockJS(js =>
        {
            js.ApplyInBrowsers();
            js.Strict = true;
        }));
    }
}

public sealed class ServiceOverrideTests
{
    private static ServiceProvider BuildApp(Action<IServiceCollection>? app = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, RealGreetingService>();
        services.Configure<WidgetSettings>(o => o.Applied.Add("app-1"));
        services.Configure<WidgetSettings>(o => o.Applied.Add("app-2"));
        services.AddHttpClient("api", c => c.BaseAddress = new Uri("https://real.example/"));
        services.AddSingleton<INotifier>(new Notifier("email"));
        services.AddSingleton<INotifier>(new Notifier("sms"));
        services.AddAuthorizationCore(o => o.AddPolicy("story-only", p => p.AddRequirements(new StoryRequirement())));
        services.AddScoped<IJSRuntime, RecordingJSRuntime>();
        app?.Invoke(services);
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<AsyncServiceScope> ActivateAsync(ServiceProvider provider, string storyId)
    {
        var scope = provider.CreateAsyncScope();
        var story = scope.ServiceProvider.GetRequiredService<StoryCatalog>().Get(storyId);
        await scope.ServiceProvider.GetRequiredService<StoryServiceScope>().ActivateAsync(story, HostingModel.InteractiveServer, authentication: null);
        return scope;
    }

    [Fact]
    public async Task Overridden_services_resolve_per_story_and_fall_back_to_the_app()
    {
        await using var provider = BuildApp();

        await using (var overridden = await ActivateAsync(provider, "tests-widget--overridden"))
        {
            Assert.Equal("fake", overridden.ServiceProvider.GetRequiredService<IGreetingService>().Greet());
        }

        await using (var plain = await ActivateAsync(provider, "tests-widget--plain"))
        {
            Assert.Equal("real", plain.ServiceProvider.GetRequiredService<IGreetingService>().Greet());
        }

        Assert.Equal("real", provider.GetRequiredService<IGreetingService>().Greet());
    }

    [Fact]
    public async Task Original_singletons_keep_their_lifetime()
    {
        await using var provider = BuildApp();
        await using var first = await ActivateAsync(provider, "tests-widget--plain");
        await using var second = await ActivateAsync(provider, "tests-widget--plain");

        Assert.Same(first.ServiceProvider.GetRequiredService<IGreetingService>(), second.ServiceProvider.GetRequiredService<IGreetingService>());
    }

    [Fact]
    public async Task Story_options_add_to_the_application_configuration_per_story()
    {
        await using var provider = BuildApp();

        await using (var scope = await ActivateAsync(provider, "tests-widget--options"))
        {
            var services = scope.ServiceProvider;
            Assert.Equal(["app-1", "app-2", "story"], services.GetRequiredService<IOptions<WidgetSettings>>().Value.Applied);
            Assert.Equal(["app-1", "app-2", "story"], services.GetRequiredService<IOptionsSnapshot<WidgetSettings>>().Value.Applied);
            Assert.Equal(["app-1", "app-2", "story"], services.GetRequiredService<IOptionsMonitor<WidgetSettings>>().CurrentValue.Applied);
            Assert.Same(services.GetRequiredService<IOptions<WidgetSettings>>(), services.GetRequiredService<IOptions<WidgetSettings>>());
        }

        await using (var plain = await ActivateAsync(provider, "tests-widget--plain"))
        {
            Assert.Equal(["app-1", "app-2"], plain.ServiceProvider.GetRequiredService<IOptions<WidgetSettings>>().Value.Applied);
            Assert.Equal(["app-1", "app-2"], plain.ServiceProvider.GetRequiredService<IOptionsMonitor<WidgetSettings>>().CurrentValue.Applied);
        }

        // Application singletons keep the application's values, and the singleton is still a singleton.
        Assert.Equal(["app-1", "app-2"], provider.GetRequiredService<IOptions<WidgetSettings>>().Value.Applied);
        Assert.Same(provider.GetRequiredService<IOptions<WidgetSettings>>(), provider.GetRequiredService<IOptions<WidgetSettings>>());
        Assert.DoesNotContain(typeof(IConfigureOptions<WidgetSettings>), provider.GetRequiredService<WorkbenchOptions>().UnwrappableOverrides);
    }

    [Fact]
    public async Task Multi_registration_services_are_composed_never_collapsed()
    {
        await using var provider = BuildApp();

        await using (var story = await ActivateAsync(provider, "tests-widget--notifiers"))
        {
            Assert.Equal(["email", "sms", "story"], story.ServiceProvider.GetServices<INotifier>().Select(n => n.Name));
            Assert.Equal("story", story.ServiceProvider.GetRequiredService<INotifier>().Name);
        }

        await using (var plain = await ActivateAsync(provider, "tests-widget--plain"))
        {
            Assert.Equal(["email", "sms"], plain.ServiceProvider.GetServices<INotifier>().Select(n => n.Name));
            Assert.Equal("sms", plain.ServiceProvider.GetRequiredService<INotifier>().Name);
        }

        Assert.Equal(["email", "sms"], provider.GetServices<INotifier>().Select(n => n.Name));
    }

    [Fact]
    public async Task Story_authorization_handlers_apply_only_to_that_story()
    {
        await using var provider = BuildApp();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ada")], "test"));

        await using (var story = await ActivateAsync(provider, "tests-widget--authorization"))
        {
            Assert.True((await story.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, "story-only")).Succeeded);
        }

        await using var plain = await ActivateAsync(provider, "tests-widget--plain");
        Assert.False((await plain.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, "story-only")).Succeeded);
    }

    [Fact]
    public async Task App_start_contracts_are_flagged_instead_of_failing_silently()
    {
        await using var provider = BuildApp();
        var options = provider.GetRequiredService<WorkbenchOptions>();
        var catalog = provider.GetRequiredService<StoryCatalog>();

        Assert.Contains(typeof(ILoggerProvider), options.UnwrappableOverrides);
        Assert.Equal([typeof(ILoggerProvider)], options.UnsupportedOverridesOf(catalog.Get("tests-widget--logging")));
        Assert.Empty(options.UnsupportedOverridesOf(catalog.Get("tests-widget--options")));
    }

    [Fact]
    public async Task Browser_js_mocks_answer_mocked_calls_and_pass_the_rest_through()
    {
        await using var provider = BuildApp();
        await using var scope = await ActivateAsync(provider, "tests-widget--js-in-browsers");
        var js = scope.ServiceProvider.GetRequiredService<IJSRuntime>();
        var real = (RecordingJSRuntime)WorkbenchServiceCollectionExtensions.RealJSRuntime(scope.ServiceProvider);

        Assert.Equal("dark", await js.InvokeAsync<string>("localStorage.getItem", "theme"));
        Assert.Equal("real", await js.InvokeAsync<string>("localStorage.getItem", "other"));
        await js.InvokeVoidAsync("analytics.track", "clicked");
        var media = await js.InvokeAsync<IJSObjectReference>("import", "./media.js");
        Assert.True(await media.InvokeAsync<bool>("prefersReducedMotion"));
        Assert.False(await media.InvokeAsync<bool>("unmocked"));

        Assert.Equal(["localStorage.getItem", "import"], real.Calls);
        Assert.Equal(
            ["localStorage.getItem(theme) [mocked]", "localStorage.getItem(other)", "analytics.track(clicked) [mocked]", "import(./media.js) [mocked]", "./media.js:prefersReducedMotion() [mocked]", "./media.js:unmocked()"],
            scope.ServiceProvider.GetRequiredService<StoryServiceScope>().JSCalls.Calls.Select(c => c.ToString()));
        Assert.Same(js, scope.ServiceProvider.GetRequiredService<IJSRuntime>());
    }

    [Fact]
    public async Task Js_mocks_without_in_browsers_only_record()
    {
        await using var provider = BuildApp();
        await using var scope = await ActivateAsync(provider, "tests-widget--js-recorded");
        var js = scope.ServiceProvider.GetRequiredService<IJSRuntime>();

        Assert.Equal("real", await js.InvokeAsync<string>("localStorage.getItem", "theme"));
        Assert.Equal(["localStorage.getItem(theme)"], scope.ServiceProvider.GetRequiredService<StoryServiceScope>().JSCalls.Calls.Select(c => c.ToString()));
    }

    [Fact]
    public async Task Strict_browser_mocks_reject_unmocked_calls_but_not_blazor_internals()
    {
        await using var provider = BuildApp();
        await using var scope = await ActivateAsync(provider, "tests-widget--js-strict");
        var js = scope.ServiceProvider.GetRequiredService<IJSRuntime>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await js.InvokeAsync<string>("localStorage.getItem", "theme"));
        Assert.Contains("strict", error.Message, StringComparison.Ordinal);
        await js.InvokeVoidAsync("Blazor._internal.domWrapper.focus");
    }

    [Fact]
    public async Task Stories_without_js_mocks_get_the_real_runtime()
    {
        await using var provider = BuildApp();
        await using var scope = await ActivateAsync(provider, "tests-widget--plain");

        Assert.IsType<RecordingJSRuntime>(scope.ServiceProvider.GetRequiredService<IJSRuntime>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<IJSRuntime>(), WorkbenchServiceCollectionExtensions.RealJSRuntime(scope.ServiceProvider));
    }

    [Fact]
    public async Task Mocked_http_replaces_named_clients_only_for_that_story()
    {
        await using var provider = BuildApp();

        await using (var mocked = await ActivateAsync(provider, "tests-widget--http"))
        {
            var client = mocked.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
            Assert.Equal("pong", await client.GetStringAsync("api/ping"));
            Assert.Single(mocked.ServiceProvider.GetRequiredService<StoryServiceScope>().Overrides!.GetRequiredService<HttpMock>().Requests);
        }

        await using var plain = await ActivateAsync(provider, "tests-widget--plain");
        Assert.Equal(new Uri("https://real.example/"), plain.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("api").BaseAddress);
    }

    [Fact]
    public async Task Fake_time_applies_per_story_and_time_provider_falls_back_to_system()
    {
        await using var provider = BuildApp();

        await using (var fake = await ActivateAsync(provider, "tests-widget--fake-time"))
        {
            Assert.Equal(2030, fake.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().Year);
        }

        await using var plain = await ActivateAsync(provider, "tests-widget--plain");
        Assert.Same(TimeProvider.System, plain.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Registering_twice_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>());
        Assert.Throws<InvalidOperationException>(() => services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<WidgetStories>()));
    }

    [Fact]
    public void Can_wrap_rules()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, RealGreetingService>();
        Assert.True(WorkbenchServiceCollectionExtensions.CanWrap(services, typeof(IGreetingService)));

        services.AddSingleton<IGreetingService, FakeGreetingService>();
        Assert.False(WorkbenchServiceCollectionExtensions.CanWrap(services, typeof(IGreetingService)));
        Assert.False(WorkbenchServiceCollectionExtensions.CanWrap(new ServiceCollection(), typeof(IConfigureOptions<WidgetSettings>)));
    }

    [Fact]
    public void Frame_urls_encode_story_and_options()
    {
        Assert.Equal("/_workbench/frame/inputs-button--primary?mode=wasm&theme=dark&viewport=mobile&prerender=false",
            WorkbenchUrls.Frame("inputs-button--primary", HostingModel.InteractiveWebAssembly, "dark", "mobile", prerender: false));
        Assert.Equal("/_workbench?story=a--b&mode=ssr", WorkbenchUrls.Shell("a--b", hostingModel: HostingModel.StaticServer));
    }
}
