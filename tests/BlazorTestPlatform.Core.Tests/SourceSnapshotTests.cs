using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

public sealed class SourceSnapshotTests
{
    private const string Source = """"
        public sealed class MenuStories : Stories<Menu>
        {
            protected override void Define(StoryCollectionBuilder<Menu> stories)
            {
                stories.Add("Open", s => s.Play(async ctx =>
                {
                    await ctx.ExpectAriaSnapshotAsync("""
                        - button "Options" [expanded]
                        - menu
                        """);
                    await ctx.ExpectAriaSnapshotAsync("- heading \"Title\" [level=2]\n- button \"Close\"", match: AriaSnapshotMatch.Contains);
                    await ctx.ExpectAriaSnapshotAsync(expectedTree);
                    await ctx.ExpectAriaSnapshotAsync("""
                        - link /Order \d+/
                        """);
                }));
            }
        }
        """";

    private static string Write(string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-source-snapshots", Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(directory, "MenuStories.cs");
        File.WriteAllText(path, text.ReplaceLineEndings("\r\n"));
        return path;
    }

    private static AriaSnapshotMismatch Mismatch(string file, int line, string expected, string actual) =>
        new() { SourceFile = file, SourceLine = line, Expected = expected, Actual = actual };

    [Fact]
    public void A_raw_literal_is_rewritten_in_place_with_its_indentation()
    {
        var path = Write(Source);

        var updates = SourceSnapshots.Update([Mismatch(path, 7, "- button \"Options\" [expanded]\n- menu", "- button \"Options\" [expanded]\n- menu:\n  - menuitem \"Rename\"")], Path.GetDirectoryName(path)!);

        Assert.True(Assert.Single(updates).Updated);
        var text = File.ReadAllText(path);
        var q = "\"\"\"";
        var expected = string.Join("\r\n",
            "            await ctx.ExpectAriaSnapshotAsync(" + q,
            "                - button \"Options\" [expanded]",
            "                - menu:",
            "                  - menuitem \"Rename\"",
            "                " + q + ");");
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Equal(Source.ReplaceLineEndings("\r\n").Split("\r\n").Length + 1, text.Split("\r\n").Length);
    }

    [Fact]
    public void A_regular_literal_becomes_a_raw_literal()
    {
        var path = Write(Source);

        var update = Assert.Single(SourceSnapshots.Update([Mismatch(path, 11, "- heading \"Title\" [level=2]\n- button \"Close\"", "- heading \"Title\" [level=2]\n- button \"Close dialog\"")], Path.GetDirectoryName(path)!));

        Assert.True(update.Updated, update.Message);
        Assert.Contains("- button \"Close dialog\"\r\n", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains(", match: AriaSnapshotMatch.Contains);", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Call_sites_that_cannot_be_rewritten_safely_are_left_alone_with_the_reason()
    {
        var path = Write(Source);
        var before = File.ReadAllText(path);

        var updates = SourceSnapshots.Update(
        [
            // Two drivers disagree: a parity bug, not an outdated snapshot.
            Mismatch(path, 7, "- button \"Options\" [expanded]\n- menu", "- menu"),
            Mismatch(path, 7, "- button \"Options\" [expanded]\n- menu", "- menu:\n  - menuitem \"Rename\""),
            // The expected tree came from a variable.
            Mismatch(path, 12, "- heading \"Elsewhere\"", "- heading \"Changed\""),
            // A pattern would be lost.
            Mismatch(path, 13, "- link /Order \\d+/", "- link \"Order 42\""),
        ], Path.GetDirectoryName(path)!);

        Assert.All(updates, u => Assert.False(u.Updated));
        Assert.Contains("disagree", updates[0].Message, StringComparison.Ordinal);
        Assert.Contains("not a string literal", updates[1].Message, StringComparison.Ordinal);
        Assert.Contains("patterns", updates[2].Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Several_call_sites_of_a_file_update_together_and_dry_runs_write_nothing()
    {
        var path = Write(Source);
        var mismatches = new[]
        {
            Mismatch(path, 7, "- button \"Options\" [expanded]\n- menu", "- button \"Options\"\n- menu"),
            Mismatch(path, 11, "- heading \"Title\" [level=2]\n- button \"Close\"", "- button \"Close\""),
        };

        Assert.All(SourceSnapshots.Update(mismatches, Path.GetDirectoryName(path)!, dryRun: true), u => Assert.True(u.Updated));
        Assert.Equal(Source.ReplaceLineEndings("\r\n"), File.ReadAllText(path));

        Assert.All(SourceSnapshots.Update(mismatches, Path.GetDirectoryName(path)!), u => Assert.True(u.Updated, u.Message));
        var text = File.ReadAllText(path);
        Assert.Contains("- button \"Options\"\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[expanded]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("- heading \"Title\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Deterministic_build_paths_resolve_against_the_source_root()
    {
        var path = Write(Source);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        var compiled = "/_/" + Path.GetFileName(Path.GetDirectoryName(path)) + "/MenuStories.cs";

        var update = Assert.Single(SourceSnapshots.Update([Mismatch(compiled, 7, "- button \"Options\" [expanded]\n- menu", "- menu")], root));

        Assert.True(update.Updated, update.Message);
        Assert.False(Assert.Single(SourceSnapshots.Update([Mismatch(compiled, 7, "- menu", "- menu")], Path.GetTempPath())).Updated);
    }

    [Fact]
    public void A_changed_source_is_detected()
    {
        var path = Write(Source);

        var update = Assert.Single(SourceSnapshots.Update([Mismatch(path, 7, "- button \"Something else\"", "- menu")], Path.GetDirectoryName(path)!));

        Assert.False(update.Updated);
        Assert.Contains("not the expected tree the run used", update.Message, StringComparison.Ordinal);
    }
}
