using System.Text.RegularExpressions;
using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Visual masks. Every render mode is compared against one baseline, so SSR, Server and WebAssembly must look the same.
/// This badge legitimately differs ("Server" or "WebAssembly"), so its snapshot masks the value and the play script
/// checks it as text instead.
/// </summary>
public sealed class RuntimeBadgeStories : Stories<RuntimeBadge>
{
    public override string Title => "Display/Runtime badge";

    protected override void Define(StoryCollectionBuilder<RuntimeBadge> stories) =>
        stories.Add("Masked value", s => s
            .Description("The value differs per render mode, so the snapshot masks it (Visual(v => v.Masks.Add(...))) and the play script asserts it for the mode that actually ran.")
            .Visual(v => v.Masks.Add(".c-runtime__value"))
            .Play(async ctx =>
            {
                var runtime = ctx.GetByTestId("runtime");
                switch (ctx.HostingModel)
                {
                    case HostingModel.InteractiveWebAssembly:
                        await ctx.Expect(runtime).ToHaveTextAsync("WebAssembly");
                        break;
                    case HostingModel.InteractiveAuto:
                        await ctx.Expect(runtime).ToHaveTextAsync(new Regex("^(Server|WebAssembly)$"));
                        break;
                    default:
                        await ctx.Expect(runtime).ToHaveTextAsync("Server");
                        break;
                }
            }));
}
