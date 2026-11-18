using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazorTestPlatform.Coverage;

/// <summary>Line and branch coverage of one type, from a code coverage report.</summary>
/// <param name="CoveredLines">Lines run at least once.</param>
/// <param name="TotalLines">Coverable lines.</param>
/// <param name="CoveredBranches">Branch outcomes taken.</param>
/// <param name="TotalBranches">Branch outcomes.</param>
public sealed record CodeCoverage(int CoveredLines, int TotalLines, int CoveredBranches, int TotalBranches)
{
    /// <summary>Covered lines over coverable lines (1 when there are none).</summary>
    public double LineRate => TotalLines == 0 ? 1 : (double)CoveredLines / TotalLines;

    /// <summary>Taken branch outcomes over all of them (1 when there are none).</summary>
    public double BranchRate => TotalBranches == 0 ? 1 : (double)CoveredBranches / TotalBranches;
}

/// <summary>
/// Reads Cobertura code coverage reports (coverlet, <c>dotnet-coverage</c>, Microsoft.Testing.Extensions.CodeCoverage) into
/// coverage per type, so story coverage can show how much of each component's code its stories run. Classes the compiler
/// generates for a type (lambdas, async state machines, nested types) count towards that type. Several reports (one per
/// test project, or bUnit and browser runs) merge line by line: a line covered in any of them is covered.
/// </summary>
public static partial class CoberturaCoverage
{
    /// <summary>Coverage per type full name (<c>MyLib.Components.Button</c>) over the given reports' XML.</summary>
    /// <exception cref="FormatException">A text is not a Cobertura report.</exception>
    public static IReadOnlyDictionary<string, CodeCoverage> Parse(params IEnumerable<string> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        // Per type: line number (per file) -> hit, and branch line -> (covered, total); the best of every report.
        var lines = new Dictionary<string, Dictionary<(string File, int Line), bool>>(StringComparer.Ordinal);
        var branches = new Dictionary<string, Dictionary<(string File, int Line), (int Covered, int Total)>>(StringComparer.Ordinal);
        foreach (var xml in reports)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new FormatException($"Not a Cobertura coverage report: {ex.Message}", ex);
            }

            if (document.Root?.Name.LocalName != "coverage")
            {
                throw new FormatException($"Not a Cobertura coverage report: the root element is <{document.Root?.Name.LocalName}>, not <coverage>.");
            }

            foreach (var type in document.Descendants("class"))
            {
                var name = TypeName((string?)type.Attribute("name") ?? string.Empty);
                if (name.Length == 0)
                {
                    continue;
                }

                // The file name, not its path: reports from different checkouts (CI and local) describe the same lines.
                var file = Path.GetFileName(((string?)type.Attribute("filename") ?? string.Empty).Replace('\\', '/'));
                var typeLines = lines.TryGetValue(name, out var existingLines) ? existingLines : lines[name] = [];
                var typeBranches = branches.TryGetValue(name, out var existingBranches) ? existingBranches : branches[name] = [];

                // The class's own <lines>; method <lines> repeat them.
                foreach (var line in type.Elements("lines").Elements("line"))
                {
                    if (!int.TryParse((string?)line.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    {
                        continue;
                    }

                    var key = (file, number);
                    var hit = long.TryParse((string?)line.Attribute("hits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hits) && hits > 0;
                    typeLines[key] = (typeLines.TryGetValue(key, out var before) && before) || hit;

                    // Cobertura counts branch outcomes without naming them, so merged reports keep the best count per
                    // line (a lower bound: two reports covering different outcomes may undercount).
                    if (string.Equals((string?)line.Attribute("branch"), "true", StringComparison.OrdinalIgnoreCase)
                        && ConditionCoverage().Match((string?)line.Attribute("condition-coverage") ?? string.Empty) is { Success: true } condition)
                    {
                        var covered = int.Parse(condition.Groups[1].Value, CultureInfo.InvariantCulture);
                        var total = int.Parse(condition.Groups[2].Value, CultureInfo.InvariantCulture);
                        typeBranches[key] = typeBranches.TryGetValue(key, out var previous)
                            ? (Math.Max(previous.Covered, covered), Math.Max(previous.Total, total))
                            : (covered, total);
                    }
                }
            }
        }

        return lines.ToDictionary(
            entry => entry.Key,
            entry => new CodeCoverage(
                entry.Value.Values.Count(hit => hit),
                entry.Value.Count,
                branches[entry.Key].Values.Sum(b => b.Covered),
                branches[entry.Key].Values.Sum(b => b.Total)),
            StringComparer.Ordinal);
    }

    /// <summary>Reads and parses report files.</summary>
    public static IReadOnlyDictionary<string, CodeCoverage> Load(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Parse(paths.Select(File.ReadAllText).ToArray());
    }

    /// <summary>
    /// The Cobertura reports under <paramref name="directory"/> (<c>*.cobertura.xml</c>, as coverlet's
    /// <c>--collect "XPlat Code Coverage"</c> and Microsoft's code coverage write them), newest first.
    /// </summary>
    public static IReadOnlyList<string> Find(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.cobertura.xml", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray()
            : [];

    // "MyLib.Button/<>c__DisplayClass3_0", "MyLib.Button/<OnClickAsync>d__5", "MyLib.Button+Nested",
    // "MyLib.Grid`1" -> the outermost type (generic arity kept, as Type.FullName has it).
    internal static string TypeName(string name)
    {
        var end = name.IndexOfAny(['/', '+']);
        var outer = end < 0 ? name : name[..end];
        var generated = outer.IndexOf('<', StringComparison.Ordinal);
        return (generated < 0 ? outer : outer[..generated]).TrimEnd('.');
    }

    [GeneratedRegex(@"\((\d+)/(\d+)\)")]
    private static partial Regex ConditionCoverage();
}
