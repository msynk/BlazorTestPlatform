# Getting started

This guide sets up the recommended layout: components, stories, a workbench host and one test project. The [`samples/`](../samples) folder contains exactly this layout and is the fastest way to see everything working.

## 1. Project layout

```
MyDesignSystem/                     your components (Razor class library)
MyDesignSystem.Stories/             stories (Razor class library, browser-compatible)
MyDesignSystem.Workbench/           Blazor Web App host: Server + WebAssembly
MyDesignSystem.Workbench.Client/    its WebAssembly client
MyDesignSystem.Tests/               bUnit + browser tests
```

Why stories get their own project:

* The production app never ships them.
* The WebAssembly client must load them, so they can run under every render mode.
* Test projects reference them directly, because play scripts run inside the test process.

## 2. Packages

| Project | Packages |
|---|---|
| Stories | `BlazorTestPlatform.Core`, `BlazorTestPlatform.Workbench` (for the shared `WorkbenchSetup`) |
| Workbench host + client | project reference to Stories (brings the workbench) |
| Tests | `BlazorTestPlatform.Bunit`, `BlazorTestPlatform.Playwright`, `Microsoft.AspNetCore.Mvc.Testing`, your test framework |
| Tooling | `dotnet tool install -g BlazorTestPlatform.Cli` |

The stories project must be browser-compatible:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <ItemGroup>
    <SupportedPlatform Include="browser" />
  </ItemGroup>
</Project>
```

## 3. First story

```csharp
public sealed class AlertStories : Stories<Alert>
{
    public override string Title => "Feedback/Alert";

    protected override void Define(StoryCollectionBuilder<Alert> stories)
    {
        stories.Defaults(s => s.Arg(a => a.Title, "Heads up").ChildContent("Your trial ends in 3 days."));

        stories.AddVariants(v => v.Vary(a => a.Severity, AlertSeverity.Info, AlertSeverity.Warning, AlertSeverity.Error));

        stories.Add("Dismissible", s => s
            .Arg(a => a.Dismissible, true)
            .Play(async ctx =>
            {
                await ctx.GetByRole("button", "Dismiss").ClickAsync();
                await ctx.Expect(ctx.GetByRole("status")).ToHaveCountAsync(0);
                await ctx.ExpectAction("OnDismiss").ToHaveBeenCalledAsync(times: 1);
            }));
    }
}
```

Share the workbench configuration between server and client:

```csharp
public static class WorkbenchSetup
{
    public static void Configure(WorkbenchOptions options)
    {
        options.Title = "My design system";
        options.AddStoriesFromAssemblyOf<AlertStories>();
    }
}
```

## 4. Workbench host

Server `Program.cs`:

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMyDesignSystem();                       // your real services first
builder.Services.AddBlazorTestWorkbench(WorkbenchSetup.Configure);

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WorkbenchOptions).Assembly);
app.Run();

public partial class Program;   // for WorkbenchApplicationFactory<Program>
```

Client `Program.cs`:

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddMyDesignSystem();
builder.Services.AddBlazorTestWorkbench(WorkbenchSetup.Configure);
await builder.Build().RunAsync();
```

`Routes.razor` must include the workbench assembly:

```razor
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="new[] { typeof(BlazorTestPlatform.Workbench.WorkbenchOptions).Assembly }">
    <Found Context="routeData"><RouteView RouteData="routeData" /></Found>
</Router>
```

Requirements for the host's `App.razor`:

* Link your design system's stylesheet. Story frames use the app's own `App.razor`, so they look exactly like production.
* Do **not** set a global render mode. The frame chooses a render mode for each story.
* Keep `#blazor-error-ui` hidden by CSS. Browser runs fail when Blazor shows it.

Run the host and open `/_workbench`. **Controls** edits the story's parameters live (see [live controls](stories.md#live-controls)), and the URL keeps the edits shareable. For stories with a play function, **▶ Run interactions** runs the script inside the frame under the selected render mode and shows its timeline next to the preview.

## 5. Tests

In-memory tests need no browser:

```csharp
public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<AlertStories>();
public static TheoryData<string> Stories => new(Catalog.Ids);

[Theory, MemberData(nameof(Stories))]
public Task Story_passes(string id) => BunitStoryRunner.VerifyAsync(Catalog.Get(id));
```

Browser tests start the host on Kestrel inside the test process:

```csharp
[assembly: AssemblyFixture(typeof(Workbench))]

public sealed class Workbench : IAsyncLifetime
{
    public WorkbenchTestEnvironment Environment { get; private set; } = default!;
    public async ValueTask InitializeAsync() =>
        Environment = await WorkbenchTestEnvironment.StartAsync(new WorkbenchApplicationFactory<Program>());
    public ValueTask DisposeAsync() => Environment.DisposeAsync();
}
```

Install the browsers once. The script is generated by the Playwright package in your test output folder:

```bash
pwsh tests/MyDesignSystem.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

You can also set `BTP_INSTALL_BROWSERS=1` to install them automatically on first use.

## 6. First run

```bash
dotnet test
btp report
```

The first browser run writes visual baselines to `__snapshots__/`, and the first performance run writes `__baselines__/performance.json`. Review both and commit them. On CI, missing baselines fail the build instead of being created.
