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

    [Fact]
    public void Rules_inside_cascade_layers_apply()
    {
        // Tailwind v4 emits its utilities inside @layer utilities.
        const string css = "@layer theme, base, components, utilities; @layer utilities { .hidden { display: none } }";

        Assert.True(Hidden(css, "<div class='hidden'><span id='x'>x</span></div>", "x"));
        Assert.True(Hidden("@layer { .h { display: none } }", "<p id='x' class='h'>x</p>", "x"));
        Assert.True(Hidden("@media (min-width: 1px) { @layer a { .h { display: none } } }", "<p id='x' class='h'>x</p>", "x"));
    }

    [Fact]
    public void Escaped_quotes_in_selectors_do_not_split_layers()
    {
        // Tailwind v4 arbitrary values escape quotes in class names: .content-[''] becomes .content-\[\'\'\].
        const string css = @"@layer utilities { .content-\[\'\'\]::after { content: '' } .hidden { display: none } } @layer utilities { .shown { display: block } }";

        Assert.True(Hidden(css, "<p id='x' class='hidden'>x</p>", "x"));
        Assert.True(Hidden(css + " .later { display: none }", "<p id='x' class='later'>x</p>", "x"));
    }

    [Theory]
    [InlineData("@layer a { #x { display: none } } .s { display: block }", false)] // unlayered beats any layer, even with lower specificity
    [InlineData("@layer a, b; @layer b { .s { display: none } } @layer a { #x { display: block } }", true)] // later layer wins
    [InlineData("@layer a, b; @layer a { .s { display: none !important } } @layer b { .s { display: block !important } }", true)] // !important: earlier layer wins
    [InlineData("@layer a { .s { display: none !important } } .s { display: block !important }", true)] // !important: layered beats unlayered
    [InlineData("@layer a { @layer inner { .s { display: block } } .s { display: none } }", true)] // a layer's own rules beat its sub-layers
    public void Layer_order_decides_before_specificity(string css, bool hidden) =>
        Assert.Equal(hidden, Hidden(css, "<p id='x' class='s'>x</p>", "x"));

    [Fact]
    public void Container_queries_are_evaluated_against_the_viewport()
    {
        const string css = "@container card (max-width: 600px) { .wide { display: none } } @container (min-width: 1px) { .always { display: none } } @container style(--dark: 1) { .styled { display: none } }";
        var html = "<p id='w' class='wide'>w</p><p id='a' class='always'>a</p><p id='s' class='styled'>s</p>";

        var desktop = DomScope.Parse(html, CssVisibility.Parse([css], MediaEnvironment.For(BlazorTestPlatform.Viewport.Desktop)));
        var mobile = DomScope.Parse(html, CssVisibility.Parse([css], MediaEnvironment.For(BlazorTestPlatform.Viewport.Mobile)));

        Assert.False(DomFacts.IsHidden(desktop.GetElementById("w")!, desktop));
        Assert.True(DomFacts.IsHidden(mobile.GetElementById("w")!, mobile));
        Assert.True(DomFacts.IsHidden(desktop.GetElementById("a")!, desktop));
        Assert.False(DomFacts.IsHidden(desktop.GetElementById("s")!, desktop));
    }

    [Fact]
    public void Each_selector_of_a_list_keeps_its_own_specificity()
    {
        // ".a.b" (0,2,0) beats ".a" (0,1,0); the list's "#zz" never matched and must not lend its specificity.
        Assert.False(Hidden(".a.b { display: block } #zz, .a { display: none }", "<p id='x' class='a b'>x</p>", "x"));
        Assert.True(Hidden(".a.b { display: block } #x, .a { display: none }", "<p id='x' class='a b'>x</p>", "x"));
    }

    [Fact]
    public void Visibility_inherit_takes_the_parent_value()
    {
        Assert.True(Hidden(".p { visibility: hidden } .c { visibility: inherit }", "<div class='p'><span id='x' class='c'>x</span></div>", "x"));
        Assert.False(Hidden(".p { visibility: hidden } .c { visibility: visible }", "<div class='p'><span id='x' class='c'>x</span></div>", "x"));
        Assert.False(Hidden(".p { visibility: hidden } .c { visibility: initial }", "<div class='p'><span id='x' class='c'>x</span></div>", "x"));
    }

    [Fact]
    public void Author_display_overrides_the_hidden_attribute()
    {
        Assert.False(Hidden(".shown { display: flex }", "<div id='x' class='shown' hidden>x</div>", "x"));
        Assert.True(Hidden(".other { display: flex }", "<div id='x' hidden>x</div>", "x"));
        Assert.False(Hidden(string.Empty, "<div id='x' hidden style='display: block'>x</div>", "x"));

        // Bootstrap's reboot: an important author rule beats a normal inline display.
        Assert.True(Hidden("[hidden] { display: none !important }", "<div id='x' hidden style='display: flex'>x</div>", "x"));
    }

    [Fact]
    public void Boolean_media_features_follow_the_environment()
    {
        Assert.True(Hidden("@media (prefers-reduced-motion) { .anim { display: none } }", "<p id='x' class='anim'>x</p>", "x"));
        Assert.False(Hidden("@media (forced-colors) { .fc { display: none } }", "<p id='x' class='fc'>x</p>", "x"));
    }
}
