using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace BlazorTestPlatform.Accessibility.Dom;

/// <summary>
/// A set of root nodes (e.g. a bUnit render output or a parsed fragment) treated as one document:
/// element enumeration in document order and id lookups scoped to the roots.
/// </summary>
public sealed class DomScope
{
    private Dictionary<string, List<IElement>>? _ids;

    /// <summary>Creates a scope over root nodes.</summary>
    public DomScope(IEnumerable<INode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = roots.ToArray();
    }

    /// <summary>Root nodes.</summary>
    public IReadOnlyList<INode> Roots { get; }

    /// <summary>Optional stylesheet cascade used for visibility (null: inline styles and attributes only).</summary>
    public CssVisibility? Styles { get; init; }

    /// <summary>Parses an HTML fragment into a scope.</summary>
    public static DomScope Parse(string markup, CssVisibility? styles = null)
    {
        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><head></head><body>" + (markup ?? string.Empty) + "</body></html>");
        return new DomScope(document.Body!.ChildNodes) { Styles = styles };
    }

    /// <summary>All elements in document order.</summary>
    public IEnumerable<IElement> Elements()
    {
        foreach (var root in Roots)
        {
            if (root is IElement element)
            {
                yield return element;
                foreach (var descendant in element.QuerySelectorAll("*"))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>Elements with the given id.</summary>
    public IReadOnlyList<IElement> ElementsWithId(string id)
    {
        _ids ??= BuildIdIndex();
        return _ids.TryGetValue(id, out var list) ? list : [];
    }

    /// <summary>First element with the given id.</summary>
    public IElement? GetElementById(string id) => ElementsWithId(id).FirstOrDefault();

    /// <summary>Elements matching a CSS selector (roots included).</summary>
    public IEnumerable<IElement> QuerySelectorAll(string selector)
    {
        foreach (var root in Roots)
        {
            if (root is IElement element)
            {
                if (element.Matches(selector))
                {
                    yield return element;
                }

                foreach (var match in element.QuerySelectorAll(selector))
                {
                    yield return match;
                }
            }
        }
    }

    /// <summary>All id references in an attribute such as <c>aria-labelledby</c>.</summary>
    public IReadOnlyList<IElement> ResolveIdRefs(string? idRefs) =>
        string.IsNullOrWhiteSpace(idRefs)
            ? []
            : idRefs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(GetElementById).OfType<IElement>().ToArray();

    private Dictionary<string, List<IElement>> BuildIdIndex()
    {
        var index = new Dictionary<string, List<IElement>>(StringComparer.Ordinal);
        foreach (var element in Elements())
        {
            var id = element.GetAttribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                if (!index.TryGetValue(id, out var list))
                {
                    index[id] = list = [];
                }

                list.Add(element);
            }
        }

        return index;
    }
}
