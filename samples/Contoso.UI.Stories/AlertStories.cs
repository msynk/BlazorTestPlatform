using BlazorTestPlatform;

namespace Contoso.UI.Stories;

public sealed class AlertStories : Stories<Alert>
{
    public override string Title => "Feedback/Alert";

    protected override void Define(StoryCollectionBuilder<Alert> stories)
    {
        stories.Defaults(s => s
            .Arg(a => a.Title, "Heads up")
            .ChildContent("Your trial ends in 3 days."));

        stories.AddVariants(v => v.Vary(a => a.Severity, AlertSeverity.Info, AlertSeverity.Success, AlertSeverity.Warning, AlertSeverity.Error));

        stories.Add("Dismissible", s => s
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
