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
