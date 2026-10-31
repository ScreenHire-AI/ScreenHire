using System.Buffers.Binary;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// The pixel size a PNG or JPEG file declares, read from its header: PNG's IHDR chunk, or a JPEG's first
/// start-of-frame marker. No image library: ScreenHire stores brand images as they are uploaded and never decodes them.
/// </summary>
public static class ImageDimensions
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The width and height <paramref name="bytes"/> declare, or null when they are not a readable PNG or JPEG.</summary>
    public static (int Width, int Height)? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(PngSignature))
        {
            return ReadPng(bytes);
        }

        return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? ReadJpeg(bytes) : null;
    }

    // The signature, then the IHDR chunk: its length (4), "IHDR" (4), the width (4) and the height (4), big-endian.
    private static (int, int)? ReadPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        return width > 0 && height > 0 ? (width, height) : null;
    }

    // Walks the segments after SOI: each is 0xFF, a marker and (for most markers) a two-byte length that counts itself.
    // A start-of-frame (C0 to CF, except C4 Huffman tables, C8 reserved and CC arithmetic coding) holds the precision
    // (1), the height (2) and the width (2). The scan (DA) or the end (D9) before any frame means there is none.
    private static (int, int)? ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        var at = 2;
        while (at + 1 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            var marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                at++; // fill byte
                continue;
            }

            at += 2;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue; // standalone markers carry no length
            }

            if (marker is 0xD9 or 0xDA || at + 2 > bytes.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));
            if (length < 2)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (at + 7 > bytes.Length)
                {
                    return null;
                }

                int height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 5, 2));
                return width > 0 && height > 0 ? (width, height) : null;
            }

            at += length;
        }

        return null;
    }
}
