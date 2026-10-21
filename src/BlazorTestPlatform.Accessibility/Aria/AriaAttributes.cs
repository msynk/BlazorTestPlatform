using System.Globalization;
using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility.Aria;

/// <summary>ARIA 1.2 attribute vocabulary and value validation.</summary>
public static class AriaAttributes
{
    private static readonly string[] TrueFalse = ["true", "false"];
    private static readonly string[] Tristate = ["true", "false", "mixed"];
    private static readonly string[] TrueFalseUndefined = ["true", "false", "undefined"];

    private static readonly Dictionary<string, string[]> Tokens = new(StringComparer.Ordinal)
    {
        ["aria-atomic"] = TrueFalse,
        ["aria-busy"] = TrueFalse,
        ["aria-disabled"] = TrueFalse,
        ["aria-modal"] = TrueFalse,
        ["aria-multiline"] = TrueFalse,
        ["aria-multiselectable"] = TrueFalse,
        ["aria-readonly"] = TrueFalse,
        ["aria-required"] = TrueFalse,
        ["aria-checked"] = [.. Tristate, "undefined"],
        ["aria-pressed"] = [.. Tristate, "undefined"],
        ["aria-expanded"] = TrueFalseUndefined,
        ["aria-hidden"] = TrueFalseUndefined,
        ["aria-selected"] = TrueFalseUndefined,
        ["aria-grabbed"] = TrueFalseUndefined,
        ["aria-autocomplete"] = ["inline", "list", "both", "none"],
        ["aria-current"] = ["page", "step", "location", "date", "time", "true", "false"],
        ["aria-haspopup"] = ["true", "false", "menu", "listbox", "tree", "grid", "dialog"],
        ["aria-invalid"] = ["grammar", "false", "spelling", "true"],
        ["aria-live"] = ["assertive", "off", "polite"],
        ["aria-orientation"] = ["horizontal", "undefined", "vertical"],
        ["aria-sort"] = ["ascending", "descending", "none", "other"],
    };

    private static readonly HashSet<string> Integers = new(StringComparer.Ordinal)
    {
        "aria-level", "aria-posinset", "aria-setsize", "aria-colcount", "aria-colindex", "aria-colspan", "aria-rowcount", "aria-rowindex", "aria-rowspan",
    };

    private static readonly HashSet<string> Numbers = new(StringComparer.Ordinal) { "aria-valuemax", "aria-valuemin", "aria-valuenow" };

    private static readonly HashSet<string> IdRefs = new(StringComparer.Ordinal)
    {
        "aria-labelledby", "aria-describedby", "aria-owns", "aria-flowto", "aria-errormessage", "aria-details", "aria-activedescendant", "aria-controls",
    };

    /// <summary>All valid ARIA 1.2 attributes.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "aria-activedescendant", "aria-atomic", "aria-autocomplete", "aria-braillelabel", "aria-brailleroledescription", "aria-busy",
        "aria-checked", "aria-colcount", "aria-colindex", "aria-colindextext", "aria-colspan", "aria-controls", "aria-current",
        "aria-describedby", "aria-description", "aria-details", "aria-disabled", "aria-dropeffect", "aria-errormessage", "aria-expanded",
        "aria-flowto", "aria-grabbed", "aria-haspopup", "aria-hidden", "aria-invalid", "aria-keyshortcuts", "aria-label",
        "aria-labelledby", "aria-level", "aria-live", "aria-modal", "aria-multiline", "aria-multiselectable", "aria-orientation",
        "aria-owns", "aria-placeholder", "aria-posinset", "aria-pressed", "aria-readonly", "aria-relevant", "aria-required",
        "aria-roledescription", "aria-rowcount", "aria-rowindex", "aria-rowindextext", "aria-rowspan", "aria-selected",
        "aria-setsize", "aria-sort", "aria-valuemax", "aria-valuemin", "aria-valuenow", "aria-valuetext",
    };

    /// <summary>Returns a problem description, or null when the value is valid.</summary>
    public static string? Validate(string name, string value, DomScope scope, IElement element)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var trimmed = value.Trim();
        if (Tokens.TryGetValue(name, out var allowed))
        {
            return allowed.Contains(trimmed.ToLowerInvariant()) ? null : $"{name}=\"{value}\" must be one of: {string.Join(", ", allowed)}.";
        }

        if (Integers.Contains(name))
        {
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? null : $"{name}=\"{value}\" must be an integer.";
        }

        if (Numbers.Contains(name))
        {
            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? null : $"{name}=\"{value}\" must be a number.";
        }

        if (IdRefs.Contains(name))
        {
            var ids = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length == 0)
            {
                return name is "aria-activedescendant" ? null : $"{name} is empty.";
            }

            // aria-controls may reference elements that only exist when expanded.
            if (name == "aria-controls" && string.Equals(element.GetAttribute("aria-expanded"), "false", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ids.Any(id => scope.GetElementById(id) is not null) ? null : $"{name} references id(s) that do not exist: {string.Join(", ", ids)}.";
        }

        return null;
    }

    /// <summary>Required attributes missing for the element's explicit role.</summary>
    public static IEnumerable<string> MissingRequired(IElement element)
    {
        var role = AriaRoles.GetExplicitRole(element);
        if (role is null)
        {
            yield break;
        }

        var isNativeCheckable = element.LocalName == "input" && (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant() is "checkbox" or "radio";
        switch (role)
        {
            case "checkbox" or "radio" or "switch" or "menuitemcheckbox" or "menuitemradio" when !isNativeCheckable && !element.HasAttribute("aria-checked"):
                yield return "aria-checked";
                break;
            case "slider" when element.LocalName != "input" && !element.HasAttribute("aria-valuenow"):
                yield return "aria-valuenow";
                break;
            case "scrollbar":
                if (!element.HasAttribute("aria-controls"))
                {
                    yield return "aria-controls";
                }

                if (!element.HasAttribute("aria-valuenow"))
                {
                    yield return "aria-valuenow";
                }

                break;
            case "heading" when !element.LocalName.StartsWith('h') && !element.HasAttribute("aria-level"):
                yield return "aria-level";
                break;
            case "combobox" when element.LocalName is not ("select" or "input") && !element.HasAttribute("aria-expanded"):
                yield return "aria-expanded";
                break;
        }
    }
}

