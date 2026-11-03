using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Writes JUnit XML (understood by GitHub Actions, Azure DevOps, GitLab, Jenkins, TeamCity).</summary>
public static class JUnitReportWriter
{
    /// <summary>Builds the XML document.</summary>
    public static XDocument Create(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var suites = new XElement("testsuites",
            new XAttribute("name", "BlazorTestPlatform"),
            new XAttribute("tests", summary.Total),
            new XAttribute("failures", summary.Failed),
            new XAttribute("skipped", summary.Skipped),
            new XAttribute("time", Seconds(summary.TotalDurationMilliseconds)));

        foreach (var group in summary.Results.GroupBy(r => r.Title, StringComparer.Ordinal))
        {
            var suite = new XElement("testsuite",
                new XAttribute("name", Text(group.Key)),
                new XAttribute("tests", group.Count()),
                new XAttribute("failures", group.Count(r => r.Outcome == StoryOutcome.Failed)),
                new XAttribute("skipped", group.Count(r => r.Outcome == StoryOutcome.Skipped)),
                new XAttribute("time", Seconds(group.Sum(r => r.DurationMilliseconds))));

            foreach (var result in group)
            {
                var testCase = new XElement("testcase",
                    new XAttribute("classname", Text(group.Key.Replace('/', '.'))),
                    new XAttribute("name", Text($"{result.Name} [{Cell(result)}]")),
                    new XAttribute("time", Seconds(result.DurationMilliseconds)));

                switch (result.Outcome)
                {
                    case StoryOutcome.Failed:
                        testCase.Add(new XElement("failure",
                            new XAttribute("message", Text(FirstLine(result.Error ?? "failed"))),
                            new XAttribute("type", Text(result.ErrorType ?? "StoryFailure")),
                            new XCData(Text(string.Join(Environment.NewLine, new[] { result.Error, result.StackTrace }.Where(s => s is not null))))));
                        break;
                    case StoryOutcome.Skipped:
                        testCase.Add(new XElement("skipped", new XAttribute("message", Text(result.SkipReason ?? "skipped"))));
                        break;
                    case StoryOutcome.Flaky:
                        testCase.Add(new XElement("system-out", new XCData(Text($"FLAKY: passed after {result.Attempts} attempts.{Environment.NewLine}{string.Join(Environment.NewLine, result.AttemptErrors)}"))));
                        break;
                }

                foreach (var artifact in result.Artifacts)
                {
                    testCase.Add(new XElement("system-out", new XCData(Text($"[[ATTACHMENT|{artifact.Path}]]"))));
                }

                suite.Add(testCase);
            }

            suites.Add(suite);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), suites);
    }

    /// <summary>Writes the XML to a file.</summary>
    public static void Write(RunSummary summary, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Create(summary).Save(path);
    }

    internal static string Cell(StoryResult result) =>
        string.Join(", ", new[] { result.Driver, result.HostingModel, result.Viewport, result.Theme, ArgsLabel(result) }.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary><c>name=value&amp;...</c> of the args a run set, or null.</summary>
    internal static string? ArgsLabel(StoryResult result) =>
        result.Args is { Count: > 0 } args ? string.Join('&', args.Select(a => $"{a.Key}={a.Value}")) : null;

    private static string Seconds(double milliseconds) => (milliseconds / 1000).ToString("0.###", CultureInfo.InvariantCulture);

    private static readonly System.Text.RegularExpressions.Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Text XML 1.0 can carry: ANSI colour sequences (common in error output) are removed, and other characters XML
    /// forbids (control characters, unpaired surrogates) become U+FFFD, so one odd message never breaks the report.
    /// </summary>
    internal static string Text(string value)
    {
        var text = value.Contains('\x1B', StringComparison.Ordinal) ? AnsiEscape.Replace(value, string.Empty) : value;
        System.Text.StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var valid = XmlConvert.IsXmlChar(ch) || (char.IsHighSurrogate(ch) && i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], ch));
            if (valid && char.IsHighSurrogate(ch))
            {
                builder?.Append(ch).Append(text[i + 1]);
                i++;
                continue;
            }

            if (!valid)
            {
                builder ??= new System.Text.StringBuilder(text, 0, i, text.Length);
                builder.Append('\uFFFD');
            }
            else
            {
                builder?.Append(ch);
            }
        }

        return builder?.ToString() ?? text;
    }

    private static string FirstLine(string value)
    {
        var index = value.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? value : value[..index].TrimEnd('\r');
    }
}
