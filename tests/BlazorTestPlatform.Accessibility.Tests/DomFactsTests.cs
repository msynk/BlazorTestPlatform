using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility.Tests;

public sealed class DomFactsTests
{
    private const string Markup = """
        <button id="before">before</button>
        <fieldset disabled>
            <legend><button id="in-legend">legend</button></legend>
            <legend><button id="second-legend">second legend</button></legend>
            <input id="field">
            <fieldset id="nested"><button id="nested-button">nested</button></fieldset>
            <a id="link" href="#">link</a>
        </fieldset>
        <div inert><button id="inert-button">inert</button><p><span id="inert-span" tabindex="0">inert span</span></p></div>
        <button id="aria-disabled" aria-disabled="true">aria</button>
        <select id="select"><optgroup label="g" disabled><option id="grouped">a</option></optgroup><option id="free">b</option></select>
        <details><summary id="s1">one</summary><summary id="s2">two</summary><p id="body">body</p></details>
        <details open><summary id="s3">three</summary><summary id="s4">four</summary></details>
        <button id="after">after</button>
        """;

    private static readonly DomScope Scope = DomScope.Parse(Markup);

    private static AngleSharp.Dom.IElement Element(string id) => Scope.GetElementById(id)!;

    [Fact]
    public void Disabled_fieldsets_inert_subtrees_and_extra_summaries_leave_the_tab_order()
    {
        Assert.Equal(["before", "in-legend", "link", "aria-disabled", "select", "s1", "s3", "after"], DomFacts.TabOrder(Scope).Select(e => e.Id));
    }

    [Theory]
    [InlineData("field", true)]
    [InlineData("nested", true)]
    [InlineData("nested-button", true)]
    [InlineData("second-legend", true)]
    [InlineData("in-legend", false)]
    [InlineData("link", false)]
    [InlineData("grouped", true)]
    [InlineData("free", false)]
    [InlineData("aria-disabled", false)]
    [InlineData("inert-button", false)]
    public void Native_disabled_state_follows_fieldsets_legends_and_optgroups(string id, bool disabled)
    {
        Assert.Equal(disabled, DomFacts.IsNativelyDisabled(Element(id)));
        Assert.Equal(disabled || id == "aria-disabled", DomFacts.IsDisabled(Element(id)));
    }

    [Theory]
    [InlineData("before", true)]
    [InlineData("in-legend", true)]
    [InlineData("field", false)]
    [InlineData("nested-button", false)]
    [InlineData("inert-button", false)]
    [InlineData("inert-span", false)]
    [InlineData("aria-disabled", true)]
    [InlineData("s1", true)]
    [InlineData("s2", false)]
    [InlineData("s3", true)]
    [InlineData("s4", false)]
    public void Focusability_excludes_disabled_inert_and_non_toggling_summaries(string id, bool focusable)
    {
        Assert.Equal(focusable, DomFacts.IsFocusable(Element(id), Scope));
    }

    [Fact]
    public void Inert_applies_to_the_whole_subtree_but_does_not_disable()
    {
        Assert.True(DomFacts.IsInert(Element("inert-span")));
        Assert.False(DomFacts.IsInert(Element("before")));
        Assert.False(DomFacts.IsDisabled(Element("inert-button")));
    }

    [Fact]
    public void Only_the_first_summary_stays_visible_in_closed_details()
    {
        Assert.True(DomFacts.IsDetailsSummary(Element("s1")));
        Assert.False(DomFacts.IsDetailsSummary(Element("s2")));
        Assert.False(DomFacts.IsHidden(Element("s1")));
        Assert.True(DomFacts.IsHidden(Element("s2")));
        Assert.True(DomFacts.IsHidden(Element("body")));
        Assert.False(DomFacts.IsHidden(Element("s4")));
    }

    [Fact]
    public void Css_paths_resolve_to_exactly_their_element()
    {
        // Without anchoring, the second button's path "div:nth-child(2) > button:nth-child(1)" also matches the first.
        var scope = DomScope.Parse("<div><label>a</label><div><button>A</button></div></div><div><button>B</button></div>" + Markup.Replace("id=", "data-id=", StringComparison.Ordinal));

        foreach (var element in scope.Elements())
        {
            var path = DomFacts.CssPath(element, scope);
            Assert.Same(element, Assert.Single(scope.QuerySelectorAll(path)));
        }

        Assert.Equal("body > div:nth-child(2) > button:nth-child(1)", DomFacts.CssPath(scope.QuerySelectorAll("button").ElementAt(1), scope));
    }
}
