namespace ScreenHire.Domain.Jobs;

/// <summary>
/// A place the job is based: schema.org <c>PostalAddress</c> without the street. <see cref="Country"/> is an ISO 3166-1
/// alpha-2 code. The API requires it; a location carried over from the old free-text column may lack it.
/// </summary>
public sealed record JobLocation(string? City, string? Region, string? Country)
{
    /// <summary>
    /// A one-line label for lists and page titles, such as "London, England, GB" or "Remote (GB, IE)". Structured data
    /// (JSON-LD, feeds) uses the fields, never this.
    /// </summary>
    public static string Describe(WorkplaceType workplaceType, IReadOnlyList<JobLocation> locations, IReadOnlyList<string> applicantLocationRequirements)
    {
        if (workplaceType == WorkplaceType.Remote)
        {
            return applicantLocationRequirements.Count == 0 ? "Remote" : $"Remote ({string.Join(", ", applicantLocationRequirements)})";
        }

        return string.Join("; ", locations.Select(l => string.Join(", ", new[] { l.City, l.Region, l.Country }.Where(p => !string.IsNullOrWhiteSpace(p)))));
    }
}

/// <summary>What a salary figure is per: schema.org <c>unitText</c>.</summary>
public enum SalaryPeriod
{
    Hour,
    Day,
    Week,
    Month,
    Year,
}

/// <summary>A pay range. At least one of <see cref="Min"/> and <see cref="Max"/> is set; <see cref="Currency"/> is ISO 4217.</summary>
public sealed record JobSalary(decimal? Min, decimal? Max, string Currency, SalaryPeriod Period);

/// <summary>
/// The posting content of a job at one moment: everything a careers page, a feed or a job board shows. A
/// <see cref="JobVersion"/> freezes one. Equality compares the content, list members included.
/// </summary>
public sealed record JobSnapshot(
    string Title,
    string Slug,
    string Description,
    string? Department,
    EmploymentType EmploymentType,
    WorkplaceType WorkplaceType,
    IReadOnlyList<JobLocation> Locations,
    IReadOnlyList<string> ApplicantLocationRequirements,
    JobSalary? Salary,
    DateTimeOffset? ValidThrough)
{
    public bool Equals(JobSnapshot? other) =>
        other is not null
        && Title == other.Title
        && Slug == other.Slug
        && Description == other.Description
        && Department == other.Department
        && EmploymentType == other.EmploymentType
        && WorkplaceType == other.WorkplaceType
        && Locations.SequenceEqual(other.Locations)
        && ApplicantLocationRequirements.SequenceEqual(other.ApplicantLocationRequirements)
        && Salary == other.Salary
        && ValidThrough == other.ValidThrough;

    public override int GetHashCode() => HashCode.Combine(Title, Slug, Description, Locations.Count, ValidThrough);
}
