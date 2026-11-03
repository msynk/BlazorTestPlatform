using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace BlazorTestPlatform;

/// <summary>What the mock does with a request that no rule matches.</summary>
public enum UnmatchedRequestBehavior
{
    /// <summary>Respond <c>404 Not Found</c> (default; the request is still recorded).</summary>
    NotFound,

    /// <summary>Throw <see cref="HttpRequestException"/>, as for a network failure.</summary>
    Throw,
}

/// <summary>One request observed by an <see cref="HttpMock"/>.</summary>
public sealed record HttpRequestRecord
{
    /// <summary>Sequence number within the story.</summary>
    public int Sequence { get; init; }

    /// <summary>HTTP method (upper case).</summary>
    public required string Method { get; init; }

    /// <summary>Absolute request URL.</summary>
    public required string Url { get; init; }

    /// <summary>URL relative to the mock's base address (no leading slash), or the absolute URL when it is elsewhere.</summary>
    public required string RelativeUrl { get; init; }

    /// <summary>Request body as text, if any.</summary>
    public string? Body { get; init; }

    /// <summary>Request headers (content headers included).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Response status code (0 when the request failed or never completed).</summary>
    public int StatusCode { get; init; }

    /// <summary>Description of the rule that handled the request, or null when unmatched.</summary>
    public string? Rule { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Method} {RelativeUrl} -> {(StatusCode == 0 ? "(no response)" : StatusCode.ToString(CultureInfo.InvariantCulture))}";
}

/// <summary>
/// Story-scoped fake HTTP backend. Rules answer requests made through <see cref="HttpClient"/> and
/// <see cref="IHttpClientFactory"/> (named and typed clients) in every driver: bUnit, Interactive Server (on the
/// server) and WebAssembly (in the browser). Requests are recorded for <c>ctx.ExpectRequest(...)</c>.
/// </summary>
/// <remarks>Rules registered later take precedence, so stories can override collection defaults.</remarks>
public sealed class HttpMock
{
    private readonly List<HttpMockRule> _rules = [];
    private readonly List<HttpRequestRecord> _requests = [];

    // Sequence numbers keep increasing across ClearRequests(): an in-flight request must never share its number with a
    // later one.
    private int _lastSequence;
    private readonly Lock _gate = new();

    /// <summary>Base address of clients created by the mock, and the root for relative rule patterns. Default <c>https://localhost/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://localhost/");

    /// <summary>Behaviour for unmatched requests.</summary>
    public UnmatchedRequestBehavior Unmatched { get; set; } = UnmatchedRequestBehavior.NotFound;

    /// <summary>Clock used for <see cref="HttpMockRule.Delay"/> (the story's fake clock when it uses one).</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Recorded requests in arrival order.</summary>
    public IReadOnlyList<HttpRequestRecord> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Adds a rule for GET requests matching <paramref name="pattern"/>.</summary>
    public HttpMockRule Get(string pattern) => When(HttpMethod.Get, pattern);

    /// <summary>Adds a rule for POST requests.</summary>
    public HttpMockRule Post(string pattern) => When(HttpMethod.Post, pattern);

    /// <summary>Adds a rule for PUT requests.</summary>
    public HttpMockRule Put(string pattern) => When(HttpMethod.Put, pattern);

    /// <summary>Adds a rule for PATCH requests.</summary>
    public HttpMockRule Patch(string pattern) => When(HttpMethod.Patch, pattern);

    /// <summary>Adds a rule for DELETE requests.</summary>
    public HttpMockRule Delete(string pattern) => When(HttpMethod.Delete, pattern);

    /// <summary>
    /// Adds a rule. <paramref name="pattern"/> is relative to <see cref="BaseAddress"/> (<c>api/people</c>) or absolute,
    /// may contain <c>*</c> wildcards, and matches the query string only when it contains <c>?</c>.
    /// Pass a null method to match every method.
    /// </summary>
    public HttpMockRule When(HttpMethod? method, string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var rule = new HttpMockRule(method, pattern, null);
        lock (_gate)
        {
            _rules.Add(rule);
        }

        return rule;
    }

    /// <summary>Adds a rule for any method.</summary>
    public HttpMockRule Any(string pattern) => When(null, pattern);

