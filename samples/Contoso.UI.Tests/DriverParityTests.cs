using System.Globalization;
using BlazorTestPlatform.Accessibility.Dom;
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Playwright;
using Microsoft.Playwright;

namespace Contoso.UI.Tests;

/// <summary>
/// The browser drivers resolve locators with the workbench's locator engine (btp-driver.js), a port of the in-memory
/// engine. Markup fixtures are rendered in a real story frame and in memory, and every element's role, accessible name,
/// text and state, and the results of many queries, must be the same.
/// </summary>
public sealed class DriverParityTests(WorkbenchFixture workbench)
{
    private static readonly Dictionary<string, string> Fixtures = new()
    {
        ["landmarks"] = """
            <header>Site</header>
            <nav aria-label="Primary"><a href="#home">Home</a> <a>No href</a></nav>
            <main>
              <article><header>Article head</header><h2>Title</h2><footer>Article foot</footer></article>
              <section>Plain section</section><section aria-label="Named">Named section</section><section title="Titled">Titled</section>
              <aside>Aside</aside><search>Find</search><form aria-label="Order"></form>
            </main>
            <footer>Site foot</footer>
            """,
        ["content roles"] = """
            <ul><li>One</li></ul><ol><li>Two</li></ol><menu><li>Three</li></menu><dl><dt>Term</dt><dd>Definition</dd></dl>
            <p>Para <b>b</b> <i>i</i> <u>u</u> <small>s</small> <em>em</em> <strong>st</strong> <code>c</code> <mark>m</mark> <sub>1</sub>
              <sup>2</sup> <del>d</del> <ins>n</ins> <dfn>df</dfn> <q>q</q> <s>s</s> <samp>sa</samp> <data value="1">da</data> <time>t</time> <bdi>bd</bdi> <bdo dir="rtl">bo</bdo> <span>sp</span></p>
            <blockquote>Quote</blockquote><pre>Pre</pre><hr>
            <figure><img src="data:," alt="Chart"><figcaption>Figure caption</figcaption></figure>
            <img src="data:," alt=""><img src="data:," alt="" aria-label="Labelled"><img src="data:,">
            <table><caption>Prices</caption><thead><tr><th>Name</th><th scope="row">Row</th><th scope="ROW">Upper</th></tr></thead>
              <tbody><tr><td>Cell</td></tr></tbody><tfoot><tr><td>Foot</td></tr></tfoot></table>
            <progress value="1" max="2" aria-label="Progress"></progress><meter value="1" aria-label="Meter"></meter><output>Out</output>
            <svg viewBox="0 0 1 1" width="10" height="10"><title>Logo</title><circle cx="0" cy="0" r="1"></circle></svg>
            <math><mi>x</mi></math>
            <details open><summary>Summary</summary><summary>Second summary</summary>Body</details>
            <details><summary>Closed</summary>Hidden body</details>
            <dialog>Closed dialog</dialog><dialog open>Open dialog</dialog>
            <div role="foo button">Tokens</div><div role="widget">Abstract</div><div role="NAVIGATION">Upper</div><div role="doc-note">Doc</div>
            <button role="none">Focusable none</button><span role="presentation">Plain none</span><span role="none" aria-label="Named none">x</span>
            """,
        ["names"] = """
            <span id="first">First</span><span id="second" hidden>Second</span>
            <button aria-labelledby="first second missing">Ignored</button>
            <button aria-label="  ">Blank label</button><button aria-label="Close">X</button>
            <button>Save <span hidden>draft</span><span style="display: none">none</span><span aria-hidden="true">*</span><span style="visibility: hidden">invisible</span></button>
            <button><img src="data:," alt="Print"> report</button><button title="Tip"></button>
            <a href="#block"><div>Block</div><div>Link</div></a><a href="#inline">Inline<span>Link</span></a>
            <label for="email">Email address</label><input id="email" type="email">
            <label>Phone <input type="tel"></label>
            <label for="both">For</label><label>Wrap <input id="both"></label>
            <input type="text" title="Title name"><input type="text" placeholder="Placeholder name"><input type="text" title="Tooltip" placeholder="Hint">
            <textarea placeholder="Notes"></textarea>
            <input type="button" value="Go"><input type="button"><input type="submit"><input type="reset"><input type="submit" value="Ship">
            <input type="image" alt="Map"><input type="image" src="data:,">
            <input type="checkbox" id="agree"><label for="agree">Agree <select><option>to the terms</option><option selected>later</option></select> now</label>
            <label>Size <input type="number" value="3"> <input type="range" value="7" aria-label="Zoom"> items</label>
            <fieldset><legend>Shipping</legend><input aria-label="Street"></fieldset>
            <figure><figcaption>Figure name</figcaption></figure>
            <table><caption>Table name</caption><tbody><tr><td>c</td></tr></tbody></table>
            <select aria-label="Pick"><option>A</option></select><select multiple><option>M</option></select><select size="3"><option>S</option></select>
            <input list="suggestions"><input type="search"><input type="search" list="suggestions"><input type="hidden" value="h">
            <input type="password"><input type="color"><input type="date"><input type="range"><input type="number"><input type="file">
            <h3>Head<span>ing</span></h3><h4><div>Two</div><div>Lines</div></h4><p title="Paragraph title">Text</p>
            <div role="button" aria-label="Fake">Content</div><div role="checkbox" aria-checked="true">Remember</div><div role="switch">Off</div>
            <div role="option" aria-selected="true">Selected</div><div role="treeitem">Tree</div><div role="menuitemcheckbox">Item</div>
            <button aria-pressed="true">Bold</button><button aria-pressed="mixed">Italic</button>
            """,
        ["state"] = """
            <fieldset disabled>
              <legend>Legend <button>In legend</button><input aria-label="Legend input"></legend>
              <button>Inside</button>
              <fieldset><legend>Nested</legend><input aria-label="Nested input"></fieldset>
            </fieldset>
            <select aria-label="Groups"><optgroup label="G" disabled><option>Option</option></optgroup></select>
            <button disabled>Disabled</button><button aria-disabled="TRUE">Aria disabled</button>
            <input readonly aria-label="Read only"><textarea disabled aria-label="Off"></textarea>
            <div contenteditable="true">Edit me</div><div contenteditable="false">Not editable</div><span contenteditable>Empty attribute</span>
            <input type="checkbox" checked aria-label="Checked"><input type="radio" aria-label="Radio"><input type="checkbox" role="switch" checked aria-label="Switch input">
            <div inert><button>Inert button</button></div><div aria-hidden="true"><button>Hidden button</button></div>
            <div hidden>Hidden <button>In hidden</button></div><div style="display: none">None</div>
            """,
        ["text"] = """
            <div><div>A</div><div>B</div></div><p>One<br>Two</p><ul><li>x</li><li>y</li></ul>
            <div>Before<script>var ignored = 1;</script><style>.ignored { color: red; }</style>After</div>
            <span>in</span><span>line</span><div>Mixed <p>paragraph</p> tail</div><h1>Head</h1><h2>Sub</h2> trailing text
            <input type="submit" value="Send it"><button>Button text</button><textarea>Area text</textarea>
            <select><option>Option one</option><option>Option two</option></select>
            <div>Zero&#8203;width</div><div>Non&nbsp;breaking</div><div>  lots   of
              space </div>
            <img src="data:," alt="Photo" title="Photo title"><input placeholder="Search here" data-testid="search">
            """,
    };

