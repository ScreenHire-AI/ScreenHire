using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Notifications;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Notifications;
using ScreenHire.IntegrationTests.Scheduling;
using ScreenHire.Testing;
using Wolverine;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// Reminders before the free trial ends (docs/architecture/billing.md, "Trial reminders"), through the API, the running worker's
/// Wolverine schedule, Mailpit and the bell. Signing up schedules the reminders for the trial's end; a reminder waits days, so the
/// tests read when the schedule would release it and make it due now, as the interview reminder tests do. Each test has its own
/// organization, and finds its messages by its organization's id.
/// </summary>
public sealed class TrialReminderTests(AppHostFixture fixture)
{
    private const string EmailType = ".RemindTrialEnding";
    private const string NoticeType = ".ShowTrialNotice";
    private const string SevenDays = "Your ScreenHire trial ends in 7 days";
    private const string OneDay = "Your ScreenHire trial ends tomorrow";
    private const string EndDay = "Your ScreenHire trial ends today";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signing_up_schedules_an_email_at_seven_days_and_one_day_and_at_the_end_and_the_bell_notice_at_seven_days()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (store, ends) = await ScheduledAsync(owner);

        var due = await store.ScheduledTimesAsync(owner.OrganizationId.ToString(), Ct);

        due.Should().HaveCount(5);
        due.Select(d => d - ends).Should().SatisfyRespectively(
            first => first.Should().BeCloseTo(TimeSpan.FromDays(-7), TimeSpan.FromSeconds(1)),
            second => second.Should().BeCloseTo(TimeSpan.FromDays(-7), TimeSpan.FromSeconds(1)),
            third => third.Should().BeCloseTo(TimeSpan.FromDays(-1), TimeSpan.FromSeconds(1)),
            fourth => fourth.Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            fifth => fifth.Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        (await store.GetDeadLettersAsync(owner.OrganizationId.ToString(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Scheduling_the_reminders_leaves_the_billing_account_alone_so_an_immediate_checkout_cannot_collide_with_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, ends) = await ScheduledAsync(owner);
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);

        var account = await db.BillingAccounts.AsNoTracking().SingleAsync(Ct);
        var records = await db.BillingEvents.AsNoTracking().Select(e => e.EventId).ToListAsync(Ct);

        account.UpdatedAt.Should().Be(account.CreatedAt, "the worker wrote nothing to the row that checkouts and webhooks save with a concurrency token");
        records.Should().Equal(TrialReminderKeys.ScheduledRecord(ends));

        // A checkout straight after sign-up, while the worker is scheduling, is applied.
        await fixture.SubscribeAsync(owner, "starter");
        (await owner.BillingAsync()).Text("state").Should().Be("Active");
    }

    [Fact]
    public async Task Each_reminder_emails_every_owner_once_and_the_bell_follows_the_trial_to_its_end_for_owners_only()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.InviteAndJoinAsync(first, "Owner");
        using var admin = await fixture.InviteAndJoinAsync(first, "Admin");
        var (store, ends) = await ScheduledAsync(first);
        var org = first.OrganizationId.ToString();
        var firstEmail = await first.EmailOfAsync();
        var secondEmail = await second.EmailOfAsync();
        var adminEmail = await admin.EmailOfAsync();

        // Seven days before: the email, and the notice in the bell.
        await ReleaseEmailAsync(store, first, ends, 7);
        (await ReleaseNoticeAsync(store, org)).Should().Be(1);
        var email = await WaitForEmailAsync(firstEmail, SevenDays);
        await WaitForEmailAsync(secondEmail, SevenDays);
        email.Text.Should().Contain("Growth plan").And.Contain("open jobs stay open").And.Contain("/settings/billing");
        var ownerList = await first.WaitForItemsAsync(1);
        var item = ownerList.At("items.0");
        item.GetProperty("kind").GetString().Should().Be("trial-ending");
        item.GetProperty("link").GetString().Should().Be("/settings/billing");
        item.GetProperty("payload").GetProperty("endsAt").GetDateTimeOffset().Should().BeCloseTo(ends, TimeSpan.FromSeconds(1));
        (await second.WaitForItemsAsync(1)).At("items.0").GetProperty("kind").GetString().Should().Be("trial-ending");

        // Delivered again, the reminder sends nothing: a redelivery is the same intent.
        await DeliverAgainAsync(first, ends, 7);
        (await Matching(firstEmail, SevenDays)).Should().ContainSingle();
        (await Matching(secondEmail, SevenDays)).Should().ContainSingle();

        // The admin is not an Owner: no email, nothing in the bell.
        (await Matching(adminEmail, "Your ScreenHire trial")).Should().BeEmpty();
        (await admin.NotificationsAsync()).Count("items").Should().Be(0);

        // One day before: another email, and still one notification, not one per day.
        await ReleaseEmailAsync(store, first, ends, 1);
        await WaitForEmailAsync(firstEmail, OneDay);
        (await first.NotificationsAsync()).Count("items").Should().Be(1);

        // The day it ends: an email, and the ending notice gives way to the ended one, unread.
        await ReleaseEmailAsync(store, first, ends, 0);
        await WaitForEmailAsync(firstEmail, EndDay);
        (await store.ReleaseNextScheduledAsync(org, NoticeType, Ct)).Should().Be(1);
        await WolverineStore.WaitUntilAsync(async () => await store.CountHandledAsync(org, Ct, NoticeType) >= 2, "the ended notice to be handled", Ct);
        var ended = await WaitForOnlyAsync(first, "trial-ended");
        ended.Count("items").Should().Be(1, "the ending notice was replaced, not added to");
        ended.At("unreadCount").GetInt32().Should().Be(1);
        (await admin.NotificationsAsync()).Count("items").Should().Be(0);
        (await Matching(adminEmail, "Your ScreenHire trial")).Should().BeEmpty();
        (await store.GetDeadLettersAsync(org, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_trial_that_ended_stays_in_the_bell_past_the_retention_until_a_plan_starts()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (store, _) = await ScheduledAsync(owner);
        var org = owner.OrganizationId.ToString();
        (await ReleaseNoticeAsync(store, org)).Should().Be(1);
        (await store.ReleaseNextScheduledAsync(org, NoticeType, Ct)).Should().Be(1);
        await WolverineStore.WaitUntilAsync(async () => await store.CountHandledAsync(org, Ct, NoticeType) >= 2, "both notices to be handled", Ct);
        await WaitForOnlyAsync(owner, "trial-ended");

        // A month and more later, the daily purge runs: the notice says something still true, so it stays.
        await using (var db = await fixture.ContextAsync(owner.OrganizationId, Ct))
        {
            await db.Database.ExecuteSqlAsync($"UPDATE notifications SET created_at = now() - interval '90 days' WHERE organization_id = {owner.OrganizationId}", Ct);
        }

        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new PurgeOrganizationExpiredNotifications(owner.OrganizationId, DateTimeOffset.UtcNow));
        }

        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(org, Ct, ".PurgeOrganizationExpiredNotifications") >= 1, "the purge to be handled", Ct);
        (await owner.NotificationsAsync()).Count("items").Should().Be(1);

