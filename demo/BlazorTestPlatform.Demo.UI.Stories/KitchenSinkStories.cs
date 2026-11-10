using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Composition stories render several components together through the neutral <see cref="Composition"/> host. A
/// kitchen-sink page is a cheap guard for shared design tokens: one snapshot per theme and viewport.
/// </summary>
public sealed class KitchenSinkStories : Stories<Composition>
{
    public override string Title => "Overview/Kitchen sink";

    protected override void Define(StoryCollectionBuilder<Composition> stories) =>
        stories.Add("Design system", s => s
            .Description("Buttons, badges, a text field and an alert on one page. A change to a shared color, font or radius shows up here in every theme and viewport.")
            .ChildContent(Fragments.KitchenSink));
}
