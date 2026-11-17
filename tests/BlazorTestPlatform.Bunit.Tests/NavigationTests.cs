using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class NavigatorStories : Stories<Navigator>
{
    public override string Title => "Tests/Navigator";

    protected override void Define(StoryCollectionBuilder<Navigator> stories) => stories.Add("Default");
}

public sealed class NavigationTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<NavigationTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Navigations_are_recorded_in_order_with_their_options()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-navigator--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;

        await ctx.GetByRole("button", "Open").ClickAsync();
        await ctx.GetByRole("button", "Page two").ClickAsync();
        await ctx.GetByRole("button", "Help").ClickAsync();

        var navigations = await ctx.GetNavigationsAsync();
        Assert.Equal(["/orders/7", "/orders?page=2", "/https://example.org/help"], navigations.Select(n => n.ToString()));
        Assert.True(navigations[1].ReplaceHistoryEntry);
        Assert.Equal(false, navigations[1].ForceLoad);
        Assert.True(navigations[2].ForceLoad);
        await ctx.ExpectNavigation("/orders/7").ToHaveNavigatedAsync(times: 1);
        await ctx.ExpectNavigation("/orders").ToHaveNavigatedAsync(times: 1);
        await ctx.ExpectNavigation("https://example.org/*").ToBeTheLastNavigationAsync();

        var error = await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.ExpectNavigation("/cart").ToHaveNavigatedAsync(timeout: TimeSpan.FromMilliseconds(50)));
        Assert.Contains("Seen: /orders/7, /orders?page=2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Patterns_follow_request_pattern_rules()
    {
        var log = new NavigationLog();
        var record = log.Add("http://localhost/orders/7?tab=items#notes", "http://localhost/");

        Assert.Equal("orders/7?tab=items#notes", record.RelativeUri);
        Assert.True(NavigationLog.Matches(record, "/orders/7"));
        Assert.True(NavigationLog.Matches(record, "/orders/*"));
        Assert.True(NavigationLog.Matches(record, "/orders/7?tab=items"));
        Assert.False(NavigationLog.Matches(record, "/orders/7?tab=other"));
        Assert.True(NavigationLog.Matches(record, "http://localhost/orders/*"));
        Assert.Equal(log.Records, NavigationLog.ParseJson(log.ToJson()));
    }
}
