using BlazorTestPlatform.Accessibility.Aria;
using BlazorTestPlatform.Accessibility.Dom;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Accessibility.Tests;

public sealed class RoleAndNameTests
{
    [Theory]
    [InlineData("<button>Go</button>", "button")]
    [InlineData("<a href='/x'>Home</a>", "link")]
    [InlineData("<a>No href</a>", "generic")]
    [InlineData("<input type='checkbox'>", "checkbox")]
    [InlineData("<input type='range'>", "slider")]
    [InlineData("<input type='search'>", "searchbox")]
    [InlineData("<input list='l'>", "combobox")]
    [InlineData("<input>", "textbox")]
    [InlineData("<select><option>a</option></select>", "combobox")]
    [InlineData("<select multiple><option>a</option></select>", "listbox")]
    [InlineData("<img src='a.png' alt=''>", "presentation")]
    [InlineData("<img src='a.png' alt='Logo'>", "img")]
    [InlineData("<h3>T</h3>", "heading")]
    [InlineData("<nav></nav>", "navigation")]
    [InlineData("<section aria-label='Recent'></section>", "region")]
    [InlineData("<div role='tab'>T</div>", "tab")]
    [InlineData("<div role='bogus tab'>T</div>", "tab")]
    [InlineData("<div role='none' tabindex='0'>T</div>", "generic")]
    public void Roles_are_computed(string html, string role)
    {
        var scope = DomScope.Parse(html);
        Assert.Equal(role, AriaRoles.GetRole(scope.Elements().First()));
    }

    [Theory]
    [InlineData("<span id='a'>First</span><span id='b'>Second</span><button aria-labelledby='a b' aria-label='ignored'>x</button>", "button", "First Second")]
    [InlineData("<button aria-label='Close dialog'>×</button>", "button", "Close dialog")]
    [InlineData("<label for='e'>Email</label><input id='e'>", "input", "Email")]
    [InlineData("<label>Name <input></label>", "input", "Name")]
    [InlineData("<input type='submit'>", "input", "Submit")]
    [InlineData("<input type='button' value='Run'>", "input", "Run")]
    [InlineData("<img src='x' alt='Company logo'>", "img", "Company logo")]
    [InlineData("<button>Save <span aria-hidden='true'>💾</span><span hidden>secret</span></button>", "button", "Save")]
    [InlineData("<a href='#'><img src='x' alt='Home'></a>", "a", "Home")]
    [InlineData("<input placeholder='Search…'>", "input", "Search…")]
    [InlineData("<button title='More options'></button>", "button", "More options")]
    [InlineData("<fieldset><legend>Shipping</legend></fieldset>", "fieldset", "Shipping")]
    [InlineData("<div>Not named from content</div>", "div", "")]
    public void Accessible_names_are_computed(string html, string selector, string expected)
    {
        var scope = DomScope.Parse(html);
        Assert.Equal(expected, AccessibleName.Compute(scope.QuerySelectorAll(selector).First(), scope));
    }

    [Fact]
    public void Visibility_and_disabled_state_are_inferred()
    {
        var scope = DomScope.Parse("<div style='display: none'><button id='a'>A</button></div><fieldset disabled><legend><button id='b'>B</button></legend><button id='c'>C</button></fieldset><details><summary id='d'>S</summary><p id='e'>Body</p></details>");

        Assert.True(DomFacts.IsHidden(scope.GetElementById("a")!));
        Assert.False(DomFacts.IsDisabled(scope.GetElementById("b")!));
        Assert.True(DomFacts.IsDisabled(scope.GetElementById("c")!));
        Assert.False(DomFacts.IsHidden(scope.GetElementById("d")!));
        Assert.True(DomFacts.IsHidden(scope.GetElementById("e")!));
    }

    [Fact]
    public void Tab_order_puts_positive_tabindex_first_and_collapses_radio_groups()
    {
        var scope = DomScope.Parse("""
            <button id="one">1</button>
            <a id="nolink">no href</a>
            <input id="r1" type="radio" name="g"><input id="r2" type="radio" name="g" checked>
            <div id="minus" tabindex="-1">x</div>
            <button id="first" tabindex="2">first</button>
            <button id="disabled" disabled>d</button>
            <span id="zero" tabindex="0">z</span>
            """);

        Assert.Equal(["first", "one", "r2", "zero"], DomFacts.TabOrder(scope).Select(e => e.Id));
    }
}

public sealed class QueryTests
{
    private static readonly DomScope Scope = DomScope.Parse("""
        <form>
          <label for="email">Email address</label><input id="email" placeholder="you@example.com" data-testid="email">
          <label><input type="checkbox" checked> Remember me</label>
          <button type="submit">Sign in</button>
          <button type="button" aria-hidden="true">Hidden helper</button>
        </form>
        <ul><li>Alpha <b>one</b></li><li>Beta</li><li>Gamma</li></ul>
        <img src="x.png" alt="Company logo" title="Logo">
        """);

