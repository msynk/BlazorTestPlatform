using System.Globalization;
using System.Text;

namespace BlazorTestPlatform;

/// <summary>
/// Creates stable, URL-safe story identifiers such as <c>inputs-button--primary</c>.
/// Identifiers are used in workbench URLs, snapshot file names, baselines and test names,
/// so the algorithm must never change between versions.
/// </summary>
public static class StoryId
{
    /// <summary>Separator between the slugged title and the slugged story name.</summary>
    public const string Separator = "--";

    /// <summary>Creates an identifier from a story title (e.g. <c>Inputs/Button</c>) and name.</summary>
    public static string Create(string title, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Slugify(title) + Separator + Slugify(name);
    }

    /// <summary>
    /// Lower-cases, strips diacritics and collapses every run of non-alphanumeric characters into a single dash.
    /// </summary>
    public static string Slugify(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        var pendingDash = false;

        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(ch));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.Length == 0 ? "story" : builder.ToString();
    }
}
