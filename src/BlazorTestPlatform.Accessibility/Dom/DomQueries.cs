using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Aria;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Accessibility.Dom;

/// <summary>
/// Resolves <see cref="By"/> queries and <see cref="LocatorChain"/>s against a DOM without a browser,
/// with the same semantics as Playwright's <c>getBy*</c> locators.
/// </summary>
public static class DomQueries
{
    /// <summary>Resolves a locator chain to elements in document order.</summary>
    public static IReadOnlyList<IElement> Resolve(DomScope scope, LocatorChain chain)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(chain);
        IReadOnlyList<IElement>? current = null;
        foreach (var step in chain.Steps)
        {
            switch (step.Kind)
            {
                case LocatorStepKind.Find:
                    current = current is null
                        ? Query(scope, scope.Elements(), step.Query!, isRoot: true, rootScope: null).ToArray()
                        : current.SelectMany(parent => Query(scope, parent.QuerySelectorAll("*"), step.Query!, isRoot: false, rootScope: parent)).Distinct().ToArray();
                    break;
                case LocatorStepKind.Nth:
                    current ??= scope.Elements().ToArray();
                    var index = step.Index < 0 ? current.Count + step.Index : step.Index;
                    current = index >= 0 && index < current.Count ? [current[index]] : [];
                    break;
                case LocatorStepKind.HasText:
                    current = (current ?? scope.Elements().ToArray()).Where(e => TextMatch.Matches(DomFacts.TextOf(e), step.Text!, exact: false)).ToArray();
                    break;
                case LocatorStepKind.HasNotText:
                    current = (current ?? scope.Elements().ToArray()).Where(e => !TextMatch.Matches(DomFacts.TextOf(e), step.Text!, exact: false)).ToArray();
                    break;
            }
        }

