using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>Collection defaults, a single-parameter variant set, and a callback asserted after the element is gone.</summary>
public sealed class AlertStories : Stories<Alert>
{
    public override string Title => "Feedback/Alert";

    protected override void Define(StoryCollectionBuilder<Alert> stories)
    {
        stories.Defaults(s => s
            .Arg(a => a.Title, "Heads up")
            .ChildContent("Your trial ends in 3 days."));

        stories.AddVariants(
            v => v.Vary(a => a.Severity, AlertSeverity.Info, AlertSeverity.Success, AlertSeverity.Warning, AlertSeverity.Error),
            s => s.Description("Errors and warnings use role=alert, the others role=status. Story state coverage checks that every severity has a story."));

        stories.Add("Dismissible", s => s
            .Description("Defaults(...) gives every story the same title and text. Dismissing removes the alert and raises OnDismiss once.")
            .Arg(a => a.Dismissible, true)
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByRole("status")).ToContainTextAsync("trial ends");
                await ctx.GetByRole("button", "Dismiss").ClickAsync();
                await ctx.Expect(ctx.GetByRole("status")).ToHaveCountAsync(0);
                await ctx.ExpectAction("OnDismiss").ToHaveBeenCalledAsync(times: 1);
            }));
    }
}
