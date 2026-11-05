using ScreenHire.Application.Applications;
using ScreenHire.Application.Verification.Content;

namespace ScreenHire.UnitTests.Applications;

/// <summary>
/// Which uploaded files the daily sweep removes: those a day old that no submitted application names. A claimed resume
/// must never go, however old, which is why a storage lifecycle rule (age only) was not used.
/// </summary>
public sealed class UnclaimedUploadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 3, 47, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now - UnclaimedUploads.Grace;

    [Fact]
    public void The_grace_is_one_day_as_the_privacy_page_says()
    {
        UnclaimedUploads.Grace.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void An_old_upload_no_application_names_is_removed()
    {
        var blobs = new[] { new StoredUploadBlob("org/job/resume/u1/cv.pdf", Cutoff.AddHours(-1)) };

        UnclaimedUploads.Select(blobs, new HashSet<string>(), Cutoff).Should().Equal("org/job/resume/u1/cv.pdf");
    }

    [Fact]
    public void An_old_upload_an_application_names_stays_however_old()
    {
        var blobs = new[] { new StoredUploadBlob("org/job/resume/u1/cv.pdf", Cutoff.AddDays(-400)) };

        UnclaimedUploads.Select(blobs, new HashSet<string> { "org/job/resume/u1/cv.pdf" }, Cutoff).Should().BeEmpty();
    }

    [Fact]
    public void A_recent_upload_waits_for_its_application()
    {
        var blobs = new[]
        {
            new StoredUploadBlob("org/job/resume/u2/cv.pdf", Cutoff.AddMinutes(1)),
            new StoredUploadBlob("org/job/resume/u3/cv.pdf", Now),
        };

        UnclaimedUploads.Select(blobs, new HashSet<string>(), Cutoff).Should().BeEmpty();
    }

    [Fact]
    public void An_upload_made_exactly_at_the_cutoff_is_removed()
    {
        var blobs = new[] { new StoredUploadBlob("org/job/resume/u4/cv.pdf", Cutoff) };

        UnclaimedUploads.Select(blobs, new HashSet<string>(), Cutoff).Should().Equal("org/job/resume/u4/cv.pdf");
    }

    [Fact]
    public void Names_are_compared_exactly()
    {
        var blobs = new[] { new StoredUploadBlob("org/job/resume/u5/CV.pdf", Cutoff.AddDays(-2)) };

        UnclaimedUploads.Select(blobs, new HashSet<string>(StringComparer.Ordinal) { "org/job/resume/u5/cv.pdf" }, Cutoff)
            .Should().Equal("org/job/resume/u5/CV.pdf");
    }
}
