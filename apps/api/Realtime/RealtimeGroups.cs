using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Realtime;

/// <summary>
/// SignalR group names. Every group starts with the organization, so no group a client can join is shared with another
/// tenant, even if two records' ids collided.
/// </summary>
internal static class RealtimeGroups
{
    public static string Organization(Guid organizationId) => $"org:{organizationId:D}";

    public static string Topic(Guid organizationId, string topic, Guid id) => $"org:{organizationId:D}:{topic}:{id:D}";

    /// <summary>
    /// Where an event goes: the subscribers of its record for an <see cref="ITopicEvent"/>, otherwise its whole
    /// organization.
    /// </summary>
    public static string For(ITenantEvent tenantEvent) =>
        tenantEvent is ITopicEvent topicEvent
            ? Topic(tenantEvent.OrganizationId, topicEvent.Topic, topicEvent.TopicId)
            : Organization(tenantEvent.OrganizationId);
}

/// <summary>
/// The records a client may subscribe to, each with a check that the record exists for the caller. The check runs as the
/// caller's organization, so the tenant filter and row-level security make another organization's record invisible.
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
