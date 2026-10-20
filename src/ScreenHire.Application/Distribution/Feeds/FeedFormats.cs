using System.Globalization;
using System.Xml.Linq;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Domain.Jobs;
using static ScreenHire.Application.Distribution.Feeds.FeedXml;

namespace ScreenHire.Application.Distribution.Feeds;

// Each format follows the board's own published description (sources and access dates in
// docs/architecture/job-distribution.md, "Feeds"). The tests validate every feed against a schema written from it.

/// <summary>
/// Our own feed format, for any aggregator that takes a generic XML feed. It carries the canonical job model's
/// structured fields as they are: every location, the countries a remote candidate may work from, the salary range.
/// </summary>
public sealed class GenericFeedAdapter(TimeProvider time) : FeedAdapter(time)
{
    public override string BoardId => Boards.GenericFeed;

    public override string Name => "XML feed";

    public override BoardCapabilities Capabilities { get; } = FeedCapabilities(BoardMode.Pull, refreshInterval: null);

    public override IReadOnlyList<string> Validate(PostingContent content) => [];

    public override XElement Map(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var job = content.Snapshot;
        return new XElement("job",
            Plain("referencenumber", content.JobId.ToString()),
            Plain("version", content.Version.ToString(CultureInfo.InvariantCulture)),
            Data("title", job.Title),
            Data("company", content.OrganizationName),
            Data("url", content.Url),
            Plain("dateposted", Iso(content.DatePosted)),
            Plain("updated", Iso(content.VersionPublishedAt)),
            job.ValidThrough is { } validThrough ? Plain("validthrough", Iso(validThrough)) : null,
            Plain("employmenttype", job.EmploymentType.ToString()),
            Plain("workplacetype", job.WorkplaceType.ToString()),
            Data("department", job.Department),
            job.Locations.Count == 0 ? null : new XElement("locations", job.Locations.Select(l => new XElement("location",
                Data("city", l.City),
                Data("region", l.Region),
                Plain("country", l.Country)))),
            job.ApplicantLocationRequirements.Count == 0
                ? null
                : new XElement("applicantcountries", job.ApplicantLocationRequirements.Select(c => new XElement("country", c))),
            job.Salary is { } salary
                ? new XElement("salary",
                    salary.Min is { } min ? Plain("min", Number(min)) : null,
                    salary.Max is { } max ? Plain("max", Number(max)) : null,
                    Plain("currency", salary.Currency),
                    Plain("period", salary.Period.ToString()))
                : null,
            Data("description", job.Description));
    }

    protected override XElement Root(FeedHeader header, IEnumerable<XElement> jobs) =>
        new("source",
            Data("publisher", header.OrganizationName),
            Data("publisherurl", header.BoardUrl),
            Plain("lastbuilddate", Iso(header.BuiltAt)),
            jobs);
}

/// <summary>
/// Careerjet's XML feed: <c>&lt;jobs&gt;&lt;job&gt;</c> with a nested location. Careerjet reads it daily (S) and lists only
/// the jobs in the current feed.
/// </summary>
public sealed class CareerjetFeedAdapter(TimeProvider time) : FeedAdapter(time)
{
    public override string BoardId => Boards.Careerjet;

    public override string Name => "Careerjet";

    public override BoardCapabilities Capabilities { get; } = FeedCapabilities(BoardMode.Pull, TimeSpan.FromDays(1));

    public override IReadOnlyList<string> Validate(PostingContent content) => [];

    public override XElement Map(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var job = content.Snapshot;
        var (city, region, country) = Place(job);
        return new XElement("job",
            Data("id", content.JobId.ToString()),
            Data("title", job.Title),
            Data("url", content.Url),
            new XElement("location",
                Data("city", city),
                Data("region", region),
                Data("country", CountryName(country))),
            Data("company", content.OrganizationName),
            Data("description", job.Description),
            Data("contract_type", job.EmploymentType switch
            {
                EmploymentType.Contract => "contract",
                EmploymentType.Temporary => "temporary",
                EmploymentType.Internship => "internship",
                _ => "permanent",
            }),
            Data("working_hours", job.EmploymentType switch
            {
                EmploymentType.PartTime => "part-time",
                EmploymentType.FullTime => "full-time",
                _ => null,
            }),
            Data("salary", SalaryText(job.Salary)),
            Data("apply_url", content.Url));
    }

    protected override XElement Root(FeedHeader header, IEnumerable<XElement> jobs) => new("jobs", jobs);
}

/// <summary>
/// Talent.com's XML feed: a <c>&lt;source&gt;</c> with publisher details and one <c>&lt;job&gt;</c> per posting, all
/// content in CDATA. Refresh time is unpublished; about four hours is a secondary source's figure (I).
/// </summary>
public sealed class TalentFeedAdapter(TimeProvider time) : FeedAdapter(time)
{
    public override string BoardId => Boards.TalentCom;

    public override string Name => "Talent.com";

    public override BoardCapabilities Capabilities { get; } = FeedCapabilities(BoardMode.Pull, TimeSpan.FromHours(4));

    public override IReadOnlyList<string> Validate(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var issues = new List<string>();
        if (Checks.TextLength(content.Snapshot.Description) < 50)
        {
            issues.Add("Talent.com needs a description of at least 50 characters.");
        }

        if (content.Snapshot.WorkplaceType != WorkplaceType.Remote && !Checks.HasCity(content.Snapshot))
        {
            issues.Add("Talent.com needs a city for on-site and hybrid jobs.");
        }

        return issues;
    }

