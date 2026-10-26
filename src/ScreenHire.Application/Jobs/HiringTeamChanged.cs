using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// Someone joined or left a job's hiring team (ADR-0015). Open dashboards refetch the job's team, the Team page's job
/// counts and the jobs list, so a person just added sees the job appear. Ids only: who changed is in the membership log.
/// </summary>
public sealed record HiringTeamChanged(Guid OrganizationId, Guid JobId, DateTimeOffset ChangedAt) : IJobEvent;
