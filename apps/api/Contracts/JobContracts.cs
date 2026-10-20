using FluentValidation;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Contracts;

/// <summary>A job as shown on a public careers page.</summary>
/// <remarks>
/// <c>location</c> is a display label; structured data (JSON-LD, feeds) reads <c>locations</c> and
/// <c>applicantLocationRequirements</c>. <c>description</c> is sanitized HTML.
/// </remarks>
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
    DateTimeOffset? PublishedAt,
    string Description,
    string? Department,
    IReadOnlyList<JobLocation> Locations,
    IReadOnlyList<string> ApplicantLocationRequirements,
    SalaryPeriod? SalaryPeriod,
    DateTimeOffset? ValidThrough);

/// <summary>The public careers listing for one organization.</summary>
public sealed record PublicJobBoard(string OrganizationName, string OrganizationSlug, IReadOnlyList<PublicJob> Jobs);

/// <summary>A job as shown in the recruiter dashboard's list.</summary>
/// <remarks><c>templateUpdateAvailable</c>: the job's source template has a newer published version than the job copied.</remarks>
public sealed record JobSummary(
    Guid Id,
    string Title,
    string Slug,
    string Location,
    WorkplaceType WorkplaceType,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    EmploymentType EmploymentType,
    string? Department,
    bool TemplateUpdateAvailable);

/// <summary>Everything about one job: posting content, lifecycle, its workflow copy and where that came from.</summary>
/// <remarks>
/// <c>publishedVersion</c> is the newest <c>JobVersion</c> (0 before the first publish). <c>hasUnpublishedChanges</c>
/// means the job is open and its content differs from that version: publishing again freezes a new version.
/// </remarks>
public sealed record JobDetail(
    Guid Id,
    string Title,
    string Slug,
    JobStatus Status,
    string Description,
    string? Department,
    EmploymentType EmploymentType,
    WorkplaceType WorkplaceType,
    IReadOnlyList<JobLocation> Locations,
    IReadOnlyList<string> ApplicantLocationRequirements,
    string Location,
    JobSalary? Salary,
    DateTimeOffset? ValidThrough,
    HiringWorkflow Workflow,
    int WorkflowVersion,
    JobTemplateSource? Template,
    int PublishedVersion,
    bool HasUnpublishedChanges,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ClosedAt);

/// <summary>The template version a job's workflow was copied from, and whether a newer one is published.</summary>
public sealed record JobTemplateSource(
    Guid TemplateId, string TemplateName, Guid VersionId, int Version, int? LatestPublishedVersion, bool UpdateAvailable);

/// <summary>
/// A new job. With <c>templateVersionId</c> (a published version of an own or system template), the job copies that
/// version's workflow, and every field left out here takes the template's posting default. Without a slug, one is made
/// from the title.
/// </summary>
public sealed record CreateJobRequest(
    string Title,
    string? Slug = null,
    Guid? TemplateVersionId = null,
    string? Description = null,
    string? Department = null,
    EmploymentType? EmploymentType = null,
    WorkplaceType? WorkplaceType = null,
    IReadOnlyList<JobLocation>? Locations = null,
    IReadOnlyList<string>? ApplicantLocationRequirements = null,
    SalaryRequest? Salary = null,
    DateTimeOffset? ValidThrough = null);

/// <summary>Replaces a job's posting content. A published job keeps its slug; a closed job cannot change.</summary>
public sealed record UpdateJobRequest(
    string Title,
    EmploymentType EmploymentType,
    WorkplaceType WorkplaceType,
    string? Slug = null,
    string? Description = null,
    string? Department = null,
    IReadOnlyList<JobLocation>? Locations = null,
    IReadOnlyList<string>? ApplicantLocationRequirements = null,
    SalaryRequest? Salary = null,
    DateTimeOffset? ValidThrough = null);

