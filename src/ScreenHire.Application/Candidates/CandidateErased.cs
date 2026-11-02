using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Candidates;

/// <summary>
/// A candidate and everything about them was erased. Open dashboards drop them from lists and leave their pages. It carries
/// the candidate's id and the time and nothing else: after an erasure the id names no one.
/// </summary>
public sealed record CandidateErased(Guid OrganizationId, Guid CandidateId, DateTimeOffset ErasedAt) : ITenantEvent;
