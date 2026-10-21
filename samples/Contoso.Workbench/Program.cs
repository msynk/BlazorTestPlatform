using BlazorTestPlatform.Workbench;
using Contoso.UI;
using Contoso.UI.Stories;
using Contoso.Workbench.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Application services first, then the workbench (it wraps the services stories override).
builder.Services.AddContosoUI();
builder.Services.AddBlazorTestWorkbench(options =>
{
    WorkbenchSetup.Configure(options);
    options.ReviewsDirectory = Path.Combine(builder.Environment.ContentRootPath, "..", "Contoso.UI.Tests", "a11y-reviews");
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
