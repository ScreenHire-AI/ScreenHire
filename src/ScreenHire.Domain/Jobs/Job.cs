namespace ScreenHire.Domain.Jobs;

/// <summary>
/// A job opening. The canonical job model is a superset of schema.org <c>JobPosting</c>
/// (see docs/architecture/job-distribution.md); this is the minimal slice the scaffold needs.
/// </summary>
public sealed class Job
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Title { get; set; }

    /// <summary>URL-safe identifier, unique within the organization.</summary>
    public required string Slug { get; set; }

    public required string Location { get; set; }

    public WorkplaceType WorkplaceType { get; set; }

    public EmploymentType EmploymentType { get; set; }

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string? SalaryCurrency { get; set; }

    public JobStatus Status { get; private set; } = JobStatus.Draft;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public void Publish(DateTimeOffset now)
    {
        if (Status == JobStatus.Closed)
        {
            throw new InvalidOperationException("A closed job cannot be republished; create a new job instead.");
        }

        Status = JobStatus.Open;
        PublishedAt ??= now;
    }

    public void Close(DateTimeOffset now)
    {
        Status = JobStatus.Closed;
        ClosedAt = now;
    }
}

public enum JobStatus
{
    Draft,
    Open,
    Closed,
}

public enum WorkplaceType
{
    Onsite,
    Hybrid,
    Remote,
}

public enum EmploymentType
{
    FullTime,
    PartTime,
    Contract,
    Temporary,
    Internship,
}
