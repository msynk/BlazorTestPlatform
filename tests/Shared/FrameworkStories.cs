using BlazorTestPlatform;
using BlazorTestPlatform.Drivers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FrameworkStories;

// Stories shared by the xUnit, NUnit, MSTest and TUnit integration tests: one passes, one is skipped up front, one is
// skipped by the driver (capability it lacks), and one fails (tagged "failing" so data-driven tests can leave it out).

public sealed class Greeting : ComponentBase
{
    [Parameter]
    public string Name { get; set; } = "world";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, $"Hello, {Name}!");
        builder.CloseElement();
    }
}

public sealed class GreetingStories : Stories<Greeting>
{
    public override string Title => "Frameworks/Greeting";

    protected override void Define(StoryCollectionBuilder<Greeting> stories)
    {
        stories.Add("Default", s => s
            .Tags("smoke", "greeting")
            .Play(ctx => ctx.Expect(ctx.GetByText("Hello, world!")).ToBeVisibleAsync()));
        stories.Add("Skipped", s => s.Skip("Waiting for the design review."));
        stories.Add("Needs a browser", s => s.Requires(DriverCapabilities.Screenshots, "Compares pixels."));
        stories.Add("Fails", s => s
            .Tags("failing")
            .Play(ctx => ctx.Expect(ctx.GetByText("Goodbye")).ToBeVisibleAsync()));
    }
}

public static class FrameworkCatalog
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<GreetingStories>();

    public static IEnumerable<Story> Runnable => Catalog.Stories.Where(s => !s.Tags.Contains("failing"));

    public static BlazorTestPlatform.Bunit.BunitStoryOptions Options => new()
    {
        Settings = BlazorTestPlatform.Configuration.BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-framework-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };
}
