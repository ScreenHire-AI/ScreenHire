using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Notifications;
using ScreenHire.Application.SendingDomains;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using Wolverine.Runtime.Recurring;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>
/// How long the bell keeps a notification (docs/architecture/notifications.md): <c>ScreenHire:Notifications:RetentionDays</c>,
/// 30 by default, counted from when it was raised whether it was read or not. The handler runs in-process on a fixed time with the
/// setting chosen by the test; the daily fan-out runs on the real worker.
/// </summary>
public sealed class NotificationRetentionTests(AppHostFixture fixture)
{
    // Far in the future on purpose: the real worker's daily purge, which another test here runs, counts from the real clock and
    // must never reach these rows.
    private static readonly DateTimeOffset Now = new(2099, 6, 1, 4, 32, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Notifications_are_kept_30_days_unless_the_setting_says_otherwise()
    {
        new NotificationOptions().RetentionDays.Should().Be(30);
    }

    [Fact]
    public async Task The_purge_removes_notifications_older_than_the_default_30_days_and_keeps_newer_ones()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await AddAsync(owner, 90);
        await AddAsync(owner, 31);
        var recent29 = await AddAsync(owner, 29);
        var recent5 = await AddAsync(owner, 5);

        var outgoing = await PurgeAsync(owner.OrganizationId, new NotificationOptions());

        (await KeysAsync(owner.OrganizationId)).Should().BeEquivalentTo([recent29, recent5]);
        outgoing.OfType<NotificationsChanged>().Should().ContainSingle().Which.OrganizationId.Should().Be(owner.OrganizationId);
    }

    [Fact]
    public async Task A_smaller_setting_removes_more()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await AddAsync(owner, 31);
        await AddAsync(owner, 29);
        await AddAsync(owner, 8);
        var kept = await AddAsync(owner, 5);

        await PurgeAsync(owner.OrganizationId, new NotificationOptions { RetentionDays = 7 });

        (await KeysAsync(owner.OrganizationId)).Should().BeEquivalentTo([kept]);
    }

    [Fact]
    public async Task A_notification_is_removed_whether_it_was_read_or_dismissed_or_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var read = await AddAsync(owner, 40);
        var dismissed = await AddAsync(owner, 41);
        await AddAsync(owner, 42);
        await fixture.OwnerSqlAsync("UPDATE notifications SET read_at = @at WHERE key = @key", ("at", Now.AddDays(-39)), ("key", read));
        await fixture.OwnerSqlAsync("UPDATE notifications SET read_at = @at, dismissed_at = @at WHERE key = @key", ("at", Now.AddDays(-40)), ("key", dismissed));

        await PurgeAsync(owner.OrganizationId, new NotificationOptions());

        (await KeysAsync(owner.OrganizationId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_purge_with_nothing_expired_sends_nothing_and_another_organizations_old_rows_stay()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var fresh = await AddAsync(ours, 1);
        var theirOld = await AddAsync(theirs, 60);

        var outgoing = await PurgeAsync(ours.OrganizationId, new NotificationOptions());

        outgoing.Should().BeEmpty("nothing of ours expired, so nobody's bell needs a refresh");
        (await KeysAsync(ours.OrganizationId)).Should().BeEquivalentTo([fresh]);
        (await KeysAsync(theirs.OrganizationId)).Should().BeEquivalentTo([theirOld], "the purge of one organization never reaches another's rows");
    }

    [Fact]
    public async Task The_daily_fan_out_on_the_real_worker_purges_every_organizations_expired_notifications()
    {
        // The worker's clock is the real one, so the ages here are real: 45 days is past the worker's 30, one day is not.
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var worker = DateTimeOffset.UtcNow;
        var ourOld = NotificationSupport.NewKey();
        var ourNew = NotificationSupport.NewKey();
        var theirOld = NotificationSupport.NewKey();
        await fixture.AddAsync(ours.OrganizationId, ours.UserId, ourOld, worker.AddDays(-45));
        await fixture.AddAsync(ours.OrganizationId, ours.UserId, ourNew, worker.AddDays(-1));
        await fixture.AddAsync(theirs.OrganizationId, theirs.UserId, theirOld, worker.AddDays(-45));

        await using (var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new PurgeExpiredNotifications());
        }

        await WolverineStore.WaitUntilAsync(
            async () => (await KeysAsync(ours.OrganizationId)).Count == 1 && (await KeysAsync(theirs.OrganizationId)).Count == 0,
            "the worker's daily purge to remove both organizations' expired notifications", Ct);
        (await KeysAsync(ours.OrganizationId)).Should().BeEquivalentTo([ourNew]);
    }

    [Fact]
    public async Task The_purge_and_the_domain_recheck_are_tracked_daily_schedules()
    {
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], Ct, withRecurringSchedules: true);
        var schedules = worker.Services.GetRequiredService<IRecurringScheduleControl>();

        await WolverineStore.WaitUntilAsync(
            async () => (await schedules.QueryAsync(Ct)).Any(s => s.Name == nameof(PurgeExpiredNotifications))
                && (await schedules.QueryAsync(Ct)).Any(s => s.Name == nameof(RecheckSendingDomains)),
            "the notification purge and sending domain re-check schedules to be tracked", Ct);
        var all = await schedules.QueryAsync(Ct);
        var purge = all.Should().ContainSingle(s => s.Name == nameof(PurgeExpiredNotifications)).Subject;
        purge.CronExpression.Should().Be("32 4 * * *");
        purge.Paused.Should().BeFalse();
        var recheck = all.Should().ContainSingle(s => s.Name == nameof(RecheckSendingDomains)).Subject;
        recheck.CronExpression.Should().Be("47 5 * * *");
        recheck.Paused.Should().BeFalse();
    }

    // A notification raised this many days before Now, for the owner. Returns its key.
    private async Task<string> AddAsync(Tenant owner, int daysOld)
    {
        var key = NotificationSupport.NewKey();
        await fixture.AddAsync(owner.OrganizationId, owner.UserId, key, Now.AddDays(-daysOld));
        return key;
    }

    private async Task<List<string>> KeysAsync(Guid organizationId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        return await db.Notifications.AsNoTracking().Select(n => n.Key).ToListAsync(Ct);
    }

    // The handler's own shape, run in-process at a fixed time: its transaction, one save, then the messages it returns.
    private async Task<OutgoingMessages> PurgeAsync(Guid organizationId, NotificationOptions options)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var outgoing = await PurgeOrganizationExpiredNotificationsHandler.Handle(
            new PurgeOrganizationExpiredNotifications(organizationId, Now), db, Options.Create(options), Ct);
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        return outgoing;
    }
}
