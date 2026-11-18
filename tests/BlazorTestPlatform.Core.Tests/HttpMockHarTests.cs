using System.Net;

namespace BlazorTestPlatform.Core.Tests;

public sealed class HttpMockHarTests
{
    private const string Har = """
        {
          "log": {
            "version": "1.2",
            "creator": { "name": "test", "version": "1" },
            "entries": [
              {
                "request": { "method": "GET", "url": "https://api.contoso.com/api/orders/status", "headers": [] },
                "response": { "status": 200, "headers": [ { "name": "Content-Type", "value": "application/json" }, { "name": "Content-Length", "value": "999" }, { "name": "X-Trace", "value": "a1" } ],
                              "content": { "mimeType": "application/json", "text": "{\"state\":\"pending\"}" } }
              },
              {
                "request": { "method": "GET", "url": "https://api.contoso.com/api/orders/status", "headers": [] },
                "response": { "status": 200, "headers": [], "content": { "mimeType": "application/json", "text": "{\"state\":\"shipped\"}" } }
              },
              {
                "request": { "method": "POST", "url": "https://api.contoso.com/api/orders?draft=true", "headers": [] },
                "response": { "status": 201, "headers": [], "content": { "mimeType": "text/plain", "text": "Y3JlYXRlZA==", "encoding": "base64" } }
              },
              {
                "request": { "method": "GET", "url": "https://api.contoso.com/api/flaky", "headers": [] },
                "response": { "status": 0, "headers": [], "content": { "mimeType": "x-unknown" } }
              },
              {
                "request": { "method": "GET", "url": "https://cdn.contoso.com/logo.svg", "headers": [] },
                "response": { "status": 200, "headers": [], "content": { "mimeType": "image/svg+xml", "text": "<svg/>" } }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task Recorded_answers_replay_in_order_on_any_host()
    {
        var mock = new HttpMock();
        Assert.Equal(5, mock.ReplayHar(Har));
        using var client = mock.CreateClient();

        var first = await client.GetAsync("api/orders/status", TestContext.Current.CancellationToken);
        Assert.Equal("{\"state\":\"pending\"}", await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/json", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal("a1", first.Headers.GetValues("X-Trace").Single());

        // Recorded twice: the second answer comes next and then repeats.
        Assert.Equal("{\"state\":\"shipped\"}", await client.GetStringAsync("api/orders/status", TestContext.Current.CancellationToken));
        Assert.Equal("{\"state\":\"shipped\"}", await client.GetStringAsync("api/orders/status", TestContext.Current.CancellationToken));

        var created = await client.PostAsync("api/orders?draft=true", new StringContent("{}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("created", await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("api/flaky", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("api/unknown", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Filters_host_matching_and_later_rules_narrow_the_replay()
    {
        var mock = new HttpMock();
        Assert.Equal(4, mock.ReplayHar(Har, urlFilter: "*/api/*", matchHost: true));
        mock.Get("https://api.contoso.com/api/orders/status").RespondJsonText("{\"state\":\"overridden\"}");
        using var client = mock.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("api/orders/status", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("{\"state\":\"overridden\"}", await client.GetStringAsync("https://api.contoso.com/api/orders/status", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("https://cdn.contoso.com/logo.svg", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Escaped_urls_match_and_a_recording_answers_only_its_own_query()
    {
        var mock = new HttpMock();
        mock.ReplayHar("""
            { "log": { "entries": [
              { "request": { "method": "", "url": "https://api.contoso.com/api/search?q=blazor%20tests" }, "response": { "status": 200, "content": { "mimeType": "text/plain", "text": "found" } } },
              { "request": { "method": "GET", "url": "https://api.contoso.com/api/people" }, "response": { "status": 200, "content": { "mimeType": "text/plain", "text": "page 1" } } }
            ] } }
            """);
        using var client = mock.CreateClient();

        Assert.Equal("found", await client.GetStringAsync("api/search?q=blazor tests", TestContext.Current.CancellationToken));
        Assert.Equal("found", await client.GetStringAsync("api/search?q=blazor%20tests", TestContext.Current.CancellationToken));
        Assert.Equal("page 1", await client.GetStringAsync("api/people", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("api/people?page=3", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public void Text_that_is_not_a_HAR_file_is_reported()
    {
        var error = Assert.Throws<FormatException>(() => new HttpMock().ReplayHar("""{ "entries": [] }"""));

        Assert.StartsWith("Not a HAR file", error.Message, StringComparison.Ordinal);
    }
}
