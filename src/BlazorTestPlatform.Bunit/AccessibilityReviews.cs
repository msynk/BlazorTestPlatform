using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit;

/// <summary>
/// Connects the manual accessibility review workflow to code: renders every story of each component with bUnit,
/// fingerprints the accessibility-relevant structure and evaluates committed reviews against it.
/// </summary>
public static class AccessibilityReviews
{
    /// <summary>Computes the current fingerprint of every component (story title) in the catalog.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ComputeFingerprintsAsync(StoryCatalog catalog, BunitStoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in catalog.Groups)
        {
            var markups = new List<string>();
            foreach (var story in group.Stories.Where(s => s.SkipReason is null))
            {
                await using var session = await BunitStoryRunner.RenderAsync(story, options, cancellationToken).ConfigureAwait(false);
                await session.Driver.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
                markups.Add(session.Markup);
            }

            fingerprints[group.Title] = AccessibilityFingerprint.Compute(markups);
        }

        return fingerprints;
    }

    /// <summary>
    /// Evaluates committed reviews (<see cref="BtpSettings.ReviewsDirectory"/>) and throws when the gate fails.
    /// Missing reviews get a template written next to the results so a reviewer can fill and commit it.
    /// </summary>
    public static async Task<IReadOnlyList<ReviewGateEntry>> VerifyAsync(StoryCatalog catalog, ManualReviewGateOptions? gate = null, BunitStoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var settings = options?.Settings ?? BtpSettings.Current;
        var fingerprints = await ComputeFingerprintsAsync(catalog, options, cancellationToken).ConfigureAwait(false);
        var store = new ManualReviewStore(settings.ReviewsDirectoryPath);
        var entries = ManualReviewGate.Evaluate(fingerprints, store);

        var templates = new ManualReviewStore(Path.Combine(settings.ResultsDirectoryPath, "a11y-review-templates"));
        foreach (var entry in entries.Where(e => e.State is ReviewGateState.Missing or ReviewGateState.Stale))
        {
            var existing = store.Load(entry.Component);
            templates.Save(existing is null
                ? ManualReviewStore.CreateTemplate(entry.Component, entry.CurrentFingerprint)
                : existing with { Fingerprint = entry.CurrentFingerprint, Reviewer = null, ReviewedAt = null });
        }

        new ReviewGateReport { EvaluatedAt = DateTimeOffset.UtcNow, Entries = entries }.Save(settings.ResultsDirectoryPath);
        ManualReviewGate.ThrowIfFailing(entries, gate);
        return entries;
    }
}
