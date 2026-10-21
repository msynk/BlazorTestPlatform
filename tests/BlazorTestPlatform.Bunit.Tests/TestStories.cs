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
