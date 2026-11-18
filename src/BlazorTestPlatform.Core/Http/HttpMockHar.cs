using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BlazorTestPlatform;

/// <summary>
/// Replays HTTP Archive (HAR 1.2) files through an <see cref="HttpMock"/>, as Playwright's <c>routeFromHAR</c> does: record
/// the traffic once (browser developer tools "Save all as HAR", Playwright's <c>RecordHarPath</c> under WebAssembly, Fiddler,
/// a proxy) and the story answers from it in every driver, offline and deterministically.
/// </summary>
public static class HttpMockHar
{
    // Hop-by-hop and framing headers: the HAR holds the decoded body, so they no longer describe it.
    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "content-length", "content-encoding", "transfer-encoding", "connection", "keep-alive", "upgrade", ":status",
    };

    /// <summary>
    /// Adds a rule per recorded request (method, path and query) that answers with the recorded status, headers and body.
    /// Requests recorded several times answer in order, the last answer repeating: polling and retries replay as they
    /// happened. By default the host is ignored, so traffic recorded against a deployed API answers the story's client;
    /// <paramref name="matchHost"/> keeps it. <paramref name="urlFilter"/> (an <see cref="HttpMock.When"/> pattern over the
    /// recorded URL, such as <c>*/api/*</c>) limits which entries are used. Rules added after this one take precedence, so a
    /// story can override single endpoints. Requests that failed in the recording (no status) fail when replayed.
    /// </summary>
    /// <returns>The number of entries replayed.</returns>
    /// <exception cref="FormatException">The text is not a HAR file.</exception>
    public static int ReplayHar(this HttpMock mock, string harJson, string? urlFilter = null, bool matchHost = false)
    {
        ArgumentNullException.ThrowIfNull(mock);
        ArgumentException.ThrowIfNullOrEmpty(harJson);
        List<Entry> entries;
        try
        {
            entries = Parse(harJson);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"Not a HAR file: {ex.Message}", ex);
        }

        // The filter matches the recorded URL as a relative pattern (api/*) or against the whole URL (*/api/*, https://host/*).
        var used = entries.Where(e => urlFilter is null
            || HttpMock.UrlMatches(e.Url.ToString(), e.Url.PathAndQuery.TrimStart('/'), urlFilter)
            || HttpMock.UrlMatches(e.Url.ToString(), e.Url.ToString(), urlFilter)).ToList();

        // Later rules are tried first: per request, register the last answer (repeating) first, then the earlier ones once each.
        // A request matches its recording exactly (method, path and query, and the host with matchHost), compared in the
        // form Uri normalizes both to, so escaping does not matter and a recording without a query answers only that URL.
        var parts = matchHost ? UriComponents.SchemeAndServer | UriComponents.PathAndQuery : UriComponents.PathAndQuery;
        foreach (var group in used.GroupBy(e => (e.Method, Key: e.Url.GetComponents(parts, UriFormat.UriEscaped))))
        {
            var answers = group.ToList();
            var method = new HttpMethod(group.Key.Method);
            var key = group.Key.Key;
            for (var i = answers.Count - 1; i >= 0; i--)
            {
                var entry = answers[i];
                var rule = mock.Match(
                    request => request.Method == method && request.RequestUri is { IsAbsoluteUri: true } uri && string.Equals(uri.GetComponents(parts, UriFormat.UriEscaped), key, StringComparison.Ordinal),
                    $"HAR {method} {key}");
                if (entry.Status <= 0)
                {
                    rule.Fail($"The request failed when the HAR was recorded ({entry.Method} {entry.Url}).");
                }
                else
                {
                    rule.Respond(_ => entry.ToResponse());
                }

                if (i < answers.Count - 1)
                {
                    rule.Times(1);
                }
            }
        }

        return used.Count;
    }

    /// <summary>Replays a HAR read from <paramref name="har"/> (an embedded resource, for stories that also run in WebAssembly).</summary>
    public static int ReplayHar(this HttpMock mock, Stream har, string? urlFilter = null, bool matchHost = false)
    {
        ArgumentNullException.ThrowIfNull(har);
        using var reader = new StreamReader(har, Encoding.UTF8);
        return mock.ReplayHar(reader.ReadToEnd(), urlFilter, matchHost);
    }

    /// <summary>Replays a HAR file (bUnit and Interactive Server; WebAssembly cannot read the file system, so embed it instead).</summary>
    public static int ReplayHarFile(this HttpMock mock, string path, string? urlFilter = null, bool matchHost = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return mock.ReplayHar(File.ReadAllText(path), urlFilter, matchHost);
    }

    private static List<Entry> Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var list = new List<Entry>();
        foreach (var item in document.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray())
        {
            var request = item.GetProperty("request");
            var response = item.GetProperty("response");
            if (!Uri.TryCreate(request.GetProperty("url").GetString(), UriKind.Absolute, out var url))
            {
                continue;
            }

            var headers = new List<KeyValuePair<string, string>>();
            if (response.TryGetProperty("headers", out var headerArray) && headerArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var header in headerArray.EnumerateArray())
                {
                    var name = header.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(name) && !SkippedHeaders.Contains(name))
                    {
                        headers.Add(new(name, header.GetProperty("value").GetString() ?? string.Empty));
                    }
                }
            }

            byte[] body = [];
            string? mimeType = null;
            if (response.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
            {
                mimeType = content.TryGetProperty("mimeType", out var mime) ? mime.GetString() : null;
                if (content.TryGetProperty("text", out var text) && text.GetString() is { } value)
                {
                    body = content.TryGetProperty("encoding", out var encoding) && string.Equals(encoding.GetString(), "base64", StringComparison.OrdinalIgnoreCase)
                        ? Convert.FromBase64String(value)
                        : Encoding.UTF8.GetBytes(value);
                }
            }

            list.Add(new Entry(
                request.GetProperty("method").GetString() is { Length: > 0 } verb && !string.IsNullOrWhiteSpace(verb) ? verb.Trim() : "GET",
                url,
                response.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number ? status.GetInt32() : 0,
                headers,
                body,
                mimeType));
        }

        return list;
    }

    private sealed record Entry(string Method, Uri Url, int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body, string? MimeType)
    {
        public HttpResponseMessage ToResponse()
        {
            var response = new HttpResponseMessage((HttpStatusCode)Status) { Content = new ByteArrayContent(Body) };
            if (!string.IsNullOrEmpty(MimeType) && MediaTypeHeaderValue.TryParse(MimeType, out var mediaType))
            {
                response.Content.Headers.ContentType = mediaType;
            }

            foreach (var (name, value) in Headers)
            {
                if (string.Equals(name, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    if (MediaTypeHeaderValue.TryParse(value, out var recorded))
                    {
                        response.Content.Headers.ContentType = recorded;
                    }
                }
                else if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return response;
        }
    }
}
