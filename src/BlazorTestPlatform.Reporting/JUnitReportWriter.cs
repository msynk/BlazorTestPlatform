using System.Globalization;
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
                new XAttribute("name", group.Key),
                new XAttribute("tests", group.Count()),
                new XAttribute("failures", group.Count(r => r.Outcome == StoryOutcome.Failed)),
                new XAttribute("skipped", group.Count(r => r.Outcome == StoryOutcome.Skipped)),
                new XAttribute("time", Seconds(group.Sum(r => r.DurationMilliseconds))));

            foreach (var result in group)
            {
                var testCase = new XElement("testcase",
                    new XAttribute("classname", group.Key.Replace('/', '.')),
                    new XAttribute("name", $"{result.Name} [{Cell(result)}]"),
                    new XAttribute("time", Seconds(result.DurationMilliseconds)));

                switch (result.Outcome)
                {
                    case StoryOutcome.Failed:
                        testCase.Add(new XElement("failure",
                            new XAttribute("message", FirstLine(result.Error ?? "failed")),
                            new XAttribute("type", result.ErrorType ?? "StoryFailure"),
                            new XCData(string.Join(Environment.NewLine, new[] { result.Error, result.StackTrace }.Where(s => s is not null)))));
                        break;
                    case StoryOutcome.Skipped:
                        testCase.Add(new XElement("skipped", new XAttribute("message", result.SkipReason ?? "skipped")));
                        break;
                    case StoryOutcome.Flaky:
                        testCase.Add(new XElement("system-out", new XCData($"FLAKY: passed after {result.Attempts} attempts.{Environment.NewLine}{string.Join(Environment.NewLine, result.AttemptErrors)}")));
                        break;
                }

                foreach (var artifact in result.Artifacts)
                {
                    testCase.Add(new XElement("system-out", new XCData($"[[ATTACHMENT|{artifact.Path}]]")));
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
        string.Join(", ", new[] { result.Driver, result.HostingModel, result.Viewport, result.Theme }.Where(p => !string.IsNullOrEmpty(p)));

    private static string Seconds(double milliseconds) => (milliseconds / 1000).ToString("0.###", CultureInfo.InvariantCulture);

    private static string FirstLine(string value)
    {
        var index = value.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? value : value[..index].TrimEnd('\r');
    }
}
