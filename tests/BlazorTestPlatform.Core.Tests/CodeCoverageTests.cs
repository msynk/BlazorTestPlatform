using BlazorTestPlatform.Coverage;

namespace BlazorTestPlatform.Core.Tests;

public sealed class CodeCoverageTests
{
    // Two reports (say bUnit and browser runs) over the same component: compiler-generated classes count towards it.
    private const string First = """
        <?xml version="1.0" encoding="utf-8"?>
        <coverage line-rate="0.5" branch-rate="0.5" version="1.9">
          <packages>
            <package name="MyLib">
              <classes>
                <class name="MyLib.Components.Button" filename="Components/Button.razor" line-rate="0.5">
                  <methods>
                    <method name="BuildRenderTree"><lines><line number="1" hits="3" branch="false" /></lines></method>
                  </methods>
                  <lines>
                    <line number="1" hits="3" branch="false" />
                    <line number="2" hits="0" branch="false" />
                    <line number="3" hits="2" branch="true" condition-coverage="50% (1/2)" />
                  </lines>
                </class>
                <class name="MyLib.Components.Button/&lt;OnClickAsync&gt;d__4" filename="Components/Button.razor">
                  <lines><line number="10" hits="0" branch="false" /></lines>
                </class>
                <class name="MyLib.Components.Grid`1" filename="Components/Grid.razor">
                  <lines><line number="5" hits="1" branch="false" /></lines>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

    private const string Second = """
        <coverage>
          <packages><package><classes>
            <class name="MyLib.Components.Button" filename="Components/Button.razor">
              <lines>
                <line number="2" hits="1" branch="false" />
                <line number="3" hits="1" branch="true" condition-coverage="100% (2/2)" />
              </lines>
            </class>
          </classes></package></packages>
        </coverage>
        """;

    [Fact]
    public void Generated_classes_count_towards_their_type_and_reports_merge_line_by_line()
    {
        var single = CoberturaCoverage.Parse(First);
        Assert.Equal(new CodeCoverage(2, 4, 1, 2), single["MyLib.Components.Button"]);
        Assert.Equal(new CodeCoverage(1, 1, 0, 0), single["MyLib.Components.Grid`1"]);

        var merged = CoberturaCoverage.Parse(First, Second);
        Assert.Equal(new CodeCoverage(3, 4, 2, 2), merged["MyLib.Components.Button"]);
        Assert.Equal(0.75, merged["MyLib.Components.Button"].LineRate);
    }

    [Fact]
    public void Story_coverage_takes_each_components_code_coverage()
    {
        var report = new StoryCoverageReport
        {
            Components = [new ComponentCoverage { Title = "Inputs/Button", Component = "MyLib.Components.Button", Stories = 3 }, new ComponentCoverage { Title = "Other", Component = "MyLib.Other", Stories = 1 }],
            ComponentsWithoutStories = ["MyLib.Components.Grid`1"],
        };

        var merged = report.WithCodeCoverage(CoberturaCoverage.Parse(First, Second));

        Assert.Equal(new CodeCoverage(3, 4, 2, 2), merged.Components[0].Code);
        Assert.Null(merged.Components[1].Code);
        Assert.Equal(new CodeCoverage(1, 1, 0, 0), merged.CodeWithoutStories["MyLib.Components.Grid`1"]);
        Assert.Equal(new CodeCoverage(4, 5, 2, 2), merged.Code);

        // It survives the results file.
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-code-coverage", Guid.NewGuid().ToString("N"))).FullName;
        merged.Save(directory);
        Assert.Equal(merged.Code, StoryCoverageReport.Load(directory)!.Code);
    }

    [Fact]
    public void Reports_from_different_checkouts_describe_the_same_lines()
    {
        static string Report(string path, int hits) => $"""
            <coverage><packages><package><classes>
              <class name="MyLib.Card" filename="{path}"><lines><line number="1" hits="{hits}" /><line number="2" hits="0" /></lines></class>
            </classes></package></packages></coverage>
            """;

        var merged = CoberturaCoverage.Parse(Report("/home/runner/work/lib/Card.razor", 1), Report(@"C:\dev\lib\Card.razor", 0));

        Assert.Equal(new CodeCoverage(1, 2, 0, 0), merged["MyLib.Card"]);
    }

    [Fact]
    public void Nested_components_and_repeated_titles_are_handled()
    {
        var code = new Dictionary<string, CodeCoverage> { ["MyLib.Outer"] = new(5, 10, 0, 0) };
        var report = new StoryCoverageReport
        {
            Components =
            [
                new ComponentCoverage { Title = "A", Component = "MyLib.Outer+Inner", Stories = 1 },
                new ComponentCoverage { Title = "B", Component = "MyLib.Outer", Stories = 1 },
                new ComponentCoverage { Title = "C", Component = "MyLib.Outer", Stories = 1 },
            ],
        }.WithCodeCoverage(code);

        Assert.Equal(new CodeCoverage(5, 10, 0, 0), report.Components[0].Code);
        Assert.Equal(new CodeCoverage(5, 10, 0, 0), report.Code);
    }

    [Fact]
    public void Text_that_is_not_Cobertura_is_reported()
    {
        Assert.Throws<FormatException>(() => CoberturaCoverage.Parse("<report />"));
        Assert.Throws<FormatException>(() => CoberturaCoverage.Parse("not xml"));
    }
}
