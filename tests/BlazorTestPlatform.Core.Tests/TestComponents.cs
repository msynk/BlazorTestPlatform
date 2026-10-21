using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorTestPlatform.Core.Tests;

public sealed class Greeter : ComponentBase
{
    [Parameter] public string? Name { get; set; }

    [Parameter] public int Count { get; set; }

    [Parameter] public EventCallback OnPing { get; set; }

    [Parameter] public EventCallback<string> OnRename { get; set; }

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    public string? NotAParameter { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"Hello {Name}");
}

public sealed class GreeterStories : Stories<Greeter>
{
    public override string Title => "Samples/Greeter";

    protected override void Define(StoryCollectionBuilder<Greeter> stories)
    {
        stories.Defaults(s => s.Arg(g => g.Name, "Ada").Tags("default-tag"));
        stories.Add("Default");
        stories.Add("Excited", s => s.Arg(g => g.Name, "Grace!").Tags("smoke", "!default-tag"));
        stories.Add("Only server", s => s.OnlyHostingModels(HostingModel.InteractiveServer));
        stories.AddVariants(v => v.Vary(g => g.Count, 1, 2).Vary(g => g.Name, "A", "B"));
    }
}

public sealed class GlobalTags : IStoryConfiguration
{
    public void Configure(IStoryBuilder story) => story.Tags("global");
}
