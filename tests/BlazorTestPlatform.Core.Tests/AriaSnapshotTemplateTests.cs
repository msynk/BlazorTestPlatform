namespace BlazorTestPlatform.Core.Tests;

public sealed class AriaSnapshotTemplateTests
{
    private const string Tree = """
        - banner:
          - heading "Orders" [level=1]
        - main:
          - list:
            - listitem:
              - link "Order 12"
              - button "Delete" [disabled]
            - listitem:
              - link "Order 13"
          - button "Bold" [pressed]
        """;

    [Fact]
    public void Exact_matching_compares_every_node_name_and_state()
    {
        Assert.True(AriaSnapshotTemplate.Matches(Tree, Tree));
        Assert.True(AriaSnapshotTemplate.Matches(Tree, Tree.Replace("\"Order 12\"", "/Order \\d+/", StringComparison.Ordinal)));
        Assert.False(AriaSnapshotTemplate.Matches(Tree, Tree.Replace("\"Order 12\"", "/Order \\d$/", StringComparison.Ordinal)));
        Assert.False(AriaSnapshotTemplate.Matches(Tree, Tree.Replace(" [disabled]", string.Empty, StringComparison.Ordinal)));
        Assert.False(AriaSnapshotTemplate.Matches(Tree, """
            - banner:
              - heading "Orders" [level=1]
            """));
    }

    [Fact]
    public void Contains_matching_finds_partial_trees_at_any_depth_in_order()
    {
        Assert.True(AriaSnapshotTemplate.Matches(Tree, """
            - listitem:
              - button "Delete"
            - listitem
            """, AriaSnapshotMatch.Contains));
        Assert.True(AriaSnapshotTemplate.Matches(Tree, """
            - main:
              - button [pressed]
            """, AriaSnapshotMatch.Contains));
        Assert.True(AriaSnapshotTemplate.Matches(Tree, "- link /order 1[23]/i", AriaSnapshotMatch.Contains));

        // Order, names and listed states still count.
        Assert.False(AriaSnapshotTemplate.Matches(Tree, """
            - link "Order 13"
            - link "Order 12"
            """, AriaSnapshotMatch.Contains));
        Assert.False(AriaSnapshotTemplate.Matches(Tree, "- button \"Delete\" [pressed]", AriaSnapshotMatch.Contains));
        Assert.False(AriaSnapshotTemplate.Matches(Tree, "- link \"Order\"", AriaSnapshotMatch.Contains));
    }

    [Fact]
    public void Children_equal_requires_every_child_of_that_node()
    {
        Assert.False(AriaSnapshotTemplate.Matches(Tree, """
            - listitem:
              - /children: equal
              - link "Order 12"
            """, AriaSnapshotMatch.Contains));
        Assert.True(AriaSnapshotTemplate.Matches(Tree, """
            - listitem:
              - /children: equal
              - link
              - button
            """, AriaSnapshotMatch.Contains));
    }

    [Theory]
    [InlineData("button \"Save\"")]
    [InlineData("- button \"Save")]
    [InlineData("- link /Order (/")]
    [InlineData("- link /Order/x")]
    [InlineData("- button [disabled")]
    [InlineData("- /children: equal")]
    [InlineData("- button \"Save\" trailing")]
    public void Malformed_expected_trees_are_reported(string template)
    {
        Assert.Throws<FormatException>(() => AriaSnapshotTemplate.Validate(template));
    }

    [Fact]
    public void Names_unescape_quotes_and_backslashes()
    {
        Assert.True(AriaSnapshotTemplate.Matches("""- button "Save \"draft\" C:\\x" """.TrimEnd(), """- button /^Save "draft" C:\\x$/"""));
    }
}
