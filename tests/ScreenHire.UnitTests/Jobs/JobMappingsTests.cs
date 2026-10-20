using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

public sealed class JobMappingsTests
{
    private static readonly DateTimeOffset Published = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Public_job_carries_what_a_careers_page_shows()
    {
        var job = OpenJob();

        var publicJob = job.ToPublicJob("https://careers.example.com/acme/jobs/frontend-engineer-svelte");

        publicJob.Should().BeEquivalentTo(new PublicJob(
            job.Id, "Frontend Engineer (Svelte)", "frontend-engineer-svelte", "London, England, GB", WorkplaceType.Hybrid,
            EmploymentType.Contract, 60_000, 75_000, "GBP", Published, "<p>Svelte all day.</p>", "Engineering",
            [new JobLocation("London", "England", "GB")], [], SalaryPeriod.Year, null, Published,
            "https://careers.example.com/acme/jobs/frontend-engineer-svelte"));
    }

    [Fact]
    public void A_closed_job_expires_at_the_moment_it_closed()
    {
        var job = OpenJob();
        var closedAt = Published.AddDays(3);
        job.Close(closedAt);

        job.ToPublicJob("https://careers.example.com/acme/jobs/a").ValidThrough.Should().Be(closedAt, "the careers page's 410 carries a past validThrough for Google");
    }

    [Fact]
    public void Summary_carries_the_status_for_the_dashboard()
    {
        var job = OpenJob();

        var summary = job.ToSummary();

        summary.Should().BeEquivalentTo(new JobSummary(
            job.Id, "Frontend Engineer (Svelte)", "frontend-engineer-svelte", "London, England, GB", WorkplaceType.Hybrid,
            JobStatus.Open, job.CreatedAt, Published, EmploymentType.Contract, "Engineering", false));
    }

    [Fact]
    public void Detail_shows_unpublished_changes_only_when_an_open_jobs_content_differs_from_its_newest_version()
    {
        var job = OpenJob();
        var published = job.Snapshot();

        job.ToDetail(published, null, null, []).HasUnpublishedChanges.Should().BeFalse();
        job.Revise(published with { Title = "Svelte Engineer" }, Published.AddDays(1));
        job.ToDetail(published, null, null, []).HasUnpublishedChanges.Should().BeTrue();
        job.ToDetail(published, null, null, []).Template.Should().BeNull("the job has no source template");
    }

    private static Job OpenJob()
    {
        var job = new Job
        {
            OrganizationId = Guid.CreateVersion7(),
            Title = "Frontend Engineer (Svelte)",
            Slug = "frontend-engineer-svelte",
            Description = "<p>Svelte all day.</p>",
            Department = "Engineering",
            Locations = [new JobLocation("London", "England", "GB")],
            WorkplaceType = WorkplaceType.Hybrid,
            EmploymentType = EmploymentType.Contract,
        };
        job.SetSalary(new JobSalary(60_000, 75_000, "GBP", SalaryPeriod.Year));
        job.Publish(Published);
        return job;
    }
}
