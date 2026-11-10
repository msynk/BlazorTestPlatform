using BlazorTestPlatform;
using BlazorTestPlatform.Hub;

namespace BlazorTestPlatform.Demo.Workbench.Chaos;

/// <summary>
/// Faults the demo's chaos stories inject, by run number, so the hub has real flaky, broken, regressed and fixed
/// results to show. Outside hub runs (the workbench, the demo's test suite) <see cref="Run"/> is 0 and nothing fails.
/// </summary>
public static class DemoChaos
{
    /// <summary>The hub run these stories execute in (1 for the first), or 0 outside hub runs.</summary>
    public static int Run =>
        HubRunContext.Current is { } context
            ? context.Request.Properties.TryGetValue("demo-run", out var value) && int.TryParse(value, out var number) ? number : context.Sequence
            : 0;

    /// <summary>The save toast is late every third run: an intermittent race (quarantined in the demo).</summary>
    public static bool ToastIsLate => Run > 0 && Run % 3 == 2;

    /// <summary>A rounding change broke the checkout total in runs 3 and 4, then a fix landed.</summary>
    public static bool TotalIsWrong => Run is 3 or 4;

    /// <summary>The export button was renamed in run 8 and the story was never updated: broken since.</summary>
    public static bool ExportIsRenamed => Run >= 8;

    /// <summary>The search field had no label until run 10 added one: an accessibility fix.</summary>
    public static bool SearchIsUnlabelled => Run is > 0 and < 10;

    /// <summary>The latest seeded run (10) dropped the avatar button's name: a new accessibility failure.</summary>
    public static bool AvatarIsUnnamed => Run == 10;

    /// <summary>The notification bell hangs in runs 7 and 9: a flaky timeout.</summary>
    public static bool BellHangs => Run is 7 or 9;

    /// <summary>The activity feed grows by 60 items a run, so it gets slower.</summary>
    public static int FeedItems => 40 + (Math.Max(0, Run) * 60);
}
