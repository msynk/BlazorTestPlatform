using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorTestPlatform.Accessibility.Dom;

/// <summary>
/// The device a browser-free driver pretends to be, so CSS media queries can be evaluated without a browser:
/// viewport size, color scheme, motion preference and input capabilities. Mirrors what browser runs emulate.
/// </summary>
public sealed record MediaEnvironment
{
    /// <summary>Viewport width in CSS pixels.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>Viewport height in CSS pixels.</summary>
    public int Height { get; init; } = 800;

    /// <summary>Preferred color scheme (<c>light</c> or <c>dark</c>).</summary>
    public string ColorScheme { get; init; } = "light";

    /// <summary><c>prefers-reduced-motion: reduce</c> (browser runs also emulate reduced motion). Default true.</summary>
    public bool ReducedMotion { get; init; } = true;

    /// <summary>Primary input can hover (desktop).</summary>
    public bool Hover { get; init; } = true;

    /// <summary>Primary pointer is fine (mouse) rather than coarse (touch).</summary>
    public bool FinePointer { get; init; } = true;

    /// <summary>Root font size used for <c>em</c>/<c>rem</c> in media queries. Default 16.</summary>
    public double RootFontSize { get; init; } = 16;

    /// <summary>Desktop defaults.</summary>
    public static MediaEnvironment Desktop { get; } = new();

    /// <summary>Builds an environment from a viewport and theme.</summary>
    public static MediaEnvironment For(Viewport viewport, ThemeDefinition? theme = null)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        var touch = viewport.HasTouch && viewport.IsMobile;
        return new MediaEnvironment
        {
            Width = viewport.Width,
            Height = viewport.Height,
            ColorScheme = theme?.ColorScheme == BlazorTestPlatform.ColorScheme.Dark ? "dark" : "light",
            Hover = !touch,
            FinePointer = !touch,
        };
    }

    /// <summary>True when a media query list (e.g. <c>screen and (min-width: 600px), print</c>) matches.</summary>
    public bool Matches(string? mediaText)
    {
        if (string.IsNullOrWhiteSpace(mediaText))
        {
            return true;
        }

        return SplitTopLevel(mediaText).Any(MatchesQuery);
    }

    private bool MatchesQuery(string query)
    {
        var text = query.Trim().ToLowerInvariant();
        var negate = false;
        if (text.StartsWith("not ", StringComparison.Ordinal))
        {
            negate = true;
            text = text[4..].Trim();
        }
        else if (text.StartsWith("only ", StringComparison.Ordinal))
        {
            text = text[5..].Trim();
        }

        var result = true;
        var typeMatch = Regex.Match(text, "^([a-z-]+)(\\s+and\\s+|$)", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (typeMatch.Success && !text.StartsWith('('))
        {
            result = typeMatch.Groups[1].Value is "all" or "screen";
            text = text[typeMatch.Length..];
        }

        foreach (Match feature in Regex.Matches(text, "\\(([^()]*)\\)", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            result &= MatchesFeature(feature.Groups[1].Value.Trim());
        }

        return negate ? !result : result;
    }

    private bool MatchesFeature(string feature)
    {
        // Media Queries 4 range syntax: (width >= 600px), (600px <= width < 900px)
        var range = Regex.Match(feature, "^(?:([^<>=]+?)\\s*(<=|<|>=|>|=)\\s*)?([a-z-]+)\\s*(<=|<|>=|>|=)\\s*([^<>=]+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (range.Success && !feature.Contains(':', StringComparison.Ordinal))
        {
            var actual = Dimension(range.Groups[3].Value);
            if (actual is null)
            {
                return false;
            }

            var ok = Compare(actual.Value, range.Groups[4].Value, range.Groups[5].Value.Trim());
            if (range.Groups[1].Success && range.Groups[1].Value.Length > 0)
            {
                // "600px <= width": the bound is on the left, so compare it to the actual value as written.
                ok &= Length(range.Groups[1].Value.Trim()) is { } bound && Compare(bound, range.Groups[2].Value, actual.Value);
            }

            return ok;
        }

        // One-sided range with the bound first: (600px <= width)
        var reversed = Regex.Match(feature, "^([^<>=]+?)\\s*(<=|<|>=|>|=)\\s*([a-z-]+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (reversed.Success && !feature.Contains(':', StringComparison.Ordinal))
        {
            return Dimension(reversed.Groups[3].Value) is { } actualValue
                && Length(reversed.Groups[1].Value.Trim()) is { } bound
                && Compare(bound, reversed.Groups[2].Value, actualValue);
        }

        var colon = feature.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return feature switch
            {
                "hover" or "any-hover" => Hover,
                "pointer" or "any-pointer" => true,
                "color" => true,
                "monochrome" or "grid" => false,
                _ => false,
            };
        }

        var name = feature[..colon].Trim();
        var value = feature[(colon + 1)..].Trim();
        switch (name)
        {
            case "width" or "height":
                return Length(value) is { } exact && Math.Abs(Dimension(name)!.Value - exact) < 0.01;
            case "min-width" or "min-height":
                return Length(value) is { } min && Dimension(name[4..])!.Value >= min;
            case "max-width" or "max-height":
                return Length(value) is { } max && Dimension(name[4..])!.Value <= max;
            case "orientation":
                return value == (Height >= Width ? "portrait" : "landscape");
            case "prefers-color-scheme":
                return value == ColorScheme;
            case "prefers-reduced-motion":
                return value == (ReducedMotion ? "reduce" : "no-preference");
            case "hover" or "any-hover":
                return value == (Hover ? "hover" : "none");
            case "pointer" or "any-pointer":
                return value == (FinePointer ? "fine" : "coarse");
            case "prefers-contrast":
                return value == "no-preference";
            case "forced-colors":
                return value == "none";
            case "min-resolution":
                return value is "1dppx" or "1x" or "96dpi";
            case "max-resolution":
                return true;
            default:
                return false;
        }
    }

    private double? Dimension(string name) => name switch
    {
        "width" => Width,
        "height" => Height,
        _ => null,
    };

    private double? Length(string value)
    {
        var match = Regex.Match(value, "^(-?[0-9]*\\.?[0-9]+)\\s*(px|em|rem)?$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            return null;
        }

        var number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value is "em" or "rem" ? number * RootFontSize : number;
    }

    private bool Compare(double actual, string op, string expected) => Length(expected) is { } value && Compare(actual, op, value);

    private static bool Compare(double left, string op, double right) => op switch
    {
        "<" => left < right,
        "<=" => left <= right,
        ">" => left > right,
        ">=" => left >= right,
        _ => Math.Abs(left - right) < 0.01,
    };

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return text[start..];
    }
}