    /// <summary>Adds a rule with a custom predicate.</summary>
    public HttpMockRule Match(Func<HttpRequestMessage, bool> predicate, string description = "custom predicate")
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var rule = new HttpMockRule(null, description, predicate);
        lock (_gate)
        {
            _rules.Add(rule);
        }

        return rule;
    }

    /// <summary>Creates a handler that answers from this mock.</summary>
    public HttpMessageHandler CreateHandler() => new MockHttpMessageHandler(this);

    /// <summary>
    /// Creates a client whose base address is <see cref="BaseAddress"/>. Its timeout is infinite: the mock decides how long
    /// a request takes (<c>Delay</c> on the story's clock, <c>Never()</c>), so a real-time 100 s timeout never cuts a
    /// story's pending request short.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Owned by the HttpClient.")]
    public HttpClient CreateClient() => new(CreateHandler()) { BaseAddress = BaseAddress, Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Clears recorded requests (rules are kept).</summary>
    public void ClearRequests()
    {
        lock (_gate)
        {
            _requests.Clear();
        }
    }

    /// <summary>True when a record matches a method and pattern (same rules as <see cref="When(HttpMethod?, string)"/>).</summary>
    public static bool Matches(HttpRequestRecord record, string? method, string pattern)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (method is null || string.Equals(record.Method, method, StringComparison.OrdinalIgnoreCase)) && UrlMatches(record.Url, record.RelativeUrl, pattern);
    }

    /// <summary>Serializes requests for transport across the story frame bridge (reflection-free).</summary>
    public string RequestsToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var request in Requests)
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequence", request.Sequence);
                writer.WriteString("method", request.Method);
                writer.WriteString("url", request.Url);
                writer.WriteString("relativeUrl", request.RelativeUrl);
                if (request.Body is not null)
                {
                    writer.WriteString("body", request.Body);
                }

                writer.WriteNumber("statusCode", request.StatusCode);
                if (request.Rule is not null)
                {
                    writer.WriteString("rule", request.Rule);
                }

                writer.WriteStartObject("headers");
                foreach (var (name, value) in request.Headers)
                {
                    writer.WriteString(name, value);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Parses <see cref="RequestsToJson"/> output.</summary>
    public static IReadOnlyList<HttpRequestRecord> ParseRequests(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var list = new List<HttpRequestRecord>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in h.EnumerateObject())
                {
                    headers[header.Name] = header.Value.GetString() ?? string.Empty;
                }
            }

            list.Add(new HttpRequestRecord
            {
                Sequence = item.GetProperty("sequence").GetInt32(),
                Method = item.GetProperty("method").GetString()!,
                Url = item.GetProperty("url").GetString()!,
                RelativeUrl = item.GetProperty("relativeUrl").GetString()!,
                Body = item.TryGetProperty("body", out var body) ? body.GetString() : null,
                StatusCode = item.GetProperty("statusCode").GetInt32(),
                Rule = item.TryGetProperty("rule", out var rule) ? rule.GetString() : null,
                Headers = headers,
            });
        }

        return list;
    }

    internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken, Uri? clientBaseAddress = null)
    {
        // Rules are relative to the base address of the client that sent the request (a named client configured with
        // AddHttpClient keeps its own), else to the mock's.
        var baseAddress = clientBaseAddress ?? BaseAddress;
        var url = request.RequestUri is { IsAbsoluteUri: true } absolute ? absolute : new Uri(baseAddress, request.RequestUri?.ToString() ?? string.Empty);
        var relative = Relative(url, baseAddress);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(x => x.Value)), StringComparer.OrdinalIgnoreCase);

        HttpMockRule? rule;
        int sequence;
        lock (_gate)
        {
            rule = Enumerable.Reverse(_rules).FirstOrDefault(r => r.CanHandle(request, url, relative));
            rule?.Consume();
            sequence = ++_lastSequence;
            _requests.Add(new HttpRequestRecord { Sequence = sequence, Method = request.Method.Method, Url = url.ToString(), RelativeUrl = relative, Body = body, Headers = headers, Rule = rule?.ToString() });
        }

        if (rule is null)
        {
            if (Unmatched == UnmatchedRequestBehavior.Throw)
            {
                throw new HttpRequestException($"No HTTP mock rule matches {request.Method} {relative}.");
            }

            return Complete(sequence, new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request, Content = new StringContent($"No HTTP mock rule matches {request.Method} {relative}.") });
        }

        var response = await rule.ProduceAsync(request, Time, cancellationToken).ConfigureAwait(false);
        response.RequestMessage ??= request;
        return Complete(sequence, response);
    }

    private HttpResponseMessage Complete(int sequence, HttpResponseMessage response)
    {
        lock (_gate)
        {
            // The record is gone when ClearRequests() ran while the request was in flight.
            var index = _requests.FindIndex(r => r.Sequence == sequence);
            if (index >= 0)
            {
                _requests[index] = _requests[index] with { StatusCode = (int)response.StatusCode };
            }
        }

        return response;
    }

    /// <summary>
    /// The URL relative to the directory of <see cref="BaseAddress"/>, which is how <see cref="HttpClient"/> resolves
    /// relative requests: with <c>https://api/v1</c> (no trailing slash) a request for <c>people</c> goes to
    /// <c>https://api/people</c>, relative <c>people</c>; <c>https://api/v10/x</c> is <c>v10/x</c>, never <c>0/x</c>.
    /// </summary>
    private static string Relative(Uri url, Uri baseAddress)
    {
        var baseText = new Uri(baseAddress, "./").ToString();
        var text = url.ToString();
        return text.StartsWith(baseText, StringComparison.OrdinalIgnoreCase) ? text[baseText.Length..] : text;
    }

    internal static bool UrlMatches(string absolute, string relative, string pattern)
    {
        var isAbsolutePattern = pattern.Contains("://", StringComparison.Ordinal);
        var candidate = isAbsolutePattern ? absolute : relative;
        var normalizedPattern = isAbsolutePattern ? pattern : pattern.TrimStart('/');
        if (!normalizedPattern.Contains('?', StringComparison.Ordinal))
        {
            var query = candidate.IndexOf('?', StringComparison.Ordinal);
            candidate = query < 0 ? candidate : candidate[..query];
        }

        if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(candidate, normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }

        var regex = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(candidate, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    internal sealed class MockHttpMessageHandler(HttpMock mock) : HttpMessageHandler
    {
        /// <summary>Base address of the client this handler serves, when it differs from the mock's.</summary>
        public Uri? ClientBaseAddress { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            mock.HandleAsync(request, cancellationToken, ClientBaseAddress);
    }
}

/// <summary>A response rule of an <see cref="HttpMock"/>.</summary>
public sealed class HttpMockRule
{
    private readonly HttpMethod? _method;
    private readonly string _pattern;
    private readonly Func<HttpRequestMessage, bool>? _predicate;
    private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
    private TimeSpan _delay;
    private int? _remaining;
    private bool _never;
    private Exception? _failure;

    internal HttpMockRule(HttpMethod? method, string pattern, Func<HttpRequestMessage, bool>? predicate)
    {
        _method = method;
        _pattern = pattern;
        _predicate = predicate;
    }

    /// <summary>Responds with a JSON document given as text.</summary>
    public HttpMockRule RespondJsonText(string json, int status = 200)
    {
        ArgumentNullException.ThrowIfNull(json);
        _respond = _ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return this;
    }

    /// <summary>Responds with a value serialized as JSON (web defaults).</summary>
    [RequiresUnreferencedCode("Uses reflection-based JSON serialization. Use the JsonTypeInfo overload in trimmed apps.")]
    [RequiresDynamicCode("Uses reflection-based JSON serialization. Use the JsonTypeInfo overload in AOT apps.")]
    public HttpMockRule RespondJson<T>(T value, int status = 200) =>
        RespondJsonText(JsonSerializer.Serialize(value, JsonOptions), status);

    /// <summary>Responds with a value serialized with source-generated metadata (trimming safe).</summary>
    public HttpMockRule RespondJson<T>(T value, JsonTypeInfo<T> typeInfo, int status = 200) =>
        RespondJsonText(JsonSerializer.Serialize(value, typeInfo), status);

    /// <summary>Responds with text.</summary>
    public HttpMockRule RespondText(string text, string mediaType = "text/plain", int status = 200)
    {
        _respond = _ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, mediaType) };
        return this;
    }

    /// <summary>Responds with a status code and no body.</summary>
    public HttpMockRule RespondStatus(int status)
    {
        _respond = _ => new HttpResponseMessage((HttpStatusCode)status);
        return this;
    }

    /// <summary>Responds with a custom message.</summary>
    public HttpMockRule Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond ?? throw new ArgumentNullException(nameof(respond));
        return this;
    }

    /// <summary>Simulates a network failure.</summary>
    public HttpMockRule Fail(string message = "Simulated network failure.")
    {
        _failure = new HttpRequestException(message);
        return this;
    }

    /// <summary>Never responds (until the request is cancelled): captures loading states.</summary>
    public HttpMockRule Never()
    {
        _never = true;
        return this;
    }

    /// <summary>Delays the response (on the story's clock: fake time makes this instant and deterministic).</summary>
    public HttpMockRule Delay(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        _delay = delay;
        return this;
    }

    /// <summary>Only answers the next <paramref name="count"/> matching requests, then lets earlier rules answer.</summary>
    public HttpMockRule Times(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _remaining = count;
        return this;
    }

    /// <inheritdoc />
    public override string ToString() => $"{_method?.Method ?? "*"} {_pattern}";

    internal bool CanHandle(HttpRequestMessage request, Uri url, string relative)
    {
        if (_remaining is 0)
        {
            return false;
        }

        if (_predicate is not null)
        {
            return _predicate(request);
        }

        return (_method is null || _method == request.Method) && HttpMock.UrlMatches(url.ToString(), relative, _pattern);
    }

    internal void Consume()
    {
        if (_remaining is > 0)
        {
            _remaining--;
        }
    }

    internal async Task<HttpResponseMessage> ProduceAsync(HttpRequestMessage request, TimeProvider time, CancellationToken cancellationToken)
    {
        if (_never)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, time, cancellationToken).ConfigureAwait(false);
        }

        if (_failure is not null)
        {
            throw new HttpRequestException(_failure.Message);
        }

        return _respond(request);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients (named or typed) all answer from an <see cref="HttpMock"/>. With
