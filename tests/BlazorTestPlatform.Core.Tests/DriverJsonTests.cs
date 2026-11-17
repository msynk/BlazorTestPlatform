using System.Text.Json;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Core.Tests;

public sealed class DriverJsonTests
{
    [Fact]
    public void Locator_chains_serialize_every_step_and_query_kind()
    {
        var chain = LocatorChain.Start(By.Css("form"))
            .Append(new LocatorStep(LocatorStepKind.Find, By.Role("button", null)))
            .Append(new LocatorStep(LocatorStepKind.Nth, Index: -1))
            .Append(new LocatorStep(LocatorStepKind.HasText, Text: "Save"))
            .Append(new LocatorStep(LocatorStepKind.HasNotText, Text: "Cancel"))
            .Append(new LocatorStep(LocatorStepKind.Find, By.TestId("x")))
            .Append(new LocatorStep(LocatorStepKind.Find, By.Label("Email", exact: true)));

        using var json = JsonDocument.Parse(DriverJson.Steps(chain));
        var steps = json.RootElement;

        Assert.Equal("form", steps[0].GetProperty("query").GetProperty("selector").GetString());
        Assert.Equal(JsonValueKind.Null, steps[1].GetProperty("query").GetProperty("name").ValueKind);
        Assert.Equal(-1, steps[2].GetProperty("index").GetInt32());
        Assert.Equal("hasText", steps[3].GetProperty("kind").GetString());
        Assert.Equal("hasNotText", steps[4].GetProperty("kind").GetString());
        Assert.Equal("data-testid", steps[5].GetProperty("query").GetProperty("attribute").GetString());
        Assert.True(steps[6].GetProperty("query").GetProperty("exact").GetBoolean());
    }

    [Fact]
    public void Patterns_role_states_and_filters_serialize_for_the_browser_engine()
    {
        var chain = LocatorChain.Start(By.Role("button", namePattern: new System.Text.RegularExpressions.Regex("^Save", System.Text.RegularExpressions.RegexOptions.IgnoreCase), pressed: true, level: 2, includeHidden: true))
            .Append(new LocatorStep(LocatorStepKind.HasText, Text: "x") { Pattern = new System.Text.RegularExpressions.Regex("x$", System.Text.RegularExpressions.RegexOptions.Multiline) })
            .Append(new LocatorStep(LocatorStepKind.HasNot) { Inner = LocatorChain.Start(By.Text(new System.Text.RegularExpressions.Regex("a.b", System.Text.RegularExpressions.RegexOptions.Singleline))) });

        using var json = JsonDocument.Parse(DriverJson.Steps(chain));
        var steps = json.RootElement;
        var role = steps[0].GetProperty("query");

        Assert.Equal("^Save", role.GetProperty("pattern").GetString());
        Assert.Equal("i", role.GetProperty("flags").GetString());
        Assert.True(role.GetProperty("pressed").GetBoolean());
        Assert.False(role.TryGetProperty("checked", out _));
        Assert.Equal(2, role.GetProperty("level").GetInt32());
        Assert.True(role.GetProperty("includeHidden").GetBoolean());
        Assert.Equal("m", steps[1].GetProperty("flags").GetString());
        Assert.Equal("hasNot", steps[2].GetProperty("kind").GetString());
        Assert.Equal("s", steps[2].GetProperty("inner")[0].GetProperty("query").GetProperty("flags").GetString());
        Assert.Equal("role=button[name=/^Save/i][pressed=true][level=2][include-hidden] >> has-text=/x$/m >> has-not=(text=/a.b/s)", chain.ToString());
    }

    [Fact]
    public void Patterns_browsers_cannot_run_are_rejected()
    {
        var chain = LocatorChain.Start(By.Text(new System.Text.RegularExpressions.Regex("a # comment", System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace)));

        var error = Assert.Throws<NotSupportedException>(() => DriverJson.Steps(chain));
        Assert.Contains("IgnorePatternWhitespace", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ElementActionKind.DoubleClick, "dblclick")]
    [InlineData(ElementActionKind.SelectOption, "select")]
    [InlineData(ElementActionKind.DragTo, "drag")]
    [InlineData(ElementActionKind.ScrollIntoView, "scroll")]
    public void Actions_serialize_kind_modifiers_and_targets(ElementActionKind kind, string expected)
    {
        var action = new ElementAction(kind)
        {
            Values = ["a"],
            Modifiers = KeyModifiers.Shift | KeyModifiers.Control,
            Button = MouseButton.Right,
            Target = LocatorChain.Start(By.Css("#drop")),
        };

        using var json = JsonDocument.Parse(DriverJson.Action(action));

        Assert.Equal(expected, json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(10, json.RootElement.GetProperty("modifiers").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("button").GetInt32());
        Assert.Equal("#drop", json.RootElement.GetProperty("target")[0].GetProperty("query").GetProperty("selector").GetString());
    }

    [Fact]
    public void Element_snapshots_parse()
    {
        var elements = DriverJson.Elements("""[{"tagName":"input","role":"checkbox","isChecked":true,"attributes":{"name":"agree"}}]""");

        var element = Assert.Single(elements);
        Assert.Equal("checkbox", element.Role);
        Assert.True(element.IsChecked);
        Assert.Equal("agree", element.GetAttribute("name"));
        Assert.Null(DriverJson.Element("null"));
        Assert.Empty(DriverJson.Elements(null));
    }
}
