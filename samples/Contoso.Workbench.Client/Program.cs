using Contoso.UI;
using Contoso.UI.Stories;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same registrations as the server so stories behave identically under WebAssembly.
builder.Services.AddContosoUI();
builder.Services.AddBlazorTestWorkbench(WorkbenchSetup.Configure);

await builder.Build().RunAsync();