    private static IReadOnlyList<string> Resolve(By by, params LocatorStep[] extra)
    {
        var chain = LocatorChain.Start(by);
        foreach (var step in extra)
        {
            chain = chain.Append(step);
        }

        return DomQueries.Resolve(Scope, chain).Select(e => e.LocalName + (e.Id is { Length: > 0 } id ? "#" + id : string.Empty)).ToArray();
    }

    [Fact]
    public void Role_queries_match_names_and_skip_hidden_elements()
    {
        Assert.Equal(["button"], Resolve(By.Role("button")));
        Assert.Equal(["button"], Resolve(By.Role("button", "sign")));
        Assert.Empty(Resolve(By.Role("button", "sign", exact: true)));
        Assert.Equal(["button"], Resolve(By.Role("button", "Sign in", exact: true)));
        Assert.Equal(["input"], Resolve(By.Role("checkbox", "Remember me")));
    }

    [Fact]
    public void User_facing_queries_resolve()
    {
        Assert.Equal(["input#email"], Resolve(By.Label("Email")));
        Assert.Equal(["input#email"], Resolve(By.Placeholder("you@")));
        Assert.Equal(["input#email"], Resolve(By.TestId("email")));
        Assert.Equal(["img"], Resolve(By.AltText("logo")));
        Assert.Equal(["img"], Resolve(By.Title("Logo", exact: true)));
        Assert.Equal(["li"], Resolve(By.Text("Alpha one")));
        Assert.Equal(["b"], Resolve(By.Text("one", exact: true)));
    }

    [Fact]
    public void Chains_scope_index_and_filter()
    {
        Assert.Equal(3, Resolve(By.Css("ul"), new LocatorStep(LocatorStepKind.Find, By.Role("listitem"))).Count);
        Assert.Equal(["li"], Resolve(By.Role("listitem"), new LocatorStep(LocatorStepKind.HasText, Text: "beta")));
        Assert.Equal(2, Resolve(By.Role("listitem"), new LocatorStep(LocatorStepKind.HasNotText, Text: "beta")).Count);
        var last = DomQueries.Resolve(Scope, LocatorChain.Start(By.Role("listitem")).Append(new LocatorStep(LocatorStepKind.Nth, Index: -1)));
        Assert.Equal("Gamma", DomFacts.TextOf(Assert.Single(last)));
    }

    [Fact]
    public void Element_snapshots_describe_state()
    {
        var checkbox = DomQueries.Resolve(Scope, LocatorChain.Start(By.Role("checkbox")))[0];
        var info = DomQueries.Describe(checkbox, Scope, isFocused: true);

        Assert.Equal("checkbox", info.Role);
        Assert.Equal("Remember me", info.AccessibleName);
        Assert.True(info.IsChecked);
        Assert.True(info.IsFocused);
        Assert.False(info.IsEditable);
    }
}

public sealed class MarkupRuleTests
{
    private static IReadOnlyList<string> Violations(string html, AccessibilityPolicy? policy = null) =>
        new MarkupAccessibilityAnalyzer().Analyze(html, policy).Violations.Select(v => v.RuleId).ToArray();

    [Theory]
    [InlineData("<img src='a.png'>", "image-alt")]
    [InlineData("<button></button>", "button-name")]
    [InlineData("<button><svg></svg></button>", "button-name")]
    [InlineData("<a href='/x'></a>", "link-name")]
    [InlineData("<div role='button' tabindex='0'></div>", "aria-command-name")]
    [InlineData("<input type='text'>", "label")]
    [InlineData("<select><option>a</option></select>", "select-name")]
    [InlineData("<input type='image' src='go.png'>", "input-image-alt")]
    [InlineData("<div role='slider' aria-valuenow='3'></div>", "aria-input-field-name")]
    [InlineData("<div role='switch' aria-checked='true'></div>", "aria-toggle-field-name")]
    [InlineData("<div role='dialog'>Hi</div>", "aria-dialog-name")]
    [InlineData("<span role='img'></span>", "role-img-alt")]
    [InlineData("<svg role='img'></svg>", "svg-img-alt")]
    [InlineData("<iframe src='x'></iframe>", "frame-title")]
    [InlineData("<div role='buton'>x</div>", "aria-roles")]
    [InlineData("<div aria-lable='x'>x</div>", "aria-valid-attr")]
    [InlineData("<div aria-hidden='yes'>x</div>", "aria-valid-attr-value")]
    [InlineData("<div aria-labelledby='missing'>x</div>", "aria-valid-attr-value")]
    [InlineData("<div role='checkbox' aria-label='Agree'></div>", "aria-required-attr")]
    [InlineData("<div role='tablist'><div>x</div></div>", "aria-required-children")]
    [InlineData("<div role='tab'>x</div>", "aria-required-parent")]
    [InlineData("<div aria-hidden='true'><button>x</button></div>", "aria-hidden-focus")]
    [InlineData("<button>Open <a href='/x'>link</a></button>", "nested-interactive")]
    [InlineData("<button tabindex='3'>x</button>", "tabindex")]
    [InlineData("<span id='a'>A</span><span id='a'>B</span><input aria-labelledby='a'>", "duplicate-id-aria")]
    [InlineData("<div id='x'></div><div id='x'></div>", "duplicate-id")]
    [InlineData("<ul><div>x</div></ul>", "list")]
    [InlineData("<li>orphan</li>", "listitem")]
    [InlineData("<dl><p>x</p></dl>", "definition-list")]
    [InlineData("<dt>orphan</dt>", "dlitem")]
    [InlineData("<h2></h2>", "empty-heading")]
    [InlineData("<h1>A</h1><h3>C</h3>", "heading-order")]
    [InlineData("<label for='f'>A</label><label for='f'>B</label><input id='f'>", "form-field-multiple-labels")]
    [InlineData("<details><summary></summary>x</details>", "summary-name")]
    [InlineData("<div role='presentation' aria-label='x'>x</div>", "presentation-role-conflict")]
    public void Rules_detect_violations(string html, string rule) => Assert.Contains(rule, Violations(html));

