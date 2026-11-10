using BlazorTestPlatform;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>Fake users: anonymous, signed in, with roles and claims, and still authenticating. Real authorization services evaluate them.</summary>
public sealed class UserBadgeStories : Stories<UserBadge>
{
    public override string Title => "Account/User badge";

    protected override void Define(StoryCollectionBuilder<UserBadge> stories)
    {
        stories.Add("Anonymous", s => s.Description("Anonymous(): AuthorizeView renders its NotAuthorized content.").Anonymous());
        stories.Add("Signed in", s => s.Description("AuthenticatedAs(...) signs a fake user in, in bUnit and in every render mode.").AuthenticatedAs("Ada Lovelace"));
        stories.Add("Administrator", s => s
            .Description("TestUser carries roles and claims; AuthorizeView Roles=\"Admin\" sees them.")
            .User(TestUser.Named("Grace Hopper").WithRoles("Admin").WithClaim("email", "grace@example.com"))
            .Play(ctx => ctx.Expect(By.Text("Admin", exact: true)).ToBeVisibleAsync()));
        stories.Add("Signing in", s => s
            .Description("An authentication state that never resolves shows the Authorizing content. Its first render never completes, so the story turns prerendering off.")
            .User(TestUser.Authenticating)
            .Prerender(false));
    }
}

/// <summary>Fake time: components that inject TimeProvider run on a clock the play script controls.</summary>
public sealed class SessionTimerStories : Stories<SessionTimer>
{
    public override string Title => "Feedback/Session timer";

    protected override void Define(StoryCollectionBuilder<SessionTimer> stories)
    {
        stories.Defaults(s => s.UseFakeTime(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)));

        stories.Add("Counting down", s => s.Description("UseFakeTime(...) starts every story at 09:00 on 1 January 2026, so the snapshot never shows a different time."));
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
