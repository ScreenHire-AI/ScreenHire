using System.Buffers.Binary;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>Brand images (docs/architecture/branding.md): their pixel size from the header, their limits and their scan.</summary>
public sealed class BrandImageTests
{
    [Fact]
    public void A_png_declares_its_size_in_its_header()
    {
        ImageDimensions.Read(Png(400, 100)).Should().Be((400, 100));
    }

    [Fact]
    public void A_jpeg_declares_its_size_in_its_first_frame_after_any_metadata()
    {
        ImageDimensions.Read(Jpeg(1200, 630, progressive: false)).Should().Be((1200, 630));
        ImageDimensions.Read(Jpeg(64, 48, progressive: true)).Should().Be((64, 48));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0, 2 })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    public void Anything_else_has_no_size(byte[] bytes)
    {
        ImageDimensions.Read(bytes).Should().BeNull();
    }

    [Fact]
    public void A_truncated_jpeg_has_no_size()
    {
        // Cut inside the frame segment, after its height and before its width.
        ImageDimensions.Read(Jpeg(64, 48, progressive: false).AsSpan(0, 31)).Should().BeNull();
    }

    [Fact]
    public void Each_kind_has_its_limits()
    {
        BrandImageRules.For(BrandImageKind.Logo).Problem(1024 * 1024, 400, 100).Should().BeNull();
        BrandImageRules.For(BrandImageKind.Logo).Problem((1024 * 1024) + 1, 400, 100).Should().Be("This file is larger than 1 MB.");
        BrandImageRules.For(BrandImageKind.Logo).Problem(1000, 5000, 100).Should().StartWith("This image is 5000 by 100 pixels.");
        BrandImageRules.For(BrandImageKind.Favicon).Problem(1000, 64, 64).Should().BeNull();
        BrandImageRules.For(BrandImageKind.Favicon).Problem((256 * 1024) + 1, 64, 64).Should().Be("This file is larger than 256 KB.");
        BrandImageRules.For(BrandImageKind.Favicon).Problem(1000, 64, 32).Should().Be("A favicon is square, and this image is 64 by 32 pixels.");
        BrandImageRules.For(BrandImageKind.Favicon).Problem(1000, 16, 16).Should().Be("A favicon is at least 32 by 32 pixels, and this image is 16 by 16.");
        BrandImageRules.For(BrandImageKind.SharingImage).Problem(1000, 1200, 630).Should().BeNull();
        BrandImageRules.For(BrandImageKind.SharingImage).Problem(1000, 400, 400).Should()
            .Be("A sharing image is at least 600 by 315 pixels, and this image is 400 by 400.");
    }

    [Fact]
    public void An_image_is_served_once_its_scan_allows_and_is_scanned_once()
    {
        var image = new BrandImage { Id = Guid.NewGuid(), BlobName = "x", ContentType = "image/png", Kind = BrandImageKind.Logo };
        image.IsServable.Should().BeFalse("it is still being scanned");

        image.RecordScan(MalwareScanStatus.Infected, DateTimeOffset.UnixEpoch).Should().BeTrue();
        image.RecordScan(MalwareScanStatus.Clean, DateTimeOffset.UnixEpoch).Should().BeFalse("a file is scanned once");
        image.IsServable.Should().BeFalse();

        var clean = new BrandImage { Id = Guid.NewGuid(), BlobName = "y", ContentType = "image/jpeg", Kind = BrandImageKind.Logo };
        clean.RecordScan(MalwareScanStatus.NotScanned, DateTimeOffset.UnixEpoch);
        clean.IsServable.Should().BeTrue("no scanner is configured, as applicant files are offered");
    }

    [Fact]
    public void Its_address_is_the_same_for_everyone_and_names_nobody()
    {
        var image = new BrandImage { Id = Guid.Parse("0f8b3a52-6a4e-4a9b-9c1e-2d7f3e4b5a6c"), BlobName = "x", ContentType = "image/jpeg" };

        image.UrlUnder("https://acme.careers.example.com/").Should()
            .Be("https://acme.careers.example.com/brand/images/0f8b3a52-6a4e-4a9b-9c1e-2d7f3e4b5a6c.jpg");
    }

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    // SOI, an APP1 (EXIF) segment, a DQT, then the frame (SOF0 baseline or SOF2 progressive), then the scan.
    private static byte[] Jpeg(int width, int height, bool progressive)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xE1, 0x00, 0x10]);
        bytes.AddRange(new byte[14]);
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x04, 0x00, 0x00]);
        bytes.AddRange([0xFF, progressive ? (byte)0xC2 : (byte)0xC0, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        bytes.AddRange(new byte[10]);
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x02]);
        return [.. bytes];
    }
}