    public static TheoryData<string> FixtureNames => new(Fixtures.Keys);

    private static MatrixCase Cell(string storyId, HostingModel mode) =>
        new(ComponentStoryTests.Catalog.Get(storyId), Viewport.Desktop, ThemeDefinition.Light, mode, Browsers.Chromium);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public async Task Browser_locator_engine_matches_the_in_memory_engine(string fixture)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Number the elements so query results can be compared across the two DOMs.
        var scope = DomScope.Parse(Fixtures[fixture]);
        var elements = scope.Elements().ToArray();
        for (var i = 0; i < elements.Length; i++)
        {
            elements[i].SetAttribute("data-n", i.ToString(CultureInfo.InvariantCulture));
        }

        await using var page = await workbench.Environment.Browser.OpenAsync(Cell("testing-driver-parity--text", HostingModel.StaticServer));
        await page.Page.WaitForLoadStateAsync(LoadState.Load);
        await page.Page.EvaluateAsync("html => { document.querySelector('[data-btp-root]').innerHTML = html; }", elements[0].ParentElement!.InnerHtml);

        var all = LocatorChain.Start(By.Css("*"));
        var inMemory = DomQueries.Resolve(scope, all).Select(e => Summary(DomQueries.Describe(e, scope, isFocused: false))).ToArray();
        var inBrowser = (await page.Driver.QueryAsync(all, cancellationToken)).Select(Summary).ToArray();
        Assert.Equal(inMemory, inBrowser);

