using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Visual.Tests;

public sealed class PngCodecTests
{
    private static RgbaImage Gradient(int width = 37, int height = 23)
    {
        var image = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, (byte)(x * 7), (byte)(y * 11), (byte)((x + y) * 3), (byte)(255 - x));
            }
        }

        return image;
    }

    [Fact]
    public void Encode_decode_round_trips()
    {
        var image = Gradient();
        var decoded = PngCodec.Decode(PngCodec.Encode(image));

        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_filter_type_decodes(byte filter)
    {
        var image = Gradient();
        Assert.Equal(image.Pixels, PngCodec.Decode(PngCodec.EncodeWithFilter(image, filter)).Pixels);
    }

    [Fact]
    public void Grayscale_rgb_and_palette_images_decode()
    {
        // 2x1 grayscale 8-bit: black, white.
        var gray = PngCodec.Decode(RawPng(2, 1, 8, colorType: 0, rows: [[0, 0, 255]]));
        Assert.Equal((byte)255, gray.GetPixel(1, 0).R);

        // 1x1 RGB 8-bit with tRNS making the colour transparent.
        var rgb = PngCodec.Decode(RawPng(1, 1, 8, colorType: 2, rows: [[0, 10, 20, 30]], trns: [0, 10, 0, 20, 0, 30]));
        Assert.Equal((10, 20, 30, 0), ((int)rgb.GetPixel(0, 0).R, (int)rgb.GetPixel(0, 0).G, (int)rgb.GetPixel(0, 0).B, (int)rgb.GetPixel(0, 0).A));

        // 4x1 indexed 2-bit image with a 2-colour palette: indices 0,1,1,0 packed into one byte.
        var indexed = PngCodec.Decode(RawPng(4, 1, 2, colorType: 3, rows: [[0, 0b0001_0100]], palette: [255, 0, 0, 0, 0, 255]));
        Assert.Equal((byte)255, indexed.GetPixel(0, 0).R);
        Assert.Equal((byte)255, indexed.GetPixel(1, 0).B);
        Assert.Equal((byte)255, indexed.GetPixel(3, 0).R);

        // 1x1 16-bit RGBA.
        var deep = PngCodec.Decode(RawPng(1, 1, 16, colorType: 6, rows: [[0, 0xAB, 0xCD, 0x12, 0x34, 0x56, 0x78, 0xFF, 0xFF]]));
        Assert.Equal((byte)0xAB, deep.GetPixel(0, 0).R);
        Assert.Equal((byte)0xFF, deep.GetPixel(0, 0).A);
    }

    [Fact]
    public void Garbage_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => PngCodec.Decode(Encoding.ASCII.GetBytes("definitely not a png file at all")));

    internal static byte[] RawPng(int width, int height, byte bitDepth, byte colorType, byte[][] rows, byte[]? palette = null, byte[]? trns = null)
    {
        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
        {
            foreach (var row in rows)
            {
                zlib.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = bitDepth;
        header[9] = colorType;
        Chunk(png, "IHDR", header);
        if (palette is not null)
        {
            Chunk(png, "PLTE", palette);
        }

        if (trns is not null)
        {
            Chunk(png, "tRNS", trns);
        }

        Chunk(png, "IDAT", data.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]); // CRC is not validated by the decoder.
    }
}

public sealed class ImageComparerTests
{
    private static RgbaImage Solid(int width, int height, byte r, byte g, byte b)
    {
        var image = new RgbaImage(width, height);
        image.FillRectangle(new PixelRectangle(0, 0, width, height), r, g, b);
        return image;
    }

    [Fact]
    public void Identical_images_have_no_differences()
    {
        var result = ImageComparer.Compare(Solid(20, 10, 10, 20, 30), Solid(20, 10, 10, 20, 30));

        Assert.Equal(0, result.DiffPixels);
        Assert.Equal(200, result.TotalPixels);
        Assert.Null(result.DiffBounds);
    }

