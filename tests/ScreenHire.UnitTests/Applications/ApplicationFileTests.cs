using ScreenHire.Domain.Applications;

namespace ScreenHire.UnitTests.Applications;

public sealed class ApplicationFileTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_file_is_scanned_once_so_a_redelivered_result_changes_nothing()
    {
        var file = new ApplicationFile { QuestionId = "resume", BlobName = "o/j/resume/u/cv.pdf", FileName = "cv.pdf", ContentType = "application/pdf", Sha256 = "ab" };

        file.RecordScan(MalwareScanStatus.Clean, Monday).Should().BeTrue();
        file.RecordScan(MalwareScanStatus.Infected, Monday.AddMinutes(1)).Should().BeFalse();

        file.ScanStatus.Should().Be(MalwareScanStatus.Clean);
        file.ScannedAt.Should().Be(Monday);
    }

    [Fact]
    public void Pending_is_not_a_result()
    {
        var file = new ApplicationFile { QuestionId = "resume", BlobName = "b", FileName = "cv.pdf", ContentType = "application/pdf", Sha256 = "ab" };

        var record = () => file.RecordScan(MalwareScanStatus.Pending, Monday);

        record.Should().Throw<ArgumentException>();
    }
}