/// the application's services, each client keeps what <c>AddHttpClient(...)</c> configured for its name (base address,
/// default headers, timeout and delegating handlers such as authentication or resilience): only the primary handler,
/// the network, is replaced by the mock, so a story exercises the same pipeline as production.
/// </summary>
public sealed class HttpMockClientFactory : IHttpClientFactory
{
    private readonly HttpMock _mock;
    private readonly IServiceProvider? _services;

    /// <summary>Creates a factory whose clients ignore any named-client configuration.</summary>
    public HttpMockClientFactory(HttpMock mock)
        : this(mock, services: null)
    {
    }

    /// <summary>Creates a factory that applies the named-client configuration registered in <paramref name="services"/>.</summary>
    public HttpMockClientFactory(HttpMock mock, IServiceProvider? services)
    {
        _mock = mock ?? throw new ArgumentNullException(nameof(mock));
        _services = services;
    }

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Handlers are owned by the returned HttpClient.")]
    public HttpClient CreateClient(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_services?.GetService(typeof(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>))
            is not Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions> monitor)
        {
            return _mock.CreateClient();
        }

        var options = monitor.Get(name);
        var primary = new HttpMock.MockHttpMessageHandler(_mock);
        var builder = new Builder(name, _services);
        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }

        // Whatever primary handler the application configured, the story's requests never reach the network.
        if (!ReferenceEquals(builder.PrimaryHandler, primary))
        {
            builder.PrimaryHandler.Dispose();
        }

        builder.PrimaryHandler = primary;
        var client = new HttpClient(builder.Build(), disposeHandler: true) { BaseAddress = _mock.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        foreach (var configure in options.HttpClientActions)
        {
            configure(client);
        }

        primary.ClientBaseAddress = client.BaseAddress;
        return client;
    }

    /// <summary>The handler pipeline of one named client: the application's delegating handlers around the mock.</summary>
    private sealed class Builder(string name, IServiceProvider services) : Microsoft.Extensions.Http.HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; } = name;

        // Starts like the default builder, so configuration that inspects the primary handler finds the usual type.
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }
}
