using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorTestPlatform.Core.Tests;

public enum Tone
{
    Calm,
    Loud,
}

[Flags]
public enum Features
{
    None = 0,
    Bold = 1,
    Italic = 2,
}

public sealed class ArgsProbe : ComponentBase
{
    [Parameter] public string? Title { get; set; } = "Hello";

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public int Count { get; set; } = 3;

    [Parameter] public double Ratio { get; set; }

    [Parameter] public Tone Tone { get; set; }

    [Parameter] public Tone? OptionalTone { get; set; }

    [Parameter] public bool? Tri { get; set; }

    [Parameter] public DateOnly Day { get; set; }

    [Parameter] public DateTime When { get; set; }

    [Parameter] public TimeOnly At { get; set; }

    [Parameter] public Guid Id { get; set; }

    [Parameter] public TimeSpan Delay { get; set; }

    [Parameter] public Features Features { get; set; }

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public EventCallback OnClick { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public List<string> Items { get; set; } = [];

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    [CascadingParameter] public string? Ambient { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"{Title}|{Count}|{Tone}|{Value}");
}

public sealed class StoryArgsTests
{
    private static Story Probe(Action<StoryBuilder<ArgsProbe>>? configure = null) =>
        StoryCatalog.FromCollections([new ProbeStories(configure)]).Stories[0];

    [Fact]
    public void Controls_cover_editable_parameters_with_story_values_then_component_defaults()
    {
        var story = Probe(s => s.Arg(p => p.Count, 7).Arg(p => p.Ratio, ctx => 0.5).Bind(p => p.Value, "bound").ChildContent("<b>x</b>"));
        var controls = StoryArgs.ControlsFor(story).ToDictionary(c => c.Name);

        Assert.Equal(["Count", "Ratio", "Value"], StoryArgs.ControlsFor(story).Take(3).Select(c => c.Name));
        Assert.Equal("7", controls["Count"].DefaultText);
        Assert.True(controls["Count"].SetByStory);
        Assert.True(controls["Ratio"].Computed);
        Assert.Null(controls["Ratio"].DefaultText);
        Assert.Equal("bound", controls["Value"].DefaultText);
        Assert.Equal("Hello", controls["Title"].DefaultText);
        Assert.False(controls["Title"].SetByStory);
        Assert.Equal(ArgControlType.Boolean, controls["Disabled"].Control);
        Assert.Equal(ArgControlType.Select, controls["Tone"].Control);
        Assert.Equal(["Calm", "Loud"], controls["Tone"].Options);
        Assert.Equal(["", "Calm", "Loud"], controls["OptionalTone"].Options);
        Assert.Equal(["", "true", "false"], controls["Tri"].Options);
        Assert.Equal(ArgControlType.Date, controls["Day"].Control);
        Assert.Equal(ArgControlType.DateTime, controls["When"].Control);
        Assert.Equal(ArgControlType.Time, controls["At"].Control);
        Assert.Equal(ArgControlType.Text, controls["Features"].Control);
        Assert.Equal("int", controls["Count"].TypeName);
        Assert.Equal("Tone?", controls["OptionalTone"].TypeName);

        foreach (var excluded in new[] { "ValueChanged", "OnClick", "ChildContent", "Items", "Attributes", "Ambient" })
        {
            Assert.DoesNotContain(excluded, controls.Keys);
        }
    }

