using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

public sealed class JobLifecycleTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_job_is_a_draft_that_was_never_published_or_closed()
    {
        var job = NewJob();

        job.Status.Should().Be(JobStatus.Draft);
        job.PublishedAt.Should().BeNull();
        job.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void A_new_job_gets_a_time_ordered_version_7_id()
    {
        NewJob().Id.Version.Should().Be(7);
    }

    [Fact]
    public void Publishing_opens_the_job_and_records_when()
    {
        var job = NewJob();

        job.Publish(Monday);

        job.Status.Should().Be(JobStatus.Open);
        job.PublishedAt.Should().Be(Monday);
    }

    [Fact]
    public void Publishing_again_keeps_the_original_publish_date()
    {
        var job = NewJob();
        job.Publish(Monday);

        job.Publish(Monday.AddDays(3));

        job.Status.Should().Be(JobStatus.Open);
        job.PublishedAt.Should().Be(Monday);
    }

    [Fact]
    public void Closing_an_open_job_records_when_and_keeps_the_publish_date()
    {
        var job = NewJob();
        job.Publish(Monday);

        job.Close(Monday.AddDays(14));

        job.Status.Should().Be(JobStatus.Closed);
        job.PublishedAt.Should().Be(Monday);
        job.ClosedAt.Should().Be(Monday.AddDays(14));
    }

    [Fact]
    public void A_draft_can_be_closed_without_ever_being_published()
    {
        var job = NewJob();

        job.Close(Monday);

        job.Status.Should().Be(JobStatus.Closed);
        job.PublishedAt.Should().BeNull();
        job.ClosedAt.Should().Be(Monday);
    }

    [Fact]
    public void A_closed_job_cannot_be_republished()
    {
        var job = NewJob();
        job.Publish(Monday);
        job.Close(Monday.AddDays(1));

        var republish = () => job.Publish(Monday.AddDays(2));

        republish.Should().Throw<InvalidOperationException>().WithMessage("*closed job cannot be republished*");
        job.Status.Should().Be(JobStatus.Closed);
        job.PublishedAt.Should().Be(Monday);
    }

    private static Job NewJob() =>
        new()
        {
            OrganizationId = Guid.CreateVersion7(),
            Title = "Senior .NET Engineer",
            Slug = "senior-dotnet-engineer",
            Location = "Remote — UK & EU",
        };
}
