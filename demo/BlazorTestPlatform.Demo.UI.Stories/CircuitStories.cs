using BlazorTestPlatform;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Circuit pause and resume: <c>ctx.PauseAndResumeCircuitAsync()</c> releases the story's Blazor Server circuit and resumes
/// it, as when a user comes back to a tab the browser put to sleep. State in <c>[PersistentState]</c> properties survives;
/// the rest starts over. Browsers under Interactive Server only; other drivers and render modes skip it. A test fixture: no
/// visual baselines.
/// </summary>
public sealed class CircuitStories : Stories<CircuitDraft>
{
    public override string Title => "Testing/Circuit";

    protected override void Define(StoryCollectionBuilder<CircuitDraft> stories) =>
        stories.Add("Pause and resume", s => s
            .Description("Adds twice, pauses and resumes the circuit: the [PersistentState] count is restored, the in-memory one starts over.")
            .NoVisual()

            // Declared, so other drivers and render modes skip it before rendering. (Static SSR could not render it
            // anyway: ASP.NET Core rejects [PersistentState] on a component outside any render mode.)
            .Requires(DriverCapabilities.CircuitControl, "pausing and resuming the circuit")
            .Play(async ctx =>
            {
                var add = ctx.GetByRole("button", "Add");
                await add.ClickAsync();
                await add.ClickAsync();
                await ctx.Expect(ctx.GetByTestId("unsaved")).ToHaveTextAsync("Unsaved: 2");

                await ctx.PauseAndResumeCircuitAsync();

                await ctx.Expect(ctx.GetByTestId("saved")).ToHaveTextAsync("Saved: 2");
                await ctx.Expect(ctx.GetByTestId("unsaved")).ToHaveTextAsync("Unsaved: 0");
                await add.ClickAsync();
                await ctx.Expect(ctx.GetByTestId("saved")).ToHaveTextAsync("Saved: 3");
            }));
}
