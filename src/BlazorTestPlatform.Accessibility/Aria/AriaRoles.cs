using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility.Aria;

/// <summary>ARIA 1.2 role vocabulary and HTML-AAM implicit role mapping.</summary>
public static class AriaRoles
{
    /// <summary>Concrete ARIA 1.2 roles (plus DPUB/graphics prefixes accepted separately).</summary>
    public static IReadOnlySet<string> Valid { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox",
        "code", "columnheader", "combobox", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory",
        "document", "emphasis", "feed", "figure", "form", "generic", "grid", "gridcell", "group", "heading", "img", "image",
        "insertion", "link", "list", "listbox", "listitem", "log", "main", "mark", "marquee", "math", "menu", "menubar",
        "menuitem", "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none", "note", "option", "paragraph",
        "presentation", "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search",
        "searchbox", "separator", "slider", "spinbutton", "status", "strong", "subscript", "superscript", "switch", "tab",
        "table", "tablist", "tabpanel", "term", "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid", "treeitem",
    };

    /// <summary>Abstract roles that must never be used in content.</summary>
    public static IReadOnlySet<string> Abstract { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "command", "composite", "input", "landmark", "range", "roletype", "section", "sectionhead", "select", "structure", "widget", "window",
    };

    /// <summary>Roles whose accessible name can come from their content.</summary>
    public static IReadOnlySet<string> NameFromContent { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "button", "cell", "checkbox", "columnheader", "gridcell", "heading", "link", "menuitem", "menuitemcheckbox", "menuitemradio",
        "option", "radio", "row", "rowheader", "switch", "tab", "tooltip", "treeitem", "caption",
    };

    /// <summary>Interactive (widget) roles.</summary>
    public static IReadOnlySet<string> Widgets { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "button", "checkbox", "combobox", "gridcell", "link", "listbox", "menu", "menubar", "menuitem", "menuitemcheckbox",
        "menuitemradio", "option", "radio", "radiogroup", "scrollbar", "searchbox", "slider", "spinbutton", "switch", "tab",
        "tablist", "textbox", "tree", "treegrid", "treeitem", "grid",
    };

    /// <summary>True for valid concrete roles (including <c>doc-*</c> and <c>graphics-*</c>).</summary>
    public static bool IsValid(string role) =>
        Valid.Contains(role) || role.StartsWith("doc-", StringComparison.Ordinal) || role.StartsWith("graphics-", StringComparison.Ordinal);

    /// <summary>The explicit role (first valid token of <c>role</c>), or null.</summary>
    public static string? GetExplicitRole(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var attribute = element.GetAttribute("role");
        if (string.IsNullOrWhiteSpace(attribute))
        {
            return null;
        }

        foreach (var token in attribute.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var role = token.ToLowerInvariant();
            if (IsValid(role) && !Abstract.Contains(role))
            {
                return role;
            }
        }

        return null;
    }

    /// <summary>The computed role: explicit (unless a presentational role conflicts with focusability) or implicit.</summary>
    public static string? GetRole(IElement element)
    {
        var explicitRole = GetExplicitRole(element);
        if (explicitRole is "none" or "presentation" && (DomFacts.IsFocusable(element) || HasGlobalAriaAttribute(element)))
        {
            // Presentational role conflict resolution: browsers ignore none/presentation on focusable elements.
            return GetImplicitRole(element);
        }

        return explicitRole ?? GetImplicitRole(element);
    }

    /// <summary>HTML-AAM implicit role.</summary>
    public static string? GetImplicitRole(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (element.LocalName)
        {
            case "a":
            case "area":
                return element.HasAttribute("href") ? "link" : "generic";
            case "article":
                return "article";
            case "aside":
                return "complementary";
            case "blockquote":
                return "blockquote";
            case "button":
                return "button";
            case "caption":
                return "caption";
            case "code":
                return "code";
            case "datalist":
                return "listbox";
            case "dd":
                return "definition";
            case "del":
                return "deletion";
            case "details":
                return "group";
            case "dfn":
            case "dt":
                return "term";
            case "dialog":
                return "dialog";
            case "em":
                return "emphasis";
            case "fieldset":
                return "group";
            case "figure":
                return "figure";
            case "footer":
                return IsInSectioningContent(element) ? "generic" : "contentinfo";
            case "form":
                return "form";
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                return "heading";
            case "header":
                return IsInSectioningContent(element) ? "generic" : "banner";
            case "hr":
                return "separator";
            case "img":
                return element.GetAttribute("alt") == string.Empty && !element.HasAttribute("aria-label") && !element.HasAttribute("aria-labelledby") ? "presentation" : "img";
            case "input":
                return InputRole(element);
            case "ins":
                return "insertion";
            case "li":
                return "listitem";
            case "main":
                return "main";
            case "mark":
                return "mark";
            case "math":
                return "math";
            case "menu":
            case "ol":
            case "ul":
                return "list";
            case "meter":
                return "meter";
            case "nav":
                return "navigation";
            case "optgroup":
                return "group";
            case "option":
                return "option";
            case "output":
                return "status";
            case "p":
                return "paragraph";
            case "progress":
                return "progressbar";
            case "search":
                return "search";
            case "section":
                return element.HasAttribute("aria-label") || element.HasAttribute("aria-labelledby") || element.HasAttribute("title") ? "region" : "generic";
            case "select":
                return element.HasAttribute("multiple") || (int.TryParse(element.GetAttribute("size"), out var size) && size > 1) ? "listbox" : "combobox";
            case "strong":
                return "strong";
            case "sub":
                return "subscript";
            case "sup":
                return "superscript";
            case "svg":
                return "graphics-document";
            case "table":
                return "table";
            case "tbody":
            case "thead":
            case "tfoot":
                return "rowgroup";
            case "td":
                return "cell";
            case "textarea":
                return "textbox";
            case "th":
                return string.Equals(element.GetAttribute("scope"), "row", StringComparison.OrdinalIgnoreCase) ? "rowheader" : "columnheader";
            case "time":
                return "time";
            case "tr":
                return "row";
            case "div":
            case "span":
            case "b":
            case "i":
            case "u":
            case "small":
            case "bdi":
            case "bdo":
            case "data":
            case "samp":
            case "q":
            case "s":
            case "pre":
                return "generic";
            default:
                return null;
        }
    }

    /// <summary>True when the element has a global ARIA attribute (forces presentational-role conflict resolution).</summary>
    public static bool HasGlobalAriaAttribute(IElement element) =>
        element.Attributes.Any(a => a.Name is "aria-label" or "aria-labelledby" or "aria-describedby" or "aria-live" or "aria-owns" or "aria-controls" or "aria-details" or "aria-keyshortcuts" or "aria-roledescription");

    private static string InputRole(IElement element)
    {
        var type = (element.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
        return type switch
        {
            "button" or "image" or "reset" or "submit" => "button",
            "checkbox" => "checkbox",
            "radio" => "radio",
            "range" => "slider",
            "number" => "spinbutton",
            "search" => element.HasAttribute("list") ? "combobox" : "searchbox",
            "email" or "tel" or "text" or "url" or "" => element.HasAttribute("list") ? "combobox" : "textbox",
            "hidden" => "none",
            _ => "textbox",
        };
    }

    private static bool IsInSectioningContent(IElement element)
    {
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName is "article" or "aside" or "main" or "nav" or "section")
            {
                return true;
            }
        }

        return false;
    }
}