    [Fact]
    public void Accessible_markup_passes_every_rule()
    {
        const string html = """
            <header><h1>Account</h1></header>
            <nav aria-label="Main"><ul><li><a href="/">Home</a></li><li><a href="/me" aria-current="page">Profile</a></li></ul></nav>
            <main>
              <h2>Settings</h2>
              <img src="avatar.png" alt="Your avatar">
              <img src="divider.png" alt="">
              <form>
                <label for="name">Name</label><input id="name" aria-describedby="name-hint" required>
                <p id="name-hint">Shown on your profile.</p>
                <label><input type="checkbox"> Subscribe</label>
                <select aria-label="Language"><option>English</option></select>
                <button type="submit">Save</button>
                <button type="button" aria-label="Close"><svg aria-hidden="true"></svg></button>
              </form>
              <div role="tablist" aria-label="Sections"><button role="tab" aria-selected="true" aria-controls="p1">One</button></div>
              <div role="tabpanel" id="p1" aria-label="One">Panel</div>
              <dl><dt>Plan</dt><dd>Pro</dd></dl>
            </main>
            """;

        Assert.Empty(Violations(html));
    }

    [Fact]
    public void Policy_tags_and_disabled_rules_select_rules()
    {
        var bestPracticesOff = new AccessibilityPolicy { IncludeBestPractices = false };
        Assert.DoesNotContain("heading-order", Violations("<h1>A</h1><h3>C</h3>", bestPracticesOff));

        var disabled = new AccessibilityPolicy();
        disabled.DisabledRules.Add("image-alt");
        var report = new MarkupAccessibilityAnalyzer().Analyze("<img src='a.png'>", disabled);
        Assert.Empty(report.Violations);
        Assert.Contains("image-alt", report.DisabledRules);
    }

    [Fact]
    public void Violations_carry_selectors_snippets_and_help()
    {
        var violation = new MarkupAccessibilityAnalyzer().Analyze("<div><button blazor:onclick=\"3\"></button></div>").Violations.Single(v => v.RuleId == "button-name");

        Assert.Equal(AccessibilityImpact.Critical, violation.Impact);
        Assert.StartsWith("https://dequeuniversity.com/rules/axe/", violation.HelpUrl, StringComparison.Ordinal);
        var node = Assert.Single(violation.Nodes);
        Assert.Equal("body > div:nth-child(1) > button:nth-child(1)", node.Target);
        Assert.DoesNotContain("blazor:", node.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Include_and_exclude_scope_the_scan()
    {
        var options = new StoryAccessibilityOptions { Include = "#scope" };
        options.Exclude.Add(".third-party");
        var report = new MarkupAccessibilityAnalyzer().Analyze("<img src='a'><div id='scope'><button></button><div class='third-party'><img src='b'></div></div>", null, options);

        Assert.Equal(["button-name"], report.Violations.Select(v => v.RuleId));
    }
}

public sealed class FingerprintTests
{
    [Fact]
    public void Copy_changes_keep_the_fingerprint()
    {
        Assert.Equal(
            AccessibilityFingerprint.Compute("<button class='a'>Save</button><p>Hello</p>"),
            AccessibilityFingerprint.Compute("<button class='b'>Store</button><p>Hi there</p>"));
    }

    [Theory]
    [InlineData("<button>Save</button><button>Cancel</button>")]
    [InlineData("<div role='button' tabindex='0'>Save</div>")]
    [InlineData("<button aria-describedby='h'>Save</button><p id='h'>x</p>")]
    [InlineData("<button></button>")]
    public void Structural_changes_change_the_fingerprint(string changed) =>
        Assert.NotEqual(AccessibilityFingerprint.Compute("<button>Save</button>"), AccessibilityFingerprint.Compute(changed));
}