        // Choosing a plan takes it away.
        await fixture.SubscribeAsync(owner, "starter");
        await WolverineStore.WaitUntilAsync(async () => (await owner.NotificationsAsync()).Count("items") == 0, "the notice to go once a plan started", Ct);
    }

    [Fact]
    public async Task A_plan_that_starts_clears_the_notice_and_the_reminders_that_fire_after_it_do_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (store, _) = await ScheduledAsync(owner);
        var org = owner.OrganizationId.ToString();
        var email = await owner.EmailOfAsync();
        (await ReleaseNoticeAsync(store, org)).Should().Be(1);
        await owner.WaitForItemsAsync(1);

        await fixture.SubscribeAsync(owner, "starter");

        await WolverineStore.WaitUntilAsync(async () => (await owner.NotificationsAsync()).Count("items") == 0, "the notice to go once a plan started", Ct);

        // Every reminder still waiting now fires: the email ones, and the ended notice.
        (await store.ReleaseScheduledAsync(org, Ct)).Should().Be(4);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(org, Ct, EmailType) >= 3 && await store.CountHandledAsync(org, Ct, NoticeType) >= 2,
            "the late reminders to be handled",
            Ct);

        (await Matching(email, "Your ScreenHire trial")).Should().BeEmpty("a plan had started, so nothing was sent");
        (await owner.NotificationsAsync()).Count("items").Should().Be(0);
        (await store.GetDeadLettersAsync(org, Ct)).Should().BeEmpty();
    }

    // The organization's trial end as the API reports it, once the worker has scheduled all five steps.
    private async Task<(WolverineStore Store, DateTimeOffset Ends)> ScheduledAsync(Tenant owner)
    {
        var ends = (await owner.BillingAsync()).At("trialEndsAt").GetDateTimeOffset();
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => (await store.ScheduledTimesAsync(owner.OrganizationId.ToString(), Ct)).Count >= 5,
            "the worker to schedule the trial's reminders",
            Ct);
        return (store, ends);
    }

    private static async Task ReleaseEmailAsync(WolverineStore store, Tenant owner, DateTimeOffset ends, int daysBefore)
    {
        var key = RemindTrialEnding.For(owner.OrganizationId, ends, daysBefore).Key;
        (await store.ReleaseScheduledAsync(key, Ct)).Should().Be(1, "the trial scheduled exactly one reminder for that day");
        await WolverineStore.WaitUntilAsync(async () => await store.CountHandledAsync(key, Ct, EmailType) >= 1, $"the {daysBefore} day reminder to be handled", Ct);
    }

    // The bell's notice is the earlier of the two notices: the later one is the end.
    private static async Task<int> ReleaseNoticeAsync(WolverineStore store, string org)
    {
        var released = await store.ReleaseNextScheduledAsync(org, NoticeType, Ct);
        await WolverineStore.WaitUntilAsync(async () => await store.CountHandledAsync(org, Ct, NoticeType) >= 1, "the notice to be handled", Ct);
        return released;
    }

    private async Task DeliverAgainAsync(Tenant owner, DateTimeOffset ends, int daysBefore)
    {
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var message = RemindTrialEnding.For(owner.OrganizationId, ends, daysBefore);
        var handledBefore = await store.CountHandledAsync(message.Key, Ct, EmailType);
        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(message);
        // The second envelope is received and marked handled without running (it is the same intent), so wait for that
        // instead of for a time, then look at what it sent.
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(message.Key, Ct, EmailType) > handledBefore, "the worker to handle the redelivered reminder", Ct);
    }

    // Waits until the bell holds exactly that kind: a swap is a clear and a raise, two messages, so it has an instant with both.
    private static async Task<ApiResult> WaitForOnlyAsync(Tenant member, string kind)
    {
        ApiResult? list = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                list = await member.NotificationsAsync();
                return list.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).SequenceEqual([kind]);
            },
            $"only a {kind} notification",
            Ct);
        return list!;
    }

    private async Task<MailpitMessage> WaitForEmailAsync(string address, string subject)
    {
        MailpitMessage? found = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                found = (await fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject.Contains(subject, StringComparison.Ordinal));
                return found is not null;
            },
            $"an email to the test address with the subject '{subject}'",
            Ct);
        return found!;
    }

    private async Task<IReadOnlyList<MailpitMessage>> Matching(string address, string subject) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.Contains(subject, StringComparison.Ordinal))];
}
