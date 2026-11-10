using System.Net;
using System.Text.RegularExpressions;
using BlazorTestPlatform.Hub;
using BlazorTestPlatform.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>The demo host with its hub pointed at an isolated project directory holding two real in-memory runs.</summary>
public sealed class HubFixture : IAsyncLifetime
{
    public string Project { get; } = Directory.CreateTempSubdirectory("btp-hub-tests-").FullName;

    public WebApplicationFactory<Program> Host { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        Host = CreateHost(enabled: true);
        var runner = Host.Services.GetRequiredService<HubRunner>();
        string[] stories = ["inputs-button--primary", "inputs-button--click-raises-onclick", "demo-chaos--report-export", "demo-chaos--checkout-total"];
        for (var run = 8; run <= 9; run++)
        {
            var state = await runner.RunAsync(new HubRunRequest
            {
                StoryIds = stories,
                Label = $"Test run {run}",
                Trigger = "test",
                Properties = new Dictionary<string, string> { ["demo-run"] = run.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            }, TestContext.Current.CancellationToken);
            Assert.Equal(stories.Length, state.Completed);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        try
        {
            Directory.Delete(Project, recursive: true);
        }
        catch (IOException)
        {
            // A result file may still be held briefly on Windows; the temp folder is cleaned up eventually.
        }
    }

    public WebApplicationFactory<Program> CreateHost(bool enabled) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            var options = (HubOptions)services.Single(d => d.ServiceType == typeof(HubOptions)).ImplementationInstance!;
            options.ProjectDirectory = Project;
            options.ArchiveDirectory = null;
            options.Enabled = enabled;
            options.IsEnabled = enabled;
        }));
}

/// <summary>
/// The hub over real runs: every page, the JSON API, file serving limits, antiforgery on actions and the quarantine
/// action writing quarantine.json.
/// </summary>
public sealed partial class HubTests(HubFixture fixture) : IClassFixture<HubFixture>
{
    private string _project => fixture.Project;

    private WebApplicationFactory<Program> _host => fixture.Host;

    [Theory]
    [InlineData("", "Dashboard")]
    [InlineData("runs", "Test run 9")]
    [InlineData("compare", "New failures")]
    [InlineData("tests", "Report export")]
    [InlineData("tests?view=matrix", "Cross-hosting matrix")]
    [InlineData("tests/demo-chaos--report-export", "Report export")]
    [InlineData("result?key=demo-chaos--report-export%7Cbunit", "Locator")]
    [InlineData("failures", "Export CSV")]
    [InlineData("flaky", "Broken")]
    [InlineData("visual", "Visual review")]
    [InlineData("accessibility", "Accessibility")]
    [InlineData("performance", "Render budgets")]
    [InlineData("coverage", "WCAG traceability matrix")]
    [InlineData("impact?files=demo%2FBlazorTestPlatform.Demo.UI%2FButton.razor", "Inputs/Button")]
    [InlineData("live", "Test run 9")]
    [InlineData("readiness", "Release readiness")]
    [InlineData("integrations", "Pull-request comment")]
    public async Task Every_page_renders_the_real_runs(string page, string expected)
    {
        using var client = _host.CreateClient();
        using var response = await client.GetAsync(HubUrls.Page(page), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_json_api_reports_runs_stability_and_failures()
    {
        using var client = _host.CreateClient();
        var token = TestContext.Current.CancellationToken;

        var runs = await client.GetStringAsync(HubUrls.Page("api/runs"), token);
        Assert.Contains("\"label\": \"Test run 9\"", runs, StringComparison.Ordinal);
        var stability = await client.GetStringAsync(HubUrls.Page("api/stability"), token);
        Assert.Contains("demo-chaos--report-export|bunit", stability, StringComparison.Ordinal);
        var failures = await client.GetStringAsync(HubUrls.Page("api/failures"), token);
        Assert.Contains("\"category\": \"locator\"", failures, StringComparison.Ordinal);
        var junit = await client.GetStringAsync(HubUrls.Page("export/junit"), token);
        Assert.Contains("<testsuites", junit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("files/results/..%2F..%2Fsecret.png")]
    [InlineData("files/results/../../secret.png")]
    [InlineData("files/snapshots/..%2Fsecret.png")]
    [InlineData("files/elsewhere/a.png")]
    [InlineData("files/results/results/inputs-button--primary~bunit.json.exe")]
    public async Task Files_outside_the_results_and_snapshots_are_not_served(string path)
    {
        File.WriteAllText(Path.Combine(_project, "secret.png"), "not for the browser");
        using var client = _host.CreateClient();
        using var response = await client.GetAsync(HubUrls.Page(path), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Actions_need_an_antiforgery_token_and_quarantine_writes_the_file()
    {
        using var client = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = TestContext.Current.CancellationToken;

        using var forged = await client.PostAsync(HubUrls.Page("actions/quarantine"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["pattern"] = "demo-chaos--report-export",
            ["reason"] = "forged",
        }), token);
        Assert.False(forged.IsSuccessStatusCode || forged.StatusCode == HttpStatusCode.Redirect, $"A post without a token got {(int)forged.StatusCode}.");
        Assert.DoesNotContain(QuarantineList.Load(Path.Combine(_project, QuarantineList.DefaultFileName)).Entries, e => e.Reason == "forged");

        var page = await client.GetStringAsync(HubUrls.Page("result?key=demo-chaos--report-export%7Cbunit"), token);
        var antiforgery = AntiforgeryField().Match(page).Groups[1].Value;
        Assert.NotEmpty(antiforgery);
        using var posted = await client.PostAsync(HubUrls.Page("actions/quarantine"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgery,
            ["pattern"] = "demo-chaos--report-export",
            ["reason"] = "Renamed button, tracked in DEMO-34",
            ["owner"] = "UI team",
            ["expires"] = "2027-01-31",
            ["returnUrl"] = HubUrls.Page("flaky"),
        }), token);

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal(HubUrls.Page("flaky"), posted.Headers.Location?.OriginalString);
        var entry = Assert.Single(QuarantineList.Load(Path.Combine(_project, QuarantineList.DefaultFileName)).Entries, e => e.Pattern == "demo-chaos--report-export");
        Assert.Equal("Renamed button, tracked in DEMO-34", entry.Reason);
        Assert.Equal(new DateOnly(2027, 1, 31), entry.Expires);
    }

    [Fact]
    public async Task A_disabled_hub_answers_404_everywhere()
    {
        await using var host = fixture.CreateHost(enabled: false);
        using var client = host.CreateClient();
        foreach (var path in new[] { HubUrls.Page(), HubUrls.Page("api/summary"), HubUrls.Page("export/junit") })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path} answered {(int)response.StatusCode}: {body[..Math.Min(body.Length, 1500)]}");
            Assert.DoesNotContain("hub-nav", body, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
