namespace BlazorTestPlatform.Visual;

/// <summary>Options for <see cref="ImageComparer"/>.</summary>
public sealed record ImageCompareOptions
{
    /// <summary>
    /// Per-pixel perceptual (YIQ) color-distance threshold in <c>[0, 1]</c>. Smaller is stricter. Default 0.1.
    /// </summary>
    public double Threshold { get; init; } = 0.1;

    /// <summary>Count anti-aliased pixels as differences. Default false (font smoothing noise is ignored).</summary>
    public bool IncludeAntiAliasing { get; init; }

    /// <summary>Regions excluded from comparison (masked dynamic content).</summary>
    public IReadOnlyList<PixelRectangle> IgnoreRegions { get; init; } = [];

    /// <summary>Opacity of unchanged pixels in the diff image (0..1). Default 0.1.</summary>
    public double DiffImageAlpha { get; init; } = 0.1;
}

/// <summary>Result of an image comparison.</summary>
public sealed record ImageCompareResult
{
    /// <summary>Pixels that differ beyond the threshold.</summary>
    public int DiffPixels { get; init; }

    /// <summary>Pixels detected as anti-aliasing differences (not counted unless requested).</summary>
    public int AntiAliasedPixels { get; init; }

    /// <summary>Total compared pixels.</summary>
    public int TotalPixels { get; init; }

    /// <summary><see cref="DiffPixels"/> / <see cref="TotalPixels"/>.</summary>
    public double DiffRatio => TotalPixels == 0 ? 0 : (double)DiffPixels / TotalPixels;

    /// <summary>True when image sizes differ.</summary>
    public bool SizeMismatch { get; init; }

    /// <summary>Visualization: red = changed, yellow = anti-aliasing, faded = unchanged, magenta = outside the overlap.</summary>
    public RgbaImage? DiffImage { get; init; }

    /// <summary>Bounding box of differing pixels.</summary>
    public PixelRectangle? DiffBounds { get; init; }
}

/// <summary>
/// Perceptual image comparison: YIQ color distance with alpha blending over white and anti-aliasing detection
/// (after Kotsarenko &amp; Ramos, "Measuring perceived color difference using YIQ NTSC transmission color space",
/// and the anti-aliased pixel detector popularized by pixelmatch).
/// </summary>
public static class ImageComparer
{
    // Maximum possible YIQ delta (35215) scaled by threshold² gives the acceptable squared distance.
    private const double MaxYiqDelta = 35215;

    /// <summary>Compares two images.</summary>
    public static ImageCompareResult Compare(RgbaImage expected, RgbaImage actual, ImageCompareOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new ImageCompareOptions();
        if (options.Threshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Threshold must be within [0, 1].");
        }

        var width = Math.Max(expected.Width, actual.Width);
        var height = Math.Max(expected.Height, actual.Height);
        var overlapWidth = Math.Min(expected.Width, actual.Width);
        var overlapHeight = Math.Min(expected.Height, actual.Height);
        var sizeMismatch = expected.Width != actual.Width || expected.Height != actual.Height;
        var diff = new RgbaImage(width, height);
        var maxDelta = MaxYiqDelta * options.Threshold * options.Threshold;

        int diffCount = 0, aaCount = 0, total = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= overlapWidth || y >= overlapHeight)
                {
                    diff.SetPixel(x, y, 255, 0, 255);
                    diffCount++;
                    total++;
                    Track(x, y);
                    continue;
                }

                if (IsIgnored(options.IgnoreRegions, x, y))
                {
                    diff.SetPixel(x, y, 120, 120, 255, 255);
                    continue;
                }

                total++;
                var i = (y * expected.Width + x) * 4;
                var j = (y * actual.Width + x) * 4;
                var delta = ColorDelta(expected.Pixels, actual.Pixels, i, j, yOnly: false);

