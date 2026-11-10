using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Combinatorial stories. Tone × Appearance × Size × Dot is 60 combinations, 45 of which the component supports
/// (<c>Where(...)</c> drops the rest). <c>Pairwise()</c> renders every pair of values in 17 stories, picked
/// deterministically so ids and baselines never move. <c>CombinatoricsTests</c> proves the pairs are all there.
/// </summary>
public sealed class BadgeStories : Stories<Badge>
{
    public override string Title => "Display/Badge";

    protected override void Define(StoryCollectionBuilder<Badge> stories)
    {
        stories.Defaults(s => s.ChildContent("Beta"));

        stories.Add("Default", s => s.Description("A subtle, neutral, medium badge: every parameter at the component's default."));

        stories.AddVariants(
            v => v
                .Vary(b => b.Tone, BadgeTone.Neutral, BadgeTone.Info, BadgeTone.Success, BadgeTone.Warning, BadgeTone.Danger)
                .Vary(b => b.Appearance, BadgeAppearance.Subtle, BadgeAppearance.Solid, BadgeAppearance.Outline)
                .Vary(b => b.Size, BadgeSize.Small, BadgeSize.Medium)
                .Vary(b => b.Dot, false, true)
                .Where(c => !(c.Get(b => b.Size) == BadgeSize.Small && c.Get(b => b.Dot)))
                .Pairwise(),
            s => s
                .Tags("visual-matrix")
                .Description("One of the pairwise variants: together they cover every pair of parameter values (17 stories instead of the 45 supported combinations). Each one is a visual snapshot."));

        stories.Add("Custom brand color", s => s
            .Description("Unmatched attributes (Attribute) reach the element through CaptureUnmatchedValues. The brand orange fails color contrast on white, so the story waives that one axe rule with a written justification, shown in the workbench and in the report.")
            .Attribute("style", "--c-badge-tone: #ff8a3d")
            .Attribute("title", "New this week")
            .ChildContent("New")
            .DisableAccessibilityRule("color-contrast", "Brand orange (2.1:1 on white) is approved for decorative 'New' labels that always sit next to a text equivalent. Design ticket DS-142."));
    }
}