/// <summary>A pay range. Currency and period may come from the template's posting defaults on create.</summary>
public sealed record SalaryRequest(decimal? Min, decimal? Max, string? Currency = null, SalaryPeriod? Period = null);

/// <summary>One published version of a job, without its content.</summary>
public sealed record JobVersionSummary(Guid Id, int Version, DateTimeOffset PublishedAt);

/// <summary>One published version of a job: the frozen content boards were sent.</summary>
public sealed record JobVersionDetail(Guid Id, Guid JobId, int Version, DateTimeOffset PublishedAt, JobSnapshot Snapshot);

/// <summary>
/// How the job's workflow differs from the newest published version of the template it came from. Empty
/// <c>changes</c> with <c>updateAvailable</c> false: the job is up to date.
/// </summary>
public sealed record JobTemplateDiff(
    Guid TemplateId,
    Guid SourceVersionId,
    int SourceVersion,
    Guid LatestVersionId,
    int LatestVersion,
    bool UpdateAvailable,
    IReadOnlyList<WorkflowChange> Changes);

/// <summary>One difference between two workflows. <c>before</c> and <c>after</c> are values, or JSON for objects and lists.</summary>
/// <remarks>Paths name list elements by id, such as <c>stages[screening].slaDays</c>; <c>stages.order</c> is the order of the stages.</remarks>
public sealed record WorkflowChange(string Path, WorkflowChangeKind Kind, string? Before, string? After);

public enum WorkflowChangeKind
{
    Added,
    Removed,
    Changed,
}

internal static class JobMappings
{
    public static string LocationLabel(this Job job) =>
        JobLocation.Describe(job.WorkplaceType, job.Locations, job.ApplicantLocationRequirements);

    public static PublicJob ToPublicJob(this Job job) =>
        new(job.Id, job.Title, job.Slug, job.LocationLabel(), job.WorkplaceType, job.EmploymentType,
            job.SalaryMin, job.SalaryMax, job.SalaryCurrency, job.PublishedAt, job.Description, job.Department,
            job.Locations, job.ApplicantLocationRequirements, job.Salary?.Period, job.ValidThrough);

    public static JobSummary ToSummary(this Job job, int? latestTemplateVersion = null) =>
        new(job.Id, job.Title, job.Slug, job.LocationLabel(), job.WorkplaceType, job.Status, job.CreatedAt, job.PublishedAt,
            job.EmploymentType, job.Department, latestTemplateVersion > job.SourceTemplateVersion);

    /// <param name="job">The job.</param>
    /// <param name="latestSnapshot">The newest <c>JobVersion</c>'s content, or null if it was never published.</param>
    /// <param name="templateName">The source template's name, if the job has one.</param>
    /// <param name="latestTemplateVersion">The source template's newest published version number.</param>
    public static JobDetail ToDetail(this Job job, JobSnapshot? latestSnapshot, string? templateName, int? latestTemplateVersion) =>
        new(job.Id, job.Title, job.Slug, job.Status, job.Description, job.Department, job.EmploymentType, job.WorkplaceType,
            job.Locations, job.ApplicantLocationRequirements, job.LocationLabel(), job.Salary, job.ValidThrough,
            job.Workflow, job.WorkflowVersion, job.ToTemplateSource(templateName, latestTemplateVersion), job.PublishedVersion,
            job.Status == JobStatus.Open && latestSnapshot != job.Snapshot(),
            job.CreatedAt, job.UpdatedAt, job.PublishedAt, job.ClosedAt);

    private static JobTemplateSource? ToTemplateSource(this Job job, string? templateName, int? latestTemplateVersion) =>
        job is { SourceTemplateId: { } templateId, SourceTemplateVersionId: { } versionId, SourceTemplateVersion: { } version }
            ? new(templateId, templateName ?? "", versionId, version, latestTemplateVersion, latestTemplateVersion > version)
            : null;

    public static JobVersionSummary ToSummary(this JobVersion version) => new(version.Id, version.Version, version.PublishedAt);

