using BlazorTestPlatform.Demo.UI;
using BlazorTestPlatform.Demo.UI.Stories;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same registrations as the server so stories behave identically under WebAssembly.
builder.Services.AddDemoUI();
builder.Services.AddBlazorTestWorkbench(WorkbenchSetup.Configure);

await builder.Build().RunAsync();
