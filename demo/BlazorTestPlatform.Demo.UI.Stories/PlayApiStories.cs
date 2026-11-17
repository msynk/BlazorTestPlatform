using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>A part of <see cref="PlayApiPanel"/>.</summary>
public enum PlayApiSection
{
    /// <summary>A visually hidden InputFile behind a label.</summary>
    Upload,

    /// <summary>Rows with per-row actions, toggle buttons and headings.</summary>
    Locators,

    /// <summary>A field with a description, an error message and required/invalid states.</summary>
    FieldStates,

    /// <summary>Buttons that navigate with NavigationManager.</summary>
    Navigation,

    /// <summary>A component JavaScript calls back through [JSInvokable] methods.</summary>
    Interop,
}

/// <summary>
/// The play API in every driver: file uploads, locator filters and role states, accessibility-state matchers, partial
/// accessibility trees, and assertions that retry, poll or must stay true. These play scripts run in bUnit, in Playwright
/// (Server, WebAssembly, Auto, Firefox, WebKit) and in the in-app driver. Test fixtures, not components: no visual baselines.
/// </summary>
public sealed class PlayApiStories : Stories<PlayApiPanel>
{
    public override string Title => "Testing/Play API";

    protected override void Define(StoryCollectionBuilder<PlayApiPanel> stories)
    {
        stories.Defaults(s => s.NoVisual());

        stories.Add("File upload", s => s
            .Description("SetInputFilesAsync chooses files in a (visually hidden) InputFile, as the file picker would; no files clears the choice.")
            .Arg(p => p.Section, PlayApiSection.Upload)
            .Play(async ctx =>
            {
                var input = ctx.GetByLabel("Attach files");
                await input.SetInputFilesAsync(
                    StoryFile.FromText("notes.txt", "Hello"),
                    StoryFile.FromText("totals.csv", "a,b", "text/csv"));

                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("2 file(s)");
                await ctx.Expect(ctx.GetByRole("list", "Chosen files").GetByRole("listitem")).ToHaveTextAsync(
                [
                    "notes.txt (text/plain, 5 bytes): Hello",
                    "totals.csv (text/csv, 3 bytes): a,b",
                ]);

                await input.SetInputFilesAsync();
                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("No files");
            }));

        stories.Add("Locator filters", s => s
            .Description("Rows found by what they contain (Filter(has)), text patterns, role states (pressed, level) and a partial accessibility tree with a name pattern.")
            .Arg(p => p.Section, PlayApiSection.Locators)
            .Play(async ctx =>
            {
                var rows = ctx.GetByRole("list", "Orders").GetByRole("listitem");
                await ctx.Expect(rows.Filter(ctx.GetByRole("button", "Delete"))).ToHaveCountAsync(2);
                await ctx.Expect(rows.FilterNot(ctx.GetByRole("button"))).ToHaveTextAsync(["Order 11", "Order 13"]);
                await ctx.Expect(ctx.GetByText(new System.Text.RegularExpressions.Regex("^Order 1[24]$"))).ToHaveCountAsync(2);

                await rows.Filter("Order 12").GetByRole("button", "Delete").ClickAsync();
                await ctx.Expect(rows).ToHaveCountAsync(3);

                await ctx.Expect(ctx.GetByRole("heading", level: 3)).ToHaveTextAsync("Archived orders");
                await ctx.Expect(ctx.GetByRole("button", pressed: true)).ToHaveTextAsync("Bold");
                await ctx.GetByRole("button", "Bold").ClickAsync();
                await ctx.Expect(ctx.GetByRole("button", pressed: true)).ToHaveCountAsync(0);

                await ctx.ExpectAriaSnapshotAsync("""
                    - toolbar "Format":
                      - button "Bold" [pressed=false]
                    - list "Orders":
                      - listitem:
                        - button /^Del/
                    """, match: AriaSnapshotMatch.Contains);
            }));

        stories.Add("Navigation", s => s
            .Description("ExpectNavigation asserts where a component navigated; the story stays in its frame because navigations to other pages are stopped.")
            .Arg(p => p.Section, PlayApiSection.Navigation)
            .Play(async ctx =>
            {
                await ctx.ExpectNavigation("/orders/*").NotToHaveNavigatedAsync();
                await ctx.GetByRole("button", "Open order 42").ClickAsync();
                await ctx.ExpectNavigation("/orders/42").ToHaveNavigatedAsync(times: 1);

                await ctx.GetByRole("button", "Show open orders").ClickAsync();
                await ctx.ExpectNavigation("/orders?status=open").ToBeTheLastNavigationAsync();
                await ctx.ExpectNavigation(new System.Text.RegularExpressions.Regex("^/orders")).ToHaveNavigatedAsync(times: 2);

                // Still on the story: the navigations were recorded, not followed.
                await ctx.Expect(ctx.GetByRole("button", "Open order 42")).ToBeVisibleAsync();
            }));

        stories.Add("JavaScript callbacks", s => s
            .Description("InvokeDotNetAsync plays the JavaScript side of interop: it calls the component's [JSInvokable] methods through the DotNetObjectReference it passed to (mocked) JavaScript.")
            .Arg(p => p.Section, PlayApiSection.Interop)
            .MockJS(js => js.SetupVoid("playApi.observeSize").ApplyInBrowsers())
            .Play(async ctx =>
            {
                await ctx.ExpectJSCall("playApi.observeSize").ToHaveBeenCalledAsync(times: 1);
                await ctx.InvokeDotNetAsync("playApi.observeSize", "OnResize", 320, 200);
                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("320 × 200");
                await ctx.ExpectPollAsync(() => ctx.InvokeDotNetAsync("playApi.observeSize", "describe"), r => r == "\"320x200\"", "the component to describe its size");
            }));

        stories.Add("Field states", s => s
            .Description("Accessibility-state matchers (description, error message, required, invalid, mixed), soft assertions, and assertions that retry or must stay true.")
            .Arg(p => p.Section, PlayApiSection.FieldStates)
            .Play(async ctx =>
            {
                var email = ctx.GetByLabel("Email");
                await ctx.ExpectSoft(email).ToHaveAccessibleDescriptionAsync("We only use it for receipts.");
                await ctx.ExpectSoft(email).ToBeRequiredAsync();
                await ctx.ExpectSoft(email).ToBeInvalidAsync();
                await ctx.ExpectSoft(email).ToHaveAccessibleErrorMessageAsync("Enter a complete address.");
                await ctx.ExpectSoft(ctx.GetByRole("checkbox", "Select all orders")).ToBePartiallyCheckedAsync();

                await ctx.ExpectEventuallyAsync(async () =>
                {
                    await email.FillAsync("ada@example.com");
                    await ctx.Expect(email).Not.ToBeInvalidAsync();
                });
                await ctx.Expect(email).ToHaveAccessibleErrorMessageAsync(string.Empty);
                await ctx.ExpectToRemainAsync(() => ctx.Expect(email).ToHaveValueAsync("ada@example.com"), TimeSpan.FromMilliseconds(300), "the address to stay");
            }));
    }
}
