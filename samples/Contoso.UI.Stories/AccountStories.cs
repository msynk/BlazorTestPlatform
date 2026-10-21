using BlazorTestPlatform;
using BlazorTestPlatform.Drivers;

namespace Contoso.UI.Stories;

public sealed class UserBadgeStories : Stories<UserBadge>
{
    public override string Title => "Account/User badge";

    protected override void Define(StoryCollectionBuilder<UserBadge> stories)
    {
        stories.Add("Anonymous", s => s.Anonymous());
        stories.Add("Signed in", s => s.AuthenticatedAs("Ada Lovelace"));
        stories.Add("Administrator", s => s
            .User(TestUser.Named("Grace Hopper").WithRoles("Admin").WithClaim("email", "grace@example.com"))
            .Play(ctx => ctx.Expect(By.Text("Admin", exact: true)).ToBeVisibleAsync()));
        stories.Add("Signing in", s => s.User(TestUser.Authenticating).Prerender(false));
    }
}

public sealed class SessionTimerStories : Stories<SessionTimer>
{
    public override string Title => "Feedback/Session timer";

    protected override void Define(StoryCollectionBuilder<SessionTimer> stories)
    {
        stories.Defaults(s => s.UseFakeTime(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)));

        stories.Add("Counting down");
        stories.Add("Expires", s => s
            .Description("Time is fake and controlled by the play script, so this runs instantly and never flakes.")
            .NoVisual()
            .Play(async ctx =>
            {
                ctx.Require(DriverCapabilities.FakeTime, "controlling the clock");
                var timer = ctx.GetByRole("timer");
                await ctx.Expect(timer).ToHaveTextAsync("Session expires in 2:00");
                await ctx.AdvanceTimeAsync(TimeSpan.FromSeconds(30));
                await ctx.Expect(timer).ToHaveTextAsync("Session expires in 1:30");
                await ctx.AdvanceTimeAsync(TimeSpan.FromMinutes(2));
                await ctx.Expect(timer).ToHaveTextAsync("Session expired");
            }));
    }
}
