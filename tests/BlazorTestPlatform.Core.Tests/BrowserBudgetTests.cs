using BlazorTestPlatform.Performance;

namespace BlazorTestPlatform.Core.Tests;

public sealed class BrowserBudgetTests
{
    [Fact]
    public void Exceeded_limits_are_violations()
    {
        var budget = new BrowserBudget
        {
            MaxLayoutShift = 0.1,
            MaxLargestContentfulPaint = TimeSpan.FromSeconds(2.5),
            MaxLongTaskDuration = TimeSpan.FromMilliseconds(100),
            MaxDomNodes = 50,
        };
        var metrics = new BrowserMetrics
        {
            CumulativeLayoutShift = 0.25,
            LargestContentfulPaintMilliseconds = 1200,
            LongTasks = 2,
            LongTaskMilliseconds = 180,
            DomNodes = 51,
        };

        var (violations, notReported) = budget.Evaluate(metrics);

        Assert.Equal(["Cumulative layout shift", "Long tasks (ms)", "DOM nodes"], violations.Select(v => v.Metric));
        Assert.Equal(0.25, violations[0].Actual);
        Assert.Empty(notReported);
    }

    [Fact]
    public void Limits_on_metrics_the_browser_does_not_report_are_listed_not_passed()
    {
        var budget = new BrowserBudget { MaxLayoutShift = 0.1, MaxDomNodes = 50 };

        var (violations, notReported) = budget.Evaluate(new BrowserMetrics { DomNodes = 10 });

        Assert.Empty(violations);
        Assert.Equal(["Cumulative layout shift"], notReported);
    }

    [Fact]
    public void Story_budgets_are_frozen_once_built()
    {
        var story = StoryCatalog.FromCollections([new BudgetStories()]).Stories.Single();

        Assert.Equal(0.05, story.BrowserBudget!.MaxLayoutShift);
        Assert.Throws<InvalidOperationException>(() => story.BrowserBudget.MaxDomNodes = 1);
    }

    [Fact]
    public void Metrics_describe_what_was_not_reported()
    {
        var text = new BrowserMetrics { LargestContentfulPaintMilliseconds = 812.4, DomNodes = 7 }.ToString();

        Assert.Equal("CLS n/a, LCP 812 ms, long tasks n/a (n/a ms), 7 DOM nodes", text);
    }

    private sealed class BudgetStories : Stories<Greeter>
    {
        public override string Title => "Budget";

        protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Default", s => s.BrowserBudget(b => b.MaxLayoutShift = 0.05));
    }
}