                if (Math.Abs(delta) > maxDelta)
                {
                    if (!options.IncludeAntiAliasing
                        && (IsAntiAliased(expected, x, y, actual) || IsAntiAliased(actual, x, y, expected)))
                    {
                        aaCount++;
                        diff.SetPixel(x, y, 255, 255, 0);
                    }
                    else
                    {
                        diffCount++;
                        diff.SetPixel(x, y, 255, 0, 0);
                        Track(x, y);
                    }
                }
                else
                {
                    var gray = Blend(GrayOf(expected.Pixels, i), options.DiffImageAlpha * expected.Pixels[i + 3] / 255.0);
                    diff.SetPixel(x, y, gray, gray, gray);
                }
            }
        }

        return new ImageCompareResult
        {
            DiffPixels = diffCount,
            AntiAliasedPixels = aaCount,
            TotalPixels = total,
            SizeMismatch = sizeMismatch,
            DiffImage = diff,
            DiffBounds = maxX < 0 ? null : new PixelRectangle(minX, minY, maxX - minX + 1, maxY - minY + 1),
        };

        void Track(int x, int y)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
    }

    private static bool IsIgnored(IReadOnlyList<PixelRectangle> regions, int x, int y)
    {
        for (var r = 0; r < regions.Count; r++)
        {
            if (regions[r].Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects whether a pixel is likely anti-aliasing: it has at most two identical neighbours, and both its darkest and
    /// brightest neighbours sit in flat regions (many identical siblings) in both images.
    /// </summary>
    private static bool IsAntiAliased(RgbaImage image, int x1, int y1, RgbaImage other)
    {
        var x0 = Math.Max(x1 - 1, 0);
        var y0 = Math.Max(y1 - 1, 0);
        var x2 = Math.Min(x1 + 1, image.Width - 1);
        var y2 = Math.Min(y1 + 1, image.Height - 1);
        var position = (y1 * image.Width + x1) * 4;
        var zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        double min = 0, max = 0;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;

        for (var x = x0; x <= x2; x++)
        {
            for (var y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }

                var delta = ColorDelta(image.Pixels, image.Pixels, position, (y * image.Width + x) * 4, yOnly: true);
                if (delta == 0)
                {
                    zeroes++;
                    if (zeroes > 2)
                    {
                        return false;
                    }
                }
                else if (delta < min)
                {
                    min = delta;
                    minX = x;
                    minY = y;
                }
                else if (delta > max)
                {
                    max = delta;
                    maxX = x;
                    maxY = y;
                }
            }
        }

        if (min == 0 || max == 0)
        {
            return false;
        }

        return (HasManySiblings(image, minX, minY) && HasManySiblings(other, minX, minY))
            || (HasManySiblings(image, maxX, maxY) && HasManySiblings(other, maxX, maxY));
    }

    private static bool HasManySiblings(RgbaImage image, int x1, int y1)
    {
        if (x1 >= image.Width || y1 >= image.Height)
        {
            return false;
        }

        var x0 = Math.Max(x1 - 1, 0);
        var y0 = Math.Max(y1 - 1, 0);
        var x2 = Math.Min(x1 + 1, image.Width - 1);
        var y2 = Math.Min(y1 + 1, image.Height - 1);
        var position = (y1 * image.Width + x1) * 4;
        var zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        var pixels = image.Pixels;

        for (var x = x0; x <= x2; x++)
        {
            for (var y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }

                var other = (y * image.Width + x) * 4;
                if (pixels[position] == pixels[other] && pixels[position + 1] == pixels[other + 1]
                    && pixels[position + 2] == pixels[other + 2] && pixels[position + 3] == pixels[other + 3])
                {
                    zeroes++;
                }

                if (zeroes > 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Squared YIQ distance; the sign tells whether the pixel got lighter (negative) or darker.</summary>
    private static double ColorDelta(byte[] a, byte[] b, int i, int j, bool yOnly)
    {
        double r1 = a[i], g1 = a[i + 1], b1 = a[i + 2], a1 = a[i + 3];
        double r2 = b[j], g2 = b[j + 1], b2 = b[j + 2], a2 = b[j + 3];

        if (a1 == a2 && r1 == r2 && g1 == g2 && b1 == b2)
        {
            return 0;
        }

        if (a1 < 255)
        {
            a1 /= 255;
            r1 = BlendChannel(r1, a1);
            g1 = BlendChannel(g1, a1);
            b1 = BlendChannel(b1, a1);
        }

        if (a2 < 255)
        {
            a2 /= 255;
            r2 = BlendChannel(r2, a2);
            g2 = BlendChannel(g2, a2);
            b2 = BlendChannel(b2, a2);
        }

        var y1 = Y(r1, g1, b1);
        var y2 = Y(r2, g2, b2);
        var y = y1 - y2;
        if (yOnly)
        {
            return y;
        }

        var iDelta = I(r1, g1, b1) - I(r2, g2, b2);
        var qDelta = Q(r1, g1, b1) - Q(r2, g2, b2);
        var delta = 0.5053 * y * y + 0.299 * iDelta * iDelta + 0.1957 * qDelta * qDelta;
        return y1 > y2 ? -delta : delta;
    }

    private static double Y(double r, double g, double b) => r * 0.29889531 + g * 0.58662247 + b * 0.11448223;

    private static double I(double r, double g, double b) => r * 0.59597799 - g * 0.27417610 - b * 0.32180189;

    private static double Q(double r, double g, double b) => r * 0.21147017 - g * 0.52261711 + b * 0.31114694;

    private static double BlendChannel(double channel, double alpha) => 255 + (channel - 255) * alpha;

    private static double GrayOf(byte[] pixels, int i) => Y(pixels[i], pixels[i + 1], pixels[i + 2]);

    private static byte Blend(double value, double alpha) => (byte)Math.Clamp(Math.Round(255 + (value - 255) * alpha), 0, 255);
}
