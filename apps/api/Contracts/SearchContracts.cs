using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// What a search found among the candidates and jobs the reader sees: candidates by name or email, one hit per
/// application, and jobs by title. Each list is short and starts with names or titles that begin with the search.
/// </summary>
public sealed record SearchResults(IReadOnlyList<CandidateSearchHit> Candidates, IReadOnlyList<JobSearchHit> Jobs);

/// <summary>A candidate whose name or email matched, on one of their applications: the record to open.</summary>
public sealed record CandidateSearchHit(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string Email,
    Guid JobId,
    string JobTitle,
    ApplicationStage Stage);

/// <summary>A job whose title matched.</summary>
public sealed record JobSearchHit(Guid Id, string Title, JobStatus Status);
