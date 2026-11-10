namespace BlazorTestPlatform.Visual.Tests;

/// <summary>Fingerprints group the same change wherever it happens, so one review decision can cover all of them.</summary>
public sealed class DiffFingerprintTests
{
    private static RgbaImage Blank(int width = 120, int height = 80)
    {
        var image = new RgbaImage(width, height);
        image.FillRectangle(new PixelRectangle(0, 0, width, height), 255, 255, 255);
        return image;
    }

    private static RgbaImage WithBlock(int x, int y, int width, int height)
    {
        var image = Blank();
        image.FillRectangle(new PixelRectangle(x, y, width, height), 20, 40, 200);
        return image;
    }

    [Fact]
    public void The_same_change_in_another_place_has_the_same_fingerprint()
    {
        var first = ImageComparer.Compare(Blank(), WithBlock(10, 10, 20, 8));
        var moved = ImageComparer.Compare(Blank(), WithBlock(70, 50, 20, 8));
        var other = ImageComparer.Compare(Blank(), WithBlock(10, 10, 40, 30));

        Assert.NotNull(first.Fingerprint);
        Assert.Equal(first.Fingerprint, moved.Fingerprint);
        Assert.NotEqual(first.Fingerprint, other.Fingerprint);
        Assert.Equal(new PixelRectangle(70, 50, 20, 8), moved.DiffBounds);
    }

    [Fact]
    public void Identical_images_have_no_fingerprint() =>
        Assert.Null(ImageComparer.Compare(Blank(), Blank()).Fingerprint);
}
