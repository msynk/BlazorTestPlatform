using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>What kind of failure a result is, by the evidence it carries.</summary>
public enum FailureCategory
{
    /// <summary>Not a failure.</summary>
    None,

    /// <summary>An expectation in the play script did not hold (text, state, action, request, JS call).</summary>
    Assertion,

    /// <summary>A locator matched nothing, or more than one element, so an action could not run.</summary>
    Locator,

    /// <summary>The screenshot differs from the baseline, or no baseline exists.</summary>
    Visual,

    /// <summary>The accessibility gate failed (axe or markup rules above the policy's impact threshold).</summary>
    Accessibility,

    /// <summary>A render budget, render baseline or browser budget was exceeded.</summary>
    Performance,

    /// <summary>The story did not run in the render mode it asked for, or never became interactive.</summary>
    RenderMode,

    /// <summary>The component threw, or failed to render.</summary>
    ComponentCrash,

    /// <summary>The browser reported console or page errors, or showed the Blazor error UI.</summary>
    PageError,

    /// <summary>Something did not finish in time.</summary>
    Timeout,

    /// <summary>The machine, browser or host failed, not the story (network stack, crashed browser, missing host).</summary>
    Infrastructure,

    /// <summary>None of the above.</summary>
    Unknown,
}

/// <summary>A category with what it usually means and where to look first.</summary>
/// <param name="Category">The category.</param>
/// <param name="Label">Short label.</param>
/// <param name="LikelyCause">What usually causes it.</param>
/// <param name="NextStep">What to check first.</param>
/// <param name="IsProductIssue">True when the component (not the test or the machine) is the usual cause.</param>
public sealed record FailureExplanation(FailureCategory Category, string Label, string LikelyCause, string NextStep, bool IsProductIssue);

/// <summary>
/// Deterministic failure triage: a stable signature per error (so the same failure groups across stories, cells and
/// runs) and a category from the evidence on the result. No model, no network: the same input always gives the same
/// answer, which is what makes a grouping reviewable.
/// </summary>
public static partial class FailureAnalysis
{
    private static readonly string[] InfrastructureSignatures =
    [
        "ERR_NO_BUFFER_SPACE", "ERR_INSUFFICIENT_RESOURCES", "ERR_ADDRESS_IN_USE", "ERR_ADDRESS_UNREACHABLE",
        "ERR_CONNECTION_REFUSED", "browser has been closed", "Target page, context or browser has been closed",
        "Browser closed", "did not render within", "Executable doesn't exist", "Is the workbench registered",
    ];

