using System.Text.Json;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Accessibility;

/// <summary>WCAG conformance level.</summary>
public enum WcagLevel
{
    /// <summary>Level A.</summary>
    A,

    /// <summary>Level AA.</summary>
    AA,

    /// <summary>Level AAA.</summary>
    AAA,
}

/// <summary>A check that automated tools cannot decide and a person must verify.</summary>
/// <param name="Id">Stable id used in review files.</param>
/// <param name="Criterion">WCAG success criterion, e.g. <c>2.1.2</c>.</param>
/// <param name="Level">Conformance level.</param>
/// <param name="Title">Short title.</param>
/// <param name="Guidance">How to verify it.</param>
public sealed record ManualCheck(string Id, string Criterion, WcagLevel Level, string Title, string Guidance);

/// <summary>Built-in manual checklists.</summary>
public static class ManualChecklists
{
    /// <summary>WCAG 2.2 A/AA criteria that need human judgement for interactive components.</summary>
    public static IReadOnlyList<ManualCheck> Wcag22AA { get; } =
    [
        new("keyboard", "2.1.1", WcagLevel.A, "Keyboard operable", "Every action available with a pointer can be done with the keyboard alone (Tab, Shift+Tab, Enter, Space, arrows, Escape)."),
        new("no-keyboard-trap", "2.1.2", WcagLevel.A, "No keyboard trap", "Focus can always leave the component with standard keys; modal dialogs return focus to the trigger when closed."),
        new("focus-order", "2.4.3", WcagLevel.A, "Meaningful focus order", "Tab order follows the visual and logical reading order; focus moves into opened popups/dialogs."),
        new("focus-visible", "2.4.7", WcagLevel.AA, "Focus visible", "The focused element always has a clearly visible indicator in every theme."),
        new("focus-not-obscured", "2.4.11", WcagLevel.AA, "Focus not obscured", "The focused element is not hidden by sticky headers, toasts or overlays."),
        new("meaningful-sequence", "1.3.2", WcagLevel.A, "Meaningful sequence", "Reading order in the DOM matches the visual order (check with a screen reader's browse mode)."),
        new("sensory-characteristics", "1.3.3", WcagLevel.A, "No sensory-only instructions", "Instructions do not rely solely on shape, color, size, location or sound."),
        new("use-of-color", "1.4.1", WcagLevel.A, "Color not the only cue", "State (error, selected, required) is conveyed by more than color alone."),
        new("non-text-contrast", "1.4.11", WcagLevel.AA, "Non-text contrast", "Borders, focus rings, icons and control states have at least 3:1 contrast against adjacent colors."),
        new("reflow", "1.4.10", WcagLevel.AA, "Reflow", "At 320 CSS px wide (400% zoom) content reflows without two-dimensional scrolling or loss of content."),
        new("text-spacing", "1.4.12", WcagLevel.AA, "Text spacing", "Increased line/letter/word/paragraph spacing does not clip or overlap text."),
        new("content-on-hover-focus", "1.4.13", WcagLevel.AA, "Content on hover or focus", "Tooltips/popovers are dismissible (Escape), hoverable and persistent until dismissed."),
        new("pointer-cancellation", "2.5.2", WcagLevel.A, "Pointer cancellation", "Actions fire on pointer up and can be aborted by moving away before release."),
        new("label-in-name", "2.5.3", WcagLevel.A, "Label in name", "The accessible name contains the visible label text (voice-control users say what they see)."),
        new("dragging", "2.5.7", WcagLevel.AA, "Dragging alternatives", "Any drag operation has a single-pointer alternative (buttons, keyboard)."),
        new("target-size", "2.5.8", WcagLevel.AA, "Target size", "Interactive targets are at least 24x24 CSS px or have sufficient spacing."),
        new("on-focus-input", "3.2.1", WcagLevel.A, "No unexpected context change", "Focusing or changing a control does not unexpectedly navigate, submit or move focus."),
        new("error-identification", "3.3.1", WcagLevel.A, "Errors identified", "Errors are described in text, associated with the field (aria-describedby/aria-invalid) and announced."),
        new("status-messages", "4.1.3", WcagLevel.AA, "Status messages announced", "Loading, success and result-count messages are announced by screen readers without moving focus (live regions)."),
        new("screen-reader", "4.1.2", WcagLevel.A, "Screen reader name/role/state", "With NVDA/JAWS/VoiceOver each control announces a correct name, role, state and value changes."),
    ];
}

/// <summary>Status of a manual check.</summary>
public enum ReviewStatus
{
    /// <summary>Not reviewed yet.</summary>
    NotReviewed,

    /// <summary>Passes.</summary>
    Pass,