    public override XElement Map(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var job = content.Snapshot;
        var (city, region, country) = Place(job);
        return new XElement("job",
            Data("referencenumber", content.JobId.ToString()),
            Data("title", job.Title),
            Data("company", content.OrganizationName),
            Required("city", city),
            Required("state", region),
            Data("country", CountryName(country)),
            Data("dateposted", Iso(content.DatePosted)),
            Data("url", content.Url),
            Data("description", job.Description),
            // The spec's suggested format for this one field is month first.
            job.ValidThrough is { } validThrough
                ? Data("expirationdate", validThrough.UtcDateTime.ToString("MM-dd-yyyyTHH:mm:ssZ", CultureInfo.InvariantCulture))
                : null,
            Data("jobtype", job.EmploymentType switch
            {
                EmploymentType.FullTime => "Full time",
                EmploymentType.PartTime => "Part time",
                EmploymentType.Contract => "Contract",
                EmploymentType.Temporary => "Temporary",
                _ => "Internship",
            }),
            Data("isremote", job.WorkplaceType == WorkplaceType.Remote ? "yes" : "no"),
            job.Salary is { } salary
                ? new XElement("salary",
                    salary.Min is { } min ? Data("salary_min", Number(min)) : null,
                    salary.Max is { } max ? Data("salary_max", Number(max)) : null,
                    Data("salary_currency", salary.Currency),
                    Data("period", salary.Period.ToString().ToLowerInvariant()),
                    Data("type", "BASE_SALARY"))
                : null);
    }

    protected override XElement Root(FeedHeader header, IEnumerable<XElement> jobs) =>
        new("source",
            Data("publisher", "ScreenHire"),
            Data("publisherurl", header.BoardUrl),
            Data("lastbuilddate", Iso(header.BuiltAt)),
            jobs);
}

/// <summary>
/// Adzuna's XML feed: <c>&lt;jobs&gt;&lt;job&gt;</c> with separate salary fields. Adzuna downloads it at least once a day (S).
/// </summary>
public sealed class AdzunaFeedAdapter(TimeProvider time) : FeedAdapter(time)
{
    public override string BoardId => Boards.Adzuna;

    public override string Name => "Adzuna";

    public override BoardCapabilities Capabilities { get; } = FeedCapabilities(BoardMode.Pull, TimeSpan.FromDays(1));

    public override IReadOnlyList<string> Validate(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Checks.TextLength(content.Snapshot.Description) < 100
            ? ["Adzuna needs a description of at least 100 characters."]
            : [];
    }

    public override XElement Map(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var job = content.Snapshot;
        var (city, region, country) = Place(job);
        return new XElement("job",
            Data("title", job.Title),
            Plain("id", content.JobId.ToString()),
            Data("description", job.Description),
            Data("url", content.Url),
            Data("location", city ?? region ?? (job.WorkplaceType == WorkplaceType.Remote ? "Remote" : CountryName(country))),
            // Adzuna's list of codes says UK, not GB.
            Plain("country", country == "GB" ? "UK" : country),
            Data("salary", SalaryText(job.Salary)),
            job.Salary?.Min is { } min ? Plain("salary_min", Number(min)) : null,
            job.Salary?.Max is { } max ? Plain("salary_max", Number(max)) : null,
            job.Salary is { Period: not SalaryPeriod.Week } salary ? Plain("salary_frequency", salary.Period.ToString().ToLowerInvariant()) : null,
            Plain("salary_currency", job.Salary?.Currency),
            Data("company", content.OrganizationName),
            Data("category", job.Department),
            Plain("contract_type", job.EmploymentType switch
            {
                EmploymentType.FullTime or EmploymentType.PartTime => "permanent",
                EmploymentType.Contract or EmploymentType.Temporary => "contract",
                _ => null,
            }),
            Plain("contract_time", job.EmploymentType switch
            {
                EmploymentType.PartTime => "part_time",
                EmploymentType.FullTime => "full_time",
                _ => null,
            }),
            Plain("date", content.DatePosted.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    protected override XElement Root(FeedHeader header, IEnumerable<XElement> jobs) => new("jobs", jobs);
}

/// <summary>
/// Jooble's XML feed: <c>&lt;jobs&gt;&lt;job id="..."&gt;</c> with <c>link</c>, <c>name</c> and <c>region</c>. Jooble's
/// specification is a PDF we could not read; the format follows a feed Jooble accepts (see the docs), and dates use
/// its <c>dd.MM.yyyy</c>. Its refresh time is unpublished.
/// </summary>
public sealed class JoobleFeedAdapter(TimeProvider time) : FeedAdapter(time)
{
    private const string DateFormat = "dd.MM.yyyy";

    public override string BoardId => Boards.Jooble;

    public override string Name => "Jooble";

    public override BoardCapabilities Capabilities { get; } = FeedCapabilities(BoardMode.Pull, refreshInterval: null);

    public override IReadOnlyList<string> Validate(PostingContent content) => [];

    public override XElement Map(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var job = content.Snapshot;
        return new XElement("job",
            new XAttribute("id", content.JobId.ToString()),
            Data("link", content.Url),
            Data("name", job.Title),
            Data("region", PlaceText(job)),
            Data("salary", SalaryText(job.Salary)),
            Data("description", job.Description),
            Data("company", content.OrganizationName),
            Plain("pubdate", content.DatePosted.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)),
            Plain("updated", content.VersionPublishedAt.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)),
            job.ValidThrough is { } validThrough ? Plain("expire", validThrough.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)) : null,
            Data("jobtype", job.EmploymentType switch
            {
                EmploymentType.FullTime => "Full-time",
                EmploymentType.PartTime => "Part-time",
                EmploymentType.Contract => "Contract",
                EmploymentType.Temporary => "Temporary",
                _ => "Internship",
            }));
    }

    protected override XElement Root(FeedHeader header, IEnumerable<XElement> jobs) => new("jobs", jobs);
}
