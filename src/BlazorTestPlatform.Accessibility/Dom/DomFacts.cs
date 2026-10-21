using System.Globalization;
using System.Text;
using AngleSharp.Dom;

namespace BlazorTestPlatform.Accessibility.Dom;

/// <summary>Browser-free approximations of DOM facts (visibility, focusability, disabled state, selectors).</summary>
public static class DomFacts
{
    private static readonly HashSet<string> NeverRendered = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "template", "head", "noscript", "meta", "link", "title" };
    private static readonly HashSet<string> Disableable = new(StringComparer.OrdinalIgnoreCase) { "button", "input", "select", "textarea", "optgroup", "option", "fieldset" };

    /// <summary>
    /// True when the element would not be rendered: <c>hidden</c>, <c>display:none</c>/<c>visibility:hidden</c> inline styles,
    /// <c>input[type=hidden]</c>, closed <c>details</c>/<c>dialog</c>, or non-rendered elements — on the element or an ancestor.
    /// </summary>
    public static bool IsHidden(IElement element, DomScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (scope?.Styles is { } styles && styles.IsHidden(element))
        {
            return true;
        }

        if (element.LocalName == "input" && string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        IElement? previous = null;
        for (var current = element; current is not null; previous = current, current = current.ParentElement)
        {
            if (NeverRendered.Contains(current.LocalName) || current.HasAttribute("hidden"))
            {
                return true;
            }

            if (HasInlineStyle(current, "display", "none") || HasInlineStyle(current, "visibility", "hidden") || HasInlineStyle(current, "visibility", "collapse"))
            {
                return true;
            }

            if (current.LocalName == "details" && !current.HasAttribute("open") && previous is not null && !IsDetailsSummary(previous))
            {
                return true;
            }

            if (current.LocalName == "dialog" && !current.HasAttribute("open"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the element or an ancestor has <c>aria-hidden="true"</c>, or the element is hidden.</summary>
    public static bool IsExcludedFromAccessibilityTree(IElement element, DomScope? scope = null)
    {
        if (IsHidden(element, scope))
        {
            return true;
        }

        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (string.Equals(current.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) || current.HasAttribute("inert"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the element is disabled natively (including via a disabled fieldset) or with <c>aria-disabled</c>.</summary>
    public static bool IsDisabled(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return string.Equals(element.GetAttribute("aria-disabled"), "true", StringComparison.OrdinalIgnoreCase) || IsNativelyDisabled(element);
    }

    /// <summary>
    /// True when HTML itself disables the element (what <c>:disabled</c> matches): its own <c>disabled</c> attribute, a
    /// disabled parent <c>optgroup</c> for options, or a disabled ancestor <c>fieldset</c> unless the element is inside
    /// that fieldset's first <c>legend</c>. Unlike <c>aria-disabled</c>, this removes the element from focus and stops activation.
    /// </summary>
    public static bool IsNativelyDisabled(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!Disableable.Contains(element.LocalName))
        {
            return false;
        }

        if (element.HasAttribute("disabled"))
        {
            return true;
        }

        if (element.LocalName is "option" or "optgroup")
        {
            return element.LocalName == "option" && element.ParentElement is { LocalName: "optgroup" } group && group.HasAttribute("disabled");
        }

        // Form controls, nested fieldsets included, inherit every disabled ancestor fieldset.
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName == "fieldset" && parent.HasAttribute("disabled"))
            {
                var firstLegend = parent.Children.FirstOrDefault(c => c.LocalName == "legend");
                if (firstLegend is null || !firstLegend.Contains(element))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when the element or an ancestor has <c>inert</c>: it cannot be focused, clicked or hovered.</summary>
    public static bool IsInert(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.HasAttribute("inert"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the element can receive focus (natively or via tabindex).</summary>
    public static bool IsFocusable(IElement element, DomScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (IsHidden(element, scope) || IsInert(element) || IsNativelyDisabled(element))
        {
            return false;
        }

        if (TryGetTabIndex(element, out _))
        {
            return true;
        }

        return IsNativelyFocusable(element);
    }

    /// <summary>True when the element participates in sequential (Tab) navigation.</summary>
    public static bool IsTabbable(IElement element, DomScope? scope = null) =>
        IsFocusable(element, scope) && (!TryGetTabIndex(element, out var tabIndex) || tabIndex >= 0);

    /// <summary>Elements in Tab order: positive tabindex ascending first, then document order.</summary>
    public static IReadOnlyList<IElement> TabOrder(DomScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var tabbable = scope.Elements().Where(e => IsTabbable(e, scope)).Select((element, index) => (element, index, tabIndex: TryGetTabIndex(element, out var t) ? t : 0)).ToList();

        // Radio groups: only the checked radio (or the first one) of a named group is in the tab sequence.
        var radioGroupsSeen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(IElement Element, int Index, int TabIndex)>();
        foreach (var item in tabbable)
        {
            if (item.element.LocalName == "input" && string.Equals(item.element.GetAttribute("type"), "radio", StringComparison.OrdinalIgnoreCase)
                && item.element.GetAttribute("name") is { Length: > 0 } groupName)
            {
                var group = tabbable.Where(t => t.element.LocalName == "input" && t.element.GetAttribute("name") == groupName).ToList();
                var selected = group.FirstOrDefault(t => t.element.HasAttribute("checked"));
                var representative = selected.element ?? group[0].element;
                if (!ReferenceEquals(representative, item.element) || !radioGroupsSeen.Add(groupName))
                {
                    continue;
                }
            }

            result.Add(item);
        }

        return result
            .OrderBy(t => t.TabIndex > 0 ? 0 : 1)
            .ThenBy(t => t.TabIndex > 0 ? t.TabIndex : 0)
            .ThenBy(t => t.Index)
            .Select(t => t.Element)
            .ToArray();
    }

    /// <summary>Parses <c>tabindex</c>.</summary>
    public static bool TryGetTabIndex(IElement element, out int tabIndex)
    {
        tabIndex = 0;
        var value = element.GetAttribute("tabindex");
        return value is not null && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tabIndex);
    }

    /// <summary>True for elements focusable without tabindex.</summary>
    public static bool IsNativelyFocusable(IElement element) => element.LocalName switch
    {
        "a" or "area" => element.HasAttribute("href"),
        "button" or "select" or "textarea" or "iframe" or "embed" or "object" => true,
        "input" => !string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase),
        "summary" => IsDetailsSummary(element),
        "audio" or "video" => element.HasAttribute("controls"),
        _ => IsContentEditable(element),
    };

    /// <summary>
    /// True for the <c>summary</c> that toggles its <c>details</c>: the first <c>summary</c> child. Later ones are ordinary
    /// content (not focusable, hidden while the details are closed).
    /// </summary>
    public static bool IsDetailsSummary(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.LocalName == "summary"
            && element.ParentElement is { LocalName: "details" } details
            && ReferenceEquals(details.Children.FirstOrDefault(c => c.LocalName == "summary"), element);
    }

    /// <summary>True for <c>contenteditable</c> elements.</summary>
    public static bool IsContentEditable(IElement element)
    {
        var value = element.GetAttribute("contenteditable");
        return value is not null && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the element accepts text input.</summary>
    public static bool IsEditable(IElement element)
    {
        if (IsDisabled(element) || element.HasAttribute("readonly"))
        {
            return false;
        }

        if (element.LocalName == "textarea" || IsContentEditable(element))
        {
            return true;
        }

        if (element.LocalName != "input")
        {
            return false;
        }

        var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        return type is "text" or "search" or "email" or "url" or "tel" or "password" or "number" or "date" or "datetime-local" or "month" or "time" or "week";
    }

    /// <summary>Whitespace-normalized text content, excluding scripts/styles/templates.</summary>
    public static string TextOf(INode node)
    {
        var builder = new StringBuilder();
        AppendText(node, builder);
        return TextMatch.Normalize(builder.ToString());
    }

    /// <summary>A CSS selector path that re-resolves the element after re-renders (<c>#id</c> or <c>:nth-child</c> chain).</summary>
    public static string CssPath(IElement element, DomScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var id = element.GetAttribute("id");
        if (!string.IsNullOrEmpty(id) && IsSimpleIdentifier(id) && (scope is null || scope.ElementsWithId(id).Count == 1))
        {
            return "#" + id;
        }

        var segments = new List<string>();
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.LocalName is "body" or "html")
            {
                break;
            }

            var currentId = current.GetAttribute("id");
            if (!ReferenceEquals(current, element) && !string.IsNullOrEmpty(currentId) && IsSimpleIdentifier(currentId)
                && (scope is null || scope.ElementsWithId(currentId).Count == 1))
            {
                segments.Add("#" + currentId);
                break;
            }

            var index = 1;
            for (var sibling = current.PreviousElementSibling; sibling is not null; sibling = sibling.PreviousElementSibling)
            {
                index++;
            }

            // Selectors match against the whole ancestry, so a top-level segment is anchored to the container the roots
            // sit in (bUnit and parsed fragments both use a body): otherwise "div:nth-child(2) > a:nth-child(1)" can also
            // match a deeper element that comes first. A detached root has no parent for :nth-child to count against.
            var nth = $"{current.LocalName}:nth-child({index.ToString(CultureInfo.InvariantCulture)})";
            segments.Add(current.ParentElement switch
            {
                null => current.LocalName,
                { LocalName: "body" } => "body > " + nth,
                _ => nth,
            });
        }

        segments.Reverse();
        return string.Join(" > ", segments);
    }

    /// <summary>Outer HTML without Blazor bookkeeping attributes, truncated.</summary>
    public static string Snippet(IElement element, int maxLength = 300)
    {
        var html = element.OuterHtml;
        html = System.Text.RegularExpressions.Regex.Replace(html, "\\s(?:blazor:[^\\s=>]+|b-[a-z0-9]{10}|_bl_[0-9a-f-]+)(?:=\"[^\"]*\")?", string.Empty, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        return html.Length <= maxLength ? html : html[..maxLength] + "…";
    }

    internal static bool HasInlineStyle(IElement element, string property, string value)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return false;
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0
                && string.Equals(declaration[..colon].Trim(), property, StringComparison.OrdinalIgnoreCase)
                && declaration[(colon + 1)..].Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSimpleIdentifier(string id) => id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && !char.IsAsciiDigit(id[0]);

    private static void AppendText(INode node, StringBuilder builder)
    {
        if (node is IElement element && NeverRendered.Contains(element.LocalName))
        {
            return;
        }

        if (node.NodeType == NodeType.Text)
        {
            builder.Append(node.TextContent);
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendText(child, builder);
            if (child is IElement { LocalName: "br" or "p" or "div" or "li" })
            {
                builder.Append(' ');
            }
        }
    }
}
