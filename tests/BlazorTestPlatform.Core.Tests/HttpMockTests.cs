using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BlazorTestPlatform.Core.Tests;

public sealed class HttpMockTests
{
    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMock mock, HttpMethod method, string url, string? body = null)
    {
        using var client = mock.CreateClient();
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(body);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Relative_patterns_match_paths_and_ignore_queries_unless_specified()
    {
        var mock = new HttpMock();
        mock.Get("api/people").RespondText("all");
        mock.Get("api/people?team=Design").RespondText("design");

        Assert.Equal("all", (await SendAsync(mock, HttpMethod.Get, "api/people")).Body);
        Assert.Equal("all", (await SendAsync(mock, HttpMethod.Get, "/api/people?page=2")).Body);
        Assert.Equal("design", (await SendAsync(mock, HttpMethod.Get, "api/people?team=Design")).Body);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(mock, HttpMethod.Post, "api/people")).Status);
    }

    [Fact]
    public async Task Wildcards_absolute_urls_and_any_method_match()
    {
        var mock = new HttpMock();
        mock.Any("api/*/avatar").RespondStatus(204);
        mock.Match(r => r.Headers.Contains("X-Probe"), "probe header").RespondStatus(202);
        mock.Get("https://cdn.example.com/config.json").RespondJsonText("{\"on\":true}");

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(mock, HttpMethod.Put, "api/ada/avatar")).Status);
        using (var client = mock.CreateClient())
        using (var probe = new HttpRequestMessage(HttpMethod.Get, "anything"))
        {
            probe.Headers.Add("X-Probe", "1");
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(probe)).StatusCode);
        }

        Assert.Equal("{\"on\":true}", (await SendAsync(mock, HttpMethod.Get, "https://cdn.example.com/config.json")).Body);
    }

    [Fact]
    public async Task Later_rules_win_and_times_falls_back_to_earlier_rules()
    {
        var mock = new HttpMock();
        mock.Get("api/x").RespondText("default");
        mock.Get("api/x").RespondStatus(500).Times(2);

        Assert.Equal(HttpStatusCode.InternalServerError, (await SendAsync(mock, HttpMethod.Get, "api/x")).Status);
        Assert.Equal(HttpStatusCode.InternalServerError, (await SendAsync(mock, HttpMethod.Get, "api/x")).Status);
        Assert.Equal("default", (await SendAsync(mock, HttpMethod.Get, "api/x")).Body);
    }

    [Fact]
    public async Task Unmatched_requests_are_recorded_and_can_throw()
    {
        var mock = new HttpMock();
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(mock, HttpMethod.Get, "api/missing")).Status);
        Assert.Null(Assert.Single(mock.Requests).Rule);

        mock.Unmatched = UnmatchedRequestBehavior.Throw;
        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(mock, HttpMethod.Get, "api/missing"));
    }

    [Fact]
    public async Task Requests_are_recorded_with_body_headers_status_and_rule()
    {
        var mock = new HttpMock();
        mock.Post("api/todos").RespondStatus(201);

        await SendAsync(mock, HttpMethod.Post, "api/todos", "{\"title\":\"Ship\"}");

        var request = Assert.Single(mock.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("api/todos", request.RelativeUrl);
        Assert.Equal("https://localhost/api/todos", request.Url);
        Assert.Equal("{\"title\":\"Ship\"}", request.Body);
        Assert.Equal(201, request.StatusCode);
        Assert.Equal("POST api/todos", request.Rule);
        Assert.Contains("Content-Type", request.Headers.Keys);
    }

    [Fact]
    public async Task Failures_never_and_delays_simulate_slow_or_broken_backends()
    {
        var time = new FakeTimeProvider();
        var mock = new HttpMock { Time = time };
        mock.Get("api/broken").Fail();
        mock.Get("api/slow").Delay(TimeSpan.FromSeconds(10)).RespondText("late");
        mock.Get("api/never").Never();

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(mock, HttpMethod.Get, "api/broken"));

        var slow = SendAsync(mock, HttpMethod.Get, "api/slow");
        await Task.Delay(50);
        Assert.False(slow.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("late", (await slow).Body);

        using var client = mock.CreateClient();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("api/never", cancel.Token));
        Assert.Equal(0, mock.Requests[^1].StatusCode);
    }

    [Fact]
    public async Task Requests_round_trip_through_json_for_the_frame_bridge()
    {
        var mock = new HttpMock();
        mock.Get("api/a").RespondText("x");
        await SendAsync(mock, HttpMethod.Get, "api/a?x=1");
        await SendAsync(mock, HttpMethod.Post, "api/b", "body");

        var parsed = HttpMock.ParseRequests(mock.RequestsToJson());

        Assert.Equal(mock.Requests.Select(r => r.ToString()), parsed.Select(r => r.ToString()));
        Assert.Equal("body", parsed[1].Body);
        Assert.True(HttpMock.Matches(parsed[0], "get", "api/a"));
        Assert.False(HttpMock.Matches(parsed[0], "GET", "api/a?x=2"));
        Assert.Empty(HttpMock.ParseRequests(null));
    }

    [Fact]
    public async Task Stories_register_the_mock_for_clients_factories_and_typed_clients()
    {
        var story = StoryCatalog.FromCollections([new HttpStories()]).Stories[0];
        var services = new ServiceCollection();
        story.ConfigureServices(services, new FixtureContext(story, HostingModel.InMemory));
        services.AddTransient<TypedClient>();
        await using var provider = services.BuildServiceProvider();

        Assert.True(story.HasServiceOverrides);
        Assert.Equal("{\"name\":\"Ada\"}", await provider.GetRequiredService<HttpClient>().GetStringAsync("api/me"));
        Assert.Equal("{\"name\":\"Ada\"}", await provider.GetRequiredService<IHttpClientFactory>().CreateClient("named").GetStringAsync("api/me"));
        Assert.Equal("Ada", await provider.GetRequiredService<TypedClient>().NameAsync());
        Assert.Equal(3, provider.GetRequiredService<HttpMock>().Requests.Count);
        Assert.IsType<FakeTimeProvider>(provider.GetRequiredService<HttpMock>().Time);
    }

    private sealed class HttpStories : Stories<Greeter>
    {
        protected override void Define(StoryCollectionBuilder<Greeter> stories) =>
            stories.Add("Mocked", s => s
                .UseFakeTime(DateTimeOffset.UnixEpoch)
                .MockHttp(http => http.Get("api/me").RespondText("{\"name\":\"Bob\"}"))
                .MockHttp(http => http.Get("api/me").RespondJsonText("{\"name\":\"Ada\"}")));
    }

    private sealed class TypedClient(HttpClient http)
    {
        public async Task<string?> NameAsync() => JsonDocument.Parse(await http.GetStringAsync("api/me")).RootElement.GetProperty("name").GetString();
    }
}
