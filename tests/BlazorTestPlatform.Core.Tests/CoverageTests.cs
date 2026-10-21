using BlazorTestPlatform.Coverage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorTestPlatform.Core.Tests;

public sealed class CoverageTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new ToneBadgeStories()]);

    [Fact]
    public void Finite_parameters_report_the_states_stories_render()
    {
        var report = StoryCoverage.Analyze(Catalog);
        var badge = Assert.Single(report.Components);

        var tone = badge.Parameters.Single(p => p.Name == "Tone");
        Assert.Equal(["Neutral", "Good", "Bad"], tone.Domain);
        Assert.Equal(["Neutral", "Good"], tone.Values.Select(v => v.Value));
        Assert.Equal(["Bad"], tone.Missing);
        Assert.Equal(["coverage-badge--default", "coverage-badge--computed-label"], tone.Values[0].Stories);

        var pill = badge.Parameters.Single(p => p.Name == "Pill");
        Assert.Equal(["true", "false"], pill.Domain);
        Assert.Empty(pill.Missing);

        var accent = badge.Parameters.Single(p => p.Name == "Accent");
        Assert.Equal(["null", "Neutral", "Good", "Bad"], accent.Domain);
        Assert.Equal(["null"], accent.Values.Select(v => v.Value));

        var label = badge.Parameters.Single(p => p.Name == "Label");
        Assert.False(label.IsFinite);
        Assert.Equal(["coverage-badge--computed-label"], label.ComputedIn);

        Assert.Equal((5, 9), (badge.CoveredStates, badge.TotalStates));
        Assert.Equal(["Tone=Bad", "Accent=Neutral", "Accent=Good", "Accent=Bad"], badge.MissingStates);
        Assert.DoesNotContain(badge.Parameters, p => p.Name == "OnClick");
    }

    [Fact]
    public void Components_without_stories_are_listed_and_the_gate_explains_what_is_missing()
    {
        var report = StoryCoverage.Analyze(Catalog, [typeof(ToneBadge), typeof(Greeter)]);

        Assert.Equal([typeof(Greeter).FullName!], report.ComponentsWithoutStories);
        report.ThrowIfBelow(0.5);
        var error = Assert.Throws<StoryAssertionException>(() => report.ThrowIfBelow(0.6, requireStoriesForEveryComponent: true));
        Assert.Contains("Coverage/Badge: Tone=Bad, Accent=Neutral", error.Message, StringComparison.Ordinal);
        Assert.Contains("Components without stories: " + typeof(Greeter).FullName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_round_trip_through_the_results_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-coverage-" + Guid.NewGuid().ToString("N"));
        var report = StoryCoverage.Analyze(Catalog, [typeof(ToneBadge), typeof(Greeter)]);

        report.Save(directory);
        var loaded = StoryCoverageReport.Load(directory)!;

        Assert.Equal((report.CoveredStates, report.TotalStates), (loaded.CoveredStates, loaded.TotalStates));
        Assert.Equal(report.ComponentsWithoutStories, loaded.ComponentsWithoutStories);
        Assert.Null(StoryCoverageReport.Load(Path.Combine(directory, "missing")));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Component_discovery_skips_imports_layouts_and_abstract_types()
    {
        var components = StoryCoverage.ComponentsIn(typeof(StoryCatalog).Assembly);

        Assert.Contains(typeof(Composition), components);
        Assert.DoesNotContain(components, t => t.IsAbstract || t.Name == "_Imports" || typeof(LayoutComponentBase).IsAssignableFrom(t));
    }

    // Private: kept out of assembly-wide discovery, which other tests count.
    private sealed class ToneBadgeStories : Stories<ToneBadge>
    {
        public override string Title => "Coverage/Badge";

        protected override void Define(StoryCollectionBuilder<ToneBadge> stories)
        {
            stories.Add("Default");
            stories.Add("Good pill", s => s.Arg(b => b.Tone, Mood.Good).Arg(b => b.Pill, true).Arg(b => b.Label, "Fine"));
            stories.Add("Computed label", s => s.Arg(b => b.Label, ctx => ctx.Fake.Word()));
            stories.Add("Skipped", s => s.Arg(b => b.Tone, Mood.Bad).Skip("Not ready"));
        }
    }
}

public enum Mood
{
    Neutral,
    Good,
    Bad,
}

public sealed class ToneBadge : ComponentBase
{
    [Parameter] public Mood Tone { get; set; }

    [Parameter] public Mood? Accent { get; set; }

    [Parameter] public bool Pill { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public EventCallback OnClick { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Label);
}
