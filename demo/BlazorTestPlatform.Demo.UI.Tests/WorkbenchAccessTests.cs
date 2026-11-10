using System.Net;
using BlazorTestPlatform.Workbench;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// The workbench pages run play scripts, sign fake users in and replace services: a host can restrict them with an
/// authorization policy, and users that fail it get a plain 404 without any workbench markup.
/// </summary>
public sealed class WorkbenchAccessTests
{
    private static WebApplicationFactory<Program> Host(bool allow) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.AddAuthorizationCore(o => o.AddPolicy("workbench-users", p => p.RequireAssertion(_ => allow)));
            var options = (WorkbenchOptions)services.Single(d => d.ServiceType == typeof(WorkbenchOptions)).ImplementationInstance!;
            options.AuthorizationPolicy = "workbench-users";
        }));

    [Theory]
    [InlineData("/_workbench")]
    [InlineData("/_workbench/frame/inputs-button--primary?mode=ssr")]
    public async Task Users_failing_the_policy_get_404_without_workbench_markup(string path)
    {
        await using var host = Host(allow: false);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("data-btp-root", body, StringComparison.Ordinal);
        Assert.DoesNotContain("btp-header", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_satisfying_the_policy_see_the_workbench()
    {
        await using var host = Host(allow: true);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/_workbench/frame/inputs-button--primary?mode=ssr", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-btp-root", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