    [Fact]
    public void Changed_pixels_are_counted_and_bounded()
    {
        var expected = Solid(20, 10, 255, 255, 255);
        var actual = expected.Clone();
        actual.FillRectangle(new PixelRectangle(5, 2, 3, 2), 0, 0, 0);

        var result = ImageComparer.Compare(expected, actual);

        Assert.Equal(6, result.DiffPixels);
        Assert.Equal(new PixelRectangle(5, 2, 3, 2), result.DiffBounds);
        Assert.Equal((255, 0, 0), ((int)result.DiffImage!.GetPixel(5, 2).R, (int)result.DiffImage.GetPixel(5, 2).G, (int)result.DiffImage.GetPixel(5, 2).B));
    }

    [Fact]
    public void Threshold_ignores_imperceptible_changes()
    {
        var expected = Solid(4, 4, 100, 100, 100);
        var actual = Solid(4, 4, 102, 100, 100);

        Assert.Equal(0, ImageComparer.Compare(expected, actual, new ImageCompareOptions { Threshold = 0.1 }).DiffPixels);
        Assert.Equal(16, ImageComparer.Compare(expected, actual, new ImageCompareOptions { Threshold = 0 }).DiffPixels);
    }

    [Fact]
    public void Ignore_regions_mask_dynamic_content()
    {
        var expected = Solid(10, 10, 255, 255, 255);
        var actual = expected.Clone();
        actual.FillRectangle(new PixelRectangle(0, 0, 5, 5), 0, 0, 0);

        var result = ImageComparer.Compare(expected, actual, new ImageCompareOptions { IgnoreRegions = [new PixelRectangle(0, 0, 5, 5)] });

        Assert.Equal(0, result.DiffPixels);
        Assert.Equal(75, result.TotalPixels);
    }

    [Fact]
    public void Size_changes_are_reported()
    {
        var result = ImageComparer.Compare(Solid(10, 10, 0, 0, 0), Solid(12, 10, 0, 0, 0));

        Assert.True(result.SizeMismatch);
        Assert.Equal(20, result.DiffPixels);
        Assert.Equal(12, result.DiffImage!.Width);
    }
}

