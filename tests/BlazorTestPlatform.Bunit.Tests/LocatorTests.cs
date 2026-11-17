using System.Text.RegularExpressions;
using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class RowsStories : Stories<Rows>
{
    public override string Title => "Tests/Rows";

    protected override void Define(StoryCollectionBuilder<Rows> stories) => stories.Add("Default");
}

public sealed partial class LocatorTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<LocatorTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Rows_are_found_by_what_they_contain_and_text_patterns()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-rows--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;
        var rows = ctx.GetByRole("listitem");

        await ctx.Expect(rows.Filter(ctx.GetByRole("button", "Delete"))).ToHaveTextAsync(["Order 12Delete", "Order 14Delete"]);
        await ctx.Expect(rows.FilterNot(ctx.GetByRole("button"))).ToHaveCountAsync(2);
        await ctx.Expect(rows.Filter(OddOrder())).ToHaveCountAsync(2);
        await ctx.Expect(rows.FilterNot(OddOrder())).ToHaveCountAsync(2);
        await ctx.Expect(ctx.GetByText(OrderTwelve())).ToHaveCountAsync(1);

        await rows.Filter("Order 12").GetByRole("button", "Delete").ClickAsync();
        await ctx.Expect(rows).ToHaveTextAsync(["Order 11", "Order 13", "Order 14Delete"]);
    }

    [Fact]
    public async Task Roles_are_filtered_by_state_and_level()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-rows--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;

        await ctx.Expect(ctx.GetByRole("button", pressed: true)).ToHaveTextAsync("Bold");
        await ctx.Expect(ctx.GetByRole("button", pressed: false)).ToHaveTextAsync("Italic");
        await ctx.Expect(ctx.GetByRole("heading", level: 3)).ToHaveTextAsync("Archived");
        await ctx.Expect(ctx.GetByRole("heading", namePattern: OrdersHeading())).ToHaveCountAsync(1);
        Assert.Equal("role=heading[name=/^ord/i]", ctx.GetByRole("heading", namePattern: OrdersHeading()).ToString());
        Assert.Throws<ArgumentException>(() => ctx.GetByRole("heading", "Orders", namePattern: OrdersHeading()));
    }

    [GeneratedRegex(@"Order \d*[13]\b")]
    private static partial Regex OddOrder();

    [GeneratedRegex("^Order 12")]
    private static partial Regex OrderTwelve();

    [GeneratedRegex("^ord", RegexOptions.IgnoreCase)]
    private static partial Regex OrdersHeading();
}
