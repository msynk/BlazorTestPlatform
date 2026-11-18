using Assert = Xunit.Assert;
using TestContext = Xunit.TestContext;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Cli;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BlazorTestPlatform.Cli.Tests;

public sealed class McpTests
{
    private static string Project()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-mcp", Guid.NewGuid().ToString("N"))).FullName;
        var store = new ResultsStore(Path.Combine(directory, "TestResults", "btp"));
        store.Write(new StoryResult { Key = "inputs-button--primary|bunit", StoryId = "inputs-button--primary", Title = "Inputs/Button", Name = "Primary", Driver = "bunit", Outcome = StoryOutcome.Passed });
        store.Write(new StoryResult
        {
            Key = "forms-field--invalid|chromium|desktop|light|server",
            StoryId = "forms-field--invalid",
            Title = "Forms/Field",
            Name = "Invalid",
            Driver = "chromium",
            HostingModel = "server",
            Outcome = StoryOutcome.Failed,
            Error = "Expected role=alert to have text \"Enter a date\"\n  Actual: \"\"",
            Accessibility = new AccessibilityReport { Engine = "axe-core 4.14.0", Violations = [new AccessibilityViolation { RuleId = "label", Impact = AccessibilityImpact.Critical, Help = "Form elements must have labels" }] },
            Artifacts = [new ArtifactReference("screenshot", "artifacts/failure.png", "Failure screenshot")],
        });
        var received = Path.Combine(directory, "__snapshots__", "forms-field--invalid", "story" + MarkupSnapshots.ReceivedSuffix);
        Directory.CreateDirectory(Path.GetDirectoryName(received)!);
        File.WriteAllText(received, "<p>changed</p>\n");
        return directory;
    }

    // The tool's own build output: the test output of other frameworks (net11.0) holds libraries the net10.0 tool cannot load.
    private static string CliPath()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var cli = Path.Combine(directory.FullName, "src", "BlazorTestPlatform.Cli", "bin", configuration);
            if (Directory.Exists(cli))
            {
                return Directory.GetDirectories(cli).Select(d => Path.Combine(d, "btp.dll")).First(File.Exists);
            }
        }

        throw new InvalidOperationException("The btp build output was not found.");
    }

    private static BtpMcpTools Tools(string project)
    {
        var settings = BtpSettings.Load(project, new Dictionary<string, string?>());
        return new BtpMcpTools(new McpResults([settings.ResultsDirectoryPath], settings));
    }

    [Fact]
    public void Tools_describe_the_last_run_for_an_agent()
    {
        var tools = Tools(Project());

        var summary = tools.RunSummary();
        Assert.Contains("2 results: 1 passed, 1 failed", summary, StringComparison.Ordinal);
        Assert.Contains("- forms-field--invalid|chromium|desktop|light|server: Expected role=alert to have text \"Enter a date\"", summary, StringComparison.Ordinal);

        Assert.StartsWith("forms-field--invalid|chromium|desktop|light|server [failed]", tools.ListResults(outcome: "failed"), StringComparison.Ordinal);
        Assert.Contains("1 of 1 matching", tools.ListResults(search: "button"), StringComparison.Ordinal);
        Assert.StartsWith("Unknown outcome", tools.ListResults(outcome: "broken"), StringComparison.Ordinal);

        var failure = tools.GetFailure("forms-field--invalid");
        Assert.Contains("Story: Forms/Field / Invalid (forms-field--invalid)", failure, StringComparison.Ordinal);
        Assert.Contains("- [Critical] label: Form elements must have labels", failure, StringComparison.Ordinal);
        Assert.Contains("- screenshot: artifacts/failure.png (Failure screenshot)", failure, StringComparison.Ordinal);
        Assert.Contains("BTP_GREP=\"^forms-field--invalid$\"", failure, StringComparison.Ordinal);

        Assert.Contains("1 x ", tools.FailuresByCause(), StringComparison.Ordinal);
        Assert.Contains("Markup snapshots (1; accept with 'btp snapshots accept')", tools.PendingReviews(), StringComparison.Ordinal);
        Assert.StartsWith("No story-coverage.json", tools.StoryCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_can_use_btp_mcp_over_stdio()
    {
        var project = Project();
        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "btp",
                Command = "dotnet",
                Arguments = [CliPath(), "mcp"],
                WorkingDirectory = project,
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            ["failures_by_cause", "get_failure", "list_results", "pending_reviews", "run_summary", "story_coverage"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));

        var result = await client.CallToolAsync("get_failure", new Dictionary<string, object?> { ["key"] = "forms-field--invalid" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Form elements must have labels", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
    }
}
