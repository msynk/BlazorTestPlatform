using BlazorTestPlatform;

namespace Contoso.UI.Stories;

/// <summary>
/// The banner reads and writes <c>localStorage</c> through JS interop. The mocks apply in bUnit and, with
/// <c>ApplyInBrowsers()</c>, in every browser render mode too, so the story looks the same everywhere and never
/// depends on what an earlier run left in the browser's storage.
/// </summary>
public sealed class CookieConsentStories : Stories<CookieConsent>
{
    public override string Title => "Feedback/Cookie consent";

    protected override void Define(StoryCollectionBuilder<CookieConsent> stories)
    {
        stories.Add("First visit", s => s
            .MockJS(js => js
                .ApplyInBrowsers()
                .Setup<string?>("localStorage.getItem", null, CookieConsent.StorageKey)
                .SetupVoid("localStorage.setItem"))
            .Play(async ctx =>
            {
                var banner = ctx.GetByRole("region", "Cookie consent");
                await ctx.Expect(banner).ToBeVisibleAsync();

                await ctx.GetByRole("button", "Accept").ClickAsync();

                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Cookie preferences saved: accepted.");
                await ctx.ExpectJSCall("localStorage.setItem").ToHaveBeenLastCalledWithAsync(CookieConsent.StorageKey, "accepted");
                await ctx.ExpectAction("OnDecision").ToHaveBeenCalledAsync(times: 1);
            }));

        stories.Add("Returning visitor", s => s
            .MockJS(js => js
                .ApplyInBrowsers()
                .Setup("localStorage.getItem", "declined", CookieConsent.StorageKey))
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Cookie preferences saved: declined.");
                await ctx.ExpectJSCall("localStorage.getItem").ToHaveBeenCalledAsync(times: 1);
                await ctx.ExpectJSCall("localStorage.setItem").NotToHaveBeenCalledAsync();
            }));
    }
}
