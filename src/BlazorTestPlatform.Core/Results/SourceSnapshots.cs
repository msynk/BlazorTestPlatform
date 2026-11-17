using System.Text;

namespace BlazorTestPlatform.Results;

/// <summary>
/// An accessibility snapshot written in a story's source (<c>ctx.ExpectAriaSnapshotAsync("""...""")</c>) that did not match:
/// where the call is, what it expected and what the driver saw. Results carry them so <c>btp snapshots update-source</c>
/// can rewrite the literal in place, as Playwright's <c>updateSourceMethod</c> and insta's inline snapshots do.
/// </summary>
public sealed record AriaSnapshotMismatch
{
    /// <summary>The source file of the call, as compiled (<c>[CallerFilePath]</c>).</summary>
    public required string SourceFile { get; init; }

    /// <summary>The line of the call (<c>[CallerLineNumber]</c>).</summary>
    public required int SourceLine { get; init; }

    /// <summary>The expected tree, normalized.</summary>
    public required string Expected { get; init; }

    /// <summary>The tree the driver saw last, normalized.</summary>
    public required string Actual { get; init; }

    /// <summary>How the trees were compared.</summary>
    public AriaSnapshotMatch Match { get; init; }
}

/// <summary>The outcome of updating one call site.</summary>
/// <param name="SourceFile">The file.</param>
/// <param name="SourceLine">The call's line.</param>
/// <param name="Updated">True when the literal was (or, in a dry run, would be) rewritten.</param>
/// <param name="Message">What happened, or why the call site was left alone.</param>
public sealed record SourceSnapshotUpdate(string SourceFile, int SourceLine, bool Updated, string Message);

/// <summary>
/// Rewrites the expected accessibility trees of failed <c>ExpectAriaSnapshotAsync</c> calls in source files. A call site is
/// updated only when every run that reached it saw the same tree (drivers that disagree point at a parity bug, not at an
/// outdated snapshot), when the expected text has no name patterns or <c>/children</c> directives (they would be lost),
/// and when the first string literal after the call is the expected text (not a variable or a computed string).
/// </summary>
public static class SourceSnapshots
{
    private const string Method = "ExpectAriaSnapshotAsync";

