using System.Globalization;
using System.Text;
using System.Text.Json;
using BlazorTestPlatform;
using BlazorTestPlatform.Hub;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;
using BlazorTestPlatform.Visual;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers and maps the BlazorTestPlatform hub.</summary>
public static class HubServiceCollectionExtensions
{
    /// <summary>
    /// Adds the hub's services. Also map its endpoints with <see cref="MapBlazorTestHub"/> and add its pages to the
    /// router (<c>AddAdditionalAssemblies(typeof(HubOptions).Assembly)</c>).
    /// </summary>
    public static IServiceCollection AddBlazorTestHub(this IServiceCollection services, Action<HubOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new HubOptions();
        configure(options);
        if (options.Enabled is { } enabled)
        {
            options.IsEnabled = enabled;
        }
        else if (Environment.GetEnvironmentVariable("BTP_HUB_ENABLED") is { Length: > 0 } variable)
        {
            options.IsEnabled = BlazorTestPlatform.Configuration.BtpSettings.IsTruthy(variable.Trim());
        }
        else
        {
            var environment = services.LastOrDefault(d => d.ServiceType == typeof(IHostEnvironment) && !d.IsKeyedService)?.ImplementationInstance as IHostEnvironment;
            options.IsEnabled = environment is null || !environment.IsProduction();
        }

        services.AddSingleton(options);
        services.AddSingleton<HubStore>();
        services.AddSingleton<HubRunner>();
        services.AddAuthorizationCore();
        return services;
    }

