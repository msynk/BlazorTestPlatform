using System.Globalization;
using System.Net;
using System.Text;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Coverage;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Options for <see cref="HtmlReportWriter"/>.</summary>
public sealed class HtmlReportOptions
{
    /// <summary>Report title.</summary>
    public string Title { get; set; } = "BlazorTestPlatform report";

    /// <summary>Flake history analysis to show.</summary>
    public FlakeReport? Flakes { get; set; }

    /// <summary>Story state coverage to show.</summary>
    public StoryCoverageReport? Coverage { get; set; }

    /// <summary>Copy images next to the report (default true) so the folder is portable as a CI artifact.</summary>
    public bool CopyAssets { get; set; } = true;
}

/// <summary>
/// Writes a self-contained, dependency-free HTML report: summary, filters, visual diff viewer (side by side and
/// slider overlay), accessibility violations, performance changes, step timelines, console and network logs.
/// </summary>
public static class HtmlReportWriter
{
    /// <summary>Writes <c>index.html</c> (and <c>assets/</c>) into <paramref name="directory"/>. Returns the report path.</summary>
    public static string Write(RunSummary summary, string directory, HtmlReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        options ??= new HtmlReportOptions();
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var assets = new AssetCopier(directory, options.CopyAssets);
        var html = Render(summary, options, assets);
        var path = Path.Combine(directory, "index.html");
        File.WriteAllText(path, html, Encoding.UTF8);
        return path;
    }

    private static string Render(RunSummary summary, HtmlReportOptions options, AssetCopier assets)
    {
        var h = new StringBuilder(64 * 1024);
        h.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        h.Append("<title>").Append(E(options.Title)).Append("</title><style>").Append(Css).Append("</style></head><body>");
        h.Append("<header><div><h1>").Append(E(options.Title)).Append("</h1><p class=\"muted\">")
            .Append(E(string.Create(CultureInfo.InvariantCulture, $"{summary.Total} runs · {summary.Stories} stories · {string.Join(", ", summary.Drivers)} · {string.Join(", ", summary.HostingModels)} · generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC")))
            .Append("</p></div><span class=\"verdict ").Append(summary.Success ? "ok\">Passed" : "bad\">Failed").Append("</span></header>");

        h.Append("<section class=\"cards\">");
        Card(h, "Passed", summary.Passed, "passed");
        Card(h, "Failed", summary.Failed, "failed");
        Card(h, "Flaky", summary.Flaky, "flaky");
        Card(h, "Skipped", summary.Skipped, "skipped");
        Card(h, "Visual changes", summary.VisualChanges, summary.VisualChanges > 0 ? "failed" : "neutral");
        Card(h, "New baselines", summary.NewBaselines, "neutral");
        Card(h, "A11y failures", summary.AccessibilityFailures, summary.AccessibilityFailures > 0 ? "failed" : "neutral");
        Card(h, "Perf regressions", summary.PerformanceRegressions, summary.PerformanceRegressions > 0 ? "failed" : "neutral");
        h.Append("</section>");

        if (options.Flakes is { Entries.Count: > 0 } flakes)
        {
            h.Append("<section class=\"panel\"><h2>Flakiest over the last ").Append(flakes.Runs).Append(" runs</h2><table><thead><tr><th>Run</th><th class=\"num\">Flake rate</th><th class=\"num\">Failed</th><th class=\"num\">Flaky</th><th class=\"num\">Runs</th></tr></thead><tbody>");
            foreach (var entry in flakes.Entries.Take(20))
            {
                h.Append("<tr><td><code>").Append(E(entry.Key)).Append("</code></td><td class=\"num\">").Append(entry.FlakeRate.ToString("P0", CultureInfo.InvariantCulture))
                    .Append("</td><td class=\"num\">").Append(entry.Failures).Append("</td><td class=\"num\">").Append(entry.Flaky).Append("</td><td class=\"num\">").Append(entry.Runs).Append("</td></tr>");
            }

            h.Append("</tbody></table></section>");
        }

        if (options.Coverage is { } coverage)
        {
            RenderCoverage(h, coverage);
        }

        h.Append("<nav class=\"filters\" aria-label=\"Filter results\">");
        foreach (var (value, label) in new[] { ("all", "All"), ("failed", "Failed"), ("flaky", "Flaky"), ("visual", "Visual changes"), ("passed", "Passed"), ("skipped", "Skipped") })
        {
            h.Append("<button type=\"button\" data-filter=\"").Append(value).Append("\" aria-pressed=\"").Append(value == (summary.Failed > 0 ? "failed" : "all") ? "true" : "false").Append("\">").Append(label).Append("</button>");
        }

        h.Append("<label class=\"search\"><span class=\"sr\">Search</span><input type=\"search\" id=\"q\" placeholder=\"Filter by story, driver, mode…\"></label></nav>");

        h.Append("<main>");
        foreach (var group in summary.Results.GroupBy(r => r.Title, StringComparer.Ordinal))
        {
            h.Append("<section class=\"group\"><h2>").Append(E(group.Key)).Append("</h2>");
            foreach (var result in group)
            {
                RenderResult(h, result, assets);
            }

            h.Append("</section>");
        }

        h.Append("</main><script>").Append(Script).Append("</script></body></html>");
        return h.ToString();
    }