    /// <summary>Fails (must be fixed or tracked).</summary>
    Fail,

    /// <summary>Does not apply to this component.</summary>
    NotApplicable,
}

/// <summary>Result of one manual check.</summary>
public sealed record ReviewItem
{
    /// <summary>The <see cref="ManualCheck.Id"/>.</summary>
    public required string CheckId { get; init; }

    /// <summary>Status.</summary>
    public ReviewStatus Status { get; init; }

    /// <summary>Notes, evidence or ticket links.</summary>
    public string? Notes { get; init; }

    /// <summary>The check's title, copied into templates for the reviewer (informational).</summary>
    public string? Title { get; init; }

    /// <summary>The WCAG success criterion, copied into templates for the reviewer (informational).</summary>
    public string? Criterion { get; init; }

    /// <summary>How to verify the check, copied into templates for the reviewer (informational).</summary>
    public string? Guidance { get; init; }
}

/// <summary>
/// A committed, per-component manual accessibility review. The <see cref="Fingerprint"/> captures the component's
/// accessibility-relevant structure at review time; when the structure changes, the review becomes stale and CI
/// asks for a re-review — so manual audits stay connected to the code they audited.
/// </summary>
public sealed record ComponentReview
{
    /// <summary>Story title of the component (e.g. <c>Inputs/Button</c>).</summary>
    public required string Component { get; init; }

    /// <summary>Accessibility fingerprint of the reviewed stories.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Who reviewed.</summary>
    public string? Reviewer { get; init; }

    /// <summary>When.</summary>
    public DateTimeOffset? ReviewedAt { get; init; }

    /// <summary>Assistive technology and browsers used, e.g. <c>NVDA 2025.1 + Firefox</c>.</summary>
    public string? AssistiveTechnology { get; init; }

    /// <summary>Per-check results.</summary>
    public IReadOnlyList<ReviewItem> Items { get; init; } = [];

    /// <summary>General notes.</summary>
    public string? Notes { get; init; }
}

/// <summary>Reads and writes <see cref="ComponentReview"/> files (<c>{slug}.a11y-review.json</c>).</summary>
public sealed class ManualReviewStore
{
    /// <summary>File-name suffix.</summary>
    public const string FileSuffix = ".a11y-review.json";

    /// <summary>Creates a store in a directory.</summary>
    public ManualReviewStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    /// <summary>The review directory.</summary>
    public string Directory { get; }

    /// <summary>Path of a component's review file.</summary>
    public string PathFor(string component) => System.IO.Path.Combine(Directory, StoryId.Slugify(component) + FileSuffix);

    /// <summary>Loads a review or null.</summary>
    public ComponentReview? Load(string component)
    {
        var path = PathFor(component);
        return File.Exists(path) ? BtpJson.Deserialize<ComponentReview>(File.ReadAllText(path)) : null;
    }

    /// <summary>Loads every review.</summary>
    public IReadOnlyList<ComponentReview> LoadAll() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*" + FileSuffix)
                .Select(f => BtpJson.Deserialize<ComponentReview>(File.ReadAllText(f))!)
                .Where(r => r is not null)
                .ToArray()
            : [];

    /// <summary>Saves a review.</summary>
    public string Save(ComponentReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(review.Component);
        File.WriteAllText(path, BtpJson.Serialize(review));
        return path;
    }

    /// <summary>Creates a template with every check set to <see cref="ReviewStatus.NotReviewed"/>.</summary>
    public static ComponentReview CreateTemplate(string component, string? fingerprint, IEnumerable<ManualCheck>? checklist = null) => new()
    {
        Component = component,
        Fingerprint = fingerprint,
        Items = (checklist ?? ManualChecklists.Wcag22AA)
            .Select(c => new ReviewItem { CheckId = c.Id, Status = ReviewStatus.NotReviewed, Title = c.Title, Criterion = $"{c.Criterion} ({c.Level})", Guidance = c.Guidance })
            .ToArray(),
    };
}

/// <summary>
/// The outcome of the last manual-review gate run (<c>{results}/a11y-review-gate.json</c>), written by the gate test so
/// tools that cannot compute fingerprints themselves (<c>btp a11y status</c>) can still show stale and missing reviews.
/// </summary>
public sealed record ReviewGateReport
{
    /// <summary>File name inside the results directory.</summary>
    public const string FileName = "a11y-review-gate.json";

    /// <summary>When the gate ran.</summary>
    public DateTimeOffset EvaluatedAt { get; init; }

    /// <summary>One entry per component.</summary>
    public IReadOnlyList<ReviewGateEntry> Entries { get; init; } = [];

