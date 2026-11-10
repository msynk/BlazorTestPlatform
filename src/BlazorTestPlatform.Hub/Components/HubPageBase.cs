using System.Globalization;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Hub.Components;

/// <summary>Base of every hub page: access check and the current data snapshot.</summary>
public abstract class HubPageBase : ComponentBase
{
    /// <summary>Hub data.</summary>
    [Inject] protected HubStore Store { get; set; } = default!;

    /// <summary>Options.</summary>
    [Inject] protected HubOptions Options { get; set; } = default!;

    /// <summary>Runner.</summary>
    [Inject] protected HubRunner Runner { get; set; } = default!;

    /// <summary>Navigation.</summary>
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    /// <summary>Services, for authorization.</summary>
    [Inject] protected IServiceProvider Services { get; set; } = default!;

    /// <summary>Whether the user may see the hub.</summary>
    protected bool Allowed { get; private set; }

    /// <summary>The data snapshot for this request.</summary>
    protected HubSnapshot Data { get; private set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Allowed = await HubAccess.IsAllowedAsync(Options, Services);
        if (!Allowed)
        {
            Navigation.NotFound();
            return;
        }

        Data = Store.Current;
        await OnHubInitializedAsync();
    }

    /// <summary>Page initialization after the access check.</summary>
    protected virtual Task OnHubInitializedAsync() => Task.CompletedTask;

    /// <summary>The current URL (for form return addresses).</summary>
    protected string CurrentUrl => "/" + Navigation.ToBaseRelativePath(Navigation.Uri);

    /// <summary>Workbench link of a story, when a workbench is configured and knows it.</summary>
    protected string? WorkbenchUrl(string storyId, string? mode = null, bool play = false) =>
        Options.WorkbenchUrl is { } root && Data.Catalog.TryGet(storyId, out var story) && !story.HasTag(HubOptions.HubOnlyTag)
            ? $"{root}?story={Uri.EscapeDataString(storyId)}{(mode is null || mode == "in-memory" ? string.Empty : "&mode=" + Uri.EscapeDataString(mode))}{(play ? "&play=1" : string.Empty)}"
            : null;

    /// <summary>Commit link, when a repository URL is configured.</summary>
    protected string? CommitUrl(string? sha) => Options.RepositoryUrl is { } repo && sha is { Length: > 0 } ? $"{repo.TrimEnd('/')}/commit/{sha}" : null;
}

/// <summary>Formatting used across hub pages.</summary>
public static class Fmt
{
    /// <summary>Milliseconds as "12 ms", "1.4 s" or "2 m 05 s".</summary>
    public static string Duration(double milliseconds) => milliseconds switch
    {
        < 1000 => string.Create(CultureInfo.InvariantCulture, $"{milliseconds:0} ms"),
        < 60_000 => string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000:0.0} s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)(milliseconds / 60_000)} m {(int)(milliseconds / 1000 % 60):00} s"),
    };

    /// <summary>A ratio as a percentage.</summary>
    public static string Percent(double ratio, int decimals = 0) => ratio.ToString(decimals == 0 ? "P0" : "P" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>A number with separators.</summary>
    public static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Bytes as KB or MB.</summary>
    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.##} MB"),
    };

    /// <summary>How long ago, roughly.</summary>
    public static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        return span.TotalSeconds switch
        {
            < 0 => when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            < 60 => "just now",
            < 3600 => $"{(int)span.TotalMinutes} min ago",
            < 86400 => $"{(int)span.TotalHours} h ago",
            < 86400 * 30 => $"{(int)span.TotalDays} d ago",
            _ => when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>A date and time.</summary>
    public static string When(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The first 7 characters of a commit.</summary>
    public static string Sha(string? sha) => sha is { Length: > 7 } ? sha[..7] : sha ?? "—";

    /// <summary>CSS class of an outcome.</summary>
    public static string Css(StoryOutcome? outcome, bool quarantined = false) =>
        quarantined && outcome == StoryOutcome.Failed ? "quarantined" : outcome?.ToString().ToLowerInvariant() ?? "none";

    /// <summary>The render-mode label.</summary>
    public static string Mode(string? token) => token switch
    {
        null or "" or "memory" or "in-memory" => "in-memory",
        "ssr" => "SSR",
        "server" => "Server",
        "wasm" => "WebAssembly",
        "auto" => "Auto",
        _ => token,
    };

    /// <summary>A compact cell description: driver · mode · viewport · theme.</summary>
    public static string Cell(string driver, string? mode, string? viewport, string? theme) =>
        string.Join(" · ", new[] { driver, mode is null ? null : Mode(mode), viewport, theme }.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>The first line of a message.</summary>
    public static string FirstLine(string? text, int max = 180)
    {
        var lines = (text ?? string.Empty).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.FirstOrDefault() ?? string.Empty;

        // "1 accessibility violation(s) in x:" says what follows on the next line; keep it.
        if (line.EndsWith(':') && lines.Length > 1)
        {
            line += " " + lines[1];
        }

        return line.Length > max ? line[..max] + "…" : line;
    }

    /// <summary>Human label of a tag.</summary>
    public static string Tag(ResultTag tag) => tag switch
    {
        ResultTag.NewTest => "new test",
        ResultTag.NewFailure => "new failure",
        ResultTag.StillFailing => "still failing",
        ResultTag.AlwaysFailing => "always failing",
        ResultTag.Fixed => "fixed",
        ResultTag.Flaky => "flaky",
        ResultTag.Slower => "slower",
        ResultTag.Quarantined => "quarantined",
        _ => tag.ToString(),
    };

    /// <summary>Tone of a tag.</summary>
    public static string TagTone(ResultTag tag) => tag switch
    {
        ResultTag.NewFailure or ResultTag.AlwaysFailing or ResultTag.StillFailing => "alert",
        ResultTag.Fixed => "good",
        ResultTag.Flaky or ResultTag.Slower => "warn",
        _ => string.Empty,
    };
}
