using System.Text.Json.Serialization;
using ScreenHire.Application.Distribution;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Contracts;

/// <summary>A board an organization may post to, and what it can promise (docs/architecture/job-distribution.md §1).</summary>
/// <remarks>
/// <c>confirmsLive</c> and <c>confirmsRemoval</c> false: the board never reports back, so its postings stay
/// <c>Submitted</c> after publishing and <c>Closing</c> after a close, with <c>publishEtaSeconds</c> or
/// <c>removalEtaSeconds</c> as its own estimate (null when it publishes none). <c>dailyQuota</c> is shared by every
/// organization.
/// </remarks>
public sealed record BoardSummary(
    string Id,
    string Name,
    BoardMode Mode,
    bool CanClose,
    bool CanUnpublishPerBoard,
    bool ConfirmsLive,
    bool ConfirmsRemoval,
    int? PublishEtaSeconds,
    int? RemovalEtaSeconds,
    int? DailyQuota);

/// <summary>One job on one board: where it stands and what is still uncertain.</summary>
/// <remarks>
/// <c>state</c> follows <c>Draft → Queued → Submitted → Live | Rejected → Closing → Closed</c>, plus <c>Error</c>.
/// Draft: the board needs <c>issues</c> fixed first. Queued: waiting for the worker, or for Google's quota.
/// <c>expectedBy</c> is the board's estimate for the next step: when a Submitted posting should show, or a Closing one
/// be gone. A board that does not confirm (<c>confirmsLive</c> / <c>confirmsRemoval</c> false) never reaches Live or
/// Closed: say "removal requested", never "removed". <c>mode</c> is null for a board whose configuration was removed.
/// </remarks>
public sealed record JobPostingStatus(
    Guid Id,
    string BoardId,
    string BoardName,
    BoardMode? Mode,
    PostingState State,
    int JobVersion,
    bool ConfirmsLive,
    bool ConfirmsRemoval,
    bool CanUnpublish,
    DateTimeOffset? ExpectedBy,
    IReadOnlyList<string> Issues,
    string? LastError,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? LiveAt,
    DateTimeOffset? ClosingAt,
    DateTimeOffset? ClosedAt);

/// <summary>
/// Publishing a job: optionally, the boards to post it to. Left out, the first publish posts to every board in
/// <c>GET /v1/boards</c> and a later one updates the boards the job is already on. Naming a board the job was taken
/// down from posts it again.
/// </summary>
public sealed record PublishJobRequest(IReadOnlyList<string>? Boards = null);

/// <summary>One listing on the local sandbox board, as the board itself would show it.</summary>
public sealed record SandboxBoardListing(Guid Id, Guid JobId, int JobVersion, string Title, string Url, DateTimeOffset AcceptedAt);

/// <summary>A tenant's board in the public job-board API (Greenhouse-style: <c>GET /v1/public/boards/{organizationSlug}</c>).</summary>
public sealed record JobBoardInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("absolute_url")] string AbsoluteUrl);

/// <summary>Every job on a tenant's public job board (Greenhouse-style <c>jobs</c> and <c>meta.total</c>).</summary>
public sealed record JobBoardJobList(
    [property: JsonPropertyName("jobs")] IReadOnlyList<JobBoardJob> Jobs,
    [property: JsonPropertyName("meta")] JobBoardMeta Meta);

public sealed record JobBoardMeta([property: JsonPropertyName("total")] int Total);

/// <summary>
/// A job in the public job-board API, in Greenhouse's snake_case shape so tools built for Greenhouse boards read it.
/// <c>content</c> is the description as sanitized HTML (Greenhouse escapes it; we do not), present with
/// <c>?content=true</c> and on a single job. Ids are our job ids (UUIDs), not Greenhouse's integers.
/// </summary>
public sealed record JobBoardJob(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("first_published")] DateTimeOffset FirstPublished,
    [property: JsonPropertyName("location")] JobBoardLocation Location,
    [property: JsonPropertyName("absolute_url")] string AbsoluteUrl,
    [property: JsonPropertyName("company_name")] string CompanyName,
    [property: JsonPropertyName("departments")] IReadOnlyList<JobBoardDepartment> Departments,
    [property: JsonPropertyName("offices")] IReadOnlyList<JobBoardOffice> Offices,
    [property: JsonPropertyName("employment_type")] EmploymentType EmploymentType,
    [property: JsonPropertyName("workplace_type")] WorkplaceType WorkplaceType,
    [property: JsonPropertyName("pay_input_ranges")] IReadOnlyList<JobBoardPayRange> PayInputRanges,
    [property: JsonPropertyName("valid_through")] DateTimeOffset? ValidThrough,
    [property: JsonPropertyName("content")] string? Content);