    /// <summary>
    /// Maps the hub's files, JSON API, exports and actions under <c>/_btp</c>. Actions are form posts protected by
    /// antiforgery tokens (call <c>UseAntiforgery</c>); every endpoint answers 404 when the hub is disabled or the user
    /// fails its authorization policy.
    /// </summary>
    public static IEndpointConventionBuilder MapBlazorTestHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(HubOptions.RoutePrefix);
        group.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<HubOptions>();
            return await HubAccess.IsAllowedAsync(options, context.HttpContext.RequestServices, context.HttpContext.User).ConfigureAwait(false)
                ? await next(context).ConfigureAwait(false)
                : Results.NotFound();
        });

        group.MapGet("/files/{root}/{**path}", (string root, string path, HubStore store) => ServeFile(store.Current, root, path));

        // Agent- and script-friendly JSON.
        group.MapGet("/api/summary", (HubStore store) => Json(Summary(store.Current)));
        group.MapGet("/api/runs", (HubStore store) => Json(store.Current.History.Runs.Reverse().Select(r => new
        {
            r.Run,
            Total = r.Results.Count,
            Passed = r.Count(StoryOutcome.Passed),
            r.Failed,
            Flaky = r.Count(StoryOutcome.Flaky),
            Skipped = r.Count(StoryOutcome.Skipped),
            r.Quarantined,
            r.PassRate,
        })));
        group.MapGet("/api/runs/{id}", (string id, HubStore store) => store.Current.History.Find(id) is { } run ? Json(run) : Results.NotFound());
        group.MapGet("/api/failures", (HubStore store) => Json(store.Current.Clusters));
        group.MapGet("/api/stability", (HubStore store) => Json(store.Current.Stability.Select(s => new
        {
            s.Key,
            s.Last.StoryId,
            Status = s.Status.ToString(),
            s.FlakeRate,
            s.FlakeRateLow,
            s.FlakeRateHigh,
            s.Flips,
            s.Runs,
            Tags = s.Tags.Select(t => t.ToString()),
        })));
        group.MapGet("/api/results/{**key}", (string key, HubStore store) => store.Current.Result(Uri.UnescapeDataString(key)) is { } result ? Json(result) : Results.NotFound());
        group.MapGet("/api/live", (HubRunner runner) => Json(Live(runner.Current)));

        // Exports of the latest results.
        group.MapGet("/export/junit", (HubStore store) => Export(store.Current, "junit.xml", "application/xml", (summary, path) => JUnitReportWriter.Write(summary, path)));
        group.MapGet("/export/sarif", (HubStore store) => Export(store.Current, "results.sarif", "application/sarif+json", (summary, path) => SarifReportWriter.Write(summary, path, new SarifOptions { SourceRoot = FindRepositoryRoot(store.Current.Settings.ProjectDirectory) })));
        group.MapGet("/export/markdown", (HubStore store) => Results.Text(MarkdownSummaryWriter.Create(store.Current.Summary, null, null, store.Current.Coverage), "text/markdown; charset=utf-8"));
        group.MapGet("/export/results.csv", (HubStore store) => Results.File(Encoding.UTF8.GetBytes(Csv(store.Current)), "text/csv", "results.csv"));

        // Actions: form posts with antiforgery tokens, then back to the page.
        group.MapPost("/actions/visual", (HttpContext http, HubStore store, HubOptions options) => VisualDecisionAsync(http, store, options)).WithMetadata(new RequireAntiforgeryTokenAttribute());
        group.MapPost("/actions/quarantine", (HttpContext http, HubStore store, HubOptions options) => QuarantineAsync(http, store, options)).WithMetadata(new RequireAntiforgeryTokenAttribute());
        group.MapPost("/actions/run", (HttpContext http, HubStore store, HubRunner runner, HubOptions options) => StartRunAsync(http, store, runner, options)).WithMetadata(new RequireAntiforgeryTokenAttribute());
        group.MapPost("/actions/cancel", (HttpContext http, HubRunner runner, HubOptions options) =>
        {
            if (options.AllowChanges)
            {
                runner.Cancel();
            }

            return Results.Redirect(HubUrls.Page("live"));
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());
        return group;
    }

    private static IResult Json<T>(T value) => Results.Text(JsonSerializer.Serialize(value, BtpJson.Options), "application/json; charset=utf-8");

    private static object Summary(HubSnapshot snapshot) => new
    {
        Latest = new
        {
            snapshot.Summary.Total,
            snapshot.Summary.Passed,
            snapshot.Summary.Failed,
            snapshot.Summary.Flaky,
            snapshot.Summary.Skipped,
            snapshot.Summary.Quarantined,
            snapshot.Summary.VisualChanges,
            snapshot.Summary.AccessibilityFailures,
            snapshot.Summary.PerformanceRegressions,
            snapshot.Summary.Stories,
        },
        Runs = snapshot.History.Runs.Count,
        Stability = snapshot.Stability.GroupBy(s => s.Status).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        UniqueErrors = snapshot.Clusters.Count(c => c.Active),
        Quarantined = snapshot.Quarantine.Entries.Count,
        Coverage = snapshot.Coverage?.Ratio,
    };

    private static object? Live(HubRunState? state) => state is null ? null : new
    {
        state.Id,
        Label = state.Request.Label,
        state.Status,
        state.Error,
        state.Total,
        state.Completed,
        state.StartedAt,
        state.FinishedAt,
        Running = state.Running.Keys.Order(StringComparer.Ordinal).ToArray(),
        Passed = state.Results.Count(r => r.Outcome == StoryOutcome.Passed),
        Failed = state.Results.Count(r => r.IsBlockingFailure),
        Flaky = state.Results.Count(r => r.Outcome == StoryOutcome.Flaky),
        Skipped = state.Results.Count(r => r.Outcome == StoryOutcome.Skipped),
        Quarantined = state.Results.Count(r => r.Outcome == StoryOutcome.Failed && r.Quarantine is not null),
        Results = state.Results.Select(r => new
        {
            r.Key,
            r.StoryId,
            r.Title,
            r.Name,
            Outcome = r.Outcome.ToString(),
            Quarantined = r.Quarantine is not null,
            Duration = Math.Round(r.DurationMilliseconds),
            Error = r.Error?.ReplaceLineEndings(" ") is { } e && e.Length > 160 ? e[..160] + "…" : r.Error?.ReplaceLineEndings(" "),
            Url = HubUrls.Result(r.Key),
        }),
    };

    private static IResult ServeFile(HubSnapshot snapshot, string root, string path)
    {
        var directory = root switch
        {
            "results" => snapshot.Settings.ResultsDirectoryPath,
            "snapshots" => snapshot.Settings.SnapshotDirectoryPath,
            _ => null,
        };
        if (directory is null)
        {
            return Results.NotFound();
        }

        var full = Path.GetFullPath(Path.Combine(directory, Uri.UnescapeDataString(path)));
        var relative = Path.GetRelativePath(directory, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || !File.Exists(full))
        {
            return Results.NotFound();
        }

        var type = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".zip" => "application/zip",
            ".html" or ".htm" => "text/plain; charset=utf-8",
            ".json" => "application/json",
            ".txt" or ".log" => "text/plain; charset=utf-8",
            _ => null,
        };
        return type is null ? Results.NotFound() : Results.File(full, type, enableRangeProcessing: false);
    }

    private static IResult Export(HubSnapshot snapshot, string name, string type, Action<RunSummary, string> write)
    {
        var path = Path.Combine(Path.GetTempPath(), $"btp-hub-{Guid.NewGuid():N}-{name}");
        try
        {
            write(snapshot.Summary, path);
            return Results.File(File.ReadAllBytes(path), type, name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Csv(HubSnapshot snapshot)
    {
        static string Cell(string? value) => value is null ? string.Empty : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal).ReplaceLineEndings(" ") + "\"";
        var csv = new StringBuilder("key,story,title,name,driver,mode,viewport,theme,outcome,quarantined,attempts,duration_ms,category,signature,error\n");
        foreach (var r in snapshot.Results.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            csv.Append(CultureInfo.InvariantCulture, $"{Cell(r.Key)},{Cell(r.StoryId)},{Cell(r.Title)},{Cell(r.Name)},{Cell(r.Driver)},{Cell(r.HostingModel)},{Cell(r.Viewport)},{Cell(r.Theme)},{r.Outcome},{r.Quarantine is not null},{r.Attempts},{r.DurationMilliseconds:0},{FailureAnalysis.Categorize(r)},{FailureAnalysis.Signature(r)},{Cell(r.Error)}\n");
        }

        return csv.ToString();
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string ReturnUrl(IFormCollection form, string fallback) =>
        form["returnUrl"].ToString() is { Length: > 0 } url && url.StartsWith(HubOptions.RoutePrefix, StringComparison.Ordinal) && !url.Contains("//", StringComparison.Ordinal)
            ? url
            : fallback;

    private static async Task<IResult> VisualDecisionAsync(HttpContext http, HubStore store, HubOptions options)
    {
        var form = await http.Request.ReadFormAsync().ConfigureAwait(false);
        var back = ReturnUrl(form, HubUrls.Page("visual"));
        if (!options.AllowChanges)
        {
            return Results.Redirect(back);
        }

        var snapshot = store.Current;
        var decision = form["decision"].ToString();
        var names = form["snapshot"].Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToHashSet(StringComparer.Ordinal);
        var candidates = snapshot.Results
            .Where(r => r.Visual is { IsFailure: true } v && names.Contains(v.SnapshotName))
            .GroupBy(r => r.Visual!.SnapshotName, StringComparer.Ordinal)
            .Select(g => g.First().Visual!)
            .ToArray();
        foreach (var visual in candidates)
        {
            if (decision == "accept" && visual.ActualPath is not null)
            {
                SnapshotStore.Approve(visual, snapshot.Settings.SnapshotDirectoryPath, snapshot.Settings.ResultsDirectoryPath);
            }
            else if (decision == "reject")
            {
                store.RecordVisualDecision(new VisualDecision(visual.SnapshotName, visual.DiffFingerprint, "rejected", DateTimeOffset.UtcNow, Environment.UserName));
            }
        }

        store.Invalidate();
        return Results.Redirect(back);
    }

    private static async Task<IResult> QuarantineAsync(HttpContext http, HubStore store, HubOptions options)
    {
        var form = await http.Request.ReadFormAsync().ConfigureAwait(false);
        var back = ReturnUrl(form, HubUrls.Page("flaky"));
        var pattern = form["pattern"].ToString().Trim();
        if (!options.AllowChanges || pattern.Length == 0)
        {
            return Results.Redirect(back);
        }

        var settings = store.Current.Settings;
        if (form["release"].ToString() == "true")
        {
            settings.Quarantine.Without(pattern).Save(settings.QuarantineFilePath);
        }
        else
        {
            var reason = form["reason"].ToString().Trim();
            DateOnly? expires = DateOnly.TryParseExact(form["expires"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
            settings.Quarantine.With(new QuarantineEntry
            {
                Pattern = pattern,
                Reason = reason.Length == 0 ? "Quarantined from the hub" : reason,
                Owner = form["owner"].ToString() is { Length: > 0 } owner ? owner : null,
                Issue = form["issue"].ToString() is { Length: > 0 } issue ? issue : null,
                Expires = expires,
                Added = DateTimeOffset.UtcNow,
            }).Save(settings.QuarantineFilePath);
        }

        store.Invalidate();
        return Results.Redirect(back);
    }

    private static async Task<IResult> StartRunAsync(HttpContext http, HubStore store, HubRunner runner, HubOptions options)
    {
        var form = await http.Request.ReadFormAsync().ConfigureAwait(false);
        if (!options.AllowChanges || !runner.CanRun || runner.IsRunning)
        {
            return Results.Redirect(HubUrls.Page("live"));
        }

        var snapshot = store.Current;
        var scope = form["scope"].ToString();
        var value = form["value"].ToString();
        var catalog = store.Catalog;
        IEnumerable<Story> stories = scope switch
        {
            "failed" => catalog.Stories.Where(s => snapshot.Results.Any(r => r.StoryId == s.Id && r.Outcome == StoryOutcome.Failed)),
            "unstable" => catalog.Stories.Where(s => snapshot.Stability.Any(k => k.Last.StoryId == s.Id && k.Status is Stability.Flaky or Stability.Broken or Stability.Failing)),
            "affected" => StoryImpact.Analyze(catalog, value.Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Stories,
            "component" => catalog.Stories.Where(s => string.Equals(s.Title, value, StringComparison.Ordinal)),
            "tag" => catalog.Stories.Where(s => s.HasTag(value)),
            "story" => catalog.Stories.Where(s => string.Equals(s.Id, value, StringComparison.Ordinal)),
            _ => catalog.Stories,
        };
        var ids = stories.Select(s => s.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length > 0)
        {
            var label = form["label"].ToString() is { Length: > 0 } text ? text : scope switch
            {
                "failed" => "Rerun failed stories",
                "unstable" => "Rerun unstable stories",
                "affected" => "Stories affected by a change",
                "component" => $"{value}",
                "tag" => $"Stories tagged {value}",
                "story" => value,
                _ => "All stories in memory",
            };
            runner.Start(new HubRunRequest { StoryIds = ids, Label = label });
        }

        return Results.Redirect(HubUrls.Page("live"));
    }
}
