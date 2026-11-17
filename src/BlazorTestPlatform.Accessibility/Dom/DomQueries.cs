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
                    current = (current ?? scope.Elements().ToArray()).Where(e => TextMatch.Matches(DomFacts.TextOf(e), step.Text!, exact: false, step.Pattern)).ToArray();
                    break;
                case LocatorStepKind.HasNotText:
                    current = (current ?? scope.Elements().ToArray()).Where(e => !TextMatch.Matches(DomFacts.TextOf(e), step.Text!, exact: false, step.Pattern)).ToArray();
                    break;
                case LocatorStepKind.Has:
                    current = (current ?? scope.Elements().ToArray()).Where(e => ResolveWithin(scope, e, step.Inner!).Count > 0).ToArray();
                    break;
                case LocatorStepKind.HasNot:
                    current = (current ?? scope.Elements().ToArray()).Where(e => ResolveWithin(scope, e, step.Inner!).Count == 0).ToArray();
                    break;
            }
        }

        return current ?? [];
    }

    // A filter's inner locator, resolved among the descendants of one element (Playwright's has/hasNot).
    private static IReadOnlyList<IElement> ResolveWithin(DomScope scope, IElement element, LocatorChain chain)
    {
        IReadOnlyList<IElement> current = [element];
        foreach (var step in chain.Steps)
        {
            current = step.Kind switch
            {
                LocatorStepKind.Find => current.SelectMany(parent => Query(scope, parent.QuerySelectorAll("*"), step.Query!, isRoot: false, rootScope: parent)).Distinct().ToArray(),
                _ => Resolve(scope, current, step),
            };
        }

        return current;
    }

    private static IReadOnlyList<IElement> Resolve(DomScope scope, IReadOnlyList<IElement> current, LocatorStep step)
    {
        switch (step.Kind)
        {
            case LocatorStepKind.Nth:
                var index = step.Index < 0 ? current.Count + step.Index : step.Index;
                return index >= 0 && index < current.Count ? [current[index]] : [];
            case LocatorStepKind.HasText:
                return current.Where(e => TextMatch.Matches(DomFacts.TextOf(e), step.Text!, exact: false, step.Pattern)).ToArray();
            case LocatorStepKind.HasNotText:
                return current.Where(e => !TextMatch.Matches(DomFacts.TextOf(e), step.Text!, exact: false, step.Pattern)).ToArray();
            case LocatorStepKind.Has:
                return current.Where(e => ResolveWithin(scope, e, step.Inner!).Count > 0).ToArray();
            case LocatorStepKind.HasNot:
                return current.Where(e => ResolveWithin(scope, e, step.Inner!).Count == 0).ToArray();
            default:
                throw new NotSupportedException($"Locator step {step.Kind} is not supported.");
        }
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
                && (role.IncludeHidden || !DomFacts.IsExcludedFromAccessibilityTree(e, scope))
                && MatchesRoleState(e, role)
                && (role.Name is null || TextMatch.Matches(AccessibleName.Compute(e, scope), role.Name, role.Exact, role.Pattern))),
            TextQuery text => Innermost(candidates.Where(e => IsTextCandidate(e) && TextMatch.Matches(TextFor(e), text.Value, text.Exact, text.Pattern)).ToArray()),
            LabelQuery label => candidates.Where(e => MatchesLabel(e, scope, label)),
            PlaceholderQuery placeholder => candidates.Where(e => e.GetAttribute("placeholder") is { } p && TextMatch.Matches(p, placeholder.Value, placeholder.Exact, placeholder.Pattern)),
            TestIdQuery testId => candidates.Where(e => e.GetAttribute(By.TestIdAttribute) == testId.Value),
            AltTextQuery alt => candidates.Where(e => e.LocalName is "img" or "area" or "input" && e.GetAttribute("alt") is { } a && TextMatch.Matches(a, alt.Value, alt.Exact, alt.Pattern)),
            TitleQuery title => candidates.Where(e => e.GetAttribute("title") is { } t && TextMatch.Matches(t, title.Value, title.Exact, title.Pattern)),
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
            AccessibleDescription = AccessibleName.ComputeDescription(element, scope),
            AccessibleErrorMessage = AccessibleName.ComputeErrorMessage(element, scope),
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

    private static bool MatchesLabel(IElement element, DomScope scope, LabelQuery query)
    {
        bool Match(string? text) => TextMatch.Matches(text, query.Value, query.Exact, query.Pattern);
        if (element.GetAttribute("aria-label") is { } ariaLabel && Match(ariaLabel))
        {
            return true;
        }

        var labelledBy = scope.ResolveIdRefs(element.GetAttribute("aria-labelledby"));
        if (labelledBy.Count > 0 && Match(string.Join(' ', labelledBy.Select(DomFacts.TextOf))))
        {
            return true;
        }

        if (element.LocalName is "input" or "textarea" or "select" or "button" or "meter" or "output" or "progress")
        {
            return AccessibleName.GetLabels(element, scope).Any(label => Match(DomFacts.TextOf(label)));
        }

        return false;
    }

    // The state options of a role query, read the same way as the JavaScript engine (btp-driver.js roleStateMatches).
    private static bool MatchesRoleState(IElement element, RoleQuery role)
    {
        if (role.Checked is { } wantChecked)
        {
            if (AriaRoles.GetRole(element) == "button" || CheckedState(element) is not { } isChecked || isChecked != wantChecked
                || string.Equals(element.GetAttribute("aria-checked")?.Trim(), "mixed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return Tristate(element.GetAttribute("aria-pressed"), role.Pressed)
            && Tristate(element.GetAttribute("aria-expanded"), role.Expanded)
            && Tristate(element.GetAttribute("aria-selected") ?? (element.LocalName == "option" ? (IsOptionSelected(element) ? "true" : "false") : null), role.Selected)
            && (role.Disabled is not { } wantDisabled || DomFacts.IsDisabled(element) == wantDisabled)
            && (role.Level is not { } level || LevelOf(element) == level);

        static bool Tristate(string? attribute, bool? wanted) =>
            wanted is not { } want || attribute?.Trim().ToLowerInvariant() == (want ? "true" : "false");
    }

    // HTML's selectedness: a single-choice select (no multiple, display size 1) selects its value (as Blazor renders it),
    // else the last option marked selected, else its first enabled option; a list box selects only marked options.
    private static bool IsOptionSelected(IElement option)
    {
        var select = option.Closest("select");
        if (select is null)
        {
            return option.HasAttribute("selected");
        }

        var single = !select.HasAttribute("multiple")
            && (!int.TryParse(select.GetAttribute("size"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var size) || size <= 1);
        if (!single)
        {
            return option.HasAttribute("selected");
        }

        var options = select.QuerySelectorAll("option").ToArray();
        var chosen = select.GetAttribute("value") is { } value
            ? options.FirstOrDefault(o => string.Equals(o.GetAttribute("value") ?? DomFacts.TextOf(o), value, StringComparison.Ordinal))
            : options.LastOrDefault(o => o.HasAttribute("selected")) ?? options.FirstOrDefault(o => !DomFacts.IsDisabled(o));
        return ReferenceEquals(chosen, option);
    }

    private static int? LevelOf(IElement element) =>
        AriaRoles.GetRole(element) == "heading"
            ? AriaSnapshot.HeadingLevel(element)
            : int.TryParse(element.GetAttribute("aria-level"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var level) ? level : null;
}
