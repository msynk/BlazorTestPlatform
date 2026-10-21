using BlazorTestPlatform.Accessibility.Dom;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Accessibility.Tests;

public sealed class CssVisibilityTests
{
    private static bool Hidden(string css, string html, string id)
    {
        var scope = DomScope.Parse(html, CssVisibility.Parse(css));
        return DomFacts.IsHidden(scope.GetElementById(id)!, scope);
    }

    [Fact]
    public void Class_rules_hide_elements_and_their_descendants()
    {
        Assert.True(Hidden(".collapsed { display: none }", "<div class='collapsed'><span id='x'>x</span></div>", "x"));
        Assert.False(Hidden(".collapsed { display: none }", "<div><span id='x'>x</span></div>", "x"));
    }

    [Fact]
    public void Specificity_and_source_order_decide()
    {
        const string css = ".panel { display: none } .panel.open { display: block } #p2 { display: none }";
        Assert.False(Hidden(css, "<div id='p' class='panel open'></div>", "p"));
        Assert.True(Hidden(css, "<div id='p2' class='panel open'></div>", "p2"));
        Assert.False(Hidden(".a { display: none } .a { display: flex }", "<div id='a' class='a'></div>", "a"));
    }

    [Fact]
    public void Important_beats_specificity_and_inline_styles()
    {
        Assert.True(Hidden(".a { display: none !important } #a { display: block }", "<div id='a' class='a'></div>", "a"));
        Assert.True(Hidden(".a { display: none !important }", "<div id='a' class='a' style='display: block'></div>", "a"));
        Assert.False(Hidden(".a { display: none }", "<div id='a' class='a' style='display: block'></div>", "a"));
    }

    [Fact]
    public void Visibility_is_inherited_and_can_be_restored()
    {
        const string css = ".ghost { visibility: hidden } .shown { visibility: visible }";
        Assert.True(Hidden(css, "<div class='ghost'><span id='x'>x</span></div>", "x"));
        Assert.False(Hidden(css, "<div class='ghost'><span id='x' class='shown'>x</span></div>", "x"));
    }

    private static bool HiddenOn(MediaEnvironment environment, string css, string html = "<div id='m' class='m'></div>", string id = "m")
    {
        var scope = DomScope.Parse(html, CssVisibility.Parse([css], environment));
        return DomFacts.IsHidden(scope.GetElementById(id)!, scope);
    }

    [Fact]
    public void Media_queries_default_to_a_desktop_screen()
    {
        Assert.False(Hidden("@media (max-width: 600px) { .m { display: none } }", "<div id='m' class='m'></div>", "m"));
        Assert.True(Hidden("@media screen { .m { display: none } }", "<div id='m' class='m'></div>", "m"));
        Assert.True(Hidden("@media (min-width: 1024px) { .m { display: none } }", "<div id='m' class='m'></div>", "m"));
        Assert.False(Hidden("@media print { .m { display: none } }", "<div id='m' class='m'></div>", "m"));
    }

    [Fact]
    public void Viewport_width_and_height_queries_follow_the_environment()
    {
        var mobile = MediaEnvironment.For(Viewport.Mobile);
        var desktop = MediaEnvironment.For(Viewport.Desktop);
        const string css = ".m { display: block } @media (max-width: 600px) { .m { display: none } }";

        Assert.True(HiddenOn(mobile, css));
        Assert.False(HiddenOn(desktop, css));
        Assert.True(HiddenOn(desktop, "@media (min-width: 40em) and (max-height: 900px) { .m { display: none } }"));
        Assert.False(HiddenOn(mobile, "@media screen and (min-width: 40em) { .m { display: none } }"));
        Assert.True(HiddenOn(mobile, "@media (orientation: portrait) { .m { display: none } }"));
        Assert.False(HiddenOn(desktop, "@media (orientation: portrait) { .m { display: none } }"));
    }

    [Fact]
    public void Range_syntax_lists_and_negation_are_supported()
    {
        var tablet = MediaEnvironment.For(Viewport.Tablet);
        Assert.True(HiddenOn(tablet, "@media (600px <= width < 900px) { .m { display: none } }"));
        Assert.False(HiddenOn(tablet, "@media (width >= 900px) { .m { display: none } }"));
        Assert.True(HiddenOn(tablet, "@media print, (max-width: 800px) { .m { display: none } }"));
        Assert.True(HiddenOn(tablet, "@media not print { .m { display: none } }"));
        Assert.False(HiddenOn(tablet, "@media not all and (min-width: 700px) { .m { display: none } }"));
    }

    [Fact]
    public void Preference_queries_follow_the_theme_and_input()
    {
        var dark = MediaEnvironment.For(Viewport.Desktop, ThemeDefinition.Dark);
        var light = MediaEnvironment.For(Viewport.Desktop, ThemeDefinition.Light);
        var phone = MediaEnvironment.For(Viewport.Mobile);
        const string css = "@media (prefers-color-scheme: dark) { .m { display: none } }";

        Assert.True(HiddenOn(dark, css));
        Assert.False(HiddenOn(light, css));
        Assert.True(HiddenOn(light, "@media (prefers-reduced-motion: reduce) { .m { display: none } }"));
        Assert.True(HiddenOn(phone, "@media (hover: none) and (pointer: coarse) { .m { display: none } }"));
        Assert.False(HiddenOn(light, "@media (hover: none) { .m { display: none } }"));
    }

    [Fact]
    public void Nested_media_and_supports_blocks_compose()
    {
        const string css = "@media screen { @media (max-width: 600px) { .m { display: none } } } @supports (display: grid) { .s { display: none } }";
        Assert.True(HiddenOn(MediaEnvironment.For(Viewport.Mobile), css));
        Assert.False(HiddenOn(MediaEnvironment.Desktop, css));
        Assert.True(HiddenOn(MediaEnvironment.Desktop, css, "<div id='s' class='s'></div>", "s"));
    }

    [Fact]
    public void One_parse_can_be_evaluated_for_several_devices()
    {
        var styles = CssVisibility.Parse(".m { display: none } @media (max-width: 600px) { .m { display: block } }");
        var mobile = styles.For(MediaEnvironment.For(Viewport.Mobile));

        Assert.Equal(2, styles.RuleCount);
        Assert.Equal(1, styles.ActiveRuleCount);
        Assert.Equal(2, mobile.ActiveRuleCount);
        Assert.Same(styles, styles.For(MediaEnvironment.Desktop));

        var scope = DomScope.Parse("<div id='m' class='m'></div>", mobile);
        Assert.False(DomFacts.IsHidden(scope.GetElementById("m")!, scope));
    }

    [Fact]
    public void Blazor_css_isolation_selectors_match_scope_attributes()
    {
        Assert.True(Hidden(".tip[b-abc123xyz] { display: none }", "<span id='t' class='tip' b-abc123xyz></span>", "t"));
    }

    [Fact]
    public void Queries_and_rules_respect_css_hidden_content()
    {
        var scope = DomScope.Parse("<nav class='closed'><a href='/a'>Hidden link</a></nav><a href='/b'>Visible link</a>", CssVisibility.Parse(".closed { display: none }"));

        var links = DomQueries.Resolve(scope, LocatorChain.Start(By.Role("link")));
        Assert.Equal("Visible link", DomFacts.TextOf(Assert.Single(links)));
        Assert.Single(DomFacts.TabOrder(scope));
        Assert.Empty(new MarkupAccessibilityAnalyzer().Analyze(DomScope.Parse("<div class='closed'><button></button></div>", CssVisibility.Parse(".closed { display: none }"))).Violations);
    }
}
