using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Core.Tests;

public sealed class CombinatoricsTests
{
    [Theory]
    [InlineData(new[] { 2, 2, 2 })]
    [InlineData(new[] { 3, 3, 3, 3 })]
    [InlineData(new[] { 4, 4, 4, 4 })]
    [InlineData(new[] { 5, 2, 3, 4, 2, 6 })]
    [InlineData(new[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 })]
    [InlineData(new[] { 1, 3, 1, 4 })]
    public void Pairwise_covers_every_pair_with_far_fewer_rows(int[] sizes)
    {
        var rows = Combinatorics.Pairwise(sizes);

        Assert.Empty(Combinatorics.MissingPairs(sizes, rows));
        Assert.All(rows, row => Assert.All(row.Select((v, i) => (v, i)), x => Assert.InRange(x.v, 0, sizes[x.i] - 1)));
        Assert.Equal(rows.Count, rows.Select(r => string.Join(',', r)).Distinct().Count());
        Assert.True(rows.Count <= Combinatorics.CartesianCount(sizes));

        // The two largest dimensions alone need size1 x size2 rows; all-pairs designs grow with log(dimensions) beyond that.
        var sorted = sizes.OrderDescending().ToArray();
        var growth = Math.Max(2, (int)Math.Ceiling(Math.Log2(sizes.Length)));
        Assert.InRange(rows.Count, sorted[0] * sorted[1], sorted[0] * sorted[1] * growth);
    }

    [Fact]
    public void Pairwise_is_much_smaller_than_the_product_for_many_dimensions()
    {
        int[] sizes = [3, 3, 3, 3, 3, 3, 3, 3, 3, 3];

        var rows = Combinatorics.Pairwise(sizes);

        Assert.Equal(59_049, Combinatorics.CartesianCount(sizes));
        Assert.True(rows.Count <= 20, $"{rows.Count} rows");
        Assert.Empty(Combinatorics.MissingPairs(sizes, rows));
    }

    [Fact]
    public void Pairwise_is_deterministic()
    {
        int[] sizes = [4, 3, 5, 2, 3];

        var first = Combinatorics.Pairwise(sizes).Select(r => string.Join(',', r)).ToArray();
        var second = Combinatorics.Pairwise(sizes).Select(r => string.Join(',', r)).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fewer_than_three_dimensions_give_the_full_product()
    {
        Assert.Equal(12, Combinatorics.Pairwise([3, 4]).Count);
        Assert.Equal(5, Combinatorics.Pairwise([5]).Count);
        Assert.Empty(Combinatorics.Pairwise([]));
    }

    [Fact]
    public void Constraints_are_never_violated_and_feasible_pairs_stay_covered()
    {
        int[] sizes = [3, 3, 3, 3];
        static bool Valid(IReadOnlyList<int> row) => !(row[0] == 0 && row[1] == 0) && !(row[2] == 2 && row[3] == 1);

        var rows = Combinatorics.Pairwise(sizes, Valid);

        Assert.All(rows, row => Assert.True(Valid(row), string.Join(',', row)));
        var missing = Combinatorics.MissingPairs(sizes, rows);
        Assert.Equal([(0, 0, 1, 0), (2, 2, 3, 1)], missing);
    }

    [Fact]
    public void Cartesian_product_is_in_odometer_order_and_honours_constraints()
    {
        var rows = Combinatorics.CartesianProduct([2, 2], row => row[0] != row[1]);

        Assert.Equal(["0,1", "1,0"], rows.Select(r => string.Join(',', r)));
    }

    [Fact]
    public void Value_overload_maps_rows_to_values()
    {
        IReadOnlyList<IReadOnlyList<string>> dimensions = [["a", "b"], ["x", "y"], ["1", "2"]];

        var rows = Combinatorics.Pairwise(dimensions, row => row[0] != "b" || row[2] != "2");

        Assert.DoesNotContain(rows, r => r[0] == "b" && r[2] == "2");
        Assert.Contains(rows, r => r[0] == "b" && r[1] == "y");
    }

    [Fact]
    public void Empty_dimensions_are_rejected() =>
        Assert.Throws<ArgumentException>(() => Combinatorics.Pairwise([2, 0, 3]));

    [Fact]
    public void Pairwise_variants_cover_every_pair_of_parameter_values()
    {
        var catalog = StoryCatalog.FromCollections([new PairwiseStories()]);
        var names = catalog.Stories.Where(s => s.Name.StartsWith("Size=", StringComparison.Ordinal)).Select(s => s.Name).ToArray();

        Assert.True(names.Length < 3 * 3 * 2 * 2, $"{names.Length} stories");
        foreach (var size in new[] { "Small", "Medium", "Large" })
        {
            foreach (var tone in new[] { "Neutral", "Danger", "Success" })
            {
                Assert.Contains(names, n => n.Contains($"Size={size}", StringComparison.Ordinal) && n.Contains($"Tone={tone}", StringComparison.Ordinal));
            }
        }

        // The constraint holds and story ids are unique.
        Assert.DoesNotContain(names, n => n.Contains("Tone=Danger", StringComparison.Ordinal) && n.Contains("Disabled=True", StringComparison.Ordinal));
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void Varying_a_parameter_twice_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => StoryCatalog.FromCollections([new DuplicateDimensionStories()]));
        Assert.Contains("already varied", error.Message, StringComparison.Ordinal);
    }

    public enum Size { Small, Medium, Large }

    public enum Tone { Neutral, Danger, Success }

    public sealed class Chip : ComponentBase
    {
        [Parameter] public Size Size { get; set; }

        [Parameter] public Tone Tone { get; set; }

        [Parameter] public bool Disabled { get; set; }

        [Parameter] public bool Dense { get; set; }
    }

    private sealed class PairwiseStories : Stories<Chip>
    {
        public override string Title => "Tests/Chip";

        protected override void Define(StoryCollectionBuilder<Chip> stories) =>
            stories.AddVariants(v => v
                .Vary(c => c.Size, Size.Small, Size.Medium, Size.Large)
                .Vary(c => c.Tone, Tone.Neutral, Tone.Danger, Tone.Success)
                .Vary(c => c.Disabled, false, true)
                .Vary(c => c.Dense, false, true)
                .Where(c => !(c.Get(x => x.Tone) == Tone.Danger && c.Get(x => x.Disabled)))
                .Pairwise());
    }

    private sealed class DuplicateDimensionStories : Stories<Chip>
    {
        public override string Title => "Tests/Duplicate";

        protected override void Define(StoryCollectionBuilder<Chip> stories) =>
            stories.AddVariants(v => v.Vary(c => c.Size, Size.Small).Vary(c => c.Size, Size.Large));
    }
}
