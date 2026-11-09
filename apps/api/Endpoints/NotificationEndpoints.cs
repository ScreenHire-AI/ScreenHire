using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in member's notifications, the bell in the dashboard's top bar (docs/architecture/notifications.md). Every
/// route reads and writes only the caller's own rows: another member's id is a 404. Session only: an API key is no member and
/// has no bell. No permission, because a notification is addressed to a person by the permission that raised it, and the
/// routes change nothing but their own row, as <c>/v1/notification-settings</c> does. Not in the change history.
/// </summary>
internal static class NotificationEndpoints
{
    /// <summary>The most notifications one read returns. The bell lists what needs attention, not a history.</summary>
    private const int PageSize = 50;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/notifications").WithTags("Notifications").RequireAuthorization();
        group.MapGet("", ListNotifications).WithName("ListNotifications").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/read-all", MarkAllNotificationsRead).WithName("MarkAllNotificationsRead")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{id:guid}/read", MarkNotificationRead).WithName("MarkNotificationRead")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{id:guid}/dismiss", DismissNotification).WithName("DismissNotification")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<NotificationList>, ProblemHttpResult>> ListNotifications(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId)
        {
            return SignIn();
        }

        var own = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.DismissedAt == null);
        var items = await own
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(PageSize)
            .ToListAsync(cancellationToken);
        var unread = await own.CountAsync(n => n.ReadAt == null, cancellationToken);
        return TypedResults.Ok(new NotificationList([.. items.Select(n => new NotificationItem(n.Id, n.Kind, n.Payload, n.Link, n.CreatedAt, n.ReadAt))], unread));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> MarkNotificationRead(
        Guid id, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken) =>
        await ChangeOneAsync(id, outbox, tenant, (n, now) => n.MarkRead(now), time, cancellationToken);

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DismissNotification(
        Guid id, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken) =>
        await ChangeOneAsync(id, outbox, tenant, (n, now) => n.Dismiss(now), time, cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> MarkAllNotificationsRead(
        IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId || tenant.OrganizationId is not { } organizationId)
        {
            return SignIn();
        }

        var db = outbox.DbContext;
        var unread = await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null && n.DismissedAt == null).ToListAsync(cancellationToken);
        if (unread.Count > 0)
        {
            var now = time.GetUtcNow();
            foreach (var notification in unread)
            {
                notification.MarkRead(now);
            }

            await outbox.PublishAsync(new NotificationsChanged(organizationId));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    // Reading or dismissing twice is not an error: the second request finds nothing to change and says so by changing nothing.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ChangeOneAsync(
        Guid id,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        Func<Notification, DateTimeOffset, bool> change,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId || tenant.OrganizationId is not { } organizationId)
        {
            return SignIn();
        }

        var notification = await outbox.DbContext.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        if (change(notification, time.GetUtcNow()))
        {
            await outbox.PublishAsync(new NotificationsChanged(organizationId));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult SignIn() => TypedResults.Problem(title: "Sign in to see notifications", statusCode: StatusCodes.Status401Unauthorized);
}
