using System.Text.RegularExpressions;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

public sealed class MarkupSnapshotStoreTests
{
    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), "btp-markup-store", Guid.NewGuid().ToString("N"), "story" + MarkupSnapshots.Suffix);

    [Fact]
    public void Each_update_mode_writes_only_what_it_allows()
    {
        var path = NewPath();
        var received = path.Replace(MarkupSnapshots.Suffix, MarkupSnapshots.ReceivedSuffix, StringComparison.Ordinal);

        Assert.Equal(MarkupSnapshotStatus.Missing, MarkupSnapshots.Check(path, "<p>a</p>\n", SnapshotUpdateMode.None).Status);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(received));

        Assert.Equal(MarkupSnapshotStatus.Written, MarkupSnapshots.Check(path, "<p>a</p>\n", SnapshotUpdateMode.Missing).Status);
        Assert.False(File.Exists(received));

        // Line endings are not significant.
        Assert.Equal(MarkupSnapshotStatus.Matched, MarkupSnapshots.Check(path, "<p>a</p>\r\n", SnapshotUpdateMode.None).Status);

        var mismatch = MarkupSnapshots.Check(path, "<p>b</p>\n", SnapshotUpdateMode.Missing);
        Assert.Equal(MarkupSnapshotStatus.Mismatched, mismatch.Status);
        Assert.Equal("- <p>a</p>\n+ <p>b</p>", mismatch.Diff);
        Assert.Equal("<p>a</p>\n", File.ReadAllText(path));

        var updated = MarkupSnapshots.Check(path, "<p>b</p>\n", SnapshotUpdateMode.Changed);
        Assert.Equal(MarkupSnapshotStatus.Updated, updated.Status);
        Assert.Equal("<p>b</p>\n", File.ReadAllText(path));

        MarkupSnapshots.Revert(updated);
        Assert.Equal("<p>a</p>\n", File.ReadAllText(path));
    }

    [Fact]
    public void Diffs_show_changed_lines_with_one_line_of_context()
    {
        var expected = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"<li>{i}</li>"));
        var actual = expected.Replace("<li>3</li>", "<li>three</li>", StringComparison.Ordinal).Replace("<li>9</li>\n", string.Empty, StringComparison.Ordinal);

        Assert.Equal(
            "  <li>2</li>\n- <li>3</li>\n+ <li>three</li>\n  <li>4</li>\n  ...\n  <li>8</li>\n- <li>9</li>\n  <li>10</li>",
            MarkupSnapshots.Diff(expected, actual));
    }

    [Fact]
    public void More_changes_is_only_said_when_changes_are_hidden()
    {
        Assert.Equal("  c\n- d", MarkupSnapshots.Diff("a\nb\nc\nd", "a\nb\nc", maxLines: 2));
        Assert.Equal("- a\n- b\n  (more changes)", MarkupSnapshots.Diff("a\nb\nc", "x", maxLines: 2));
    }

    [Fact]
    public void Scrubbers_apply_in_order_after_the_default()
    {
        var scrubbed = MarkupSnapshots.Scrub(
            "<p id=\"1b4e28ba-2fa1-11d2-883f-0016d3cca427\">2026-11-18</p>",
            MarkupSnapshots.DefaultScrubbers.Append(new MarkupScrubber(new Regex(@"\d{4}-\d{2}-\d{2}"), "{date}")));

        Assert.Equal("<p id=\"{guid}\">{date}</p>", scrubbed);
    }
}
