using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The Reports page (docs/architecture/reports.md): headline numbers, applications and hires by week, the funnel, time to
/// hire, sources and open jobs for the jobs the person can see, between two instants. All of it comes from one snapshot,
/// so the numbers agree. Nothing here reads verification (verification) results.
/// </summary>
/// <remarks>
/// <c>from</c>, <c>to</c>: The range asked for, half open: from inclusive, to exclusive.
/// <c>headline</c>: The five headline numbers for the range.
/// <c>previous</c>: The same numbers for the previous range the caller named; null when it named none.
/// <c>weekly</c>: Applications submitted and hires made in each week of the range (Monday weeks in the caller's time zone).
/// <c>funnel</c>: Applications submitted in the range, counted at every stage kind up to the furthest one they reached.
/// Empty when none were submitted.
/// <c>timeToHire</c>: Hires made in the range, whenever the candidate applied.
/// <c>sources</c>: Applications submitted in the range and how many of them are hired now, by where they came from.
/// <c>openJobs</c>: Each open job the person can see.
/// </remarks>
public sealed record HiringReport(
    DateTimeOffset From,
    DateTimeOffset To,
    ReportHeadline Headline,
    ReportHeadline? Previous,
    IReadOnlyList<ReportWeek> Weekly,
    IReadOnlyList<FunnelStage> Funnel,
    TimeToHire TimeToHire,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<ReportOpenJob> OpenJobs);

/// <summary>The headline numbers of one range.</summary>
/// <remarks>
/// <c>applications</c>, <c>reachedInterview</c>, <c>offers</c>: Applications submitted in the range, and how many of them
/// reached the Interview and Offer kinds or a later one (the funnel's rows).
/// <c>hires</c>: Hires made in the range, whenever the candidate applied.
/// <c>medianDaysToHire</c>: The median days from applying to being hired over those hires; null with no hires.
/// </remarks>
public sealed record ReportHeadline(int Applications, int ReachedInterview, int Offers, int Hires, double? MedianDaysToHire);

/// <summary>One Monday-start week: applications submitted in it and hires made in it, both inside the range.</summary>
public sealed record ReportWeek(DateOnly WeekStart, int Applications, int Hires);

/// <summary>One row of the funnel: a stage kind and the applications that reached it or a later one.</summary>
/// <remarks>
/// <c>movedOn</c>: Of those, the applications that reached a later row's kind; null on Hired, the last row.
/// </remarks>
public sealed record FunnelStage(StageKind Kind, int Applications, int? MovedOn);

/// <summary>Days from applying to being hired, for the hires made in the range.</summary>
/// <remarks>
/// <c>stages</c>: The median days a hire spent in each stage kind (Applied to Offer), over the hires that were in it.
/// <c>jobs</c>: Each job with at least one hire in the range.
/// </remarks>
public sealed record TimeToHire(IReadOnlyList<StageDays> Stages, IReadOnlyList<JobTimeToHire> Jobs);

/// <summary>The median days the hires made in the range spent in one stage kind, and how many of them were in it.</summary>
public sealed record StageDays(StageKind Kind, double MedianDays, int Hires);

/// <summary>One job's hires in the range and their median days from applying to being hired.</summary>
public sealed record JobTimeToHire(Guid JobId, string JobTitle, int Hires, double MedianDays);

/// <summary>Applications submitted in the range from one source, and how many of them are hired now.</summary>
/// <remarks>
/// <c>key</c>: <c>careers</c>, <c>website</c> (the careers page embedded in the organization's own site),
/// <c>recruiter</c>, or <c>board:{id}</c> (a job board's apply link).
/// <c>name</c>: The source in words.
/// </remarks>
public sealed record SourceCount(string Key, string Name, int Applications, int Hires);

/// <summary>An open job: applications in the range, applications in an Interview stage now, days open and hires in the range.</summary>
public sealed record ReportOpenJob(Guid JobId, string Title, int Applications, int InInterview, int DaysOpen, int Hires);
