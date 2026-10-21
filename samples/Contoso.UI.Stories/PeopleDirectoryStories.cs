using BlazorTestPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace Contoso.UI.Stories;

/// <summary>
/// An HTTP-backed component. Every story answers the component's requests from a story-scoped fake backend
/// (<c>MockHttp</c>), so it renders the same in bUnit, under Interactive Server and in WebAssembly.
/// </summary>
public sealed class PeopleDirectoryStories : Stories<PeopleDirectory>
{
    private static readonly Person[] Everyone =
    [
        new("Ada Lovelace", "ada@example.com", "Engineering"),
        new("Grace Hopper", "grace@example.com", "Engineering"),
        new("Dieter Rams", "dieter@example.com", "Design"),
    ];

    public override string Title => "Data/People directory";

    protected override void Define(StoryCollectionBuilder<PeopleDirectory> stories)
    {
        stories.Defaults(s => s
            .Services(services => services.AddTransient<PeopleClient>())
            .MockHttp(http =>
            {
                http.Get("api/people").RespondJson(Everyone, ContosoJsonContext.Default.PersonArray);
                http.Get("api/people?team=Design").RespondJson([.. Everyone.Where(p => p.Team == "Design")], ContosoJsonContext.Default.PersonArray);
                http.Get("api/people?team=Support").RespondJson([], ContosoJsonContext.Default.PersonArray);
            }));

        stories.Add("Loaded");

        stories.Add("Empty team", s => s.MockHttp(http => http.Get("api/people").RespondJson([], ContosoJsonContext.Default.PersonArray)));

        stories.Add("Loading", s => s
            .MockHttp(http => http.Get("api/people").Never())
            .Prerender(false));

        stories.Add("Server error with retry", s => s
            .MockHttp(http => http.Get("api/people").RespondStatus(500).Times(1))
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByRole("alert")).ToContainTextAsync("The server answered 500.");
                await ctx.GetByRole("button", "Retry").ClickAsync();
                await ctx.Expect(ctx.GetByRole("listitem")).ToHaveCountAsync(3);
                await ctx.ExpectRequest("GET", "api/people").ToHaveBeenMadeAsync(times: 2);
            }));

        stories.Add("Filter by team", s => s.Play(async ctx =>
        {
            await ctx.Expect(ctx.GetByRole("listitem")).ToHaveCountAsync(3);

            await ctx.GetByLabel("Team", exact: true).SelectOptionAsync("Design");
            await ctx.ExpectRequest("GET", "api/people?team=Design").ToHaveBeenMadeAsync(times: 1);
            await ctx.Expect(ctx.GetByRole("listitem")).ToHaveCountAsync(1);
            await ctx.Expect(ctx.GetByRole("listitem")).ToContainTextAsync("Dieter Rams");

            await ctx.GetByLabel("Team", exact: true).SelectOptionAsync("Support");
            await ctx.Expect(ctx.GetByText("No people in this team yet.")).ToBeVisibleAsync();
            await ctx.ExpectRequest("POST", "api/*").NotToHaveBeenMadeAsync();
        }));
    }
}
