using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.Demo.UI;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// Combinatorial testing. The badge's pairwise variants are checked for gaps with <c>Combinatorics.MissingPairs</c>, and
/// the same algorithm drives an ordinary data-driven test: sign-up inputs, chosen pairwise, each run against a story in
/// memory with a hand-written play.
/// </summary>
public sealed class CombinatoricsTests
{
    [Fact]
    public void Badge_variants_cover_every_pair_the_component_supports()
    {
        string[] parameters = ["Tone", "Appearance", "Size", "Dot"];
        object[][] values =
        [
            [.. Enum.GetValues<BadgeTone>().Cast<object>()],
            [.. Enum.GetValues<BadgeAppearance>().Cast<object>()],
            [.. Enum.GetValues<BadgeSize>().Cast<object>()],
            [false, true],
        ];
        int[] sizes = [.. values.Select(v => v.Length)];

        var variants = ComponentStoryTests.Catalog.ForComponent<Badge>().WithTag("visual-matrix").Stories;
        var rows = variants.Select(story => parameters
            .Select((parameter, d) => Array.IndexOf(values[d], story.Args.Single(a => a.Name == parameter).ConstantValue))
            .ToArray());

        // Every pair is rendered except the one the stories exclude on purpose: small badges have no room for a dot.
        var missing = Assert.Single(Combinatorics.MissingPairs(sizes, rows));
        Assert.Equal((2, Array.IndexOf(values[2], BadgeSize.Small), 3, 1), missing);

        // ...in a fraction of the full product.
        Assert.InRange(variants.Count, 1, Combinatorics.CartesianCount(sizes) / 3);
    }

    // Name × email × how the form is submitted: 18 combinations, covered pairwise in about half as many rows.
    public static TheoryData<string, string, string> SignUpInputs()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var row in Combinatorics.Pairwise<string>([["", "   ", "Ada Lovelace"], ["", "ada@", "ada@example.com"], ["click", "enter"]]))
        {
            data.Add(row[0], row[1], row[2]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SignUpInputs))]
    public async Task Sign_up_posts_only_valid_input(string name, string email, string submit)
    {
        await using var session = await BunitStoryRunner.RenderAsync(
            ComponentStoryTests.Catalog.Get("patterns-sign-up-form--empty"),
            cancellationToken: TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;

        await ctx.GetByRole("textbox", "Name").FillAsync(name);
        await ctx.GetByRole("textbox", "Email").FillAsync(email);
        if (submit == "enter")
        {
            await ctx.GetByRole("textbox", "Email").PressAsync(Keys.Enter);
        }
        else
        {
            await ctx.GetByRole("button", "Create account").ClickAsync();
        }

        if (name.Trim().Length > 0 && email == "ada@example.com")
        {
            await ctx.Expect(ctx.GetByRole("status")).ToContainTextAsync("Account created");
            await ctx.ExpectRequest("POST", "api/accounts").ToHaveBeenMadeAsync(times: 1);
        }
        else
        {
            await ctx.Expect(ctx.Locate("[aria-invalid=true]").First).ToBeVisibleAsync();
            await ctx.ExpectRequest("POST", "api/accounts").NotToHaveBeenMadeAsync();
        }
    }
}
