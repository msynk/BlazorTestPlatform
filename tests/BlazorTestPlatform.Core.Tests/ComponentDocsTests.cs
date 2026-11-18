using System.Xml.Linq;

namespace BlazorTestPlatform.Core.Tests;

public sealed class ComponentDocsTests
{
    [Fact]
    public void Summaries_read_as_text()
    {
        var summary = XElement.Parse(
            "<summary>Raised when <paramref name=\"value\"/> changes &amp; the <see cref=\"T:My.Lib.Button`1\"/> is <c>enabled</c>,\n    or <see langword=\"null\"/>.</summary>");

        Assert.Equal("Raised when value changes & the Button is enabled, or null.", ComponentDocs.Text(summary));
    }

    [Theory]
    [InlineData(typeof(int?), false, "int?")]
    [InlineData(typeof(string), true, "string?")]
    [InlineData(typeof(IReadOnlyList<KeyValuePair<string, int>>), false, "IReadOnlyList<KeyValuePair<string, int>>")]
    [InlineData(typeof(Microsoft.AspNetCore.Components.EventCallback<bool>), false, "EventCallback<bool>")]
    [InlineData(typeof(string[]), false, "string[]")]
    public void Type_names_read_as_in_CSharp(Type type, bool nullable, string expected) =>
        Assert.Equal(expected, ComponentDocs.FriendlyName(type, nullable));
}
