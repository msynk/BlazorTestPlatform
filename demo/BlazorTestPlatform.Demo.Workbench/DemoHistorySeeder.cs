using BlazorTestPlatform.Hub;

namespace BlazorTestPlatform.Demo.Workbench;

/// <summary>
/// Gives the demo's hub a history to show on first start: ten real in-memory runs of every story, the chaos stories
/// included, recorded a day apart. Nothing is invented; each run executes and records what happened. It only runs when
/// the archive is empty (delete <c>.btp/runs</c> in the demo test project to reseed) and when <c>BTP_DEMO_SEED</c> is
/// set, which the launch profile does and tests do not.
/// </summary>
internal sealed class DemoHistorySeeder(HubRunner runner, HubStore store, ILogger<DemoHistorySeeder> logger) : BackgroundService
{
    private const int Runs = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (store.Archive.Load().Count > 0 || !runner.CanRun)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        var ids = store.Catalog.Stories.Select(s => s.Id).ToArray();
        var now = DateTimeOffset.UtcNow;
        for (var run = 1; run <= Runs && !stoppingToken.IsCancellationRequested; run++)
        {
            logger.LogInformation("Seeding demo history: run {Run} of {Runs} ({Stories} stories)", run, Runs, ids.Length);
            var state = await runner.RunAsync(
                new HubRunRequest
                {
                    StoryIds = ids,
                    Label = $"Seeded demo run {run} of {Runs}",
                    Trigger = "seed",
                    RecordAt = now.AddDays(run - Runs).AddMinutes(run),
                    Properties = new Dictionary<string, string> { ["demo-run"] = run.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                },
                stoppingToken);
            if (state.Status is "error" or "cancelled")
            {
                logger.LogWarning("Seeding stopped: {Status} {Error}", state.Status, state.Error);
                return;
            }
        }

        logger.LogInformation("Demo history seeded: open /_btp");
    }
}