public sealed record JobBoardLocation([property: JsonPropertyName("name")] string Name);

public sealed record JobBoardDepartment([property: JsonPropertyName("name")] string Name);

public sealed record JobBoardOffice(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("location")] string Location);

/// <summary>Greenhouse's pay transparency range, in minor units of <c>currency_type</c>.</summary>
public sealed record JobBoardPayRange(
    [property: JsonPropertyName("min_cents")] long? MinCents,
    [property: JsonPropertyName("max_cents")] long? MaxCents,
    [property: JsonPropertyName("currency_type")] string CurrencyType,
    [property: JsonPropertyName("period")] SalaryPeriod Period);

internal static class DistributionMappings
{
    public static BoardSummary ToSummary(this IJobBoardAdapter adapter)
    {
        var capabilities = adapter.Capabilities;
        return new(adapter.BoardId, adapter.Name, capabilities.Mode, capabilities.CanClose, capabilities.CanUnpublishPerBoard,
            capabilities.ConfirmsLive, capabilities.ConfirmsRemoval, Seconds(capabilities.PublishEta), Seconds(capabilities.RemovalEta),
            capabilities.DailyQuota);
    }

    public static JobPostingStatus ToStatus(this JobPosting posting, BoardRegistry boards)
    {
        var adapter = boards.Find(posting.BoardId);
        var capabilities = adapter?.Capabilities;
        return new(posting.Id, posting.BoardId, adapter?.Name ?? posting.BoardId, capabilities?.Mode, posting.State, posting.JobVersion,
            capabilities?.ConfirmsLive ?? false, capabilities?.ConfirmsRemoval ?? false, capabilities?.CanUnpublishPerBoard ?? false,
            posting.ExpectedBy, posting.Issues, posting.LastError, posting.UpdatedAt, posting.SubmittedAt, posting.LiveAt,
            posting.ClosingAt, posting.ClosedAt);
    }

    public static List<JobPostingStatus> ToStatuses(this IEnumerable<JobPosting> postings, BoardRegistry boards) =>
        [.. postings.Select(p => p.ToStatus(boards)).OrderBy(p => p.BoardName, StringComparer.Ordinal)];

    public static JobBoardJob ToJobBoardJob(this PostingContent content, bool withContent)
    {
        var job = content.Snapshot;
        var location = JobLocation.Describe(job.WorkplaceType, job.Locations, job.ApplicantLocationRequirements);
        return new(
            content.JobId, job.Title, content.VersionPublishedAt, content.DatePosted, new JobBoardLocation(location), content.Url,
            content.OrganizationName,
            job.Department is { } department ? [new JobBoardDepartment(department)] : [],
            [.. job.Locations.Select(l => new JobBoardOffice(
                l.City ?? l.Region ?? l.Country ?? "",
                string.Join(", ", new[] { l.City, l.Region, l.Country }.Where(p => !string.IsNullOrWhiteSpace(p)))))],
            job.EmploymentType, job.WorkplaceType,
            job.Salary is { } salary
                ? [new JobBoardPayRange(MinorUnits(salary.Min, salary.Currency), MinorUnits(salary.Max, salary.Currency), salary.Currency, salary.Period)]
                : [],
            job.ValidThrough,
            withContent ? job.Description : null);
    }

    private static int? Seconds(TimeSpan? value) => value is { } span ? (int)span.TotalSeconds : null;

    // Greenhouse's "cents" are the currency's minor units: 100 for most, 1 for yen, 1000 for dinars. ISO 4217's exceptions to
    // two decimals, which change only when a currency is created or retired.
    private static readonly HashSet<string> NoMinorUnit = new(
        ["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ThreeMinorDigits = new(
        ["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"], StringComparer.OrdinalIgnoreCase);

    internal static long? MinorUnits(decimal? amount, string currency) =>
        amount is { } value
            ? (long)decimal.Round(value * (NoMinorUnit.Contains(currency) ? 1 : ThreeMinorDigits.Contains(currency) ? 1000 : 100))
            : null;
}
