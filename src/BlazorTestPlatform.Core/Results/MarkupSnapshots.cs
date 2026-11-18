using System.Text;
using System.Text.RegularExpressions;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Results;

/// <summary>Replaces what changes between runs (ids, dates) in a markup snapshot before it is compared.</summary>
/// <param name="Pattern">What to replace.</param>
/// <param name="Replacement">What to put instead (may use <c>$1</c>-style groups).</param>
public sealed record MarkupScrubber(Regex Pattern, string Replacement)
{
    /// <summary>Applies the scrubber.</summary>
    public string Apply(string markup) => Pattern.Replace(markup ?? throw new ArgumentNullException(nameof(markup)), Replacement);
}

/// <summary>What comparing a markup snapshot with its committed file found.</summary>
public enum MarkupSnapshotStatus
{
    /// <summary>The snapshot equals the committed file.</summary>
    Matched,

    /// <summary>There was no file; it was written (update mode <c>missing</c> or higher).</summary>
    Written,

    /// <summary>The file differed and was overwritten (update mode <c>changed</c> or <c>all</c>).</summary>
    Updated,

    /// <summary>There is no file and the update mode does not allow writing one (CI).</summary>
    Missing,

    /// <summary>The snapshot differs from the committed file.</summary>
    Mismatched,
}

/// <summary>The outcome of <see cref="MarkupSnapshots.Check"/>.</summary>
/// <param name="Status">What was found.</param>
/// <param name="Path">The committed snapshot file.</param>
/// <param name="ReceivedPath">The file with what the run rendered, for a missing or mismatched snapshot.</param>
/// <param name="Diff">For a mismatch, the changed lines (<c>-</c> committed, <c>+</c> rendered).</param>
/// <param name="Previous">For <see cref="MarkupSnapshotStatus.Updated"/>, the content that was overwritten.</param>
public sealed record MarkupSnapshotCheck(MarkupSnapshotStatus Status, string Path, string? ReceivedPath = null, string? Diff = null, string? Previous = null);

/// <summary>
/// Markup snapshots (Verify's and Jest's snapshot files for HTML): a story's rendered markup, normalized by the driver and
/// scrubbed, committed next to its visual baselines as <c>{story-id}/{name}.markup.html</c>. A run that renders something
/// else writes <c>{name}.markup.received.html</c> beside it; <c>btp snapshots accept</c> or
/// <c>BTP_UPDATE_SNAPSHOTS=changed</c> make it the new snapshot.
/// </summary>
public static partial class MarkupSnapshots
{
    /// <summary>The suffix of committed snapshots.</summary>
    public const string Suffix = ".markup.html";

    /// <summary>The suffix of the files a mismatched run leaves beside them.</summary>
    public const string ReceivedSuffix = ".markup.received.html";

