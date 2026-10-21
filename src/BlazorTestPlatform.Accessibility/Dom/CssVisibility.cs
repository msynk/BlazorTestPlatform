using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using System.Text.RegularExpressions;

namespace BlazorTestPlatform.Accessibility.Dom;

/// <summary>
/// A minimal cascade for the two properties that decide visibility without layout: <c>display</c> and
/// <c>visibility</c>. Lets browser-free drivers honour stylesheets (including Blazor CSS-isolation bundles), so
/// a component hidden by a class is hidden in bUnit too. Specificity, <c>!important</c>, source order, inline styles
/// and <c>visibility</c> inheritance are applied. <c>@media</c> blocks are evaluated against a
/// <see cref="MediaEnvironment"/> (viewport size, color scheme, reduced motion, pointer), desktop by default, so a
/// rule like <c>@media (max-width: 600px) { .sidebar { display: none } }</c> hides the sidebar on a mobile viewport only.
/// </summary>
public sealed partial class CssVisibility
{
    private readonly IReadOnlyList<Declaration> _allDisplay;
    private readonly IReadOnlyList<Declaration> _allVisibility;
    private readonly Declaration[] _display;
    private readonly Declaration[] _visibility;

    private CssVisibility(IReadOnlyList<Declaration> display, IReadOnlyList<Declaration> visibility, MediaEnvironment environment)
    {
        _allDisplay = display;
        _allVisibility = visibility;
        Environment = environment;
        _display = display.Where(d => d.AppliesTo(environment)).ToArray();
        _visibility = visibility.Where(d => d.AppliesTo(environment)).ToArray();
    }

    /// <summary>The device media queries are evaluated against.</summary>
    public MediaEnvironment Environment { get; }

    /// <summary>Number of rules that set <c>display</c> or <c>visibility</c> (all media).</summary>
    public int RuleCount => _allDisplay.Count + _allVisibility.Count;

    /// <summary>Number of those rules that apply in <see cref="Environment"/>.</summary>
    public int ActiveRuleCount => _display.Length + _visibility.Length;

    /// <summary>Parses stylesheets (CSS text), evaluating <c>@media</c> blocks against <paramref name="environment"/> (desktop when null).</summary>
    public static CssVisibility Parse(IEnumerable<string> stylesheets, MediaEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(stylesheets);
        var display = new List<Declaration>();
        var visibility = new List<Declaration>();
        var parser = new CssParser();
        var order = 0;
        foreach (var css in stylesheets)
        {
            if (string.IsNullOrWhiteSpace(css))
            {
                continue;
            }

            var sheet = parser.ParseStyleSheet(ExpandRangeQueries(css));
            Collect(sheet.Rules, [], display, visibility, ref order);
        }

        return new CssVisibility(display, visibility, environment ?? MediaEnvironment.Desktop);
    }

    /// <summary>Parses stylesheets for the desktop environment.</summary>
    public static CssVisibility Parse(params string[] stylesheets) => Parse((IEnumerable<string>)stylesheets);

    /// <summary>The same stylesheets evaluated for another device (parsing is not repeated).</summary>
    public CssVisibility For(MediaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment == Environment ? this : new CssVisibility(_allDisplay, _allVisibility, environment);
    }

    /// <summary>True when the element is hidden by <c>display: none</c> on itself or an ancestor, or by its computed <c>visibility</c>.</summary>
    public bool IsHidden(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (string.Equals(Resolve(current, _display, "display"), "none", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // visibility is inherited: the nearest element that declares it wins.
        for (var current = element; current is not null; current = current.ParentElement)
        {
            var visibility = Resolve(current, _visibility, "visibility");
            if (visibility is not null)
            {
                return visibility.Equals("hidden", StringComparison.OrdinalIgnoreCase) || visibility.Equals("collapse", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string? Resolve(IElement element, Declaration[] declarations, string property)
    {
        Declaration? winner = null;
        foreach (var declaration in declarations)
        {
            if (declaration.Selector.Match(element) && (winner is null || declaration.Beats(winner.Value)))
            {
                winner = declaration;
            }
        }

        var inline = InlineValue(element, property, out var inlineImportant);
        if (inline is not null && (winner is null || !winner.Value.Important || inlineImportant))
        {
            return inline;
        }

        return winner?.Value;
    }

    private static string? InlineValue(IElement element, string property, out bool important)
    {
        important = false;
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        string? value = null;
        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && part[..colon].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                var raw = part[(colon + 1)..].Trim();
                important = raw.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
                value = (important ? raw[..^"!important".Length] : raw).Trim();
            }
        }

        return value;
    }

    /// <summary>
    /// AngleSharp rejects two-sided range queries (<c>(600px &lt;= width &lt; 900px)</c>) as <c>not all</c>, so they are
    /// rewritten into the equivalent pair of one-sided conditions it keeps (<c>(600px &lt;= width) and (width &lt; 900px)</c>).
    /// </summary>
    private static string ExpandRangeQueries(string css) =>
        css.Contains("@media", StringComparison.OrdinalIgnoreCase)
            ? MediaPrelude().Replace(css, prelude => TwoSidedRange().Replace(prelude.Value, m => $"({m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}) and ({m.Groups[3].Value} {m.Groups[4].Value} {m.Groups[5].Value})"))
            : css;

    [GeneratedRegex("@media[^{;]*", RegexOptions.IgnoreCase)]
    private static partial Regex MediaPrelude();

    [GeneratedRegex(@"\(\s*([^()<>=:]+?)\s*(<=|<|>=|>)\s*([a-z-]+)\s*(<=|<|>=|>)\s*([^()<>=:]+?)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex TwoSidedRange();

    private static void Collect(ICssRuleList rules, string[] media, List<Declaration> display, List<Declaration> visibility, ref int order)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case ICssStyleRule style:
                    Add(style, "display", media, display, ref order);
                    Add(style, "visibility", media, visibility, ref order);
                    break;
                case ICssMediaRule block:
                    Collect(block.Rules, [.. media, block.Media.MediaText], display, visibility, ref order);
                    break;
                case ICssSupportsRule supports:
                    // Feature queries: display/visibility themselves are universally supported, so the block applies.
                    Collect(supports.Rules, media, display, visibility, ref order);
                    break;
            }
        }
    }

    private static void Add(ICssStyleRule rule, string property, string[] media, List<Declaration> target, ref int order)
    {
        var value = rule.Style.GetPropertyValue(property);
        if (string.IsNullOrWhiteSpace(value) || rule.Selector is null)
        {
            return;
        }

        var important = string.Equals(rule.Style.GetPropertyPriority(property), "important", StringComparison.OrdinalIgnoreCase);
        target.Add(new Declaration(rule.Selector, value.Trim(), important, rule.Selector.Specificity, order++, media));
    }

    private readonly record struct Declaration(ISelector Selector, string Value, bool Important, Priority Specificity, int Order, string[] Media)
    {
        public bool AppliesTo(MediaEnvironment environment) => Array.TrueForAll(Media, environment.Matches);

        public bool Beats(Declaration other) =>
            Important != other.Important ? Important
            : Specificity != other.Specificity ? Specificity > other.Specificity
            : Order > other.Order;
    }
}
