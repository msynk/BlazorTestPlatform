using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Bunit.Tests.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class CounterStories : Stories<Counter>
{
    public override string Title => "Tests/Counter";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        stories.Add("Default");
        stories.Add("By five", s => s.Arg(c => c.IncrementAmount, 5));
        stories.Add("Clicks twice", s => s
            .Performance(p =>
            {
                p.MaxRootRenders = 10;
                p.Iterations = 2;
                p.WarmupIterations = 0;
                p.TrackBaseline = false;
            })
            .Play(async ctx =>
            {
                var button = ctx.GetByRole("button", "Increment");
                await button.ClickAsync();
                await button.ClickAsync();
                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 2");
                await ctx.ExpectAction("OnCountChanged").ToHaveBeenCalledAsync(times: 2);
                await ctx.ExpectAction("OnCountChanged").ToHaveBeenLastCalledWithAsync(2);
            }));
        stories.Add("Over budget", s => s
            .Performance(p =>
            {
                p.MaxRootRenders = 1;
                p.Iterations = 1;
                p.WarmupIterations = 0;
                p.TrackBaseline = false;
            })
            .Play(ctx => ctx.GetByRole("button").ClickAsync()));
        stories.Add("Failing play", s => s.Play(async ctx =>
        {
            await ctx.StepAsync("check wrong text", () => ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 42", TimeSpan.FromMilliseconds(200)));
        }));
        stories.Add("Needs layout", s => s.Play(ctx =>
        {
            ctx.Require(DriverCapabilities.Layout, "measuring the button");
            return Task.CompletedTask;
        }));
        stories.Add("Declares layout", s => s
            .Requires(DriverCapabilities.Layout, "measuring the popover position")
            .Play(_ => throw new InvalidOperationException("A driver without layout must not run this play function.")));
        stories.Add("Declares fake time", s => s
            .UseFakeTime(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .Requires(DriverCapabilities.FakeTime, "advancing the clock")
            .Play(ctx => ctx.AdvanceTimeAsync(TimeSpan.FromSeconds(1))));
    }
}

public sealed class NameFormStories : Stories<NameForm>
{
    public override string Title => "Tests/Name form";

    protected override void Define(StoryCollectionBuilder<NameForm> stories) => stories.Add("Default");
}

public sealed class TabsStories : Stories<Tabs>
{
    public override string Title => "Tests/Tabs";

    protected override void Define(StoryCollectionBuilder<Tabs> stories) => stories.Add("Default");
}

public sealed class AsyncLoaderStories : Stories<AsyncLoader>
{
    public override string Title => "Tests/Async loader";

    protected override void Define(StoryCollectionBuilder<AsyncLoader> stories) =>
        stories.Add("Frozen clock", s => s.UseFakeTime(new DateTimeOffset(2030, 1, 2, 9, 0, 0, TimeSpan.Zero)));
}

public sealed class GreetingStories : Stories<Greeting>
{
    public override string Title => "Tests/Greeting";

    protected override void Define(StoryCollectionBuilder<Greeting> stories)
    {
        stories.Add("Anonymous", s => s.Anonymous());
        stories.Add("Signed in", s => s.AuthenticatedAs("Ada"));
        stories.Add("Admin", s => s.AuthenticatedAs("Grace", "Admin"));
    }
}

public sealed class InaccessibleStories : Stories<Inaccessible>
{
    public override string Title => "Tests/Inaccessible";

    protected override void Define(StoryCollectionBuilder<Inaccessible> stories)
    {
        stories.Add("Default");
        stories.Add("Suppressed", s => s
            .DisableAccessibilityRule("image-alt", "Decorative logo tracked in #123")
            .DisableAccessibilityRule("button-name", "Icon font loads later, tracked in #124")
            .DisableAccessibilityRule("label", "Tracked in #125")
            .DisableAccessibilityRule("aria-roles", "Tracked in #126")
            .DisableAccessibilityRule("list", "Tracked in #127"));
    }
}

public sealed class RatingStories : Stories<Rating>
{
    public override string Title => "Tests/Rating";

    protected override void Define(StoryCollectionBuilder<Rating> stories)
    {
        stories.Add("Bound", s => s.Bind(r => r.Value, 2));
        stories.AddVariants(v => v.Vary(r => r.Value, 1, 3).Vary(r => r.Max, 3, 5));
    }
}

public sealed class WeatherFixture : StoryFixture
{
    public override void ConfigureServices(IServiceCollection services, FixtureContext context)
    {
        var forecasts = context.Fake.Many(3, (fake, i) => $"{fake.City()}: {fake.Random.Next(-5, 30)}°C");
        services.AddSingleton<WeatherList.IWeatherService>(new FakeWeather(forecasts));
    }

    private sealed class FakeWeather(IReadOnlyList<string> forecasts) : WeatherList.IWeatherService
    {
        public Task<IReadOnlyList<string>> GetAsync() => Task.FromResult(forecasts);
    }
}

public sealed class WeatherStories : Stories<WeatherList>
{
    public override string Title => "Tests/Weather";

    protected override void Define(StoryCollectionBuilder<WeatherList> stories) =>
        stories.Add("Seeded", s => s.UseFixture<WeatherFixture>().Seed(42));
}

public sealed class FocusScopesStories : Stories<FocusScopes>
{
    public override string Title => "Tests/Focus scopes";

    protected override void Define(StoryCollectionBuilder<FocusScopes> stories) => stories.Add("Default");
}

public sealed class KeyboardFormStories : Stories<KeyboardForm>
{
    public override string Title => "Tests/Keyboard form";

    protected override void Define(StoryCollectionBuilder<KeyboardForm> stories) => stories.Add("Default");
}

public sealed class ImplicitSubmissionStories : Stories<ImplicitSubmission>
{
    public override string Title => "Tests/Implicit submission";

    protected override void Define(StoryCollectionBuilder<ImplicitSubmission> stories) => stories.Add("Default");
}

public sealed class FocusEventsStories : Stories<FocusEvents>
{
    public override string Title => "Tests/Focus events";

    protected override void Define(StoryCollectionBuilder<FocusEvents> stories) => stories.Add("Default");
}

public sealed class UnboundInputsStories : Stories<UnboundInputs>
{
    public override string Title => "Tests/Unbound inputs";

    protected override void Define(StoryCollectionBuilder<UnboundInputs> stories) => stories.Add("Default");
}

public sealed class CrashesLaterStories : Stories<CrashesLater>
{
    public override string Title => "Tests/Crashes later";

    protected override void Define(StoryCollectionBuilder<CrashesLater> stories) => stories.Add("Failing after await");
}

public sealed class EmulationExtrasStories : Stories<EmulationExtras>
{
    public override string Title => "Tests/Emulation extras";

    protected override void Define(StoryCollectionBuilder<EmulationExtras> stories) => stories.Add("Default", s => s.Accessibility(a => a.Skip = true));
}

public sealed class InsertsBeforeFocusStories : Stories<InsertsBeforeFocus>
{
    public override string Title => "Tests/Inserts before focus";

    protected override void Define(StoryCollectionBuilder<InsertsBeforeFocus> stories) => stories.Add("Default");
}

public sealed class RelocatesByTextStories : Stories<RelocatesByText>
{
    public override string Title => "Tests/Relocates by text";

    protected override void Define(StoryCollectionBuilder<RelocatesByText> stories) => stories.Add("Default");
}

public sealed class SummaryActionsStories : Stories<SummaryActions>
{
    public override string Title => "Tests/Summary actions";

    protected override void Define(StoryCollectionBuilder<SummaryActions> stories) => stories.Add("Default");
}
