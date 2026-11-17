using System.Text.RegularExpressions;
using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class FieldStatesStories : Stories<FieldStates>
{
    public override string Title => "Tests/Field states";

    protected override void Define(StoryCollectionBuilder<FieldStates> stories) =>
        stories.Add("Default", s => s.Accessibility(a => a.Skip = true));
}

public sealed partial class MatcherTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<MatcherTests>();
    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(50);

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Accessibility_state_matchers_read_descriptions_errors_and_requirements()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-field-states--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;
        var email = ctx.GetByLabel("Email");

        await ctx.Expect(email).ToHaveAccessibleDescriptionAsync("We never share it.");
        await ctx.Expect(email).ToHaveAccessibleDescriptionAsync(Hint());
        await ctx.Expect(email).ToHaveAccessibleErrorMessageAsync("Enter a complete address.");
        await ctx.Expect(email).ToBeRequiredAsync();
        await ctx.Expect(email).ToBeInvalidAsync();
        await ctx.Expect(email).ToHaveIdAsync("email");
        await ctx.Expect(email).ToContainClassesAsync("wide", "field--error field");
        await ctx.Expect(email).ToHaveClassAsync(FieldClass());
        await ctx.Expect(email).ToHaveValueAsync(PartialEmail());
        await ctx.Expect(email).ToHaveAttributeAsync("type", EmailType());
        await ctx.Expect(email).Not.ToBeEmptyAsync();

        // A title that names the element does not describe it again; one that does not name it does.
        await ctx.Expect(ctx.GetByRole("button", "Save", exact: true)).ToHaveAccessibleDescriptionAsync("");
        await ctx.Expect(ctx.GetByRole("button", "Save draft")).ToHaveAccessibleDescriptionAsync("Saves the draft");
        await ctx.Expect(ctx.GetByRole("button", "Save draft")).ToHaveAccessibleNameAsync(SaveName());

        var all = ctx.GetByRole("checkbox", "All items");
        await ctx.Expect(all).ToBePartiallyCheckedAsync();
        await ctx.Expect(all).ToBeRequiredAsync();
        await ctx.Expect(all).Not.ToBeInvalidAsync();
        await ctx.Expect(all).ToHaveAccessibleErrorMessageAsync("");

        await ctx.Expect(ctx.GetByLabel("Notes")).ToBeEmptyAsync();
        await ctx.Expect(ctx.GetByTestId("empty")).ToBeEmptyAsync();
        await ctx.Expect(ctx.GetByRole("listitem")).ToHaveTextAsync(["Alpha", "Beta", "Gamma"]);
    }

    [Fact]
    public async Task Failing_state_matchers_explain_the_actual_state()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-field-states--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;

        var order = await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.Expect(ctx.GetByRole("listitem")).ToHaveTextAsync(["Alpha", "Gamma"], Quick));
        Assert.Contains("Actual:   [\"Alpha\", \"Beta\", \"Gamma\"]", order.Message, StringComparison.Ordinal);

        var description = await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.Expect(ctx.GetByLabel("Email")).ToHaveAccessibleDescriptionAsync("Required", Quick));
        Assert.Contains("\"We never share it.\"", description.Message, StringComparison.Ordinal);

        var optional = await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.Expect(ctx.GetByLabel("Notes")).ToBeRequiredAsync(Quick));
        Assert.Contains("optional", optional.Message, StringComparison.Ordinal);

        var classes = await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.Expect(ctx.GetByLabel("Email")).ToContainClassesAsync("field", "narrow"));
        Assert.Contains("field field--error wide", classes.Message, StringComparison.Ordinal);
    }

    [GeneratedRegex("never share")]
    private static partial Regex Hint();

    [GeneratedRegex(@"\bfield--error\b")]
    private static partial Regex FieldClass();

    [GeneratedRegex("^ada@$")]
    private static partial Regex PartialEmail();

    [GeneratedRegex("^e")]
    private static partial Regex EmailType();

    [GeneratedRegex("^Save d")]
    private static partial Regex SaveName();
}