    /// <summary>
    /// Updates the call sites of <paramref name="mismatches"/>. Paths compiled with deterministic source paths
    /// (<c>/_/...</c>) are resolved against <paramref name="sourceRoot"/>. With <paramref name="dryRun"/> nothing is written.
    /// </summary>
    public static IReadOnlyList<SourceSnapshotUpdate> Update(IEnumerable<AriaSnapshotMismatch> mismatches, string sourceRoot, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(mismatches);
        ArgumentException.ThrowIfNullOrEmpty(sourceRoot);
        var updates = new List<SourceSnapshotUpdate>();
        foreach (var file in mismatches.GroupBy(m => m.SourceFile, StringComparer.Ordinal))
        {
            var path = Resolve(file.Key, sourceRoot);
            if (path is null)
            {
                updates.AddRange(file.Select(m => m.SourceLine).Distinct().Select(line => new SourceSnapshotUpdate(file.Key, line, false, "The source file was not found (pass --source-root for results from another machine).")));
                continue;
            }

            var text = File.ReadAllText(path);
            var changed = false;

            // Bottom-up, so rewriting one literal never moves the lines of the calls still to do.
            foreach (var site in file.GroupBy(m => m.SourceLine).OrderByDescending(g => g.Key))
            {
                var actuals = site.Select(m => m.Actual).Distinct(StringComparer.Ordinal).ToArray();
                if (actuals.Length > 1)
                {
                    updates.Add(new(path, site.Key, false, $"Runs disagree on the tree ({actuals.Length} different ones): a driver or render-mode difference, not an outdated snapshot. Compare the results and update by hand."));
                    continue;
                }

                var expected = site.First().Expected;
                if (HasPatterns(expected))
                {
                    updates.Add(new(path, site.Key, false, "The expected tree has name patterns or /children directives, which a rewrite would lose. Update it by hand:" + Environment.NewLine + actuals[0]));
                    continue;
                }

                var (rewritten, problem) = Rewrite(text, site.Key, expected, actuals[0]);
                if (rewritten is null)
                {
                    updates.Add(new(path, site.Key, false, problem!));
                    continue;
                }

                text = rewritten;
                changed = true;
                updates.Add(new(path, site.Key, true, dryRun ? "would be updated" : "updated"));
            }

            if (changed && !dryRun)
            {
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: HasBom(path)));
            }
        }

        return updates.OrderBy(u => u.SourceFile, StringComparer.Ordinal).ThenBy(u => u.SourceLine).ToArray();
    }

    /// <summary>
    /// Replaces the first string literal after the <c>ExpectAriaSnapshotAsync(</c> call on <paramref name="line"/> (1-based)
    /// with <paramref name="actual"/>, when that literal holds <paramref name="expected"/>. Returns the new text, or null and
    /// the reason.
    /// </summary>
    internal static (string? Text, string? Problem) Rewrite(string text, int line, string expected, string actual)
    {
        var lineStart = LineStart(text, line);
        if (lineStart < 0)
        {
            return (null, "The call's line is past the end of the file: the source changed since the run.");
        }

        var call = text.IndexOf(Method, lineStart, StringComparison.Ordinal);
        var open = call < 0 ? -1 : text.IndexOf('(', call + Method.Length);
        if (call < 0 || open < 0 || LineOf(text, call) > line + 1)
        {
            return (null, $"No {Method} call at this line: the source changed since the run.");
        }

        var literal = FindLiteral(text, open + 1);
        if (literal is null)
        {
            return (null, "The expected tree is not a string literal at the call (a variable or computed text): update it by hand.");
        }

        var (start, end, value) = literal.Value;
        if (!string.Equals(StoryContext.NormalizeSnapshot(value), expected, StringComparison.Ordinal))
        {
            return (null, "The literal at the call is not the expected tree the run used (a variable, or the source changed): update it by hand.");
        }

        return (text[..start] + RawLiteral(text, start, actual) + text[end..], null);
    }

    // True when the expected tree uses what a literal actual tree cannot express.
    internal static bool HasPatterns(string expected)
    {
        try
        {
            AriaSnapshotTemplate.Matches(expected, expected);
            return false;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    // A raw string literal indented like the line the old literal started on (plus one level when it started mid-line).
    private static string RawLiteral(string text, int start, string actual)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var indentLength = 0;
        while (lineStart + indentLength < text.Length && text[lineStart + indentLength] is ' ' or '\t')
        {
            indentLength++;
        }

        var indent = text.Substring(lineStart, indentLength) + (start > lineStart + indentLength ? "    " : string.Empty);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var quotes = new string('"', Math.Max(3, LongestQuoteRun(actual) + 1));
        var builder = new StringBuilder().Append(quotes).Append(newline);
        foreach (var line in actual.Split('\n'))
        {
            builder.Append(indent).Append(line.TrimEnd('\r')).Append(newline);
        }

        return builder.Append(indent).Append(quotes).ToString();
    }

    private static int LongestQuoteRun(string value)
    {
        int longest = 0, run = 0;
        foreach (var c in value)
        {
            run = c == '"' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    // The first string literal from `from` on, skipping whitespace and comments only: (start, end, value).
    private static (int Start, int End, string Value)? FindLiteral(string text, int from)
    {
        var i = from;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i = text.IndexOf('\n', i) is var next and >= 0 ? next + 1 : text.Length;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = text.IndexOf("*/", i + 2, StringComparison.Ordinal) is var next and >= 0 ? next + 2 : text.Length;
            }
            else
            {
                break;
            }
        }

        if (i >= text.Length)
        {
            return null;
        }

        var start = i;
        if (text[i] == '"' && text.AsSpan(i).StartsWith("\"\"\""))
        {
            var count = 0;
            while (i < text.Length && text[i] == '"')
            {
                count++;
                i++;
            }

            var quotes = new string('"', count);
            var close = text.IndexOf(quotes, i, StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            var end = close + count;
            return (start, end, RawValue(text[i..close]));
        }

        if (text[i] == '"' || (text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"'))
        {
            var verbatim = text[i] == '@';
            i += verbatim ? 2 : 1;
            var value = new StringBuilder();
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '"')
                {
                    if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        value.Append('"');
                        i += 2;
                        continue;
                    }

                    return (start, i + 1, value.ToString());
                }

                if (!verbatim && c == '\\' && i + 1 < text.Length)
                {
                    value.Append(text[i + 1] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', var other => other });
                    i += 2;
                    continue;
                }

                if (!verbatim && c == '\n')
                {
                    return null;
                }

                value.Append(c);
                i++;
            }
        }

        return null;
    }

    // The value of a multi-line raw literal's body: the lines between the delimiters, without the closing line's indentation.
    private static string RawValue(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 2)
        {
            return body;
        }

        var indent = lines[^1];
        return string.Join('\n', lines[1..^1].Select(l => l.StartsWith(indent, StringComparison.Ordinal) ? l[indent.Length..] : l.TrimStart()));
    }

    private static int LineStart(string text, int line)
    {
        var index = 0;
        for (var current = 1; current < line; current++)
        {
            index = text.IndexOf('\n', index);
            if (index < 0)
            {
                return -1;
            }

            index++;
        }

        return index;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string? Resolve(string file, string sourceRoot)
    {
        if (File.Exists(file))
        {
            return Path.GetFullPath(file);
        }

        // Deterministic builds (ContinuousIntegrationBuild) compile paths as /_/relative/path.
        var normalized = file.Replace('\\', '/');
        if (normalized.StartsWith("/_/", StringComparison.Ordinal) || normalized.StartsWith("/_1/", StringComparison.Ordinal))
        {
            var relative = normalized[(normalized.IndexOf('/', 1) + 1)..];
            var candidate = Path.GetFullPath(Path.Combine(sourceRoot, relative));
            return File.Exists(candidate) ? candidate : null;
        }

        return null;
    }

    private static bool HasBom(string path)
    {
        Span<byte> head = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        return stream.Read(head) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }
}
