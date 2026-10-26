using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Realtime;

/// <summary>
/// SignalR group names. Every group starts with the organization, so no group a client can join is shared with another
/// tenant, even if two records' ids collided.
/// </summary>
/// <remarks>
/// Job access (ADR-0015) splits a job's events between two groups. A connection of a member who sees all jobs is in
/// <see cref="AllJobs"/>; one of a member on Only jobs they're on is in <see cref="Team"/> for each job whose hiring team
/// they are on. A connection is in one of the two kinds, never both, so an event sent to both arrives once. Neither name
/// collides with a record topic (<see cref="RealtimeTopics"/> has no <c>all-jobs</c> or <c>team</c> topic).
/// </remarks>
internal static class RealtimeGroups
{
    public static string Organization(Guid organizationId) => $"org:{organizationId:D}";

    public static string AllJobs(Guid organizationId) => $"org:{organizationId:D}:all-jobs";

    public static string Team(Guid organizationId, Guid jobId) => $"org:{organizationId:D}:team:{jobId:D}";

    public static string Topic(Guid organizationId, string topic, Guid id) => $"org:{organizationId:D}:{topic}:{id:D}";

    /// <summary>
    /// Where an event goes: the subscribers of its record for an <see cref="ITopicEvent"/>; for an <see cref="IJobEvent"/>,
    /// the members who see its job (everyone who sees all jobs, and its hiring team); otherwise its whole organization.
    /// </summary>
    public static IReadOnlyList<string> For(ITenantEvent tenantEvent) =>
        tenantEvent switch
        {
            ITopicEvent topicEvent => [Topic(tenantEvent.OrganizationId, topicEvent.Topic, topicEvent.TopicId)],
            IJobEvent jobEvent => [AllJobs(tenantEvent.OrganizationId), Team(tenantEvent.OrganizationId, jobEvent.JobId)],
            _ => [Organization(tenantEvent.OrganizationId)],
        };
}

/// <summary>
/// The records a client may subscribe to, each with a check that the record exists for the caller. The check runs as the
/// caller, so the tenant and job-scope filters and row-level security make another organization's record, and a job
/// outside a member's hiring teams, invisible.
/// A feature adds its topic here when it raises an <see cref="ITopicEvent"/> (the <c>/add-realtime-event</c> skill).
/// </summary>
internal static class RealtimeTopics
{
    public const string Job = "job";

    public static readonly IReadOnlyDictionary<string, Func<ScreenHireDbContext, Guid, CancellationToken, Task<bool>>> Exists =
        new Dictionary<string, Func<ScreenHireDbContext, Guid, CancellationToken, Task<bool>>>(StringComparer.Ordinal)
        {
            [Job] = (db, id, cancellationToken) => db.Jobs.AnyAsync(j => j.Id == id, cancellationToken),
        };
}
