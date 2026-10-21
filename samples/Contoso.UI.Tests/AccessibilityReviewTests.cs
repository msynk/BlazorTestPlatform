using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Bunit;

namespace Contoso.UI.Tests;

/// <summary>
/// Manual accessibility reviews are committed next to the code (<c>a11y-reviews/*.a11y-review.json</c>). The gate fails
/// when a reviewed component's accessibility structure changed since its review (stale) or a check failed. Missing
/// reviews are reported with a ready-to-fill template under <c>TestResults/btp/a11y-review-templates</c>.
/// </summary>
public sealed class AccessibilityReviewTests
{
    [Fact]
    public async Task Manual_reviews_are_current()
    {
        var entries = await AccessibilityReviews.VerifyAsync(
            ComponentStoryTests.Catalog,
            new ManualReviewGateOptions { RequireReview = false, FailOnStale = true, FailOnFailedChecks = true },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(entries);
        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, entries.Select(e => $"{e.State,-10} {e.Component}: {e.Detail}")));
    }
}
