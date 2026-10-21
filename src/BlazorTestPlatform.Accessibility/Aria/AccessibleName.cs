using System.Text;
using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Dom;

namespace BlazorTestPlatform.Accessibility.Aria;

/// <summary>
/// Accessible name computation following the main steps of W3C accname 1.2: <c>aria-labelledby</c>, <c>aria-label</c>,
/// native labelling (<c>label</c>, <c>alt</c>, <c>legend</c>, <c>caption</c>, <c>value</c>), name from content, then
/// <c>title</c>/<c>placeholder</c> fallbacks. Good enough to match how browsers name common widgets.
/// </summary>
public static class AccessibleName
{
    /// <summary>Computes the accessible name.</summary>
    public static string Compute(IElement element, DomScope scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        return TextMatch.Normalize(Compute(element, scope, new HashSet<IElement>(), inLabelledByTraversal: false, inContentTraversal: false));
    }

    /// <summary>Computes the accessible description (<c>aria-describedby</c>, <c>aria-description</c>, then <c>title</c>).</summary>
    public static string ComputeDescription(IElement element, DomScope scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        var describedBy = scope.ResolveIdRefs(element.GetAttribute("aria-describedby"));
        if (describedBy.Count > 0)
        {
            return TextMatch.Normalize(string.Join(' ', describedBy.Select(e => Compute(e, scope, new HashSet<IElement>(), true, false))));
        }

        return TextMatch.Normalize(element.GetAttribute("aria-description") ?? (Compute(element, scope).Length > 0 ? element.GetAttribute("title") : null));
    }

    /// <summary>Labels associated with a labelable element (<c>label[for]</c> and wrapping <c>label</c>).</summary>
    public static IReadOnlyList<IElement> GetLabels(IElement element, DomScope scope)
    {
        var labels = new List<IElement>();
        var id = element.GetAttribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            labels.AddRange(scope.QuerySelectorAll("label").Where(l => l.GetAttribute("for") == id));
        }

        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName == "label" && !labels.Contains(parent) && (!parent.HasAttribute("for") || parent.GetAttribute("for") == id))
            {
                labels.Add(parent);
                break;
            }
        }

        return labels;
    }

    private static string Compute(IElement element, DomScope scope, HashSet<IElement> visited, bool inLabelledByTraversal, bool inContentTraversal)
    {
        if (!visited.Add(element))
        {
            return string.Empty;
        }

        if (!inLabelledByTraversal && DomFacts.IsExcludedFromAccessibilityTree(element, scope))
        {
            return string.Empty;
        }

        // 2B: aria-labelledby (not followed recursively).
        if (!inLabelledByTraversal)
        {
            var labelledBy = scope.ResolveIdRefs(element.GetAttribute("aria-labelledby"));
            if (labelledBy.Count > 0)
            {
                return string.Join(' ', labelledBy.Select(e => Compute(e, scope, visited, inLabelledByTraversal: true, inContentTraversal: false)));
            }
        }

        var role = AriaRoles.GetRole(element);

        // 2C (embedded control inside a label): use the control's value.
        if (inContentTraversal && role is "textbox" or "combobox" or "listbox" or "slider" or "spinbutton")
        {
            return ControlValue(element);
        }

        // 2D: aria-label.
        var ariaLabel = element.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return ariaLabel;
        }

        // 2E: native host-language label.
        if (role is not "presentation" and not "none")
        {
            var native = NativeName(element, scope, visited);
            if (!string.IsNullOrWhiteSpace(native))
            {
                return native;
            }
        }

        // 2F: name from content.
        if (inLabelledByTraversal || inContentTraversal || (role is not null && AriaRoles.NameFromContent.Contains(role)) || element.LocalName is "label" or "legend" or "caption" or "figcaption" or "summary")
        {
            var content = ContentText(element, scope, visited);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        // 2I: tooltip attribute fallbacks.
        return element.GetAttribute("title") ?? (element.LocalName is "input" or "textarea" ? element.GetAttribute("placeholder") ?? string.Empty : string.Empty);
    }

    private static string NativeName(IElement element, DomScope scope, HashSet<IElement> visited)
    {
        switch (element.LocalName)
        {
            case "input":
            {
                var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
                if (type is "button" or "submit" or "reset")
                {
                    var value = element.GetAttribute("value");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }

                    return type switch
                    {
                        "submit" => "Submit",
                        "reset" => "Reset",
                        _ => LabelsText(element, scope, visited),
                    };
                }

                if (type == "image")
                {
                    return element.GetAttribute("alt") ?? element.GetAttribute("value") ?? string.Empty;
                }

                return LabelsText(element, scope, visited);
            }

            case "textarea":
            case "select":
            case "meter":
            case "progress":
            case "output":
                return LabelsText(element, scope, visited);
            case "button":
            {
                var labels = LabelsText(element, scope, visited);
                return labels.Length > 0 ? labels : string.Empty;
            }

            case "img":
            case "area":
                return element.GetAttribute("alt") ?? string.Empty;
            case "fieldset":
                return element.Children.FirstOrDefault(c => c.LocalName == "legend") is { } legend ? Compute(legend, scope, visited, false, true) : string.Empty;
            case "figure":
                return element.Children.FirstOrDefault(c => c.LocalName == "figcaption") is { } figcaption ? Compute(figcaption, scope, visited, false, true) : string.Empty;
            case "table":
                return element.Children.FirstOrDefault(c => c.LocalName == "caption") is { } caption ? Compute(caption, scope, visited, false, true) : string.Empty;
            case "svg":
                return element.Children.FirstOrDefault(c => c.LocalName == "title") is { } title ? DomFacts.TextOf(title) : string.Empty;
            case "iframe":
            case "frame":
                return element.GetAttribute("title") ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    private static string LabelsText(IElement element, DomScope scope, HashSet<IElement> visited) =>
        string.Join(' ', GetLabels(element, scope).Select(label => ContentText(label, scope, visited)).Where(t => !string.IsNullOrWhiteSpace(t)));

    private static string ContentText(IElement element, DomScope scope, HashSet<IElement> visited)
    {
        var builder = new StringBuilder();
        foreach (var child in element.ChildNodes)
        {
            if (child.NodeType == NodeType.Text)
            {
                builder.Append(child.TextContent);
            }
            else if (child is IElement childElement)
            {
                if (DomFacts.IsExcludedFromAccessibilityTree(childElement, scope))
                {
                    continue;
                }

                var text = Compute(childElement, scope, visited, inLabelledByTraversal: false, inContentTraversal: true);
                var block = childElement.LocalName is "div" or "p" or "li" or "br" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
                if (block)
                {
                    builder.Append(' ');
                }

                builder.Append(text);
                if (block)
                {
                    builder.Append(' ');
                }
            }
        }

        return builder.ToString();
    }

    private static string ControlValue(IElement element)
    {
        if (element.LocalName == "select")
        {
            var selected = element.QuerySelectorAll("option").FirstOrDefault(o => o.HasAttribute("selected")) ?? element.QuerySelector("option");
            return selected is null ? string.Empty : DomFacts.TextOf(selected);
        }

        return element.GetAttribute("value") ?? element.GetAttribute("aria-valuetext") ?? element.GetAttribute("aria-valuenow") ?? element.TextContent;
    }
}
