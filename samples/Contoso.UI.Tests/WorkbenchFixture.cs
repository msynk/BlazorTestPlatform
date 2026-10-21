using BlazorTestPlatform.Playwright;
using BlazorTestPlatform.Playwright.Hosting;

[assembly: AssemblyFixture(typeof(Contoso.UI.Tests.WorkbenchFixture))]

namespace Contoso.UI.Tests;

/// <summary>
/// Starts the workbench host (in-process on Kestrel, or an already running one when <c>BTP_BASE_URL</c> is set, e.g. a
/// trimmed Release publish) and the browsers once for the whole test assembly.
/// </summary>
public sealed class WorkbenchFixture : IAsyncLifetime
{
    public WorkbenchTestEnvironment Environment { get; private set; } = default!;

    public async ValueTask InitializeAsync() =>
        Environment = await WorkbenchTestEnvironment.StartAsync(
            string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("BTP_BASE_URL"))
                ? new WorkbenchApplicationFactory<Program>()
                : new ExternalWorkbenchHost());

    public async ValueTask DisposeAsync() => await Environment.DisposeAsync();
}
