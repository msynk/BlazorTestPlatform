using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BlazorTestPlatform.Visual;

/// <summary>
/// A small, dependency-free PNG codec. Decodes every standard color type and bit depth (non-interlaced and Adam7)
/// into RGBA8; encodes RGBA8 with per-row adaptive filtering. Keeping this in-house avoids imaging-library
/// licensing issues and keeps the package tiny.
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Decodes a PNG into an RGBA image.</summary>
    public static RgbaImage Decode(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < Signature.Length + 12 || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG file (bad signature).");
        }

        var header = default(Header);
        var headerSeen = false;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();

        var offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            if (length < 0 || offset + 12 + length > png.Length)
            {
                throw new InvalidDataException("Corrupt PNG chunk length.");
            }

            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            offset += 12 + length;

            switch (type)
            {
                case "IHDR":
                    header = new Header(
                        (int)BinaryPrimitives.ReadUInt32BigEndian(data),
                        (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
                        data[8],
                        data[9],
                        data[12]);
                    headerSeen = true;
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    transparency = data.ToArray();
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    offset = png.Length;
                    break;
            }
        }

        if (!headerSeen)
        {
            throw new InvalidDataException("PNG has no IHDR chunk.");
        }

        if (header.Width <= 0 || header.Height <= 0)
        {
            throw new InvalidDataException("PNG has invalid dimensions.");
        }

        var channels = header.ColorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"Unsupported PNG color type {header.ColorType}."),
        };

        if (header.ColorType == 3 && palette is null)
        {
            throw new InvalidDataException("Indexed PNG without palette.");
        }

        byte[] raw;
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        using (var inflated = new MemoryStream())
        {
            zlib.CopyTo(inflated);
            raw = inflated.ToArray();
        }

        var image = new RgbaImage(header.Width, header.Height);
        var bitsPerPixel = channels * header.BitDepth;
        var filterUnit = Math.Max(1, bitsPerPixel / 8);

        if (header.Interlace == 0)
        {
            var position = 0;
            DecodePass(raw, ref position, header, palette, transparency, channels, bitsPerPixel, filterUnit, image, 0, 0, 1, 1, header.Width, header.Height);
        }
        else if (header.Interlace == 1)
        {
            ReadOnlySpan<(int X0, int Y0, int Dx, int Dy)> passes = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            var position = 0;
            foreach (var (x0, y0, dx, dy) in passes)
            {
                var passWidth = (header.Width - x0 + dx - 1) / dx;
                var passHeight = (header.Height - y0 + dy - 1) / dy;
                if (passWidth > 0 && passHeight > 0)
                {
                    DecodePass(raw, ref position, header, palette, transparency, channels, bitsPerPixel, filterUnit, image, x0, y0, dx, dy, passWidth, passHeight);
                }
            }
        }
        else
        {
            throw new InvalidDataException($"Unsupported interlace method {header.Interlace}.");
        }

        return image;
    }

    /// <summary>Encodes an RGBA image as PNG (color type 6, 8-bit, adaptive filtering).</summary>
    public static byte[] Encode(RgbaImage image, CompressionLevel compression = CompressionLevel.Optimal)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stride = image.Width * 4;
        var filtered = new byte[(stride + 1) * image.Height];
        var candidate = new byte[stride];
        var best = new byte[stride];

        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Pixels.AsSpan(y * stride, stride);
            var previous = y > 0 ? image.Pixels.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            var bestScore = long.MaxValue;
            byte bestFilter = 0;

            for (byte filter = 0; filter <= 4; filter++)
            {
                ApplyFilter(filter, row, previous, candidate, 4);
                long score = 0;
                foreach (var b in candidate)
                {
                    score += (sbyte)b < 0 ? -(sbyte)b : b;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestFilter = filter;
                    candidate.CopyTo(best, 0);
                }
            }

            filtered[y * (stride + 1)] = bestFilter;
            best.CopyTo(filtered.AsSpan(y * (stride + 1) + 1));
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, compression, leaveOpen: true))
            {
                zlib.Write(filtered);
            }

            compressed = buffer.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Encodes with a forced filter type (tests and tooling).</summary>
    internal static byte[] EncodeWithFilter(RgbaImage image, byte filter)
    {
        var stride = image.Width * 4;
        var filtered = new byte[(stride + 1) * image.Height];
        var scratch = new byte[stride];
        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Pixels.AsSpan(y * stride, stride);
            var previous = y > 0 ? image.Pixels.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            ApplyFilter(filter, row, previous, scratch, 4);
            filtered[y * (stride + 1)] = filter;
            scratch.CopyTo(filtered.AsSpan(y * (stride + 1) + 1));
        }

        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        using var output = new MemoryStream();
        output.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", buffer.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void DecodePass(
        byte[] raw,
        ref int position,
        Header header,
        byte[]? palette,
        byte[]? transparency,
        int channels,
        int bitsPerPixel,
        int filterUnit,
        RgbaImage image,
        int x0,
        int y0,
        int dx,
        int dy,
        int passWidth,
        int passHeight)
    {
        var stride = (passWidth * bitsPerPixel + 7) / 8;
        var previous = new byte[stride];
        var current = new byte[stride];

        for (var row = 0; row < passHeight; row++)
        {
            if (position + 1 + stride > raw.Length)
            {
                throw new InvalidDataException("PNG image data is truncated.");
            }

            var filter = raw[position];
            raw.AsSpan(position + 1, stride).CopyTo(current);
            position += 1 + stride;
            Unfilter(filter, current, previous, filterUnit);

            var y = y0 + row * dy;
            for (var column = 0; column < passWidth; column++)
            {
                var x = x0 + column * dx;
                WritePixel(image, x, y, current, column, header, palette, transparency, channels);
            }

            (previous, current) = (current, previous);
        }
    }

    private static void WritePixel(RgbaImage image, int x, int y, byte[] row, int column, Header header, byte[]? palette, byte[]? transparency, int channels)
    {
        var depth = header.BitDepth;
        int Sample(int index)
        {
            if (depth == 8)
            {
                return row[column * channels + index];
            }

            if (depth == 16)
            {
                var offset = (column * channels + index) * 2;
                return row[offset];
            }

            var bitOffset = (column * channels + index) * depth;
            var value = (row[bitOffset >> 3] >> (8 - depth - (bitOffset & 7))) & ((1 << depth) - 1);
            return value;
        }

        int Raw16(int index)
        {
            var offset = (column * channels + index) * 2;
            return (row[offset] << 8) | row[offset + 1];
        }

        int Scale(int value) => depth switch
        {
            1 => value * 255,
            2 => value * 85,
            4 => value * 17,
            _ => value,
        };

        switch (header.ColorType)
        {
            case 0:
            {
                var gray = (byte)Scale(Sample(0));
                var alpha = (byte)255;
                if (transparency is { Length: >= 2 })
                {
                    var key = (transparency[0] << 8) | transparency[1];
                    var rawValue = depth == 16 ? Raw16(0) : Sample(0);
                    if (rawValue == key)
                    {
                        alpha = 0;
                    }
                }

                image.SetPixel(x, y, gray, gray, gray, alpha);
                break;
            }

            case 2:
            {
                var alpha = (byte)255;
                if (transparency is { Length: >= 6 })
                {
                    var r = depth == 16 ? Raw16(0) : Sample(0);
                    var g = depth == 16 ? Raw16(1) : Sample(1);
                    var b = depth == 16 ? Raw16(2) : Sample(2);
                    if (r == ((transparency[0] << 8) | transparency[1]) && g == ((transparency[2] << 8) | transparency[3]) && b == ((transparency[4] << 8) | transparency[5]))
                    {
                        alpha = 0;
                    }
                }

                image.SetPixel(x, y, (byte)Sample(0), (byte)Sample(1), (byte)Sample(2), alpha);
                break;
            }

            case 3:
            {
                var index = Sample(0);
                if (index * 3 + 2 >= palette!.Length)
                {
                    throw new InvalidDataException("PNG palette index out of range.");
                }

                var alpha = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                image.SetPixel(x, y, palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2], alpha);
                break;
            }

            case 4:
            {
                var gray = (byte)Sample(0);
                image.SetPixel(x, y, gray, gray, gray, (byte)Sample(1));
                break;
            }

            case 6:
                image.SetPixel(x, y, (byte)Sample(0), (byte)Sample(1), (byte)Sample(2), (byte)Sample(3));
                break;
        }
    }

    private static void Unfilter(byte filter, Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (var i = bpp; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + current[i - bpp]);
                }

                return;
            case 2:
                for (var i = 0; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + previous[i]);
                }

                return;
            case 3:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    current[i] = (byte)(current[i] + ((left + previous[i]) >> 1));
                }

                return;
            case 4:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    var upLeft = i >= bpp ? previous[i - bpp] : 0;
                    current[i] = (byte)(current[i] + Paeth(left, previous[i], upLeft));
                }

                return;
            default:
                throw new InvalidDataException($"Invalid PNG filter type {filter}.");
        }
    }

    private static void ApplyFilter(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> output, int bpp)
    {
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= bpp ? row[i - bpp] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            var upLeft = i >= bpp && !previous.IsEmpty ? previous[i - bpp] : 0;
            output[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] - left),
                2 => (byte)(row[i] - up),
                3 => (byte)(row[i] - ((left + up) >> 1)),
                _ => (byte)(row[i] - Paeth(left, up, upLeft)),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = UpdateCrc(0xFFFFFFFFu, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        output.Write(buffer);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private readonly record struct Header(int Width, int Height, byte BitDepth, byte ColorType, byte Interlace);
}