/// <summary>Required owned-element / context relationships (ARIA 1.2).</summary>
public static class AriaStructure
{
    /// <summary>Roles that must own at least one of the listed roles.</summary>
    public static IReadOnlyDictionary<string, string[]> RequiredChildren { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["list"] = ["listitem"],
        ["listbox"] = ["option", "group"],
        ["menu"] = ["menuitem", "menuitemcheckbox", "menuitemradio", "group", "separator"],
        ["menubar"] = ["menuitem", "menuitemcheckbox", "menuitemradio", "group", "separator"],
        ["tablist"] = ["tab"],
        ["tree"] = ["treeitem", "group"],
        ["grid"] = ["row", "rowgroup"],
        ["table"] = ["row", "rowgroup"],
        ["treegrid"] = ["row", "rowgroup"],
        ["rowgroup"] = ["row"],
        ["row"] = ["cell", "columnheader", "gridcell", "rowheader"],
    };

    /// <summary>Roles that must be owned by one of the listed roles.</summary>
    public static IReadOnlyDictionary<string, string[]> RequiredParents { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["listitem"] = ["list", "directory"],
        ["option"] = ["listbox", "group"],
        ["tab"] = ["tablist"],
        ["menuitem"] = ["menu", "menubar", "group"],
        ["menuitemcheckbox"] = ["menu", "menubar", "group"],
        ["menuitemradio"] = ["menu", "menubar", "group"],
        ["treeitem"] = ["tree", "group"],
        ["row"] = ["grid", "table", "treegrid", "rowgroup"],
        ["cell"] = ["row"],
        ["gridcell"] = ["row"],
        ["columnheader"] = ["row"],
        ["rowheader"] = ["row"],
    };

    /// <summary>Returns a problem when required owned roles are missing.</summary>
    public static string? CheckChildren(IElement element, Func<IElement, string?> roleOf)
    {
        var role = AriaRoles.GetExplicitRole(element)!;
        var required = RequiredChildren[role];
        var owned = OwnedRoles(element, roleOf).ToArray();
        if (owned.Length == 0)
        {
            return $"Role '{role}' must contain {string.Join(" or ", required)}.";
        }

        var unexpected = owned.Where(r => !required.Contains(r)).Distinct().ToArray();
        return unexpected.Length > 0 ? $"Role '{role}' must only own {string.Join("/", required)}, found {string.Join(", ", unexpected)}." : null;
    }

    /// <summary>Returns a problem when the required context role is missing.</summary>
    public static string? CheckParent(IElement element, Func<IElement, string?> roleOf)
    {
        var role = AriaRoles.GetExplicitRole(element)!;
        var required = RequiredParents[role];
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            var parentRole = roleOf(parent);
            if (parentRole is null or "generic" or "none" or "presentation")
            {
                continue;
            }

            return required.Contains(parentRole) ? null : $"Role '{role}' must be contained by {string.Join(" or ", required)}, found '{parentRole}'.";
        }

        return $"Role '{role}' must be contained by {string.Join(" or ", required)}.";
    }

    private static IEnumerable<string> OwnedRoles(IElement element, Func<IElement, string?> roleOf)
    {
        foreach (var child in element.Children)
        {
            if (DomFacts.IsExcludedFromAccessibilityTree(child) || child.LocalName is "script" or "template")
            {
                continue;
            }

            var role = roleOf(child);
            if (role is null or "generic" or "none" or "presentation")
            {
                foreach (var nested in OwnedRoles(child, roleOf))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return role;
            }
        }
    }
}
