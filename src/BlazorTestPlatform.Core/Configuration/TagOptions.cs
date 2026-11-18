namespace BlazorTestPlatform.Configuration;

/// <summary>
/// Run options every story with a tag gets (<c>btp.json</c> → <c>tagOptions</c>), as Vitest's tag options: a timeout, the
/// slow multiplier, retries, or a skip. A story's own <c>Timeout(...)</c> wins over its tags'; when several of its tags set
/// an option, the longest timeout and the most retries apply, any <see cref="Slow"/> or <see cref="Skip"/> applies.
/// </summary>
/// <example>
/// <code>
/// "tagOptions": {
///   "network": { "retries": 3, "timeoutMilliseconds": 90000 },
///   "slow": { "slow": true },
///   "flaky-on-webkit": { "skip": "tracked in #123" }
/// }
/// </code>
/// </example>
public sealed record TagOptions
{
    /// <summary>The story timeout for runners (bUnit and browsers), unless the story sets its own.</summary>
    public int? TimeoutMilliseconds { get; init; }

    /// <summary>Triples the timeout, as <c>Slow()</c> on the story.</summary>
    public bool? Slow { get; init; }

    /// <summary>Retries for browser runs, instead of <see cref="BtpSettings.Retries"/> and <c>BrowserRunOptions.Retries</c>.</summary>
    public int? Retries { get; init; }

    /// <summary>Skips the stories with this reason.</summary>
    public string? Skip { get; init; }
}
