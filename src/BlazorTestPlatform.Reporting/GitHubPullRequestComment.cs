using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BlazorTestPlatform.Reporting;

/// <summary>The outcome of <see cref="GitHubPullRequestComment.UpsertAsync"/>.</summary>
/// <param name="Created">True when a new comment was posted, false when the existing one was updated.</param>
/// <param name="Url">Link to the comment.</param>
public sealed record PullRequestCommentResult(bool Created, string? Url);

/// <summary>
/// Keeps one BlazorTestPlatform comment on a GitHub pull request up to date: the first run posts it, later runs edit
/// it in place (found by <see cref="MarkdownSummaryWriter.CommentMarker"/>), so a pull request never collects one
/// comment per push. Uses the REST API with a token that may write pull-request comments.
/// </summary>
public sealed class GitHubPullRequestComment
{
    /// <summary>GitHub's limit for a comment body.</summary>
    public const int MaxBodyLength = 65_536;

    private readonly HttpClient _http;
    private readonly Uri _api;

    /// <summary>Creates a client for one pull request.</summary>
    /// <param name="http">HTTP client (not disposed).</param>
    /// <param name="repository">Repository as <c>owner/name</c>.</param>
    /// <param name="pullRequest">Pull request number.</param>
    /// <param name="token">Token with permission to write pull-request comments.</param>
    /// <param name="apiUrl">API root (default <c>https://api.github.com</c>; GitHub Enterprise passes its own).</param>
    public GitHubPullRequestComment(HttpClient http, string repository, int pullRequest, string token, Uri? apiUrl = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pullRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _http = http;
        Repository = repository;
        PullRequest = pullRequest;
        Token = token;
        var root = (apiUrl ?? new Uri("https://api.github.com")).ToString().TrimEnd('/');
        _api = new Uri(root + "/");
    }

    /// <summary>Repository as <c>owner/name</c>.</summary>
    public string Repository { get; }

    /// <summary>Pull request number.</summary>
    public int PullRequest { get; }

    private string Token { get; }

    /// <summary>
    /// Creates a client from the variables GitHub Actions sets (<c>GITHUB_REPOSITORY</c>, <c>GITHUB_API_URL</c>, and
    /// the pull request from <c>GITHUB_EVENT_PATH</c> or <c>GITHUB_REF</c>) and a token (<paramref name="token"/>, else
    /// <c>GITHUB_TOKEN</c>). Returns null and explains why when something is missing, e.g. outside a pull request.
    /// </summary>
    public static GitHubPullRequestComment? FromEnvironment(HttpClient http, out string? problem, int? pullRequest = null, string? repository = null, string? token = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        repository ??= environment("GITHUB_REPOSITORY");
        token ??= environment("GITHUB_TOKEN");
        pullRequest ??= PullRequestFromEvent(environment("GITHUB_EVENT_PATH")) ?? PullRequestFromRef(environment("GITHUB_REF"));
        problem = string.IsNullOrWhiteSpace(repository) ? "The repository is unknown: pass --repository owner/name or set GITHUB_REPOSITORY."
            : pullRequest is null ? "This run is not for a pull request: pass --pr <number> or run on a pull_request event."
            : string.IsNullOrWhiteSpace(token) ? "No token: set GITHUB_TOKEN (for example 'GITHUB_TOKEN: ${{ github.token }}' with 'pull-requests: write')."
            : null;
        if (problem is not null)
        {
            return null;
        }

        var api = environment("GITHUB_API_URL");
        return new GitHubPullRequestComment(http, repository!, pullRequest!.Value, token!, string.IsNullOrWhiteSpace(api) ? null : new Uri(api));
    }

    /// <summary>The pull request number of a GitHub event payload (<c>pull_request.number</c>), or null.</summary>
    public static int? PullRequestFromEvent(string? eventPath)
    {
        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
            return document.RootElement.TryGetProperty("pull_request", out var pr) && pr.TryGetProperty("number", out var number) && number.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The pull request number of a <c>refs/pull/{number}/merge</c> ref, or null.</summary>
    public static int? PullRequestFromRef(string? gitRef)
    {
        const string prefix = "refs/pull/";
        if (gitRef is null || !gitRef.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = gitRef.IndexOf('/', prefix.Length);
        var number = end < 0 ? gitRef[prefix.Length..] : gitRef[prefix.Length..end];
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    /// <summary>Posts the comment, or updates the one an earlier run posted.</summary>
    public async Task<PullRequestCommentResult> UpsertAsync(string markdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var body = Body(markdown);
        var existing = await FindAsync(cancellationToken).ConfigureAwait(false);
        using var request = existing is null
            ? Request(HttpMethod.Post, $"repos/{Repository}/issues/{PullRequest.ToString(CultureInfo.InvariantCulture)}/comments", body)
            : Request(HttpMethod.Patch, $"repos/{Repository}/issues/comments/{existing.Value.ToString(CultureInfo.InvariantCulture)}", body);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var url = document.RootElement.TryGetProperty("html_url", out var link) ? link.GetString() : null;
        return new PullRequestCommentResult(existing is null, url);
    }

    /// <summary>The comment body: the marker, then the Markdown, truncated to GitHub's limit.</summary>
    public static string Body(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var body = MarkdownSummaryWriter.CommentMarker + "\n" + markdown;
        const string truncated = "\n\n…truncated; see the full report.";
        return body.Length <= MaxBodyLength ? body : body[..(MaxBodyLength - truncated.Length)] + truncated;
    }

    private async Task<long?> FindAsync(CancellationToken cancellationToken)
    {
        for (var page = 1; page <= 50; page++)
        {
            using var request = Request(HttpMethod.Get, $"repos/{Repository}/issues/{PullRequest.ToString(CultureInfo.InvariantCulture)}/comments?per_page=100&page={page.ToString(CultureInfo.InvariantCulture)}", body: null);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var comments = document.RootElement.EnumerateArray().ToArray();
            foreach (var comment in comments)
            {
                if (comment.TryGetProperty("body", out var text) && text.GetString()?.Contains(MarkdownSummaryWriter.CommentMarker, StringComparison.Ordinal) == true)
                {
                    return comment.GetProperty("id").GetInt64();
                }
            }

            if (comments.Length < 100)
            {
                break;
            }
        }

        return null;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? body)
    {
        var request = new HttpRequestMessage(method, new Uri(_api, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BlazorTestPlatform", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (body is not null)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("body", body);
                writer.WriteEndObject();
            }

            request.Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var hint = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized
            ? " The token cannot write pull-request comments: grant 'pull-requests: write' (pull requests from forks get a read-only token)."
            : string.Empty;
        throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.{hint} {detail}".TrimEnd(), null, response.StatusCode);
    }
}
