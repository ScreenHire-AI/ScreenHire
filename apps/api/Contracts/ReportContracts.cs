using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The Reports page (docs/architecture/reports.md): the funnel, time to hire and sources for the jobs the person can see,
/// between two instants. All three come from one snapshot, so their numbers agree. Nothing here reads verification
/// (integrity) results.
/// </summary>
/// <remarks>
/// <c>from</c>, <c>to</c>: The range asked for, half open: from inclusive, to exclusive.
/// <c>funnel</c>: Applications submitted in the range, counted at every stage kind up to the furthest one they reached.
/// Empty when none were submitted.
/// <c>timeToHire</c>: Hires made in the range, whenever the candidate applied.
/// <c>sources</c>: Applications submitted in the range and how many of them are hired, by where they came from.
/// </remarks>
public sealed record HiringReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<FunnelStage> Funnel,
    TimeToHire TimeToHire,
    IReadOnlyList<SourceCount> Sources);

/// <summary>One row of the funnel: a stage kind and the applications that reached it or a later one.</summary>
/// <remarks>
/// <c>movedOn</c>: Of those, the applications that reached a later row's kind; null on Hired, the last row.
/// </remarks>
public sealed record FunnelStage(StageKind Kind, int Applications, int? MovedOn);

/// <summary>Days from applying to being hired, for the hires made in the range.</summary>
/// <remarks>
/// <c>medianDays</c>: The median over every hire in the range (not a median of the jobs' medians); null with no hires.
/// <c>jobs</c>: Each job with at least one hire in the range.
/// </remarks>
public sealed record TimeToHire(double? MedianDays, int Hires, IReadOnlyList<JobTimeToHire> Jobs);

/// <summary>One job's hires in the range and their median days from applying to being hired.</summary>
public sealed record JobTimeToHire(Guid JobId, string JobTitle, int Hires, double MedianDays);

/// <summary>Applications submitted in the range from one source, and how many of them are hired now.</summary>
public sealed record SourceCount(ApplicationSource Source, int Applications, int Hires);