    private static void RenderCoverage(StringBuilder h, StoryCoverageReport coverage)
    {
        h.Append("<section class=\"panel\"><h2>Story state coverage · ").Append(coverage.Ratio.ToString("P0", CultureInfo.InvariantCulture))
            .Append(" (").Append(coverage.CoveredStates).Append('/').Append(coverage.TotalStates).Append(" states)</h2>")
            .Append("<table><thead><tr><th>Component</th><th class=\"num\">Stories</th><th class=\"num\">States</th><th>Not rendered by any story</th></tr></thead><tbody>");
        foreach (var component in coverage.Components)
        {
            var missing = component.MissingStates.ToArray();
            h.Append("<tr class=\"").Append(missing.Length > 0 ? "row-warn" : string.Empty).Append("\"><td>").Append(E(component.Title)).Append("</td><td class=\"num\">").Append(component.Stories)
                .Append("</td><td class=\"num\">").Append(component.CoveredStates).Append('/').Append(component.TotalStates).Append("</td><td>");
            h.Append(missing.Length == 0 ? "—" : string.Join(" ", missing.Select(m => "<code>" + E(m) + "</code>")));
            h.Append("</td></tr>");
        }

        foreach (var component in coverage.ComponentsWithoutStories)
        {
            h.Append("<tr class=\"row-bad\"><td><code>").Append(E(component)).Append("</code></td><td class=\"num\">0</td><td class=\"num\">—</td><td>No stories</td></tr>");
        }

        h.Append("</tbody></table></section>");
    }