public sealed class SnapshotStoreTests
{
    private static (SnapshotStore Store, string Root) NewStore(SnapshotUpdateMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "btp-visual-tests", Guid.NewGuid().ToString("N"));
        return (new SnapshotStore(Path.Combine(root, "baselines"), Path.Combine(root, "out"), mode), root);
    }

    private static byte[] Png(byte shade, int width = 8)
    {
        var image = new RgbaImage(width, 8);
        image.FillRectangle(new PixelRectangle(0, 0, width, 8), shade, shade, shade);
        return image.ToPng();
    }

    [Fact]
    public void Missing_baselines_are_written_locally_and_fail_on_ci()
    {
        var (local, _) = NewStore(SnapshotUpdateMode.Missing);
        Assert.Equal(VisualStatus.NewBaseline, local.Compare("story/desktop", Png(10)).Status);
        Assert.Equal(VisualStatus.Matched, local.Compare("story/desktop", Png(10)).Status);

        var (ci, _) = NewStore(SnapshotUpdateMode.None);
        var result = ci.Compare("story/desktop", Png(10));
        Assert.Equal(VisualStatus.MissingBaseline, result.Status);
        Assert.True(result.IsFailure);
        Assert.True(File.Exists(result.ActualPath));
    }

    [Fact]
    public void Mismatches_write_actual_expected_and_diff_then_approval_fixes_them()
    {
        var (store, _) = NewStore(SnapshotUpdateMode.Missing);
        store.Compare("button/primary", Png(10));

        var mismatch = store.Compare("button/primary", Png(250));

        Assert.Equal(VisualStatus.Mismatched, mismatch.Status);
        Assert.Equal(64, mismatch.DiffPixels);
        Assert.True(File.Exists(mismatch.DiffPath));
        Assert.True(File.Exists(mismatch.ActualPath));

        SnapshotStore.Approve(mismatch);
        Assert.Equal(VisualStatus.Matched, store.Compare("button/primary", Png(250)).Status);
        Assert.False(File.Exists(mismatch.DiffPath));
    }

    [Fact]
    public void Tolerances_and_update_modes_apply()
    {
        var (store, root) = NewStore(SnapshotUpdateMode.Missing);
        store.Compare("s", Png(10));

        Assert.Equal(VisualStatus.Matched, store.Compare("s", Png(250), maxDiffPixels: 64).Status);
        Assert.Equal(VisualStatus.SizeMismatch, store.Compare("s", Png(10, width: 9)).Status);

        var updating = new SnapshotStore(store.BaselineDirectory, Path.Combine(root, "out"), SnapshotUpdateMode.Changed);
        Assert.Equal(VisualStatus.Updated, updating.Compare("s", Png(250)).Status);
        Assert.Equal(VisualStatus.Matched, store.Compare("s", Png(250)).Status);
    }

    [Fact]
    public void Manifest_records_the_renderer_and_explains_mismatches_after_upgrades()
    {
        var old = new RenderEnvironment("chromium", "linux", "1.63.0", "141.0.1");
        var upgraded = old with { Playwright = "1.64.0", BrowserVersion = "142.0.1" };
        var (store, root) = NewStore(SnapshotUpdateMode.Missing);

        store.Compare("s", Png(10), environment: old);
        Assert.Equal(old, store.RecordedRenderer("chromium-linux"));
        Assert.Equal(old, BaselineManifest.Load(store.BaselineDirectory).Get("chromium-linux"));

        // "missing" claims empty slots only: a new story after the upgrade does not relabel older baselines.
        store.Compare("t", Png(20), environment: upgraded);
        Assert.Equal(old, BaselineManifest.Load(store.BaselineDirectory).Get("chromium-linux"));

        var ci = new SnapshotStore(store.BaselineDirectory, Path.Combine(root, "out"), SnapshotUpdateMode.None);
        var mismatch = ci.Compare("s", Png(200), environment: upgraded);
        Assert.Equal(VisualStatus.Mismatched, mismatch.Status);
        Assert.Contains("recorded with Playwright 1.63.0 (chromium 141.0.1); this run uses Playwright 1.64.0 (chromium 142.0.1)", mismatch.Note, StringComparison.Ordinal);
        Assert.Null(ci.Compare("s", Png(200), environment: old).Note);

        // Regenerating (changed) revalidates and relabels; CI never writes the manifest.
        var regenerate = new SnapshotStore(store.BaselineDirectory, Path.Combine(root, "out"), SnapshotUpdateMode.Changed);
        regenerate.Compare("s", Png(200), environment: upgraded);
        Assert.Equal(upgraded, BaselineManifest.Load(store.BaselineDirectory).Get("chromium-linux"));
        ci.Compare("s", Png(200), environment: old);
        Assert.Equal(upgraded, BaselineManifest.Load(store.BaselineDirectory).Get("chromium-linux"));
    }

    [Fact]
    public void Manifest_json_is_stable_and_round_trips()
    {
        var manifest = new BaselineManifest();
        manifest.Set(new RenderEnvironment("chromium", "win32", "1.63.0", "141.0.1"));
        manifest.Set(new RenderEnvironment("chromium", "linux", "1.63.0"));

        var json = manifest.ToJson();

        Assert.True(json.IndexOf("chromium-linux", StringComparison.Ordinal) < json.IndexOf("chromium-win32", StringComparison.Ordinal));
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.Equal(manifest.Entries, BaselineManifest.Parse(json).Entries);
        Assert.False(manifest.Set(new RenderEnvironment("chromium", "linux", "1.63.0")));
    }

    [Fact]
    public void Snapshot_names_exclude_the_hosting_model() =>
        Assert.Equal("inputs-button--primary/desktop.dark.chromium-linux.initial", SnapshotNaming.For("inputs-button--primary", "desktop", "dark", "chromium", "linux", "Initial"));
}
