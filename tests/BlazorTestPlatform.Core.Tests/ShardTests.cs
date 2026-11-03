namespace BlazorTestPlatform.Core.Tests;

public sealed class ShardTests
{
    private static readonly string[] Keys = Enumerable.Range(0, 400).Select(i => $"inputs-button--story-{i}|desktop|light|server|chromium").ToArray();

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public void Every_item_lands_in_exactly_one_shard_and_shards_stay_balanced(int count)
    {
        var shards = Enumerable.Range(1, count).Select(i => new Shard(i, count).Select(Keys, k => k).ToArray()).ToArray();

        Assert.Equal(Keys.Length, shards.Sum(s => s.Length));
        Assert.Equal(Keys.Order(StringComparer.Ordinal), shards.SelectMany(s => s).Order(StringComparer.Ordinal));
        Assert.All(shards, s => Assert.InRange(s.Length, Keys.Length / count / 2, Keys.Length / count * 2));
    }

    [Fact]
    public void Assignment_is_stable_and_independent_of_other_items()
    {
        var shard = new Shard(2, 4);
        var full = shard.Select(Keys, k => k).ToArray();
        var subset = shard.Select(Keys.Where((_, i) => i % 3 == 0), k => k).ToArray();

        Assert.Equal(full.Where(k => Array.IndexOf(Keys, k) % 3 == 0), subset);
        Assert.Equal(full, new Shard(2, 4).Select(Keys, k => k));
    }

    [Theory]
    [InlineData("2/4", 2, 4)]
    [InlineData(" 1/1 ", 1, 1)]
    public void Shards_parse(string text, int index, int count) => Assert.Equal(new Shard(index, count), Shard.Parse(text));

    [Theory]
    [InlineData("0/4")]
    [InlineData("5/4")]
    [InlineData("2")]
    [InlineData("a/b")]
    public void Invalid_shards_are_rejected(string text) => Assert.Throws<FormatException>(() => Shard.Parse(text));

    [Fact]
    public void Catalogs_and_matrix_cells_shard_by_their_keys()
    {
        var catalog = StoryCatalog.FromAssemblyOf<ShardTests>();
        var cells = TestMatrix.CrossHosting().Expand(catalog).ToArray();

        var stories = Enumerable.Range(1, 3).SelectMany(i => catalog.InShard(new Shard(i, 3)).Ids).ToArray();
        var keys = Enumerable.Range(1, 3).SelectMany(i => cells.InShard(new Shard(i, 3)).Select(c => c.Key)).ToArray();

        Assert.Equal(catalog.Ids.Order(StringComparer.Ordinal), stories.Order(StringComparer.Ordinal));
        Assert.Equal(cells.Select(c => c.Key).Order(StringComparer.Ordinal), keys.Order(StringComparer.Ordinal));
        Assert.Same(catalog.Stories[0], catalog.InShard(Shard.All).Stories[0]);
    }
}
