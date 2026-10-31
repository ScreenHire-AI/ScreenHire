using System.Buffers.Binary;
using System.IO.Compression;

namespace ScreenHire.E2E;

/// <summary>A real PNG a browser decodes, one flat colour, built in memory so no image file is committed.</summary>
internal static class TestPng
{
    public static byte[] Solid(int width, int height)
    {
        // Each row is a filter byte (0, none) then RGB pixels; a flat teal compresses to almost nothing.
        var raw = new byte[height * (1 + (width * 3))];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * (1 + (width * 3))) + 1 + (x * 3);
                raw[at] = 0x1f;
                raw[at + 1] = 0x4e;
                raw[at + 2] = 0x5a;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR"u8, header);
        Chunk(png, "IDAT"u8, compressed.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        png.Write(four);
        png.Write(type);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(four, Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF);
        png.Write(four);
    }

    // PNG's CRC-32 (ISO 3309), bit by bit: fast enough for a test image's few chunks.
    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc;
    }
}
