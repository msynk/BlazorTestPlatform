using System.Net;
using System.Text;
using System.Text.Json;
using BlazorTestPlatform.Coverage;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting.Tests;

public sealed class PullRequestCommentTests
{
    [Fact]
    public async Task First_run_posts_the_comment_and_later_runs_update_it()
    {
        var github = new FakeGitHub();
        using var http = new HttpClient(github);
        var comment = new GitHubPullRequestComment(http, "contoso/ui", 42, "token");

        var first = await comment.UpsertAsync("## all green", TestContext.Current.CancellationToken);
        var second = await comment.UpsertAsync("## one failing", TestContext.Current.CancellationToken);

        Assert.True(first.Created);
        Assert.False(second.Created);
        var stored = Assert.Single(github.Comments.Values);
        Assert.StartsWith(MarkdownSummaryWriter.CommentMarker, stored, StringComparison.Ordinal);
        Assert.EndsWith("## one failing", stored, StringComparison.Ordinal);
        Assert.All(github.Requests, r => Assert.Equal("Bearer token", r.Authorization));
        Assert.Contains(github.Requests, r => r.Method == "PATCH" && r.Path == "/repos/contoso/ui/issues/comments/1");
    }

    [Fact]
    public async Task Comments_from_other_people_are_left_alone()
    {
        var github = new FakeGitHub();
        github.Comments[7] = "LGTM";
        using var http = new HttpClient(github);

        var result = await new GitHubPullRequestComment(http, "contoso/ui", 42, "token").UpsertAsync("report", TestContext.Current.CancellationToken);

        Assert.True(result.Created);
        Assert.Equal("LGTM", github.Comments[7]);
        Assert.Equal(2, github.Comments.Count);
    }

    [Fact]
    public async Task Read_only_tokens_explain_the_missing_permission()
    {
        var github = new FakeGitHub { Forbidden = true };
        using var http = new HttpClient(github);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubPullRequestComment(http, "contoso/ui", 42, "token").UpsertAsync("report", TestContext.Current.CancellationToken));

        Assert.Contains("pull-requests: write", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pull_request_numbers_come_from_the_event_or_the_ref()
    {
        var eventPath = Path.Combine(Path.GetTempPath(), "btp-event-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(eventPath, """{ "action": "synchronize", "pull_request": { "number": 17 } }""");
        try
        {
            var environment = new Dictionary<string, string?> { ["GITHUB_REPOSITORY"] = "contoso/ui", ["GITHUB_TOKEN"] = "t", ["GITHUB_EVENT_PATH"] = eventPath };
            using var http = new HttpClient();

            Assert.Equal(17, GitHubPullRequestComment.FromEnvironment(http, out _, environment: environment.GetValueOrDefault)!.PullRequest);
            Assert.Equal(23, GitHubPullRequestComment.PullRequestFromRef("refs/pull/23/merge"));
            Assert.Null(GitHubPullRequestComment.PullRequestFromRef("refs/heads/main"));

            environment.Remove("GITHUB_EVENT_PATH");
            Assert.Null(GitHubPullRequestComment.FromEnvironment(http, out var problem, environment: environment.GetValueOrDefault));
            Assert.Contains("not for a pull request", problem, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(eventPath);
        }
    }

    [Fact]
    public void Long_reports_are_truncated_to_the_comment_limit()
    {
        var body = GitHubPullRequestComment.Body(new string('x', 100_000));

        Assert.Equal(GitHubPullRequestComment.MaxBodyLength, body.Length);
        Assert.EndsWith("see the full report.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_lists_coverage_gaps()
    {
        var coverage = new StoryCoverageReport
        {
            Components =
            [
                new ComponentCoverage
                {
                    Title = "Inputs/Button",
                    Component = "Contoso.UI.Button",
                    Stories = 3,
                    Parameters = [new ParameterCoverage { Name = "Size", Type = "ButtonSize", Domain = ["Small", "Medium"], Values = [new StateCoverage("Small", ["inputs-button--small"])] }],
                },
            ],
            ComponentsWithoutStories = ["Contoso.UI.Tooltip"],
        };

        var md = MarkdownSummaryWriter.Create(RunSummary.From([new StoryResult { Key = "k", StoryId = "a--b", Title = "A", Name = "B", Driver = "bunit", Outcome = StoryOutcome.Passed }]), coverage: coverage);

        Assert.Contains("Story state coverage: 50 % (1/2 states)", md, StringComparison.Ordinal);
        Assert.Contains("| Inputs/Button | 1/2 | Size=Medium |", md, StringComparison.Ordinal);
        Assert.Contains("`Contoso.UI.Tooltip`", md, StringComparison.Ordinal);
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        private long _nextId = 1;

        public SortedDictionary<long, string> Comments { get; } = [];

        public List<(string Method, string Path, string? Authorization)> Requests { get; } = [];

        public bool Forbidden { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            if (Forbidden)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"message":"Resource not accessible by integration"}""") };
            }

            var body = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("body").GetString();
            switch (request.Method.Method)
            {
                case "GET":
                    var list = string.Join(",", Comments.Select(c => $$"""{"id":{{c.Key}},"body":{{JsonSerializer.Serialize(c.Value)}}}"""));
                    return Json($"[{list}]");
                case "POST":
                    var id = Comments.Count == 0 ? _nextId : Math.Max(_nextId, Comments.Keys.Max() + 1);
                    _nextId = id + 1;
                    Comments[id] = body!;
                    return Json($$"""{"id":{{id}},"html_url":"https://github.com/contoso/ui/pull/42#issuecomment-{{id}}"}""");
                case "PATCH":
                    var target = long.Parse(request.RequestUri.Segments[^1], System.Globalization.CultureInfo.InvariantCulture);
                    Comments[target] = body!;
                    return Json($$"""{"id":{{target}},"html_url":"https://github.com/contoso/ui/pull/42#issuecomment-{{target}}"}""");
                default:
                    return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
