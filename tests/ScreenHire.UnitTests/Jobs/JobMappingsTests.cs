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

        var publicJob = job.ToPublicJob();

        publicJob.Should().BeEquivalentTo(new PublicJob(
            job.Id, "Frontend Engineer (Svelte)", "frontend-engineer-svelte", "London, UK", WorkplaceType.Hybrid,
            EmploymentType.Contract, 60_000, 75_000, "GBP", Published));
    }

    [Fact]
    public void Summary_carries_the_status_for_the_dashboard()
    {
        var job = OpenJob();

        var summary = job.ToSummary();

        summary.Should().BeEquivalentTo(new JobSummary(
            job.Id, "Frontend Engineer (Svelte)", "frontend-engineer-svelte", "London, UK", WorkplaceType.Hybrid,
            JobStatus.Open, job.CreatedAt, Published));
    }

    private static Job OpenJob()
    {
        var job = new Job
        {
            OrganizationId = Guid.CreateVersion7(),
            Title = "Frontend Engineer (Svelte)",
            Slug = "frontend-engineer-svelte",
            Location = "London, UK",
            WorkplaceType = WorkplaceType.Hybrid,
            EmploymentType = EmploymentType.Contract,
            SalaryMin = 60_000,
            SalaryMax = 75_000,
            SalaryCurrency = "GBP",
        };
        job.Publish(Published);
        return job;
    }
}