    public static JobVersionDetail ToDetail(this JobVersion version) =>
        new(version.Id, version.JobId, version.Version, version.PublishedAt, version.Snapshot);
}

/// <summary>
/// The posting content rules every save checks (create and update), on the content as it will be stored. What a job
/// needs before it can be published is <see cref="JobPublishValidator"/>.
/// </summary>
internal sealed class JobContentValidator : AbstractValidator<JobSnapshot>
{
    public const int MaxDescriptionLength = 50_000;

    public JobContentValidator()
    {
        RuleFor(j => j.Title).NotEmpty().MaximumLength(200);
        RuleFor(j => j.Slug).NotEmpty().MaximumLength(120).Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("A slug is lowercase letters and digits separated by single hyphens.");
        RuleFor(j => j.Description).MaximumLength(MaxDescriptionLength);
        RuleFor(j => j.Department).MaximumLength(100).Must(d => d is null || d.Trim().Length > 0)
            .WithMessage("Leave the department out rather than sending it blank.");
        RuleFor(j => j.EmploymentType).IsInEnum();
        RuleFor(j => j.WorkplaceType).IsInEnum();
        RuleFor(j => j.Locations).Must(l => l.Count <= 20).WithMessage("A job has at most 20 locations.");
        RuleForEach(j => j.Locations).ChildRules(location =>
        {
            location.RuleFor(l => l.Country).NotEmpty().Matches("^[A-Z]{2}$").WithMessage("A country is an ISO 3166-1 alpha-2 code such as GB.");
            location.RuleFor(l => l.City).MaximumLength(100);
            location.RuleFor(l => l.Region).MaximumLength(100);
        });
        RuleFor(j => j.ApplicantLocationRequirements)
            .Must(r => r.Count <= 50).WithMessage("At most 50 countries.")
            .Must(r => r.Distinct(StringComparer.Ordinal).Count() == r.Count).WithMessage("Each country once.");
        RuleForEach(j => j.ApplicantLocationRequirements).Matches("^[A-Z]{2}$")
            .WithMessage("A country is an ISO 3166-1 alpha-2 code such as GB.");
        RuleFor(j => j.Salary!).ChildRules(salary =>
        {
            salary.RuleFor(s => s.Currency).NotEmpty().Matches("^[A-Z]{3}$").WithMessage("A currency is an ISO 4217 code such as GBP.");
            salary.RuleFor(s => s.Period).IsInEnum();
            salary.RuleFor(s => s.Min).GreaterThanOrEqualTo(0);
            salary.RuleFor(s => s.Max).GreaterThanOrEqualTo(s => s.Min ?? 0).WithMessage("The maximum is below the minimum.");
            salary.RuleFor(s => s).Must(s => s.Min is not null || s.Max is not null)
                .OverridePropertyName("Min").WithMessage("A salary needs a minimum, a maximum or both.");
        }).When(j => j.Salary is not null);
    }
}

/// <summary>
/// What a job needs before it can be published, beyond <see cref="JobContentValidator"/>: what Google for Jobs requires
/// of a <c>JobPosting</c> (docs/architecture/job-distribution.md): a description, and a place for on-site and hybrid
/// jobs or the countries a remote candidate may live in.
/// </summary>
internal sealed class JobPublishValidator : AbstractValidator<JobSnapshot>
{
    public JobPublishValidator()
    {
        RuleFor(j => j.Description).NotEmpty().WithMessage("Add a description before publishing.");
        RuleFor(j => j.Locations).NotEmpty().When(j => j.WorkplaceType != WorkplaceType.Remote)
            .WithMessage("An on-site or hybrid job needs a location before publishing.");
        RuleFor(j => j.ApplicantLocationRequirements).NotEmpty().When(j => j.WorkplaceType == WorkplaceType.Remote)
            .WithMessage("A remote job needs the countries candidates may work from before publishing.");
    }
}
