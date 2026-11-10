using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Demo.UI;
using BlazorTestPlatform.Demo.UI.Stories;
using BlazorTestPlatform.Demo.Workbench;
using BlazorTestPlatform.Demo.Workbench.Chaos;
using BlazorTestPlatform.Demo.Workbench.Components;
using BlazorTestPlatform.Hub;
using BlazorTestPlatform.Workbench;

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

// The hub: the test platform website over the demo test project's results, run history, baselines and reviews. It
// can also run the stories in memory, including the demo's chaos stories, which fail on purpose in some runs.
builder.Services.AddBlazorTestHub(options =>
{
    options.Title = "BlazorTestPlatform demo";
    options.ProjectDirectory = Path.Combine(builder.Environment.ContentRootPath, "..", "BlazorTestPlatform.Demo.UI.Tests");
    options.AddStoriesFromAssemblyOf<ButtonStories>();
    options.Collections.Add(new DemoChaosStories());

    // Like the workbench, this host opts in explicitly; the hub is read-only outside Development.
    options.Enabled = true;
    options.AllowChanges = builder.Environment.IsDevelopment();
});

// `dotnet run` (launchSettings.json) seeds ten runs of history on first start, so the hub has trends to show.
if (BtpSettings.IsTruthy(Environment.GetEnvironmentVariable("BTP_DEMO_SEED")))
{
    builder.Services.AddHostedService<DemoHistorySeeder>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapBlazorTestHub();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WorkbenchOptions).Assembly, typeof(HubOptions).Assembly);

app.Run();

/// <summary>Entry point marker for <c>WorkbenchApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
