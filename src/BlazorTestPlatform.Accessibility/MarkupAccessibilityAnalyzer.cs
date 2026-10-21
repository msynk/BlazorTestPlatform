using System.Globalization;
using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Aria;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility;

/// <summary>A static (browser-free) accessibility rule. Rule ids match axe-core so results line up across engines.</summary>
public sealed record MarkupRule
{
    /// <summary>axe-compatible rule id.</summary>
    public required string Id { get; init; }

    /// <summary>Impact.</summary>
    public required AccessibilityImpact Impact { get; init; }

    /// <summary>What the rule checks.</summary>
    public required string Description { get; init; }

    /// <summary>Short help.</summary>
    public required string Help { get; init; }

    /// <summary>Tags (wcag2a, wcag412, best-practice...).</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    internal Func<RuleContext, IEnumerable<(IElement Element, string Summary)>> Evaluate { get; init; } = _ => [];
}

/// <summary>Per-analysis caches handed to rules.</summary>
internal sealed class RuleContext
{
    private readonly Dictionary<IElement, string?> _roles = [];
    private readonly Dictionary<IElement, string> _names = [];

    public RuleContext(DomScope scope, IReadOnlyList<IElement> elements)
    {
        Scope = scope;
        Elements = elements;
    }

    public DomScope Scope { get; }

    public IReadOnlyList<IElement> Elements { get; }

    public string? Role(IElement element)
    {
        if (!_roles.TryGetValue(element, out var role))
        {
            _roles[element] = role = AriaRoles.GetRole(element);
        }

        return role;
    }

    public string Name(IElement element)
    {
        if (!_names.TryGetValue(element, out var name))
        {
            _names[element] = name = AccessibleName.Compute(element, Scope);
        }

        return name;
    }

    public bool Hidden(IElement element) => DomFacts.IsExcludedFromAccessibilityTree(element, Scope);

    public IEnumerable<IElement> Visible => Elements.Where(e => !Hidden(e));
}

/// <summary>
/// Fast, browser-free accessibility checks over rendered markup (bUnit output or any HTML). Covers the structural
/// WCAG rules that do not need layout or CSS — names, roles, ARIA validity, keyboard reachability, list/heading structure.
/// Contrast, focus visibility and other layout-dependent rules run in the browser through axe-core.
/// </summary>
public sealed class MarkupAccessibilityAnalyzer
{
    /// <summary>Engine name reported in results.</summary>
    public const string EngineName = "btp-markup";

    private const string HelpBase = "https://dequeuniversity.com/rules/axe/4.10/";

    /// <summary>All rules.</summary>
    public static IReadOnlyList<MarkupRule> Rules { get; } = BuildRules();

    /// <summary>Analyzes an HTML fragment.</summary>
    public AccessibilityReport Analyze(string markup, AccessibilityPolicy? policy = null, StoryAccessibilityOptions? options = null) =>
        Analyze(DomScope.Parse(markup), policy, options);

    /// <summary>Analyzes a DOM scope.</summary>
    public AccessibilityReport Analyze(DomScope scope, AccessibilityPolicy? policy = null, StoryAccessibilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        policy ??= new AccessibilityPolicy();

        IEnumerable<IElement> elements = scope.Elements();
        if (options?.Include is { } include)
        {
            var roots = scope.QuerySelectorAll(include).ToArray();
            elements = elements.Where(e => roots.Any(r => ReferenceEquals(r, e) || r.Contains(e)));
        }

        if (options?.Exclude is { Count: > 0 } excludes)
        {
            var excluded = excludes.SelectMany(scope.QuerySelectorAll).ToArray();
            elements = elements.Where(e => !excluded.Any(x => ReferenceEquals(x, e) || x.Contains(e)));
        }

        var context = new RuleContext(scope, elements.ToArray());
        var violations = new List<AccessibilityViolation>();
        var disabled = new List<string>();
        var passed = 0;

        foreach (var rule in Rules)
        {
            if (policy.DisabledRules.Contains(rule.Id) || options?.DisabledRules.Contains(rule.Id) == true)
            {
                disabled.Add(rule.Id);
                continue;
            }

            var applies = rule.Tags.Any(policy.Tags.Contains) || (policy.IncludeBestPractices && rule.Tags.Contains("best-practice"));
            if (!applies)
            {
                continue;
            }

            var nodes = rule.Evaluate(context)
                .Select(n => new AccessibilityNode(DomFacts.CssPath(n.Element, scope), DomFacts.Snippet(n.Element, 200), n.Summary))
                .ToArray();

            if (nodes.Length == 0)
            {
                passed++;
                continue;
            }

            violations.Add(new AccessibilityViolation
            {
                RuleId = rule.Id,
                Impact = rule.Impact,
                Description = rule.Description,
                Help = rule.Help,
                HelpUrl = HelpBase + rule.Id,
                Tags = rule.Tags,
                Nodes = nodes,
                Engine = EngineName,
            });
        }

        return new AccessibilityReport { Engine = EngineName, Violations = violations, PassedRules = passed, DisabledRules = disabled };
    }