    private static void RenderResult(StringBuilder h, StoryResult r, AssetCopier assets)
    {
        var outcome = r.Outcome.ToString().ToLowerInvariant();
        var tags = new List<string> { outcome };
        if (r.Visual?.IsFailure == true)
        {
            tags.Add("visual");
        }

        h.Append("<details class=\"result ").Append(outcome).Append("\" data-tags=\"").Append(string.Join(' ', tags)).Append("\" data-text=\"")
            .Append(E($"{r.Title} {r.Name} {r.Key}".ToLowerInvariant())).Append('"');
        if (r.Outcome is StoryOutcome.Failed)
        {
            h.Append(" open");
        }

        h.Append("><summary><span class=\"badge ").Append(outcome).Append("\">").Append(r.Outcome).Append("</span><strong>").Append(E(r.Name)).Append("</strong>");
        foreach (var chip in new[] { r.Driver, r.HostingModel, r.Viewport, r.Theme })
        {
            if (!string.IsNullOrEmpty(chip))
            {
                h.Append("<span class=\"chip\">").Append(E(chip)).Append("</span>");
            }
        }

        if (r.Visual is { } v)
        {
            h.Append("<span class=\"chip ").Append(v.IsFailure ? "chip-bad" : string.Empty).Append("\">visual: ").Append(E(v.Status.ToString())).Append("</span>");
        }

        if (r.Attempts > 1)
        {
            h.Append("<span class=\"chip chip-warn\">").Append(r.Attempts).Append(" attempts</span>");
        }

        h.Append("<span class=\"time\">").Append(r.DurationMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms</span></summary><div class=\"body\">");

        if (r.Error is not null)
        {
            h.Append("<pre class=\"error\">").Append(E(r.Error)).Append("</pre>");
        }

        if (r.SkipReason is not null)
        {
            h.Append("<p class=\"muted\">Skipped: ").Append(E(r.SkipReason)).Append("</p>");
        }

        if (r.AttemptErrors.Count > 0)
        {
            h.Append("<h3>Earlier attempts</h3><ul class=\"plain\">");
            foreach (var error in r.AttemptErrors)
            {
                h.Append("<li><code>").Append(E(error)).Append("</code></li>");
            }

            h.Append("</ul>");
        }

        if (r.Visual is { } visual)
        {
            RenderVisual(h, visual, assets);
        }

        if (r.Accessibility is { } a11y && a11y.Violations.Count > 0)
        {
            h.Append("<h3>Accessibility · ").Append(E(a11y.Engine)).Append("</h3><table><thead><tr><th>Rule</th><th>Impact</th><th>Help</th><th>Elements</th></tr></thead><tbody>");
            foreach (var violation in a11y.Violations)
            {
                var failing = r.AccessibilityFailures.Contains(violation.RuleId);
                h.Append("<tr class=\"").Append(failing ? "row-bad" : string.Empty).Append("\"><td><code>").Append(E(violation.RuleId)).Append("</code></td><td>").Append(violation.Impact).Append("</td><td>");
                if (violation.HelpUrl is not null)
                {
                    h.Append("<a href=\"").Append(E(violation.HelpUrl)).Append("\" target=\"_blank\" rel=\"noopener\">").Append(E(violation.Help)).Append("</a>");
                }
                else
                {
                    h.Append(E(violation.Help));
                }

                h.Append("</td><td>");
                foreach (var node in violation.Nodes.Take(5))
                {
                    h.Append("<div><code>").Append(E(node.Target)).Append("</code></div>");
                }

                h.Append("</td></tr>");
            }

            h.Append("</tbody></table>");
        }

        if (r.Accessibility is { Waivers.Count: > 0 } waived)
        {
            h.Append("<h3>Waived accessibility rules</h3><table><thead><tr><th>Rule</th><th>Source</th><th>Justification</th><th>Expires</th></tr></thead><tbody>");
            foreach (var waiver in waived.Waivers)
            {
                var source = waiver.Source switch
                {
                    AccessibilityWaiverSource.Story => "story",
                    AccessibilityWaiverSource.Policy => "disabled for all stories",
                    _ => waiver.TargetContains is null ? "suppression" : $"suppression ({waiver.TargetContains})",
                };
                h.Append("<tr><td><code>").Append(E(waiver.RuleId)).Append("</code></td><td>").Append(E(source)).Append("</td><td>").Append(E(waiver.Justification ?? "—"))
                    .Append("</td><td>").Append(waiver.Expires?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—").Append("</td></tr>");
            }

            h.Append("</tbody></table>");
        }

        if (r.Performance is { } perf)
        {
            h.Append("<h3>Render performance</h3><p>").Append(E(perf.ToString())).Append("</p>");
            if (r.PerformanceComparison is { BaselineMissing: false } comparison)
            {
                h.Append("<table><thead><tr><th>Metric</th><th class=\"num\">Baseline</th><th class=\"num\">Current</th><th class=\"num\">Change</th></tr></thead><tbody>");
                foreach (var change in comparison.Changes)
                {
                    h.Append("<tr class=\"").Append(change.FailsBuild ? "row-bad" : change.IsImprovement ? "row-good" : string.Empty).Append("\"><td>").Append(E(change.Metric))
                        .Append("</td><td class=\"num\">").Append(change.Baseline.ToString("N0", CultureInfo.InvariantCulture))
                        .Append("</td><td class=\"num\">").Append(change.Current.ToString("N0", CultureInfo.InvariantCulture))
                        .Append("</td><td class=\"num\">").Append(double.IsInfinity(change.ChangeRatio) ? "new" : change.ChangeRatio.ToString("+0.#%;-0.#%;0%", CultureInfo.InvariantCulture)).Append("</td></tr>");
                }

                h.Append("</tbody></table>");
            }

            foreach (var violation in r.BudgetViolations)
            {
                h.Append("<p class=\"bad\">Budget exceeded: ").Append(E(violation.ToString())).Append("</p>");
            }
        }

        if (r.Steps.Count > 0)
        {
            h.Append("<h3>Timeline</h3><ol class=\"steps\">");
            RenderSteps(h, r.Steps);
            h.Append("</ol>");
        }

        var errors = r.Console.Where(c => c.Level is "error" or "pageerror" or "warning").ToArray();
        if (r.Console.Count > 0)
        {
            h.Append("<details class=\"sub\"").Append(errors.Length > 0 && r.Outcome == StoryOutcome.Failed ? " open" : string.Empty).Append("><summary>Console (").Append(r.Console.Count).Append(errors.Length > 0 ? $", {errors.Length} warnings/errors" : string.Empty).Append(")</summary><table><tbody>");
            foreach (var entry in r.Console)
            {
                h.Append("<tr class=\"").Append(entry.Level is "error" or "pageerror" ? "row-bad" : string.Empty).Append("\"><td class=\"num\">").Append(entry.AtMilliseconds.ToString("0", CultureInfo.InvariantCulture))
                    .Append(" ms</td><td>").Append(E(entry.Level)).Append("</td><td><code>").Append(E(entry.Text)).Append("</code></td></tr>");
            }

            h.Append("</tbody></table></details>");
        }

        if (r.HttpRequests.Count > 0)
        {
            var httpErrors = r.HttpRequests.Count(q => q.StatusCode is 0 or >= 400);
            h.Append("<details class=\"sub\"").Append(r.Outcome == StoryOutcome.Failed ? " open" : string.Empty).Append("><summary>Mocked HTTP (").Append(r.HttpRequests.Count)
                .Append(httpErrors > 0 ? $", {httpErrors} errors" : string.Empty).Append(")</summary><table><tbody>");
            foreach (var request in r.HttpRequests)
            {
                h.Append("<tr class=\"").Append(request.Rule is null ? "row-bad" : string.Empty).Append("\"><td>").Append(E(request.Method)).Append("</td><td><code>").Append(E(request.RelativeUrl))
                    .Append("</code></td><td>").Append(request.StatusCode == 0 ? "-" : request.StatusCode.ToString(CultureInfo.InvariantCulture)).Append("</td><td class=\"muted\">")
                    .Append(E(request.Rule ?? "no matching rule")).Append("</td></tr>");
            }

            h.Append("</tbody></table></details>");
        }

        if (r.Network.Count > 0)
        {
            var failedRequests = r.Network.Count(n => n.Failure is not null || n.Status >= 400);
            h.Append("<details class=\"sub\"><summary>Network (").Append(r.Network.Count).Append(failedRequests > 0 ? $", {failedRequests} failed" : string.Empty).Append(")</summary><table><tbody>");
            foreach (var entry in r.Network.Take(300))
            {
                h.Append("<tr class=\"").Append(entry.Failure is not null || entry.Status >= 400 ? "row-bad" : string.Empty).Append("\"><td class=\"num\">")
                    .Append(entry.StartMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms</td><td>").Append(E(entry.Method)).Append("</td><td>")
                    .Append(entry.Status == 0 ? E(entry.Failure ?? "-") : entry.Status.ToString(CultureInfo.InvariantCulture)).Append("</td><td class=\"num\">")
                    .Append(entry.DurationMilliseconds?.ToString("0", CultureInfo.InvariantCulture) ?? "-").Append("</td><td><code>").Append(E(entry.Url)).Append("</code></td></tr>");
            }

            h.Append("</tbody></table></details>");
        }

        if (r.Artifacts.Count > 0)
        {
            h.Append("<h3>Artifacts</h3><ul class=\"plain\">");
            foreach (var artifact in r.Artifacts)
            {
                var link = artifact.Kind is "screenshot" or "diff" ? assets.Copy(artifact.Path) : assets.Link(artifact.Path);
                h.Append("<li><span class=\"chip\">").Append(E(artifact.Kind)).Append("</span> ");
                if (link is not null)
                {
                    h.Append("<a href=\"").Append(E(link)).Append("\">").Append(E(Path.GetFileName(artifact.Path))).Append("</a>");
                }
                else
                {
                    h.Append(E(artifact.Path));
                }

                if (artifact.Description is not null)
                {
                    h.Append(" <span class=\"muted\">").Append(E(artifact.Description)).Append("</span>");
                }

                h.Append("</li>");
            }

            h.Append("</ul>");
        }

        if (r.Markup is { Length: > 0 } markup && r.Outcome == StoryOutcome.Failed)
        {
            h.Append("<details class=\"sub\"><summary>Final markup</summary><pre>").Append(E(markup)).Append("</pre></details>");
        }

        h.Append("</div></details>");
    }

    private static void RenderVisual(StringBuilder h, VisualResult visual, AssetCopier assets)
    {
        h.Append("<h3>Visual · <code>").Append(E(visual.SnapshotName)).Append("</code> · ").Append(E(visual.Status.ToString()));
        if (visual.DiffPixels > 0)
        {
            h.Append(" · ").Append(visual.DiffPixels.ToString("N0", CultureInfo.InvariantCulture)).Append(" px (").Append(visual.DiffRatio.ToString("P2", CultureInfo.InvariantCulture)).Append(')');
        }

        h.Append("</h3>");
        if (visual.Note is { } note)
        {
            h.Append("<p class=\"muted\">").Append(E(note)).Append("</p>");
        }

        var baseline = visual.BaselinePath is { } b && File.Exists(b) ? assets.Copy(b) : null;
        var actual = visual.ActualPath is { } a && File.Exists(a) ? assets.Copy(a) : null;
        var diff = visual.DiffPath is { } d && File.Exists(d) ? assets.Copy(d) : null;

        if (actual is null && diff is null)
        {
            if (baseline is not null)
            {
                h.Append("<figure class=\"shot\"><img loading=\"lazy\" src=\"").Append(E(baseline)).Append("\" alt=\"Baseline for ").Append(E(visual.SnapshotName)).Append("\"><figcaption>Baseline</figcaption></figure>");
            }

            return;
        }

        h.Append("<div class=\"compare\">");
        foreach (var (label, src) in new[] { ("Expected", baseline), ("Actual", actual), ("Diff", diff) })
        {
            if (src is not null)
            {
                h.Append("<figure class=\"shot\"><img loading=\"lazy\" src=\"").Append(E(src)).Append("\" alt=\"").Append(label).Append(" image for ").Append(E(visual.SnapshotName)).Append("\"><figcaption>").Append(label).Append("</figcaption></figure>");
            }
        }

        h.Append("</div>");
        if (baseline is not null && actual is not null)
        {
            h.Append("<div class=\"slider\" style=\"--pos:50%\"><img src=\"").Append(E(baseline)).Append("\" alt=\"\"><div class=\"top\"><img src=\"").Append(E(actual))
                .Append("\" alt=\"\"></div><label><span class=\"sr\">Reveal actual</span><input type=\"range\" min=\"0\" max=\"100\" value=\"50\"></label></div><p class=\"muted\">Drag to compare expected (left) with actual (right). Approve intended changes with <code>btp approve</code>.</p>");
        }
    }

    private static void RenderSteps(StringBuilder h, IReadOnlyList<StepRecord> steps)
    {
        foreach (var step in steps)
        {
            h.Append("<li class=\"").Append(step.Failed ? "step-bad" : string.Empty).Append("\"><span class=\"kind\">").Append(step.Kind.ToString().ToLowerInvariant()).Append("</span> ")
                .Append(E(step.Title)).Append(" <span class=\"muted\">+").Append(step.StartMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms · ")
                .Append(step.DurationMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms</span>");
            if (step.Error is not null)
            {
                h.Append("<div class=\"bad\">").Append(E(step.Error)).Append("</div>");
            }

            if (step.Children.Count > 0)
            {
                h.Append("<ol>");
                RenderSteps(h, step.Children);
                h.Append("</ol>");
            }

            h.Append("</li>");
        }
    }

    private static void Card(StringBuilder h, string label, int value, string tone) =>
        h.Append("<div class=\"card ").Append(tone).Append("\"><span>").Append(label).Append("</span><strong>").Append(value).Append("</strong></div>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed class AssetCopier(string reportDirectory, bool copy)
    {
        private readonly Dictionary<string, string> _copied = new(StringComparer.OrdinalIgnoreCase);

        public string? Copy(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (!copy)
            {
                return Link(path);
            }

            if (_copied.TryGetValue(path, out var existing))
            {
                return existing;
            }

            var name = DeterministicRandom.StableHash(Path.GetFullPath(path)).ToString("x16", CultureInfo.InvariantCulture) + Path.GetExtension(path);
            var target = Path.Combine(reportDirectory, "assets", name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
            return _copied[path] = "assets/" + name;
        }

        public string? Link(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var relative = Path.GetRelativePath(reportDirectory, path);
            return Path.IsPathRooted(relative) ? new Uri(path).AbsoluteUri : relative.Replace('\\', '/');
        }
    }

    private const string Css = """
        :root{--bg:#f6f7f9;--surface:#fff;--border:#dfe3e8;--text:#1f2328;--muted:#59636e;--ok:#1a7f37;--bad:#cf222e;--warn:#9a6700;--accent:#5b4dd6;--soft:#ece9ff;color-scheme:light}
        @media (prefers-color-scheme:dark){:root{--bg:#0f1115;--surface:#171a21;--border:#2b303b;--text:#e6e8eb;--muted:#9aa3ae;--ok:#3fb950;--bad:#f85149;--warn:#d29922;--accent:#a59bff;--soft:#2a2550;color-scheme:dark}}
        *{box-sizing:border-box}body{margin:0;padding:24px clamp(16px,4vw,48px) 64px;background:var(--bg);color:var(--text);font:14px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        code,pre{font:12px/1.45 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}a{color:var(--accent)}
        header{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}h1{margin:0;font-size:22px}h2{font-size:15px;margin:28px 0 8px}h3{font-size:13px;margin:16px 0 6px;color:var(--muted)}
        .muted{color:var(--muted)}.bad{color:var(--bad)}.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
        .verdict{padding:6px 14px;border-radius:999px;font-weight:700}.verdict.ok{background:color-mix(in srgb,var(--ok) 15%,transparent);color:var(--ok)}.verdict.bad{background:color-mix(in srgb,var(--bad) 15%,transparent);color:var(--bad)}
        .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px;margin:20px 0}
        .card{background:var(--surface);border:1px solid var(--border);border-radius:10px;padding:12px 14px;display:flex;flex-direction:column}.card span{color:var(--muted);font-size:12px}.card strong{font-size:24px}
        .card.passed strong{color:var(--ok)}.card.failed strong{color:var(--bad)}.card.flaky strong{color:var(--warn)}
        .panel{background:var(--surface);border:1px solid var(--border);border-radius:10px;padding:4px 16px 12px}
        .filters{display:flex;flex-wrap:wrap;gap:6px;align-items:center;position:sticky;top:0;background:var(--bg);padding:8px 0;z-index:1}
        .filters button{font:inherit;padding:5px 12px;border-radius:999px;border:1px solid var(--border);background:var(--surface);color:var(--text);cursor:pointer}
        .filters button[aria-pressed=true]{background:var(--soft);color:var(--accent);border-color:var(--accent);font-weight:600}
        .search{margin-left:auto}.search input{font:inherit;padding:6px 10px;border-radius:8px;border:1px solid var(--border);background:var(--surface);color:var(--text);min-width:260px}
        .result{background:var(--surface);border:1px solid var(--border);border-radius:10px;margin:6px 0}.result>summary{display:flex;align-items:center;gap:8px;flex-wrap:wrap;padding:10px 14px;cursor:pointer;list-style:none}
        .result>summary::-webkit-details-marker{display:none}.result.failed{border-left:4px solid var(--bad)}.result.flaky{border-left:4px solid var(--warn)}
        .body{padding:0 16px 16px;border-top:1px solid var(--border)}.time{margin-left:auto;color:var(--muted);font-variant-numeric:tabular-nums}
        .badge{font-size:11px;font-weight:700;text-transform:uppercase;padding:2px 8px;border-radius:999px}.badge.passed{color:var(--ok)}.badge.failed{color:#fff;background:var(--bad)}.badge.flaky{color:#000;background:var(--warn)}.badge.skipped{color:var(--muted)}
        .chip{font-size:12px;padding:1px 8px;border-radius:999px;border:1px solid var(--border);color:var(--muted)}.chip-bad{color:var(--bad);border-color:var(--bad)}.chip-warn{color:var(--warn);border-color:var(--warn)}
        pre{white-space:pre-wrap;overflow-wrap:anywhere;background:var(--bg);border:1px solid var(--border);border-radius:8px;padding:10px 12px;max-height:420px;overflow:auto}pre.error{border-left:4px solid var(--bad)}
        table{width:100%;border-collapse:collapse;margin:6px 0}th,td{text-align:left;padding:5px 8px;border-bottom:1px solid var(--border);vertical-align:top}th{color:var(--muted);font-weight:500;font-size:12px}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
        .row-bad td{color:var(--bad)}.row-good td{color:var(--ok)}.row-warn td:last-child{color:var(--warn)}
        .compare{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px}.shot{margin:0;background:repeating-conic-gradient(#8881 0 25%,transparent 0 50%) 0/16px 16px;border:1px solid var(--border);border-radius:8px;padding:6px}
        .shot img{max-width:100%;display:block;margin:auto}.shot figcaption{font-size:12px;color:var(--muted);text-align:center;padding-top:4px}
        .slider{position:relative;display:inline-block;max-width:100%;margin-top:12px;border:1px solid var(--border);border-radius:8px;overflow:hidden}.slider img{display:block;max-width:100%}
        .slider .top{position:absolute;inset:0;overflow:hidden;clip-path:inset(0 0 0 var(--pos));border-left:0}.slider .top img{max-width:none;width:100%}
        .slider input{position:absolute;inset:auto 0 8px 0;width:calc(100% - 24px);margin:0 12px}
        .steps,.steps ol{list-style:none;padding-left:16px;margin:4px 0}.steps>li{padding-left:0}.steps li{border-left:2px solid var(--border);padding:2px 0 2px 10px}.steps .step-bad{border-left-color:var(--bad)}
        .kind{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}.plain{margin:4px 0;padding-left:18px}
        details.sub{margin-top:10px}details.sub>summary{cursor:pointer;color:var(--muted)}
        .hidden{display:none}
        """;

    private const string Script = """
        (function(){
          var buttons=[].slice.call(document.querySelectorAll('[data-filter]'));var q=document.getElementById('q');
          function current(){var b=buttons.find(function(x){return x.getAttribute('aria-pressed')==='true'});return b?b.dataset.filter:'all'}
          function apply(){var f=current(),t=(q.value||'').toLowerCase();
            document.querySelectorAll('.result').forEach(function(r){var tags=r.dataset.tags.split(' ');var ok=(f==='all'||tags.indexOf(f)>=0)&&(!t||r.dataset.text.indexOf(t)>=0);r.classList.toggle('hidden',!ok)});
            document.querySelectorAll('.group').forEach(function(g){g.classList.toggle('hidden',!g.querySelector('.result:not(.hidden)'))});}
          buttons.forEach(function(b){b.addEventListener('click',function(){buttons.forEach(function(x){x.setAttribute('aria-pressed',x===b?'true':'false')});apply()})});
          q.addEventListener('input',apply);
          document.querySelectorAll('.slider input').forEach(function(i){i.addEventListener('input',function(){i.closest('.slider').style.setProperty('--pos',i.value+'%')})});
          apply();
        })();
        """;
}
