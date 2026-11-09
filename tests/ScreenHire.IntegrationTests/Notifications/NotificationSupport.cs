using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>What the notification tests share: putting the messages on the real worker's queue and waiting for what they did.</summary>
internal static class NotificationSupport
{
    public const string Link = "/settings/sending-domain";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A key no other test uses, so a count of handled messages is this test's own.</summary>
    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>Puts a <see cref="RaiseNotification"/> on the running worker's queue, as a handler of the worker would return it.</summary>
    public static async Task RaiseAsync(
        this AppHostFixture fixture, Guid organizationId, string key, string domain = "acme.example.org", Permission audience = Permission.ManageOwners,
        string link = Link)
    {
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new RaiseNotification(
            organizationId, audience, NotificationKinds.SendingDomainFailing, key, new Dictionary<string, string> { ["domain"] = domain }, link));
    }

    /// <summary>Puts a <see cref="ClearNotification"/> on the running worker's queue.</summary>
    public static async Task ClearAsync(this AppHostFixture fixture, Guid organizationId, string key)
    {
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(
            new ClearNotification(organizationId, NotificationKinds.SendingDomainFailing, key));
    }

    /// <summary>Waits until the worker has handled <paramref name="count"/> messages of <paramref name="messageType"/> that carry <paramref name="key"/>.</summary>
    public static async Task WaitHandledAsync(this AppHostFixture fixture, string key, string messageType, int count = 1)
    {
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(key, Ct, messageType) >= count,
            $"the worker to handle {count} {messageType} message(s) for {key}", Ct);
    }

    /// <summary>The caller's own notifications, as the bell reads them.</summary>
    public static async Task<ApiResult> NotificationsAsync(this Tenant tenant) =>
        (await tenant.GetAsync("/v1/notifications")).Expect(HttpStatusCode.OK);

    /// <summary>Waits until the caller's list has exactly <paramref name="count"/> items, and returns it.</summary>
    public static async Task<ApiResult> WaitForItemsAsync(this Tenant tenant, int count)
    {
        ApiResult? last = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                last = await tenant.NotificationsAsync();
                return last.Count("items") == count;
            },
            $"the notification list to hold {count} item(s)", Ct);
        return last!;
    }

    /// <summary>Adds a notification for <paramref name="userId"/> as the application role, with the organization as tenant.</summary>
    public static async Task<Notification> AddAsync(
        this AppHostFixture fixture, Guid organizationId, Guid userId, string key, DateTimeOffset createdAt, string domain = "acme.example.org")
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var notification = Notification.Create(
            organizationId, userId, NotificationKinds.SendingDomainFailing, key, new Dictionary<string, string> { ["domain"] = domain }, Link, createdAt);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(Ct);
        return notification;
    }
}