    private static IReadOnlyList<MarkupRule> BuildRules()
    {
        static MarkupRule Rule(string id, AccessibilityImpact impact, string description, string help, string[] tags, Func<RuleContext, IEnumerable<(IElement, string)>> evaluate) =>
            new() { Id = id, Impact = impact, Description = description, Help = help, Tags = tags, Evaluate = evaluate };

        static IEnumerable<(IElement, string)> Unnamed(RuleContext c, Func<IElement, bool> applies, string summary) =>
            c.Visible.Where(applies).Where(e => string.IsNullOrWhiteSpace(c.Name(e))).Select(e => (e, summary));

        static bool HasExplicitRole(IElement e, params string[] roles) => AriaRoles.GetExplicitRole(e) is { } r && roles.Contains(r);

        return
        [
            Rule("image-alt", AccessibilityImpact.Critical, "Ensures <img> elements have alternate text or a role of none or presentation", "Images must have alternate text",
                ["cat.text-alternatives", "wcag2a", "wcag111", "section508"],
                c => c.Visible.Where(e => e.LocalName == "img" && !e.HasAttribute("alt") && c.Role(e) is not ("none" or "presentation") && string.IsNullOrWhiteSpace(c.Name(e)))
                    .Select(e => (e, "Add an alt attribute (alt=\"\" for decorative images)."))),

            Rule("input-image-alt", AccessibilityImpact.Critical, "Ensures <input type=\"image\"> elements have alternate text", "Image buttons must have alternate text",
                ["cat.text-alternatives", "wcag2a", "wcag111", "wcag412"],
                c => Unnamed(c, e => e.LocalName == "input" && string.Equals(e.GetAttribute("type"), "image", StringComparison.OrdinalIgnoreCase), "Add an alt attribute describing the action.")),

            Rule("button-name", AccessibilityImpact.Critical, "Ensures buttons have discernible text", "Buttons must have discernible text",
                ["cat.name-role-value", "wcag2a", "wcag412", "section508"],
                c => Unnamed(c, e => e.LocalName == "button" && c.Role(e) == "button", "Add visible text, aria-label or aria-labelledby.")),

            Rule("input-button-name", AccessibilityImpact.Critical, "Ensures input buttons have discernible text", "Input buttons must have discernible text",
                ["cat.name-role-value", "wcag2a", "wcag412", "section508"],
                c => Unnamed(c, e => e.LocalName == "input" && string.Equals(e.GetAttribute("type"), "button", StringComparison.OrdinalIgnoreCase), "Add a value or aria-label.")),

            Rule("link-name", AccessibilityImpact.Serious, "Ensures links have discernible text", "Links must have discernible text",
                ["cat.name-role-value", "wcag2a", "wcag244", "wcag412", "section508"],
                c => Unnamed(c, e => e.LocalName == "a" && e.HasAttribute("href") && c.Role(e) == "link", "Add link text, aria-label, or alt text on a contained image.")),

            Rule("aria-command-name", AccessibilityImpact.Serious, "Ensures every ARIA button, link and menuitem has an accessible name", "ARIA commands must have an accessible name",
                ["cat.aria", "wcag2a", "wcag412"],
                c => Unnamed(c, e => e.LocalName is not ("button" or "a" or "input") && HasExplicitRole(e, "button", "link", "menuitem"), "Add text content, aria-label or aria-labelledby.")),

            Rule("label", AccessibilityImpact.Critical, "Ensures every form element has a label", "Form elements must have labels",
                ["cat.forms", "wcag2a", "wcag412", "section508"],
                c => Unnamed(c, e => (e.LocalName == "textarea" || (e.LocalName == "input" && (e.GetAttribute("type") ?? "text").ToLowerInvariant() is not ("hidden" or "button" or "submit" or "reset" or "image")))
                    && c.Role(e) is not ("none" or "presentation"), "Associate a <label>, or add aria-label/aria-labelledby.")),

            Rule("select-name", AccessibilityImpact.Critical, "Ensures select element has an accessible name", "Select element must have an accessible name",
                ["cat.forms", "wcag2a", "wcag412", "section508"],
                c => Unnamed(c, e => e.LocalName == "select", "Associate a <label>, or add aria-label/aria-labelledby.")),

            Rule("aria-input-field-name", AccessibilityImpact.Serious, "Ensures every ARIA input field has an accessible name", "ARIA input fields must have an accessible name",
                ["cat.aria", "wcag2a", "wcag412"],
                c => Unnamed(c, e => e.LocalName is not ("input" or "select" or "textarea") && HasExplicitRole(e, "combobox", "listbox", "searchbox", "slider", "spinbutton", "textbox"), "Add aria-label or aria-labelledby.")),

            Rule("aria-toggle-field-name", AccessibilityImpact.Serious, "Ensures every ARIA toggle field has an accessible name", "ARIA toggle fields must have an accessible name",
                ["cat.aria", "wcag2a", "wcag412"],
                c => Unnamed(c, e => e.LocalName != "input" && HasExplicitRole(e, "checkbox", "menuitemcheckbox", "menuitemradio", "radio", "switch", "option"), "Add text content, aria-label or aria-labelledby.")),

            Rule("aria-progressbar-name", AccessibilityImpact.Serious, "Ensures every ARIA progressbar node has an accessible name", "ARIA progressbar nodes must have an accessible name",
                ["cat.aria", "wcag2a", "wcag111"],
                c => Unnamed(c, e => HasExplicitRole(e, "progressbar"), "Add aria-label or aria-labelledby.")),

            Rule("aria-meter-name", AccessibilityImpact.Serious, "Ensures every ARIA meter node has an accessible name", "ARIA meter nodes must have an accessible name",
                ["cat.aria", "wcag2a", "wcag111"],
                c => Unnamed(c, e => HasExplicitRole(e, "meter"), "Add aria-label or aria-labelledby.")),

            Rule("aria-tooltip-name", AccessibilityImpact.Serious, "Ensures every ARIA tooltip node has an accessible name", "ARIA tooltip nodes must have an accessible name",
                ["cat.aria", "wcag2a", "wcag412"],
                c => Unnamed(c, e => HasExplicitRole(e, "tooltip"), "Add text content or aria-label.")),

            Rule("aria-dialog-name", AccessibilityImpact.Serious, "Ensures every ARIA dialog and alertdialog node has an accessible name", "ARIA dialog and alertdialog nodes should have an accessible name",
                ["cat.aria", "best-practice"],
                c => Unnamed(c, e => c.Role(e) is "dialog" or "alertdialog", "Reference the dialog title with aria-labelledby.")),

            Rule("role-img-alt", AccessibilityImpact.Serious, "Ensures [role=\"img\"] elements have alternate text", "[role=\"img\"] elements must have an alternative text",
                ["cat.text-alternatives", "wcag2a", "wcag111", "section508"],
                c => Unnamed(c, e => e.LocalName != "svg" && HasExplicitRole(e, "img", "image"), "Add aria-label or aria-labelledby.")),

            Rule("svg-img-alt", AccessibilityImpact.Serious, "Ensures <svg> elements with an img role have an alternative text", "<svg> elements with an img role must have an alternative text",
                ["cat.text-alternatives", "wcag2a", "wcag111", "section508"],
                c => Unnamed(c, e => e.LocalName == "svg" && HasExplicitRole(e, "img", "graphics-document", "graphics-symbol"), "Add a <title> child, aria-label or aria-labelledby.")),

            Rule("frame-title", AccessibilityImpact.Serious, "Ensures <iframe> and <frame> elements have an accessible name", "Frames must have an accessible name",
                ["cat.text-alternatives", "wcag2a", "wcag412", "section508"],
                c => Unnamed(c, e => e.LocalName is "iframe" or "frame", "Add a title attribute.")),

            Rule("summary-name", AccessibilityImpact.Serious, "Ensures summary elements have discernible text", "Summary elements must have discernible text",
                ["cat.name-role-value", "wcag2a", "wcag412", "section508"],
                c => c.Visible.Where(e => e.LocalName == "summary" && e.ParentElement?.LocalName == "details" && string.IsNullOrWhiteSpace(DomFacts.TextOf(e)) && string.IsNullOrWhiteSpace(e.GetAttribute("aria-label")))
                    .Select(e => (e, "Add text content to the summary."))),

            Rule("aria-roles", AccessibilityImpact.Critical, "Ensures all elements with a role attribute use a valid value", "ARIA roles used must conform to valid values",
                ["cat.aria", "wcag2a", "wcag412"],
                c => c.Elements.Where(e => e.GetAttribute("role") is { } role && !string.IsNullOrWhiteSpace(role) && AriaRoles.GetExplicitRole(e) is null)
                    .Select(e => (e, $"Role '{e.GetAttribute("role")}' is not a valid (non-abstract) ARIA role."))),

            Rule("aria-valid-attr", AccessibilityImpact.Critical, "Ensures attributes that begin with aria- are valid ARIA attributes", "ARIA attributes must conform to valid names",
                ["cat.aria", "wcag2a", "wcag412"],
                c => c.Elements.SelectMany(e => e.Attributes
                    .Where(a => a.Name.StartsWith("aria-", StringComparison.Ordinal) && !AriaAttributes.All.Contains(a.Name))
                    .Select(a => (e, $"'{a.Name}' is not a valid ARIA attribute.")))),

            Rule("aria-valid-attr-value", AccessibilityImpact.Critical, "Ensures all ARIA attributes have valid values", "ARIA attributes must conform to valid values",
                ["cat.aria", "wcag2a", "wcag412"],
                c => c.Elements.SelectMany(e => e.Attributes
                    .Where(a => AriaAttributes.All.Contains(a.Name))
                    .Select(a => (Attribute: a, Problem: AriaAttributes.Validate(a.Name, a.Value, c.Scope, e)))
                    .Where(x => x.Problem is not null)
                    .Select(x => (e, x.Problem!)))),

            Rule("aria-required-attr", AccessibilityImpact.Critical, "Ensures elements with ARIA roles have all required ARIA attributes", "Required ARIA attributes must be provided",
                ["cat.aria", "wcag2a", "wcag412"],
                c => c.Visible.SelectMany(e => AriaAttributes.MissingRequired(e).Select(missing => (e, $"Role '{AriaRoles.GetExplicitRole(e)}' requires {missing}.")))),

            Rule("aria-required-children", AccessibilityImpact.Critical, "Ensures elements with an ARIA role that require child roles contain them", "Certain ARIA roles must contain particular children",
                ["cat.aria", "wcag2a", "wcag131"],
                c => c.Visible.Where(e => AriaRoles.GetExplicitRole(e) is { } role && AriaStructure.RequiredChildren.ContainsKey(role) && e.GetAttribute("aria-busy") != "true")
                    .Select(e => (Element: e, Problem: AriaStructure.CheckChildren(e, c.Role)))
                    .Where(x => x.Problem is not null)
                    .Select(x => (x.Element, x.Problem!))),

            Rule("aria-required-parent", AccessibilityImpact.Critical, "Ensures elements with an ARIA role that require parent roles are contained by them", "Certain ARIA roles must be contained by particular parents",
                ["cat.aria", "wcag2a", "wcag131"],
                c => c.Visible.Where(e => AriaRoles.GetExplicitRole(e) is { } role && AriaStructure.RequiredParents.ContainsKey(role))
                    .Select(e => (Element: e, Problem: AriaStructure.CheckParent(e, c.Role)))
                    .Where(x => x.Problem is not null)
                    .Select(x => (x.Element, x.Problem!))),

            Rule("aria-hidden-focus", AccessibilityImpact.Serious, "Ensures aria-hidden elements are not focusable nor contain focusable elements", "ARIA hidden element must not be focusable or contain focusable elements",
                ["cat.name-role-value", "wcag2a", "wcag412"],
                c => c.Elements.Where(e => string.Equals(e.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) && !DomFacts.IsHidden(e, c.Scope))
                    .Where(e => DomFacts.IsTabbable(e, c.Scope) || e.QuerySelectorAll("*").Any(d => DomFacts.IsTabbable(d, c.Scope)))
                    .Select(e => (e, "Remove focusable descendants from the aria-hidden subtree or add tabindex=\"-1\"/inert."))),

            Rule("nested-interactive", AccessibilityImpact.Serious, "Ensures interactive controls are not nested", "Interactive controls must not be nested",
                ["cat.keyboard", "wcag2a", "wcag412"],
                c => c.Visible.Where(e => c.Role(e) is "button" or "link" or "checkbox" or "radio" or "switch" or "tab" or "menuitem" or "option" or "treeitem")
                    .Where(e => e.QuerySelectorAll("*").Any(d => DomFacts.IsFocusable(d, c.Scope) && !DomFacts.IsHidden(d, c.Scope)))
                    .Select(e => (e, "Interactive element contains focusable descendants that screen readers will not announce."))),

            Rule("tabindex", AccessibilityImpact.Serious, "Ensures tabindex attribute values are not greater than 0", "Elements should not have tabindex greater than zero",
                ["cat.keyboard", "best-practice"],
                c => c.Elements.Where(e => DomFacts.TryGetTabIndex(e, out var t) && t > 0)
                    .Select(e => (e, "Use tabindex=\"0\" and DOM order instead of positive tabindex."))),

            Rule("presentation-role-conflict", AccessibilityImpact.Minor, "Elements marked as presentational should not have global ARIA or tabindex", "Ensure elements marked as presentational are consistently ignored",
                ["cat.aria", "best-practice"],
                c => c.Elements.Where(e => AriaRoles.GetExplicitRole(e) is "none" or "presentation" && (DomFacts.IsFocusable(e, c.Scope) || AriaRoles.HasGlobalAriaAttribute(e)))
                    .Select(e => (e, "Presentational role is ignored because the element is focusable or has global ARIA attributes."))),

            Rule("duplicate-id-aria", AccessibilityImpact.Critical, "Ensures every id attribute value used in ARIA and in labels is unique", "IDs used in ARIA and labels must be unique",
                ["cat.parsing", "wcag2a", "wcag412"],
                c => ReferencedIds(c).Where(id => c.Scope.ElementsWithId(id).Count > 1)
                    .Select(id => (c.Scope.ElementsWithId(id)[1], $"id \"{id}\" is referenced by ARIA/label and used {c.Scope.ElementsWithId(id).Count} times."))),

            Rule("duplicate-id", AccessibilityImpact.Minor, "Ensures every id attribute value is unique", "id attribute value must be unique",
                ["cat.parsing", "best-practice"],
                c =>
                {
                    var referenced = ReferencedIds(c).ToHashSet(StringComparer.Ordinal);
                    return c.Elements.Select(e => e.GetAttribute("id")).Where(id => !string.IsNullOrEmpty(id) && !referenced.Contains(id!)).Distinct()
                        .Where(id => c.Scope.ElementsWithId(id!).Count > 1)
                        .Select(id => (c.Scope.ElementsWithId(id!)[1], $"id \"{id}\" is used {c.Scope.ElementsWithId(id!).Count} times. Blazor components rendered multiple times often duplicate ids."));
                }),

            Rule("list", AccessibilityImpact.Serious, "Ensures that lists are structured correctly", "<ul> and <ol> must only directly contain <li>, <script> or <template> elements",
                ["cat.structure", "wcag2a", "wcag131"],
                c => c.Visible.Where(e => e.LocalName is "ul" or "ol" && AriaRoles.GetExplicitRole(e) is null or "list")
                    .Where(e => e.Children.Any(child => child.LocalName is not ("li" or "script" or "template") && AriaRoles.GetExplicitRole(child) is not "listitem"))
                    .Select(e => (e, "Wrap direct children in <li> elements."))),

            Rule("listitem", AccessibilityImpact.Serious, "Ensures <li> elements are used semantically", "<li> elements must be contained in a <ul> or <ol>",
                ["cat.structure", "wcag2a", "wcag131"],
                c => c.Visible.Where(e => e.LocalName == "li" && AriaRoles.GetExplicitRole(e) is null)
                    .Where(e => e.ParentElement is not { } parent || !(parent.LocalName is "ul" or "ol" or "menu" || AriaRoles.GetExplicitRole(parent) == "list"))
                    .Select(e => (e, "Place <li> inside <ul>, <ol> or <menu>."))),

            Rule("definition-list", AccessibilityImpact.Serious, "Ensures <dl> elements are structured correctly", "<dl> elements must only directly contain properly-ordered <dt> and <dd> groups, <div>, <script>, or <template> elements",
                ["cat.structure", "wcag2a", "wcag131"],
                c => c.Visible.Where(e => e.LocalName == "dl" && e.Children.Any(child => child.LocalName is not ("dt" or "dd" or "div" or "script" or "template")))
                    .Select(e => (e, "Only use <dt>, <dd> and <div> directly inside <dl>."))),

            Rule("dlitem", AccessibilityImpact.Serious, "Ensures <dt> and <dd> elements are contained by a <dl>", "<dt> and <dd> elements must be contained by a <dl>",
                ["cat.structure", "wcag2a", "wcag131"],
                c => c.Visible.Where(e => e.LocalName is "dt" or "dd")
                    .Where(e => e.ParentElement is not { } parent || !(parent.LocalName == "dl" || (parent.LocalName == "div" && parent.ParentElement?.LocalName == "dl")))
                    .Select(e => (e, "Place <dt>/<dd> inside a <dl>."))),

            Rule("empty-heading", AccessibilityImpact.Minor, "Ensures headings have discernible text", "Headings should not be empty",
                ["cat.name-role-value", "best-practice"],
                c => Unnamed(c, e => c.Role(e) == "heading", "Add text to the heading or remove it.")),

            Rule("heading-order", AccessibilityImpact.Moderate, "Ensures the order of headings is semantically correct", "Heading levels should only increase by one",
                ["cat.semantics", "best-practice"],
                c =>
                {
                    var problems = new List<(IElement, string)>();
                    int? previous = null;
                    foreach (var heading in c.Visible.Where(e => c.Role(e) == "heading"))
                    {
                        var level = HeadingLevel(heading);
                        if (previous is { } p && level > p + 1)
                        {
                            problems.Add((heading, $"Heading level jumps from h{p} to h{level}."));
                        }

                        previous = level;
                    }

                    return problems;
                }),

            Rule("form-field-multiple-labels", AccessibilityImpact.Moderate, "Ensures form field does not have multiple label elements", "Form field must not have multiple label elements",
                ["cat.forms", "wcag2a", "wcag332"],
                c => c.Visible.Where(e => e.LocalName is "input" or "select" or "textarea" && AccessibleName.GetLabels(e, c.Scope).Count > 1)
                    .Select(e => (e, "Use a single <label>; add extra information with aria-describedby."))),
        ];
    }

    private static IEnumerable<string> ReferencedIds(RuleContext context)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in context.Elements)
        {
            foreach (var attribute in new[] { "aria-labelledby", "aria-describedby", "aria-controls", "aria-owns", "aria-activedescendant", "aria-errormessage", "aria-details", "aria-flowto" })
            {
                foreach (var id in (element.GetAttribute(attribute) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    ids.Add(id);
                }
            }

            if (element.LocalName == "label" && element.GetAttribute("for") is { Length: > 0 } forId)
            {
                ids.Add(forId);
            }
        }

        return ids;
    }

    internal static int HeadingLevel(IElement heading)
    {
        if (heading.LocalName.Length == 2 && heading.LocalName[0] == 'h' && char.IsAsciiDigit(heading.LocalName[1]))
        {
            return heading.LocalName[1] - '0';
        }

        return int.TryParse(heading.GetAttribute("aria-level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : 2;
    }
}