    /// <summary>
    /// The error with the details that change from run to run replaced by placeholders: numbers, durations, GUIDs,
    /// hexadecimal ids, quoted values, file paths and line numbers. Only the first meaningful lines are kept.
    /// </summary>
    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var lines = message.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("at ", StringComparison.Ordinal) && !l.StartsWith("---", StringComparison.Ordinal))
            .Take(2);
        var text = string.Join(" ", lines);
        text = Guid().Replace(text, "<guid>");
        text = Path().Replace(text, "<path>");
        text = Url().Replace(text, "<url>");
        text = Quoted().Replace(text, "\"…\"");
        text = Hex().Replace(text, "<hex>");
        text = Number().Replace(text, "#");
        text = Spaces().Replace(text, " ").Trim();
        return text.Length > 240 ? text[..240] : text;
    }

    /// <summary>Stable 12-character signature of a failure: equal for the same error in any story, cell or run.</summary>
    public static string Signature(string? errorType, string? message) =>
        DeterministicRandom.StableHash($"{ShortType(errorType)}|{Normalize(message)}").ToString("x16", CultureInfo.InvariantCulture)[..12];

    /// <summary>The signature of a failed result, or null when it did not fail.</summary>
    public static string? Signature(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome == StoryOutcome.Failed ? Signature(result.ErrorType, result.Error) : null;
    }

    /// <summary>Categorizes a result by its evidence (visual status, accessibility gate, budgets, message).</summary>
    public static FailureCategory Categorize(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome != StoryOutcome.Failed)
        {
            return FailureCategory.None;
        }

        return Categorize(
            result.ErrorType,
            string.Join(Environment.NewLine, new[] { result.Error }.Concat(result.Console.Select(c => c.Text))),
            visualFailed: result.Visual?.IsFailure == true,
            accessibilityFailed: result.AccessibilityFailures.Count > 0,
            budgetFailed: result.BudgetViolations.Count > 0 || result.PerformanceComparison?.HasFailures == true);
    }

    /// <summary>Categorizes a failure from its parts.</summary>
    public static FailureCategory Categorize(string? errorType, string? message, bool visualFailed = false, bool accessibilityFailed = false, bool budgetFailed = false)
    {
        var text = message ?? string.Empty;
        var type = ShortType(errorType);
        bool Has(string fragment) => text.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        if (InfrastructureSignatures.Any(Has))
        {
            return FailureCategory.Infrastructure;
        }

        if (type is "AccessibilityViolationException" || (accessibilityFailed && !visualFailed && !Has("Expected")))
        {
            return FailureCategory.Accessibility;
        }

        if (visualFailed && (Has("snapshot") || Has("baseline") || Has("visual") || text.Length == 0))
        {
            return FailureCategory.Visual;
        }

        if (budgetFailed || Has("performance regression") || Has("performance budget"))
        {
            return FailureCategory.Performance;
        }

        if (Has("but rendered with") || Has("failed to render under") || Has("hydrat") || Has("never became interactive"))
        {
            return FailureCategory.RenderMode;
        }

        if (Has("threw an unhandled exception") || Has("failed to render:") || Has("Unhandled exception rendering component"))
        {
            return FailureCategory.ComponentCrash;
        }

        if (Has("Blazor error UI") || Has("console/page error"))
        {
            return FailureCategory.PageError;
        }

        if (type is "ElementNotActionableException" || Has("strict mode violation") || Has("matches no element") || Has("Cannot click") || Has("Cannot fill"))
        {
            return FailureCategory.Locator;
        }

        if (type is "TimeoutException" or "OperationCanceledException" or "TaskCanceledException" || Has("timed out") || Has("Timeout ") || Has("exceeded."))
        {
            return Has("Expected") ? FailureCategory.Assertion : FailureCategory.Timeout;
        }

        if (visualFailed)
        {
            return FailureCategory.Visual;
        }

        if (accessibilityFailed)
        {
            return FailureCategory.Accessibility;
        }

        return Has("Expected") || type is "StoryAssertionException" or "XunitException" or "AssertFailedException"
            ? FailureCategory.Assertion
            : FailureCategory.Unknown;
    }

    /// <summary>What a category usually means and what to check first.</summary>
    public static FailureExplanation Explain(FailureCategory category) => category switch
    {
        FailureCategory.Assertion => new(category, "Assertion", "The component rendered or behaved differently from what the play script expects.", "Compare the expected and observed values in the message, then replay the steps in the workbench (Run interactions).", true),
        FailureCategory.Locator => new(category, "Locator", "An element was renamed, removed or duplicated, so a role/label/text query no longer finds exactly one.", "Open the story's accessibility tree in the inspector and check the element's role and accessible name.", true),
        FailureCategory.Visual => new(category, "Visual change", "The rendered pixels changed: styles, fonts, content, or the browser build that took the baseline.", "Review the diff; accept it if the change is intended, otherwise find the style change.", true),
        FailureCategory.Accessibility => new(category, "Accessibility", "Markup lost a name, role or relationship, or a color or size fell below WCAG thresholds.", "Read the violated rule and its elements; fix the markup or waive the rule with a justification.", true),
        FailureCategory.Performance => new(category, "Performance", "The component renders more often, allocates more or lays out worse than its budget or baseline allows.", "Compare render counts per component with the baseline; look for StateHasChanged or parameter churn.", true),
        FailureCategory.RenderMode => new(category, "Render mode", "The story did not hydrate or ran in another render mode: missing client services, non-serializable state, or prerendering a first render that never completes.", "Open the story under the failing mode in the workbench; check the client's service registrations and Prerender(false).", true),
        FailureCategory.ComponentCrash => new(category, "Component crash", "The component threw while rendering or after an await.", "Read the exception and stack trace; the story's services or arguments may not satisfy the component.", true),
        FailureCategory.PageError => new(category, "Page error", "The browser logged errors or showed the Blazor error UI: failed JS interop, a missing static asset, an exception in the circuit.", "Read the console log of the run; reproduce in the workbench with the browser's developer tools open.", true),
        FailureCategory.Timeout => new(category, "Timeout", "Something never finished: a pending request, a dialog left open, an endless loop, or a slow machine.", "Look at the last step of the timeline; mock pending work, or raise the story's Timeout only if the work is genuinely slow.", false),
        FailureCategory.Infrastructure => new(category, "Infrastructure", "The machine, browser or host failed, not the story.", "Rerun. If it repeats, check the CI image, installed browsers and that the workbench host started.", false),
        FailureCategory.Unknown => new(category, "Unclassified", "The failure did not match a known pattern.", "Read the message and stack trace.", false),
        _ => new(category, "Passed", "Not a failure.", string.Empty, false),
    };

    /// <summary>
    /// A self-contained description of a failure for an assistant or an issue: story, cell, category, error, the last
    /// steps and where the story is declared. Built from the result alone; nothing is sent anywhere.
    /// </summary>
    public static string ToPrompt(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var category = Categorize(result);
        var explanation = Explain(category);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"A BlazorTestPlatform story failed. Help me find the cause and a fix.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Story: {result.Title} / {result.Name} ({result.StoryId})");
        text.AppendLine(CultureInfo.InvariantCulture, $"Component: {result.Component}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Run: driver {result.Driver}, render mode {result.HostingModel ?? "in-memory"}, viewport {result.Viewport ?? "-"}, theme {result.Theme ?? "-"}, {result.Attempts} attempt(s)");
        if (result.SourceFile is { } file)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Declared in: {file}{(result.SourceLine is { } line ? $":{line}" : string.Empty)}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"Category (deterministic): {explanation.Label}: {explanation.LikelyCause}");
        text.AppendLine();
        text.AppendLine("Error:");
        text.AppendLine(result.Error ?? "(no message)");
        var steps = Flatten(result.Steps).TakeLast(12).ToArray();
        if (steps.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Last steps:");
            foreach (var (step, depth) in steps)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"{new string(' ', depth * 2)}{(step.Failed ? "✗" : "✓")} {step.Title}");
            }
        }

        if (result.Console.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Console:");
            foreach (var entry in result.Console.Take(10))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"[{entry.Level}] {entry.Text}");
            }
        }

        return text.ToString();
    }

    private static IEnumerable<(StepRecord Step, int Depth)> Flatten(IEnumerable<StepRecord> steps, int depth = 0)
    {
        foreach (var step in steps)
        {
            yield return (step, depth);
            foreach (var child in Flatten(step.Children, depth + 1))
            {
                yield return child;
            }
        }
    }

    private static string ShortType(string? errorType) =>
        string.IsNullOrEmpty(errorType) ? string.Empty : errorType[(errorType.LastIndexOf('.') + 1)..];

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex Guid();

    [GeneratedRegex(@"([A-Za-z]:)?([\\/][\w.\-]+){2,}(:\d+)?")]
    private static partial Regex Path();

    [GeneratedRegex(@"\bhttps?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex("\"[^\"]{24,}\"")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\b0x[0-9a-fA-F]+\b|\b[0-9a-f]{12,}\b")]
    private static partial Regex Hex();

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
