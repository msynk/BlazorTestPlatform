using BlazorTestPlatform.Workbench;
using BlazorTestPlatform.Demo.UI;
using BlazorTestPlatform.Demo.UI.Stories;
using BlazorTestPlatform.Demo.Workbench.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Application services first, then the workbench (it wraps the services stories override).
builder.Services.AddDemoUI();
builder.Services.AddBlazorTestWorkbench(options =>
{
    WorkbenchSetup.Configure(options);
    options.ReviewsDirectory = Path.Combine(builder.Environment.ContentRootPath, "..", "BlazorTestPlatform.Demo.UI.Tests", "a11y-reviews");

    // This host exists only to serve the workbench (CI also runs it as a trimmed Production publish), so it opts in
    // explicitly. An application that adds the workbench to its own host leaves Enabled unset: then the workbench is
    // off in Production. Add options.AuthorizationPolicy to restrict who may open it.
    options.Enabled = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WorkbenchOptions).Assembly);

app.Run();

/// <summary>Entry point marker for <c>WorkbenchApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