    [Theory]
    [InlineData(typeof(int), "42", 42)]
    [InlineData(typeof(int?), "", null)]
    [InlineData(typeof(bool), "true", true)]
    [InlineData(typeof(double), "1.5", 1.5)]
    [InlineData(typeof(Tone), "loud", Tone.Loud)]
    [InlineData(typeof(Features), "Bold, Italic", Features.Bold | Features.Italic)]
    [InlineData(typeof(string), "", "")]
    [InlineData(typeof(string), StoryArgs.NullToken, null)]
    [InlineData(typeof(char), "x", 'x')]
    public void Text_converts_to_parameter_types(Type type, string text, object? expected)
    {
        Assert.True(StoryArgs.TryConvert(text, type, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Dates_and_times_round_trip_through_control_text()
    {
        var when = new DateTime(2030, 1, 2, 3, 4, 0);
        Assert.Equal("2030-01-02T03:04", StoryArgs.Format(when));
        Assert.True(StoryArgs.TryConvert("2030-01-02T03:04", typeof(DateTime), out var parsed, out _));
        Assert.Equal(when, parsed);
        Assert.Equal("2030-01-02", StoryArgs.Format(new DateOnly(2030, 1, 2)));
        Assert.Equal("09:30", StoryArgs.Format(new TimeOnly(9, 30)));
        Assert.Equal("00:00:01.5000000", StoryArgs.Format(TimeSpan.FromSeconds(1.5)));
        Assert.True(StoryArgs.TryConvert("09:30", typeof(TimeOnly), out var time, out _));
        Assert.Equal(new TimeOnly(9, 30), time);
    }

    [Theory]
    [InlineData(typeof(int), "abc", "'abc' is not a valid int.")]
    [InlineData(typeof(int), StoryArgs.NullToken, "int cannot be null.")]
    [InlineData(typeof(Tone), "Quiet", "'Quiet' is not a valid Tone (Calm, Loud).")]
    [InlineData(typeof(Tone), "5", "'5' is not a valid Tone (Calm, Loud).")]
    [InlineData(typeof(List<string>), "a", "List`1 parameters cannot be edited.")]
    public void Invalid_text_is_explained(Type type, string text, string message)
    {
        Assert.False(StoryArgs.TryConvert(text, type, out _, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public void Resolve_drops_defaults_rejects_markup_from_links_and_reports_unknown_names()
    {
        var story = Probe(s => s.Arg(p => p.Count, 7));
        var resolved = StoryArgs.Resolve(story, new Dictionary<string, string?>
        {
            ["count"] = "7",              // the story's own value: no override
            ["Title"] = "<img onerror=x>", // markup from a link
            ["Tone"] = "Loud",
            ["Nope"] = "1",
            ["Ratio"] = "",                // a cleared number field (its default is 0): invalid, not "unset"
        });

        Assert.Equal(["Tone"], resolved.Values.Keys);
        Assert.Contains(resolved.Errors, e => e.Contains("cannot contain '<'", StringComparison.Ordinal));
        Assert.Contains(resolved.Errors, e => e.StartsWith("'Nope' is not an editable parameter", StringComparison.Ordinal));
        Assert.Contains(resolved.Errors, e => e.StartsWith("'Ratio': '' is not a valid double", StringComparison.Ordinal));

        var typed = StoryArgs.Resolve(story, new Dictionary<string, string?> { ["Title"] = "<b>typed</b>" }, fromLink: false);
        Assert.Equal("<b>typed</b>", typed.Values["Title"]);
    }

    [Fact]
    public void Empty_fields_without_a_default_leave_the_parameter_alone()
    {
        var story = Probe(s => s.Arg(p => p.Title, (string?)null).Arg(p => p.Count, ctx => 9));

        var resolved = StoryArgs.Resolve(story, new Dictionary<string, string?> { ["Title"] = "", ["Count"] = "" });

        Assert.Empty(resolved.Values);
        Assert.Empty(resolved.Errors);
    }

    [Fact]
    public void Query_and_token_encoding_round_trip()
    {
        var args = new Dictionary<string, string?> { ["Title"] = "Hello world & more", ["Tone"] = "Loud", ["Value"] = null };

        var query = StoryArgs.ToQuery(args);

        Assert.Equal("arg.Title=Hello%20world%20%26%20more&arg.Tone=Loud&arg.Value=%21null", query);
        Assert.Equal(args, StoryArgs.FromQuery("?story=x&" + query + "&theme=dark"));
        Assert.Equal(args, StoryArgs.Decode(StoryArgs.Encode(args)));
        Assert.Equal("Hello world", StoryArgs.Decode("Title=Hello+world")["Title"]);
        Assert.Equal("b", StoryArgs.FromQuery("arg.X=a&arg.x=b")["X"]);
    }

    private sealed class ProbeStories(Action<StoryBuilder<ArgsProbe>>? configure) : Stories<ArgsProbe>
    {
        protected override void Define(StoryCollectionBuilder<ArgsProbe> stories) => stories.Add("Probe", s => configure?.Invoke(s));
    }
}