    /// <summary>Scrubbers every snapshot gets: GUIDs become <c>{guid}</c>.</summary>
    public static IReadOnlyList<MarkupScrubber> DefaultScrubbers { get; } = [new(GuidPattern(), "{guid}")];

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    /// <summary>Applies <paramref name="scrubbers"/> in order.</summary>
    public static string Scrub(string markup, IEnumerable<MarkupScrubber> scrubbers)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentNullException.ThrowIfNull(scrubbers);
        return scrubbers.Aggregate(markup, (text, scrubber) => scrubber.Apply(text));
    }

    /// <summary>
    /// The committed file of a snapshot: <c>{snapshot directory}/{story-id}/{name}[.{variant}].markup.html</c>. The variant
    /// separates runs of one story that render differently (a theme, arg overrides).
    /// </summary>
    public static string PathFor(BtpSettings settings, string storyId, string name, string? variant = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(storyId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Path.Combine(settings.SnapshotDirectoryPath, storyId, name + (string.IsNullOrEmpty(variant) ? string.Empty : "." + variant) + Suffix);
    }

    /// <summary>
    /// Compares <paramref name="actual"/> with the file at <paramref name="path"/>, writing or overwriting it as
    /// <paramref name="mode"/> allows. A missing or mismatched snapshot leaves the rendered markup in a
    /// <see cref="ReceivedSuffix"/> file; a match removes a stale one. Line endings are not significant.
    /// </summary>
    public static MarkupSnapshotCheck Check(string path, string actual, SnapshotUpdateMode mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(actual);
        var received = ReceivedPathFor(path);
        actual = Normalize(actual);
        if (!File.Exists(path))
        {
            if (mode == SnapshotUpdateMode.None)
            {
                Write(received, actual);
                return new MarkupSnapshotCheck(MarkupSnapshotStatus.Missing, path, received);
            }

            Write(path, actual);
            Delete(received);
            return new MarkupSnapshotCheck(MarkupSnapshotStatus.Written, path);
        }

        var expected = Normalize(File.ReadAllText(path));
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Delete(received);
            return new MarkupSnapshotCheck(MarkupSnapshotStatus.Matched, path);
        }

        if (mode is SnapshotUpdateMode.Changed or SnapshotUpdateMode.All)
        {
            Write(path, actual);
            Delete(received);
            return new MarkupSnapshotCheck(MarkupSnapshotStatus.Updated, path, Previous: expected);
        }

        Write(received, actual);
        return new MarkupSnapshotCheck(MarkupSnapshotStatus.Mismatched, path, received, Diff(expected, actual));
    }

    /// <summary>Undoes a write by <see cref="Check"/> (a run that failed later must not leave its markup as the reference).</summary>
    public static void Revert(MarkupSnapshotCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        switch (check.Status)
        {
            case MarkupSnapshotStatus.Written:
                Delete(check.Path);
                break;
            case MarkupSnapshotStatus.Updated when check.Previous is not null:
                Write(check.Path, check.Previous);
                break;
        }
    }

    /// <summary>
    /// Makes every <see cref="ReceivedSuffix"/> file under <paramref name="snapshotDirectory"/> the committed snapshot
    /// (<c>btp snapshots accept</c>). Returns the snapshots accepted (or that would be, with <paramref name="dryRun"/>). A
    /// received file stays until its snapshot matches or is accepted, so one left by a story since renamed or removed is
    /// accepted too: check the list (or <paramref name="dryRun"/>) and delete such files.
    /// </summary>
    public static IReadOnlyList<string> Accept(string snapshotDirectory, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshotDirectory);
        if (!Directory.Exists(snapshotDirectory))
        {
            return [];
        }

        var accepted = new List<string>();
        foreach (var received in Directory.EnumerateFiles(snapshotDirectory, "*" + ReceivedSuffix, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var path = received[..^ReceivedSuffix.Length] + Suffix;
            if (!dryRun)
            {
                File.Move(received, path, overwrite: true);
            }

            accepted.Add(path);
        }

        return accepted;
    }

    /// <summary>
    /// The lines that differ between <paramref name="expected"/> and <paramref name="actual"/>, with a line of context
    /// around each change: <c>-</c> for lines only in the expected text, <c>+</c> for lines only in the actual one.
    /// At most <paramref name="maxLines"/> lines are shown.
    /// </summary>
    public static string Diff(string expected, string actual, int maxLines = 40)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var a = Normalize(expected).TrimEnd('\n').Split('\n');
        var b = Normalize(actual).TrimEnd('\n').Split('\n');

        // Longest common subsequence over lines (snapshots are small); past the bound, the first difference only.
        List<(char Kind, string Line)> edits;
        var truncated = false;
        if ((long)a.Length * b.Length <= 4_000_000)
        {
            var lcs = new int[a.Length + 1, b.Length + 1];
            for (var i = a.Length - 1; i >= 0; i--)
            {
                for (var j = b.Length - 1; j >= 0; j--)
                {
                    lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            edits = [];
            int x = 0, y = 0;
            while (x < a.Length || y < b.Length)
            {
                if (x < a.Length && y < b.Length && string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    edits.Add((' ', a[x]));
                    x++;
                    y++;
                }
                else if (y < b.Length && (x == a.Length || lcs[x, y + 1] > lcs[x + 1, y]))
                {
                    edits.Add(('+', b[y++]));
                }
                else
                {
                    edits.Add(('-', a[x++]));
                }
            }
        }
        else
        {
            var first = 0;
            while (first < a.Length && first < b.Length && string.Equals(a[first], b[first], StringComparison.Ordinal))
            {
                first++;
            }

            edits = [.. a.Skip(first).Take(maxLines / 2).Select(l => ('-', l)), .. b.Skip(first).Take(maxLines / 2).Select(l => ('+', l))];
            truncated = a.Length - first > maxLines / 2 || b.Length - first > maxLines / 2;
        }

        var builder = new StringBuilder();
        var shown = 0;
        var shownChanges = 0;
        var skipped = false;
        for (var k = 0; k < edits.Count && shown < maxLines; k++)
        {
            var near = edits[k].Kind != ' '
                || (k > 0 && edits[k - 1].Kind != ' ')
                || (k + 1 < edits.Count && edits[k + 1].Kind != ' ');
            if (!near)
            {
                skipped = true;
                continue;
            }

            if (skipped && builder.Length > 0)
            {
                builder.Append("  ...\n");
            }

            skipped = false;
            builder.Append(edits[k].Kind).Append(' ').Append(edits[k].Line).Append('\n');
            shown++;
            if (edits[k].Kind != ' ')
            {
                shownChanges++;
            }
        }

        if (truncated || edits.Count(e => e.Kind != ' ') > shownChanges)
        {
            builder.Append("  (more changes)\n");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string ReceivedPathFor(string path) =>
        path.EndsWith(Suffix, StringComparison.Ordinal) ? path[..^Suffix.Length] + ReceivedSuffix : path + ".received";

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    // Through a temporary file in the same directory, so an interrupted run never leaves a truncated snapshot.
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            Delete(temporary);
        }
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
