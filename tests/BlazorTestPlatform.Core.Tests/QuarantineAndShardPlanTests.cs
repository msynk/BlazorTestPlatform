using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

public sealed class QuarantineAndShardPlanTests
{
    private static StoryResult Failed(string id, string key) => new()
    {
        Key = key,
        StoryId = id,
        Title = "T",
        Name = "N",
        Driver = "chromium",
        Outcome = StoryOutcome.Failed,
        Error = "boom",
    };

    [Fact]
    public void Entries_match_story_ids_keys_and_globs_until_they_expire()
    {
        var list = QuarantineList.From(
        [
            new QuarantineEntry { Pattern = "data-people-*", Reason = "Flaky backend mock, #12" },
            new QuarantineEntry { Pattern = "*|webkit|*", Reason = "WebKit font hinting, #13", Expires = new DateOnly(2026, 6, 30) },
        ]);

        Assert.NotNull(list.Find("data-people-directory--loaded", "data-people-directory--loaded|chromium"));
        Assert.Null(list.Find("inputs-button--primary", "inputs-button--primary|chromium|desktop|light|server"));
        Assert.NotNull(list.Find("inputs-button--primary", "inputs-button--primary|webkit|desktop|light|server", new DateOnly(2026, 6, 30)));
        Assert.Null(list.Find("inputs-button--primary", "inputs-button--primary|webkit|desktop|light|server", new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void Apply_marks_only_failures()
    {
        var list = QuarantineList.From([new QuarantineEntry { Pattern = "a", Reason = "r" }]);
        var failed = list.Apply(Failed("a", "a|bunit"));
        Assert.Equal("r", failed.Quarantine?.Reason);
        Assert.False(failed.IsBlockingFailure);
        Assert.Null(list.Apply(Failed("a", "a|bunit") with { Outcome = StoryOutcome.Passed }).Quarantine);
        Assert.True(list.Apply(Failed("b", "b|bunit")).IsBlockingFailure);
    }

    [Fact]
    public void The_file_round_trips_and_is_loaded_with_the_settings()
    {
        var directory = Directory.CreateTempSubdirectory("btp-quarantine-").FullName;
        try
        {
            var path = Path.Combine(directory, QuarantineList.DefaultFileName);
            QuarantineList.Empty
                .With(new QuarantineEntry { Pattern = "a", Reason = "first" })
                .With(new QuarantineEntry { Pattern = "b", Reason = "second", Owner = "@ui-team", Issue = "#7", Expires = new DateOnly(2027, 1, 1) })
                .With(new QuarantineEntry { Pattern = "A", Reason = "replaced" })
                .Save(path);

            var settings = BtpSettings.Load(directory, new Dictionary<string, string?>());
            Assert.Equal(path, settings.QuarantineFilePath);
            Assert.Equal(["b", "A"], settings.Quarantine.Entries.Select(e => e.Pattern).ToArray());
            Assert.Equal(new DateOnly(2027, 1, 1), settings.Quarantine.Entries[0].Expires);
            Assert.Equal(["b"], settings.Quarantine.Without("a").Entries.Select(e => e.Pattern).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Entries_need_a_reason_and_a_malformed_file_names_itself()
    {
        Assert.Throws<ArgumentException>(() => QuarantineList.From([new QuarantineEntry { Pattern = "a", Reason = " " }]));
        var path = Path.Combine(Path.GetTempPath(), $"btp-bad-quarantine-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => QuarantineList.Load(path));
            Assert.Contains(path, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Shard_plan_balances_by_duration_longest_first()
    {
        var durations = new Dictionary<string, double> { ["a"] = 70, ["b"] = 50, ["c"] = 40, ["d"] = 30, ["e"] = 10 };
        var plan = ShardPlan.Create(["a", "b", "c", "d", "e", "unknown"], durations, 2);

        Assert.Equal(2, plan.Shards.Count);
        Assert.Equal(["unknown"], plan.EstimatedWithoutHistory);
        Assert.Equal(70 + 50 + 40 + 30 + 10 + 40, plan.TotalMilliseconds);
        Assert.True(plan.MakespanMilliseconds <= 125, plan.ToString());
        Assert.True(plan.Balance > 0.9, plan.ToString());
        Assert.Equal(6, plan.Shards.Sum(s => s.Keys.Count));
        Assert.NotNull(plan.ShardOf("e"));

        // Deterministic.
        Assert.Equal(plan.Shards.Select(s => string.Join(",", s.Keys)), ShardPlan.Create(["e", "d", "c", "b", "a", "unknown"], durations, 2).Shards.Select(s => string.Join(",", s.Keys)));
    }
}
