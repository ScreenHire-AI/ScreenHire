using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Contracts;

/// <summary>A job as shown on a public careers page.</summary>
public sealed record PublicJob(
    Guid Id,
    string Title,
    string Slug,
    string Location,
    WorkplaceType WorkplaceType,
    EmploymentType EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    DateTimeOffset? PublishedAt);

/// <summary>The public careers listing for one organization.</summary>
public sealed record PublicJobBoard(string OrganizationName, string OrganizationSlug, IReadOnlyList<PublicJob> Jobs);

/// <summary>A job as shown in the recruiter dashboard.</summary>
public sealed record JobSummary(
    Guid Id,
    string Title,
    string Slug,
    string Location,
    WorkplaceType WorkplaceType,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);

internal static class JobMappings
{
    public static PublicJob ToPublicJob(this Job job) =>
        new(job.Id, job.Title, job.Slug, job.Location, job.WorkplaceType, job.EmploymentType,
            job.SalaryMin, job.SalaryMax, job.SalaryCurrency, job.PublishedAt);

    public static JobSummary ToSummary(this Job job) =>
        new(job.Id, job.Title, job.Slug, job.Location, job.WorkplaceType, job.Status, job.CreatedAt, job.PublishedAt);
}