        var mismatches = new List<string>();
        foreach (var chain in Queries(scope))
        {
            var expected = string.Join(",", DomQueries.Resolve(scope, chain).Select(e => e.GetAttribute("data-n")));
            var actual = string.Join(",", (await page.Driver.QueryAsync(chain, cancellationToken)).Select(e => e.GetAttribute("data-n")));
            if (expected != actual)
            {
                mismatches.Add($"{chain}: in memory [{expected}], in the browser [{actual}]");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public async Task Queries_see_the_story_but_not_the_workbench_around_it()
    {
        var options = new StoryPageOptions();
        options.Args["Section"] = "Bogus";
        await using var page = await workbench.Environment.Browser.OpenAsync(Cell("testing-driver-parity--text", HostingModel.InteractiveServer), options);
        var ctx = page.CreateStoryContext(cancellationToken: TestContext.Current.CancellationToken);
        await Assertions.Expect(page.Page.Locator("[data-btp-arg-errors] li")).ToHaveCountAsync(1);

        // The rejected-args list (role=alert, one item) and the host's #blazor-error-ui are not part of the story.
        await ctx.Expect(ctx.GetByRole("alert")).ToHaveCountAsync(0);
        await ctx.Expect(ctx.GetByRole("listitem")).ToHaveCountAsync(2);
        await ctx.Expect(ctx.GetByRole("listitem").First).ToHaveTextAsync("First");
        await ctx.Expect(ctx.GetByText("Bogus")).ToHaveCountAsync(0);
        await ctx.Expect(ctx.GetByText("An unhandled error has occurred")).ToHaveCountAsync(0);
        Assert.Equal(2, await page.Driver.ToLocator(LocatorChain.Start(By.Css("li"))).CountAsync());
        Assert.Equal(0, await page.Driver.ToLocator(LocatorChain.Start(By.Text("An unhandled error has occurred"))).CountAsync());
    }

    [Fact]
    public async Task Ambiguous_locators_fail_with_the_bunit_drivers_message()
    {
        var story = ComponentStoryTests.Catalog.Get("testing-driver-parity--keyboard");
        await using var session = await BunitStoryRunner.RenderAsync(story, cancellationToken: TestContext.Current.CancellationToken);
        var inMemory = await Assert.ThrowsAsync<ElementNotActionableException>(
            () => session.StoryContext.GetByRole("button").ClickAsync(timeout: TimeSpan.FromMilliseconds(200)));

        await using var page = await workbench.Environment.Browser.OpenAsync(Cell(story.Id, HostingModel.InteractiveServer));
        var ctx = page.CreateStoryContext(cancellationToken: TestContext.Current.CancellationToken);
        var inBrowser = await Assert.ThrowsAsync<ElementNotActionableException>(
            () => ctx.GetByRole("button").ClickAsync(timeout: TimeSpan.FromMilliseconds(200)));

        Assert.StartsWith("Cannot click role=button: strict mode violation: 6 elements match (waited 200 ms).", inBrowser.Message, StringComparison.Ordinal);
        Assert.Equal(inMemory.Message.Split(Environment.NewLine), inBrowser.Message.Split(Environment.NewLine));
    }

    private static string Summary(ElementInfo e) =>
        $"{e.GetAttribute("data-n")} <{e.TagName}> role={e.Role ?? "-"} name=\"{e.AccessibleName}\" text=\"{e.Text}\" enabled={e.IsEnabled} checked={e.IsChecked?.ToString() ?? "-"} editable={e.IsEditable}";

    /// <summary>Role, name, text, label and attribute queries for everything in the fixture, plus chained and filtered ones.</summary>
    private static IEnumerable<LocatorChain> Queries(DomScope scope)
    {
        var described = scope.Elements().Select(e => DomQueries.Describe(e, scope, isFocused: false)).ToArray();
        var queries = new List<LocatorChain>();
        foreach (var role in described.Select(d => d.Role).OfType<string>().Distinct())
        {
            queries.Add(LocatorChain.Start(By.Role(role)));
            queries.Add(LocatorChain.Start(By.Role(role.ToUpperInvariant())));
        }

        foreach (var element in described.Where(d => d.Role is not null && d.AccessibleName.Length > 0))
        {
            queries.Add(LocatorChain.Start(By.Role(element.Role!, element.AccessibleName, exact: true)));
            queries.Add(LocatorChain.Start(By.Role(element.Role!, element.AccessibleName.ToLowerInvariant())));
            queries.Add(LocatorChain.Start(By.Label(element.AccessibleName)));
        }

        foreach (var text in described.Select(d => d.Text).Where(t => t.Length is > 0 and < 60).Distinct())
        {
            queries.Add(LocatorChain.Start(By.Text(text, exact: true)));
            queries.Add(LocatorChain.Start(By.Text(text.ToUpperInvariant())));
            queries.Add(LocatorChain.Start(By.Css("*")).Append(new LocatorStep(LocatorStepKind.HasText, Text: text)));
            queries.Add(LocatorChain.Start(By.Css("div, p, span")).Append(new LocatorStep(LocatorStepKind.HasNotText, Text: text)));
        }

        queries.Add(LocatorChain.Start(By.Placeholder("name")));
        queries.Add(LocatorChain.Start(By.Placeholder("Search here", exact: true)));
        queries.Add(LocatorChain.Start(By.AltText("photo")));
        queries.Add(LocatorChain.Start(By.Title("title")));
        queries.Add(LocatorChain.Start(By.TestId("search")));
        queries.Add(LocatorChain.Start(By.Css("section, fieldset, label, nav, p, div")).Append(new LocatorStep(LocatorStepKind.Find, By.Role("button"))));
        queries.Add(LocatorChain.Start(By.Css("div, label")).Append(new LocatorStep(LocatorStepKind.Find, By.Text("a"))));
        queries.Add(LocatorChain.Start(By.Role("button")).Append(new LocatorStep(LocatorStepKind.Nth, Index: -1)));
        queries.Add(LocatorChain.Start(By.Css("*")).Append(new LocatorStep(LocatorStepKind.Nth, Index: 3)));
        return queries.Distinct();
    }
}

[CollectionDefinition(nameof(TestIdAttributeCollection), DisableParallelization = true)]
public sealed class TestIdAttributeCollection;

/// <summary>Changes the global <see cref="By.TestIdAttribute"/>, so it runs alone.</summary>
[Collection(nameof(TestIdAttributeCollection))]
public sealed class TestIdAttributeTests(WorkbenchFixture workbench)
{
    [Fact]
    public async Task Browser_drivers_read_the_test_id_attribute_when_the_query_runs()
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("testing-driver-parity--text"), Viewport.Desktop, ThemeDefinition.Light, HostingModel.StaticServer, Browsers.Chromium);
        await using var page = await workbench.Environment.Browser.OpenAsync(cell);
        var ctx = page.CreateStoryContext(cancellationToken: TestContext.Current.CancellationToken);
        var original = By.TestIdAttribute;
        try
        {
            await ctx.Expect(ctx.GetByTestId("blocks")).ToHaveTextAsync("Alpha Beta");

            By.TestIdAttribute = "aria-label";
            await ctx.Expect(ctx.GetByTestId("Steps")).ToHaveTextAsync("First Second");
            await ctx.Expect(ctx.GetByTestId("blocks")).ToHaveCountAsync(0);
            Assert.Equal(1, await page.Driver.ToLocator(LocatorChain.Start(By.TestId("Steps"))).CountAsync());
        }
        finally
        {
            By.TestIdAttribute = original;
        }
    }
}
