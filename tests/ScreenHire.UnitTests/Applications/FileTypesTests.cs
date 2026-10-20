using System.Text;
using ScreenHire.Api.Applications;

namespace ScreenHire.UnitTests.Applications;

public sealed class FileTypesTests
{
    [Theory]
    [InlineData(".pdf", "%PDF-1.7")]
    [InlineData(".PDF", "%PDF-1.4")]
    [InlineData(".docx", "PK\u0003\u0004rest")]
    [InlineData(".png", "\u0089PNG\r\n\u001a\n")]
    [InlineData(".jpg", "\u00ff\u00d8\u00ffjpeg")]
    public void A_file_that_starts_like_its_type_matches(string extension, string head)
    {
        FileTypes.Matches(extension, Encoding.Latin1.GetBytes(head)).Should().BeTrue();
    }

    [Theory]
    [InlineData(".pdf", "MZ\u0090\u0000")] // a Windows program named .pdf
    [InlineData(".docx", "%PDF-1.7")]
    [InlineData(".pdf", "")]
    [InlineData(".exe", "MZ")]
    [InlineData(".svg", "<svg")]
    public void A_file_that_does_not_start_like_its_type_does_not_match(string extension, string head)
    {
        FileTypes.Matches(extension, Encoding.Latin1.GetBytes(head)).Should().BeFalse();
    }

    [Fact]
    public void A_question_narrows_the_allowed_types_and_never_widens_them()
    {
        FileTypes.AcceptedBy([".PDF", ".docx", ".exe"]).Should().Equal(".pdf", ".docx");
        FileTypes.AcceptedBy(null).Should().Contain([".pdf", ".docx", ".doc", ".png", ".jpg"]).And.NotContain(".exe");
    }

    [Theory]
    [InlineData("Casey Morgan CV.pdf", "Casey Morgan CV.pdf")]
    [InlineData(@"C:\Users\dev\Desktop\cv.PDF", "cv.pdf")]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("résumé (final).docx", "r_sum_ _final_.docx")]
    [InlineData(".pdf", "file.pdf")]
    [InlineData("no-extension", null)]
    [InlineData("cv.p/df", null)]
    [InlineData("", null)]
    public void File_names_are_made_safe_to_store_and_show(string name, string? expected)
    {
        FileTypes.SafeFileName(name).Should().Be(expected);
    }

    [Fact]
    public void A_long_name_is_cut_and_keeps_its_extension()
    {
        var safe = FileTypes.SafeFileName(new string('a', 300) + ".pdf");

        safe.Should().HaveLength(100).And.EndWith(".pdf");
    }
}
