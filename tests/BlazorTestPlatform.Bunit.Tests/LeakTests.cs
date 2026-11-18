using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Bunit.Tests;

/// <summary>A singleton service with an event, as components subscribe to.</summary>
public sealed class Notifier
{
    /// <summary>Raised on change.</summary>
    public event Action? Changed;

    /// <summary>Raises <see cref="Changed"/>.</summary>
    public void Notify() => Changed?.Invoke();
}

public sealed class LeakyObserverStories : Stories<LeakyObserver>
{
    public override string Title => "Tests/Leaks/Observer";

    protected override void Define(StoryCollectionBuilder<LeakyObserver> stories)
    {
        stories.Add("Leaks", s => s.Tags("fails-on-purpose"));
        stories.Add("Leaks with mocks", s => s.Tags("fails-on-purpose").MockJS(js => js.SetupVoid("leaky.observe")));
        stories.Add("Disposes", s => s.Arg(c => c.DisposeReference, true));
        stories.Add("Leaks on purpose", s => s.AllowLeaks("the reference is shared with a test double"));
    }
}

public sealed class LeakySubscriberStories : Stories<LeakySubscriber>
{
    public override string Title => "Tests/Leaks/Subscriber";

    protected override void Define(StoryCollectionBuilder<LeakySubscriber> stories)
    {
        stories.Add("Leaks", s => s.Tags("fails-on-purpose").Services(services => services.AddSingleton<Notifier>()));
        stories.Add("Unsubscribes", s => s.Arg(c => c.Unsubscribe, true).Services(services => services.AddSingleton<Notifier>()));
        stories.Add("Scoped notifier", s => s.Tags("fails-on-purpose").Services(services => services.AddScoped<Notifier>()));

        // A transient service belongs to the component and goes with it: only the NavigationManager handler leaks.
        stories.Add("Transient notifier", s => s.Tags("fails-on-purpose").Services(services => services.AddTransient<Notifier>()));
    }
}

public sealed class LeakyHostStories : Stories<LeakyHost>
{
    public override string Title => "Tests/Leaks/Host";

    protected override void Define(StoryCollectionBuilder<LeakyHost> stories) =>
        stories.Add("Child removed in play", s => s.Tags("fails-on-purpose").Services(services => services.AddSingleton<Notifier>()).Play(ctx => ctx.GetByRole("button", "Hide").ClickAsync()));
}

public sealed class ServiceReferenceStories : Stories<ServiceReference>
{
    public override string Title => "Tests/Leaks/Service reference";

    protected override void Define(StoryCollectionBuilder<ServiceReference> stories) =>
        stories.Add("App level", s => s.Services(services => services.AddSingleton<Notifier>()));
}

public sealed class LeakTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new LeakyObserverStories(), new LeakySubscriberStories(), new LeakyHostStories(), new ServiceReferenceStories()]);

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Theory]
    [InlineData("tests-leaks-observer--leaks", "leaky.observe")]
    [InlineData("tests-leaks-observer--leaks-with-mocks", "leaky.observe")]
    public async Task An_undisposed_DotNetObjectReference_fails_the_story(string id, string call)
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get(id), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("left 1 reference(s) behind", result.Error, StringComparison.Ordinal);
        Assert.Contains($"DotNetObjectReference<LeakyObserver> passed to JavaScript ({call}) was not disposed", result.Error, StringComparison.Ordinal);
        Assert.Contains("AllowLeaks(reason)", result.Error, StringComparison.Ordinal);
        Assert.Contains(result.Steps, s => s.Title == "leak check (disposed components)" && s.Failed);
    }

    [Fact]
    public async Task Event_handlers_left_on_injected_services_fail_the_story()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-leaks-subscriber--leaks"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("left 2 reference(s) behind", result.Error, StringComparison.Ordinal);
        // A method group on a custom service, and a closure (capturing a local and the component) on NavigationManager.
        Assert.Contains("Notifier.Changed still calls LeakySubscriber.OnChanged after the component was disposed", result.Error, StringComparison.Ordinal);
        Assert.Contains("NavigationManager.LocationChanged still calls LeakySubscriber.OnInitialized (lambda) after the component was disposed", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tests-leaks-subscriber--scoped-notifier", 2)]
    [InlineData("tests-leaks-subscriber--transient-notifier", 1)]
    public async Task Handlers_count_on_services_that_outlive_the_component(string id, int leaks)
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get(id), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains($"left {leaks} reference(s) behind", result.Error, StringComparison.Ordinal);
        Assert.Contains("NavigationManager.LocationChanged", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_component_play_removed_is_checked_too()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-leaks-host--child-removed-in-play"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("Notifier.Changed still calls LeakySubscriber.OnChanged", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tests-leaks-service-reference--app-level")]
    [InlineData("tests-leaks-observer--disposes")]
    [InlineData("tests-leaks-subscriber--unsubscribes")]
    [InlineData("tests-leaks-observer--leaks-on-purpose")]
    public async Task Components_that_clean_up_or_say_why_not_pass(string id)
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get(id), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task Leak_detection_can_be_turned_off()
    {
        var options = Options;
        options.DetectLeaks = false;

        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-leaks-subscriber--leaks"), options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        Assert.DoesNotContain(result.Steps, s => s.Title.StartsWith("leak check", StringComparison.Ordinal));
    }
}
