using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Notifications;

/// <summary>
/// Puts one notification in the bell of every member who holds <paramref name="Audience"/> and sees every job (docs/architecture/notifications.md).
/// This is the one way to notify a person: a handler returns it, an endpoint publishes it through the outbox.
/// </summary>
/// <param name="OrganizationId">The organization the members belong to.</param>
/// <param name="Audience">The permission that picks the members: <c>ManageOwners</c> for Owners only, <c>ManageBilling</c> for who handles billing.</param>
/// <param name="Kind">One of <see cref="NotificationKinds"/>.</param>
/// <param name="Key">What it is about, stable for that thing (the domain's id). Raising the same kind and key again for a person changes nothing.</param>
/// <param name="Payload">The facts the dashboard builds the line from. Short, and never personal data about a candidate.</param>
/// <param name="Link">The dashboard page that fixes it, as a path (<see cref="Notification.IsDashboardPath"/>).</param>
public sealed record RaiseNotification(
    Guid OrganizationId, Permission Audience, string Kind, string Key, IReadOnlyDictionary<string, string> Payload, string Link) : ITenantMessage;

/// <summary>Removes the notification for <paramref name="Kind"/> and <paramref name="Key"/> from every member's bell, because what it said is no longer true.</summary>
public sealed record ClearNotification(Guid OrganizationId, string Kind, string Key) : ITenantMessage;

/// <summary>
/// A bell changed: an open dashboard refetches its notifications. It carries no person and no words, because the push reaches every
/// member of the organization; the read decides who sees what.
/// </summary>
public sealed record NotificationsChanged(Guid OrganizationId) : ITenantEvent;

public static partial class RaiseNotificationHandler
{
    // Idempotent on its own, with no [Deduplicated]: the unique index on (organization, person, kind, key) makes a repeat insert
    // nothing. Wolverine's 24 hour claim would also swallow a real second raise after a clear.
    public static async Task<OutgoingMessages> Handle(
        RaiseNotification message, ScreenHireDbContext db, TimeProvider time, ILogger<RaiseNotification> logger, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (!Notification.IsDashboardPath(message.Link))
        {
            LogRefused(logger, message.OrganizationId, message.Kind);
            return outgoing;
        }

        var recipients = await NotificationRecipients.ForAsync(db, message.OrganizationId, message.Audience, cancellationToken);
        var now = time.GetUtcNow();
        var payload = JsonSerializer.Serialize(message.Payload);
        var inserted = 0;
        foreach (var recipient in recipients.Select(r => r.UserId).Distinct())
        {
            // A conflict is the normal repeat, so it is a statement that does nothing, not an exception that rolls the handler back.
            inserted += await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO notifications (id, organization_id, user_id, kind, key, payload, link, created_at)
                VALUES ({Guid.CreateVersion7()}, {message.OrganizationId}, {recipient}, {message.Kind}, {message.Key}, {payload}::jsonb, {message.Link}, {now})
                ON CONFLICT (organization_id, user_id, kind, key) DO NOTHING
                """,
                cancellationToken);
        }

        if (inserted > 0)
        {
            outgoing.Add(new NotificationsChanged(message.OrganizationId));
        }

        LogRaised(logger, message.OrganizationId, message.Kind, inserted);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Organization {OrganizationId}: a {Kind} notification was not raised because its link is not a dashboard path")]
    private static partial void LogRefused(ILogger logger, Guid organizationId, string kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: raised a {Kind} notification for {People} new people")]
    private static partial void LogRaised(ILogger logger, Guid organizationId, string kind, int people);
}

public static class ClearNotificationHandler
{
    // Deleting rows that are gone is nothing, so a redelivery does no harm. Notifications are not change-tracked objects.
    public static async Task<OutgoingMessages> Handle(ClearNotification message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var removed = await db.Notifications
            .Where(n => n.Kind == message.Kind && n.Key == message.Key)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            outgoing.Add(new NotificationsChanged(message.OrganizationId));
        }

        return outgoing;
    }
}

/// <summary>Who a notification is for: the members who hold a permission and see every job, as the verification alerts pick theirs.</summary>
internal static class NotificationRecipients
{
    public static Task<List<VerificationRecipient>> ForAsync(
        ScreenHireDbContext db, Guid organizationId, Permission audience, CancellationToken cancellationToken) =>
        VerificationRecipients.WithPermissionAsync(db, audience, organizationId, null, cancellationToken);
}

/// <summary><c>ScreenHire:Notifications</c>: how long notifications are kept.</summary>
public sealed class NotificationOptions
{
    public const string Section = "ScreenHire:Notifications";

    /// <summary>Days a notification is kept after it was raised, read or not. The daily purge removes older ones.</summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>The daily purge. It has no tenant of its own, so it fans out one <see cref="PurgeOrganizationExpiredNotifications"/> per organization.</summary>
public sealed record PurgeExpiredNotifications;

public sealed record PurgeOrganizationExpiredNotifications(Guid OrganizationId, DateTimeOffset Now) : ITenantMessage;

public static class PurgeExpiredNotificationsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredNotifications message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationExpiredNotifications(id, now)));
        return outgoing;
    }
}

public static class PurgeOrganizationExpiredNotificationsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeOrganizationExpiredNotifications message, ScreenHireDbContext db, IOptions<NotificationOptions> options, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var cutoff = message.Now - TimeSpan.FromDays(Math.Max(1, options.Value.RetentionDays));
        // A kind that stays true until someone acts (a trial that ended with no plan) is cleared by the code that raised it, not by age.
        var kept = NotificationKinds.KeptUntilCleared;
        var removed = await db.Notifications.Where(n => n.CreatedAt < cutoff && !kept.Contains(n.Kind)).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            outgoing.Add(new NotificationsChanged(message.OrganizationId));
        }

        return outgoing;
    }
}

public static class NotificationsRegistration
{
    /// <summary>The notification options. Registered by the worker's messaging setup.</summary>
    public static IServiceCollection AddScreenHireNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<NotificationOptions>()
            .BindConfiguration(NotificationOptions.Section)
            .Validate(o => o.RetentionDays >= 1, "ScreenHire:Notifications:RetentionDays must be at least 1.")
            .ValidateOnStart();
        return services;
    }
}
