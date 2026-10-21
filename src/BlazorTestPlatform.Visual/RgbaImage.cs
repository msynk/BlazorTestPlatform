namespace BlazorTestPlatform.Visual;

/// <summary>An 8-bit RGBA image in row-major order (4 bytes per pixel, non-premultiplied).</summary>
public sealed class RgbaImage
{
    /// <summary>Creates a transparent image.</summary>
    public RgbaImage(int width, int height)
        : this(width, height, new byte[checked(width * height * 4)])
    {
    }

    /// <summary>Wraps existing pixel data (not copied).</summary>
    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>RGBA bytes.</summary>
    public byte[] Pixels { get; }

    /// <summary>Reads a pixel.</summary>
    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var i = Offset(x, y);
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    /// <summary>Writes a pixel.</summary>
    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        var i = Offset(x, y);
        Pixels[i] = r;
        Pixels[i + 1] = g;
        Pixels[i + 2] = b;
        Pixels[i + 3] = a;
    }

    /// <summary>Fills a rectangle (clipped to the image).</summary>
    public void FillRectangle(PixelRectangle rectangle, byte r, byte g, byte b, byte a = 255)
    {
        var clipped = rectangle.Intersect(new PixelRectangle(0, 0, Width, Height));
        for (var y = clipped.Y; y < clipped.Bottom; y++)
        {
            for (var x = clipped.X; x < clipped.Right; x++)
            {
                SetPixel(x, y, r, g, b, a);
            }
        }
    }

    /// <summary>Returns a copy.</summary>
    public RgbaImage Clone() => new(Width, Height, (byte[])Pixels.Clone());

    /// <summary>Decodes a PNG.</summary>
    public static RgbaImage FromPng(byte[] png) => PngCodec.Decode(png);

    /// <summary>Encodes as PNG.</summary>
    public byte[] ToPng() => PngCodec.Encode(this);

    private int Offset(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Pixel ({x},{y}) is outside {Width}x{Height}.");
        }

        return (y * Width + x) * 4;
    }
}

/// <summary>An integer rectangle in pixel coordinates.</summary>
public readonly record struct PixelRectangle(int X, int Y, int Width, int Height)
{
    /// <summary>Exclusive right edge.</summary>
    public int Right => X + Width;

    /// <summary>Exclusive bottom edge.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when the point lies inside.</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Intersection (empty when disjoint).</summary>
    public PixelRectangle Intersect(PixelRectangle other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? default : new PixelRectangle(x, y, right - x, bottom - y);
    }
}
