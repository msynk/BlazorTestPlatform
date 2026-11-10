using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Arguments beyond plain values: data computed from the story's seed (<c>Arg(..., ctx =&gt; ctx.Fake...)</c>), a
/// <c>RenderFragment&lt;T&gt;</c> template, a <c>RenderFragment</c>, a cascading value, and a render budget on a long list.
/// </summary>
public sealed class PersonListStories : Stories<PersonList>
{
    private static readonly string[] Teams = ["Design", "Engineering", "Support"];

    private static readonly Person[] Unsorted =
    [
        new("Grace Hopper", "grace@example.com", "Engineering"),
        new("Ada Lovelace", "ada@example.com", "Engineering"),
        new("Margaret Hamilton", "margaret@example.com", "Support"),
        new("Dieter Rams", "dieter@example.com", "Design"),
    ];

    public override string Title => "Data/Person list";

    protected override void Define(StoryCollectionBuilder<PersonList> stories)
    {
        stories.Defaults(s => s
            .Arg(l => l.Label, "Team")
            .Arg(l => l.People, ctx => SeededPeople(ctx, 4)));

        stories.Add("Seeded people", s => s
            .Description("Names, emails and teams come from FakeData, seeded by the story id: the same people on every machine, in every render mode and in every snapshot."));

        stories.Add("Custom item template", s => s
            .Description("Template(...) supplies a RenderFragment<Person>, written in Razor in Fragments.razor.")
            .Template(l => l.ItemTemplate, Fragments.PersonCard));

        stories.Add("Empty with custom content", s => s
            .Description("Fragment(...) supplies a RenderFragment parameter: here the empty state.")
            .Arg(l => l.People, [])
            .Fragment(l => l.EmptyContent, Fragments.InviteTeammates));

        stories.Add("Compact density", s => s
            .Description("A decorator supplies the Density a layout would cascade (CascadingValue). Container, Wrap<T> and Decorate are the other decorators.")
            .CascadingValue(Density.Compact));

        stories.Add("Sort by name", s => s
            .Description("A render budget: sorting may render the list once more, never more (MaxRootRenders).")
            .Arg(l => l.Sortable, true)
            .Arg(l => l.People, Unsorted)
            .Performance(p => p.MaxRootRenders = 2)
            .Play(async ctx =>
            {
                var sort = ctx.GetByRole("button", "Sort by name");
                await ctx.Expect(ctx.GetByRole("listitem").First).ToContainTextAsync("Grace Hopper");
                await ctx.Expect(sort).ToHaveAttributeAsync("aria-pressed", "false");

                await sort.ClickAsync();

                await ctx.Expect(sort).ToHaveAttributeAsync("aria-pressed", "true");
                await ctx.Expect(ctx.GetByRole("listitem").First).ToContainTextAsync("Ada Lovelace");
                await ctx.Expect(ctx.GetByRole("listitem").Last).ToContainTextAsync("Margaret Hamilton");
                await ctx.ExpectAction("OnSortChanged").ToHaveBeenLastCalledWithAsync(true);
            }));

        stories.Add("Large list", s => s
            .Description("Performance gates: 200 seeded people must render in one pass under a memory budget (bUnit, with a committed baseline), and stay under a DOM-size and layout-shift budget in real browsers.")
            .Arg(l => l.People, ctx => SeededPeople(ctx, 200))
            .NoVisual()
            .Performance(p =>
            {
                p.MaxTotalRenders = 1;
                p.MaxAllocatedBytes = 1_500_000;
            })
            .BrowserBudget(b =>
            {
                b.MaxDomNodes = 1_500;
                b.MaxLayoutShift = 0.1;
            })
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByRole("listitem").Nth(199)).ToBeVisibleAsync();
                await ctx.Expect(ctx.GetByRole("listitem").Nth(200)).ToHaveCountAsync(0);
            }));
    }

    private static IReadOnlyList<Person> SeededPeople(StoryRenderContext ctx, int count) =>
        ctx.Fake.Many(count, (fake, _) =>
        {
            var name = fake.FullName();
            return new Person(name, fake.Email(name), fake.Random.Pick(Teams));
        });
}
