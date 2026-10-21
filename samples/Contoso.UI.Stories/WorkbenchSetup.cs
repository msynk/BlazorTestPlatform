using BlazorTestPlatform;
using BlazorTestPlatform.Workbench;

namespace Contoso.UI.Stories;

/// <summary>Workbench configuration shared by the server and the WebAssembly client.</summary>
public static class WorkbenchSetup
{
    public static void Configure(WorkbenchOptions options)
    {
        options.Title = "Contoso UI workbench";
        options.AddStoriesFromAssemblyOf<ButtonStories>();
        options.Viewports.Clear();
        options.Viewports.AddRange([Viewport.Mobile, Viewport.Tablet, Viewport.Desktop]);
    }
}

/// <summary>Defaults applied to every Contoso story.</summary>
public sealed class ContosoStoryDefaults : IStoryConfiguration
{
    public void Configure(IStoryBuilder story) => story.Tags("contoso");
}
