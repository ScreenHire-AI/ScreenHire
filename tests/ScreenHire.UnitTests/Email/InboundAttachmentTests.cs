using ScreenHire.Api.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// Which files that come with a candidate's reply ScreenHire keeps: PDF, Word and PNG or JPEG, up to 10 MB, whose first
/// bytes are what the extension says. Everything else is dropped.
/// </summary>
public sealed class InboundAttachmentTests
{
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

    private static bool Accepts(string name, byte[] content, out (string FileName, string ContentType) accepted) =>
        AttachmentFiles.TryAccept(new InboundAttachment(name, content), out accepted);

    [Fact]
    public void A_pdf_is_kept_with_its_content_type()
    {
        Accepts("Portfolio.PDF", Pdf, out var accepted).Should().BeTrue();

        accepted.Should().Be(("Portfolio.pdf", "application/pdf"));
    }

    [Fact]
    public void A_png_is_kept()
    {
        Accepts("photo.png", Png, out var accepted).Should().BeTrue();

        accepted.ContentType.Should().Be("image/png");
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("script.js")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void A_type_ScreenHire_does_not_keep_is_dropped(string name)
    {
        Accepts(name, Exe, out _).Should().BeFalse();
    }

    [Fact]
    public void A_file_whose_bytes_are_not_what_its_extension_says_is_dropped()
    {
        Accepts("resume.pdf", Exe, out _).Should().BeFalse("an executable renamed to .pdf");
        Accepts("photo.png", Pdf, out _).Should().BeFalse();
    }

    [Fact]
    public void An_empty_file_and_one_over_10_MB_are_dropped_and_one_of_exactly_10_MB_is_kept()
    {
        Accepts("resume.pdf", [], out _).Should().BeFalse();

        var limit = new byte[InboundEmailIngest.MaxAttachmentBytes];
        Pdf.CopyTo(limit, 0);
        Accepts("resume.pdf", limit, out _).Should().BeTrue();
        Accepts("resume.pdf", [.. limit, 0], out _).Should().BeFalse();
    }

    [Fact]
    public void A_path_in_the_name_is_cut_to_the_file_name()
    {
        Accepts("..\\..\\windows\\notes.pdf", Pdf, out var accepted).Should().BeTrue();

        accepted.FileName.Should().Be("notes.pdf");
    }
}