        return current ?? [];
    }

    /// <summary>Applies one query to candidate elements.</summary>
    public static IEnumerable<IElement> Query(DomScope scope, IEnumerable<IElement> candidates, By query, bool isRoot = true, IElement? rootScope = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(candidates);
        return query switch
        {
            CssQuery css => isRoot ? scope.QuerySelectorAll(css.Selector) : rootScope!.QuerySelectorAll(css.Selector),
            RoleQuery role => candidates.Where(e =>
                string.Equals(AriaRoles.GetRole(e), role.RoleName, StringComparison.OrdinalIgnoreCase)
                && !DomFacts.IsExcludedFromAccessibilityTree(e, scope)
                && (role.Name is null || TextMatch.Matches(AccessibleName.Compute(e, scope), role.Name, role.Exact))),
            TextQuery text => Innermost(candidates.Where(e => IsTextCandidate(e) && TextMatch.Matches(TextFor(e), text.Value, text.Exact)).ToArray()),
            LabelQuery label => candidates.Where(e => MatchesLabel(e, scope, label.Value, label.Exact)),
            PlaceholderQuery placeholder => candidates.Where(e => e.GetAttribute("placeholder") is { } p && TextMatch.Matches(p, placeholder.Value, placeholder.Exact)),
            TestIdQuery testId => candidates.Where(e => e.GetAttribute(By.TestIdAttribute) == testId.Value),
            AltTextQuery alt => candidates.Where(e => e.LocalName is "img" or "area" or "input" && e.GetAttribute("alt") is { } a && TextMatch.Matches(a, alt.Value, alt.Exact)),
            TitleQuery title => candidates.Where(e => e.GetAttribute("title") is { } t && TextMatch.Matches(t, title.Value, title.Exact)),
            _ => throw new NotSupportedException($"Query type '{query.GetType().Name}' is not supported."),
        };
    }

    /// <summary>Creates a driver-neutral snapshot of an element.</summary>
    public static ElementInfo Describe(IElement element, DomScope scope, bool isFocused, string? valueOverride = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        var attributes = element.Attributes
            .Where(a => !a.Name.StartsWith("blazor:", StringComparison.Ordinal) && !a.Name.StartsWith("_bl_", StringComparison.Ordinal))
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        return new ElementInfo
        {
            TagName = element.LocalName,
            Role = AriaRoles.GetRole(element),
            AccessibleName = AccessibleName.Compute(element, scope),
            Text = DomFacts.TextOf(element),
            Value = valueOverride ?? ValueOf(element),
            Attributes = attributes,
            IsVisible = !DomFacts.IsHidden(element, scope),
            IsEnabled = !DomFacts.IsDisabled(element),
            IsChecked = CheckedState(element),
            IsFocused = isFocused,
            IsEditable = DomFacts.IsEditable(element),
            Html = DomFacts.Snippet(element),
        };
    }

    /// <summary>The current value of a form control.</summary>
    public static string? ValueOf(IElement element)
    {
        switch (element.LocalName)
        {
            case "input":
                return element.GetAttribute("value") ?? string.Empty;
            case "textarea":
                return element.GetAttribute("value") ?? element.TextContent;
            case "select":
            {
                if (element.GetAttribute("value") is { } value)
                {
                    return value;
                }

                var selected = element.QuerySelectorAll("option").FirstOrDefault(o => o.HasAttribute("selected")) ?? element.QuerySelector("option");
                return selected is null ? string.Empty : selected.GetAttribute("value") ?? DomFacts.TextOf(selected);
            }

            default:
                return DomFacts.IsContentEditable(element) ? element.TextContent : null;
        }
    }

    /// <summary>Checked state (null when the element is not checkable).</summary>
    public static bool? CheckedState(IElement element)
    {
        if (element.LocalName == "input")
        {
            var type = (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant();
            if (type is "checkbox" or "radio")
            {
                var value = element.GetAttribute("checked");
                return value is not null && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            }
        }

        var role = AriaRoles.GetRole(element);
        if (role is "checkbox" or "radio" or "switch" or "menuitemcheckbox" or "menuitemradio" or "option" or "treeitem")
        {
            var ariaChecked = element.GetAttribute("aria-checked") ?? (role is "option" or "treeitem" ? element.GetAttribute("aria-selected") : null);
            return ariaChecked is not null ? string.Equals(ariaChecked, "true", StringComparison.OrdinalIgnoreCase) : role is "checkbox" or "radio" or "switch" ? false : null;
        }

        if (role == "button" && element.GetAttribute("aria-pressed") is { } pressed)
        {
            return string.Equals(pressed, "true", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static bool IsTextCandidate(IElement element) => element.LocalName is not ("script" or "style" or "template" or "head" or "html" or "body");

    private static string TextFor(IElement element) =>
        element.LocalName == "input" && (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant() is "button" or "submit" or "reset"
            ? element.GetAttribute("value") ?? string.Empty
            : DomFacts.TextOf(element);

    private static IEnumerable<IElement> Innermost(IElement[] matches) =>
        matches.Where(candidate => !matches.Any(other => !ReferenceEquals(other, candidate) && candidate.Contains(other)));

    private static bool MatchesLabel(IElement element, DomScope scope, string text, bool exact)
    {
        if (element.GetAttribute("aria-label") is { } ariaLabel && TextMatch.Matches(ariaLabel, text, exact))
        {
            return true;
        }

        var labelledBy = scope.ResolveIdRefs(element.GetAttribute("aria-labelledby"));
        if (labelledBy.Count > 0 && TextMatch.Matches(string.Join(' ', labelledBy.Select(DomFacts.TextOf)), text, exact))
        {
            return true;
        }

        if (element.LocalName is "input" or "textarea" or "select" or "button" or "meter" or "output" or "progress")
        {
            return AccessibleName.GetLabels(element, scope).Any(label => TextMatch.Matches(DomFacts.TextOf(label), text, exact));
        }

        return false;
    }
}