    /// <summary>Writes the report into a results directory and returns its path.</summary>
    public string Save(string resultsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        System.IO.Directory.CreateDirectory(resultsDirectory);
        var path = Path.Combine(resultsDirectory, FileName);
        File.WriteAllText(path, BtpJson.Serialize(this));
        return path;
    }

    /// <summary>Loads the report of a results directory, or null when the gate has not run there.</summary>
    public static ReviewGateReport? Load(string resultsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        var path = Path.Combine(resultsDirectory, FileName);
        return File.Exists(path) ? BtpJson.Deserialize<ReviewGateReport>(File.ReadAllText(path)) : null;
    }
}

/// <summary>State of one component in the manual-review gate.</summary>
public enum ReviewGateState
{
    /// <summary>Reviewed, current and passing.</summary>
    Approved,

    /// <summary>No review file.</summary>
    Missing,

    /// <summary>Structure changed since the review.</summary>
    Stale,

    /// <summary>Some checks not reviewed yet.</summary>
    Incomplete,

    /// <summary>Some checks failed.</summary>
    Failed,
}

/// <summary>Gate result for one component.</summary>
/// <param name="Component">Component title.</param>
/// <param name="State">State.</param>
/// <param name="Detail">Explanation.</param>
/// <param name="CurrentFingerprint">Fingerprint computed now.</param>
public sealed record ReviewGateEntry(string Component, ReviewGateState State, string Detail, string? CurrentFingerprint);

/// <summary>Options of the manual-review gate.</summary>
public sealed class ManualReviewGateOptions
{
    /// <summary>Fail when a component has no review. Default false (report only) so adoption can be gradual.</summary>
    public bool RequireReview { get; set; }

    /// <summary>Fail when a review is stale. Default true.</summary>
    public bool FailOnStale { get; set; } = true;

    /// <summary>Fail when a review has failed checks. Default true.</summary>
    public bool FailOnFailedChecks { get; set; } = true;

    /// <summary>Fail when a review has unreviewed checks. Default false.</summary>
    public bool FailOnIncomplete { get; set; }
}

/// <summary>Evaluates committed manual reviews against current component fingerprints.</summary>
public static class ManualReviewGate
{
    /// <summary>Evaluates every component.</summary>
    /// <param name="fingerprints">Current fingerprint per component title.</param>
    /// <param name="store">Review store.</param>
    public static IReadOnlyList<ReviewGateEntry> Evaluate(IReadOnlyDictionary<string, string> fingerprints, ManualReviewStore store)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(store);
        var entries = new List<ReviewGateEntry>();
        foreach (var (component, fingerprint) in fingerprints.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var review = store.Load(component);
            if (review is null)
            {
                entries.Add(new(component, ReviewGateState.Missing, $"No manual review. Create {Path.GetFileName(store.PathFor(component))}.", fingerprint));
                continue;
            }

            if (review.Fingerprint is not null && !string.Equals(review.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                entries.Add(new(component, ReviewGateState.Stale, $"Accessibility structure changed since the review on {review.ReviewedAt:yyyy-MM-dd} ({review.Fingerprint} -> {fingerprint}).", fingerprint));
                continue;
            }

            var failed = review.Items.Where(i => i.Status == ReviewStatus.Fail).ToArray();
            if (failed.Length > 0)
            {
                entries.Add(new(component, ReviewGateState.Failed, "Failed checks: " + string.Join(", ", failed.Select(f => f.CheckId)), fingerprint));
                continue;
            }

            var pending = review.Items.Where(i => i.Status == ReviewStatus.NotReviewed).ToArray();
            entries.Add(pending.Length > 0
                ? new(component, ReviewGateState.Incomplete, "Not reviewed: " + string.Join(", ", pending.Select(p => p.CheckId)), fingerprint)
                : new(component, ReviewGateState.Approved, $"Reviewed by {review.Reviewer ?? "unknown"} on {review.ReviewedAt:yyyy-MM-dd}.", fingerprint));
        }

        return entries;
    }

    /// <summary>Throws when any entry fails under the options.</summary>
    public static void ThrowIfFailing(IReadOnlyList<ReviewGateEntry> entries, ManualReviewGateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        options ??= new ManualReviewGateOptions();
        var failing = entries.Where(e => e.State switch
        {
            ReviewGateState.Missing => options.RequireReview,
            ReviewGateState.Stale => options.FailOnStale,
            ReviewGateState.Failed => options.FailOnFailedChecks,
            ReviewGateState.Incomplete => options.FailOnIncomplete,
            _ => false,
        }).ToArray();

        if (failing.Length > 0)
        {
            throw new StoryAssertionException("Manual accessibility review gate failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, failing.Select(f => $"  [{f.State}] {f.Component}: {f.Detail}")));
        }
    }
}
