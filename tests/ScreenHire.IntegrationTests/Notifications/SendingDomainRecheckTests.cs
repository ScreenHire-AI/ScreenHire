using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>
/// The daily re-check of the sending domain (docs/architecture/notifications.md, "The daily email domain check"). A verified
/// domain that fails a daily look changes nothing until a second look an hour later agrees; then it falls back to Pending, each
/// Owner is told once in the bell and by email, and a later pass clears it. The running worker uses the sandbox, whose DNS is in
/// the API's memory, so these run on an in-process worker host (a private Wolverine schema) with a DNS zone of its own and an email
/// sender that records. The domain is set up through the real API; the host's zone starts empty, so a verified domain fails there
/// until the test publishes it. The second look is released by moving its scheduled time to now, never waited for.
/// </summary>
public sealed class SendingDomainRecheckTests(AppHostFixture fixture)
{
    private const string Path = "/v1/organization/sending-domain";

    private static readonly DateOnly Monday = new(2026, 11, 9);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_verified_domain_failing_its_first_daily_look_changes_nothing_and_schedules_one_second_look()
    {
        await using var world = await StartAsync(verified: true);
        var before = await world.StateAsync();
        var historyBefore = (await world.Owner.GetAsync($"{Path}/history")).Expect(HttpStatusCode.OK).Count("entries");

        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));

        var after = await world.StateAsync();
        after.Status.Should().Be(SendingDomainStatus.Verified, "one failed lookup changes nothing");
        after.LastCheckedAt.Should().Be(before.LastCheckedAt, "nothing was saved, not even the time of the look");
        after.VerificationLostAt.Should().BeNull();
        (await world.Owner.GetAsync($"{Path}/history")).Count("entries").Should().Be(historyBefore, "nothing happened to the domain, so history has no line");
        var scheduled = await world.Host.Store.ScheduledTimesAsync(world.ConfirmKey(Monday), Ct);
        scheduled.Should().ContainSingle().Which.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(30), "the second look is an hour away");
        await world.ExpectNothingToldAsync();
    }

    [Fact]
    public async Task A_second_look_that_passes_saves_the_pass_and_tells_nobody()
    {
        await using var world = await StartAsync(verified: true);
        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));
        var before = await world.StateAsync();
        world.Zone.Publish(world.Domain);

        await world.ReleaseConfirmAsync(Monday);

        var after = await world.StateAsync();
        after.Status.Should().Be(SendingDomainStatus.Verified);
        after.LastCheckedAt.Should().BeAfter(before.LastCheckedAt!.Value, "the pass is recorded");
        after.VerificationLostAt.Should().BeNull();
        after.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.Matches);
        await world.ExpectNothingToldAsync();
    }

    [Fact]
    public async Task A_second_look_that_still_fails_loses_verification_and_tells_each_owner_once_in_the_bell_and_by_email()
    {
        await using var world = await StartAsync(verified: true);
        var historyBefore = (await world.Owner.GetAsync($"{Path}/history")).Expect(HttpStatusCode.OK).Count("entries");

        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));
        await world.ReleaseConfirmAsync(Monday);

        var state = await world.StateAsync();
        state.Status.Should().Be(SendingDomainStatus.Pending);
        state.VerifiedAt.Should().BeNull();
        state.VerificationLostAt.Should().NotBeNull();

        var first = await world.Owner.WaitForItemsAsync(1);
        var second = await world.SecondOwner.WaitForItemsAsync(1);
        foreach (var list in new[] { first, second })
        {
            list.At("items.0.kind").GetString().Should().Be("sending-domain-failing");
            list.At("items.0.payload.domain").GetString().Should().Be(world.Domain);
            list.At("items.0.link").GetString().Should().Be("/settings/sending-domain");
            list.At("unreadCount").GetInt32().Should().Be(1);
        }

        var admin = await world.Admin.NotificationsAsync();
        admin.Count("items").Should().Be(0, "an Admin is not an Owner");

        // Each Owner's email is a message of its own, sent after the mark and the alert are saved.
        await world.WaitEmailedAsync(owners: 2);
        var owners = new[] { await world.EmailOfAsync(world.Owner), await world.EmailOfAsync(world.SecondOwner) };
        world.Mail.Sent.Select(m => m.To).Should().BeEquivalentTo(owners, "exactly one email per Owner, and none to the Admin");
        world.Mail.Sent.Should().OnlyContain(m => m.Subject == $"{world.Domain} stopped verifying");
        world.Mail.Sent.Should().OnlyContain(m => m.TextBody.Contains("https://app.example.test/settings/sending-domain", StringComparison.Ordinal));

        var history = (await world.Owner.GetAsync($"{Path}/history")).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(historyBefore + 1);
        history.Text("entries.0.lines.0.text").Should().StartWith($"found the sending domain {world.Domain} no longer verified");
        (await world.Host.Store.CountIncomingAsync(world.OrganizationId.ToString(), Ct, "RaiseNotification")).Should().Be(1);
    }

    [Fact]
    public async Task The_next_days_daily_look_of_a_lost_domain_neither_adds_a_row_nor_emails_again()
    {
        await using var world = await StartAsync(verified: true);
        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));
        await world.ReleaseConfirmAsync(Monday);
        await world.Owner.WaitForItemsAsync(1);
        await world.SecondOwner.WaitForItemsAsync(1);
        await world.WaitEmailedAsync(owners: 2);
        var lostAt = (await world.StateAsync()).VerificationLostAt;
        var checkedAt = (await world.StateAsync()).LastCheckedAt;
        var mailBefore = world.Mail.Sent.Count;

        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday.AddDays(1)));
        await world.WaitHandledAsync(world.DomainKey, "RaiseNotification", 2);

        var state = await world.StateAsync();
        state.VerificationLostAt.Should().Be(lostAt, "the Owners were told once");
        state.LastCheckedAt.Should().BeAfter(checkedAt!.Value, "the look is still recorded");
        state.Status.Should().Be(SendingDomainStatus.Pending);
        world.Mail.Sent.Count.Should().Be(mailBefore, "no second email");
        (await world.Host.Store.CountIncomingAsync(world.OrganizationId.ToString(), Ct, "EmailSendingDomainFailing")).Should().Be(2, "only the first loss emails");
        (await world.Host.Store.ScheduledTimesAsync(world.ConfirmKey(Monday.AddDays(1)), Ct)).Should().BeEmpty("a lost domain has no second look to wait for");
        (await world.Owner.NotificationsAsync()).Count("items").Should().Be(1, "raising it again adds no row for an Owner who has it");
        (await world.SecondOwner.NotificationsAsync()).Count("items").Should().Be(1);
    }

    [Fact]
    public async Task A_lost_domain_that_still_fails_puts_the_alert_back_for_an_owner_whose_copy_was_purged_and_for_a_new_owner()
    {
        await using var world = await StartAsync(verified: true);
        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));
        await world.ReleaseConfirmAsync(Monday);
        var purged = (await world.Owner.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        await world.SecondOwner.WaitForItemsAsync(1);
        await world.WaitEmailedAsync(owners: 2);
        var mailBefore = world.Mail.Sent.Count;

        // The retention purge removes the first Owner's row while the domain is still broken, and a third Owner joins.
        await fixture.OwnerSqlAsync("DELETE FROM notifications WHERE id = @id", ("id", purged));
        using var newOwner = await fixture.InviteAndJoinAsync(world.Owner, "Owner");

        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday.AddDays(1)));

        var back = await world.Owner.WaitForItemsAsync(1);
        back.At("items.0.kind").GetString().Should().Be("sending-domain-failing");
        back.At("items.0.id").GetGuid().Should().NotBe(purged, "it is a new row");
        (await newOwner.WaitForItemsAsync(1)).At("items.0.payload.domain").GetString().Should().Be(world.Domain);
        (await world.SecondOwner.NotificationsAsync()).Count("items").Should().Be(1);
        world.Mail.Sent.Count.Should().Be(mailBefore, "the reminder is the bell only");
    }

    [Fact]
    public async Task A_manual_check_that_finds_a_verified_domain_failing_tells_the_owners_as_the_daily_check_would()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var domain = NewDomain();
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        await PublishInApiAsync(domain);
        (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");
        var key = (await fixture.OwnerScalarAsync<Guid>("SELECT id FROM sending_domains WHERE organization_id = @org", ("org", owner.OrganizationId))).ToString("N");

        await SandboxDnsAsync("unpublish", domain);
        (await admin.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Pending");

        (await fixture.OwnerScalarAsync<bool>("SELECT verification_lost_at IS NOT NULL FROM sending_domains WHERE organization_id = @org", ("org", owner.OrganizationId)))
            .Should().BeTrue("the daily check now waits for the recovery instead of taking it for a new domain");
        var item = await owner.WaitForItemsAsync(1);
        item.At("items.0.kind").GetString().Should().Be("sending-domain-failing");
        item.At("items.0.link").GetString().Should().Be("/settings/sending-domain");
        await fixture.WaitHandledAsync(key, "EmailSendingDomainFailing");
        (await admin.NotificationsAsync()).Count("items").Should().Be(0, "an Admin is not an Owner, even the one who pressed Check records");

        // Putting the records back and checking again clears it.
        await PublishInApiAsync(domain);
        (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");
        await owner.WaitForItemsAsync(0);
    }

    [Fact]
    public async Task Dismissing_and_purging_the_notification_does_not_stop_the_recovery_and_a_pass_clears_what_is_left()
    {
        await using var world = await StartAsync(verified: true);
        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));
        await world.ReleaseConfirmAsync(Monday);
        var ownersItem = (await world.Owner.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        await world.SecondOwner.WaitForItemsAsync(1);

        // The first Owner dismisses it and the retention purge removes their row; the second Owner leaves theirs alone.
        (await world.Owner.PostAsync($"/v1/notifications/{ownersItem}/dismiss")).Expect(HttpStatusCode.NoContent);
        await fixture.OwnerSqlAsync("DELETE FROM notifications WHERE id = @id", ("id", ownersItem));
        (await world.StateAsync()).VerificationLostAt.Should().NotBeNull("the mark lives on the domain, not in the notifications");

        world.Zone.Publish(world.Domain);
        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday.AddDays(1)));
        await world.WaitHandledAsync(world.DomainKey, "ClearNotification");

        var state = await world.StateAsync();
        state.Status.Should().Be(SendingDomainStatus.Verified);
        state.VerificationLostAt.Should().BeNull();
        state.VerifiedAt.Should().NotBeNull();
        await world.SecondOwner.WaitForItemsAsync(0);
        (await world.Owner.NotificationsAsync()).Count("items").Should().Be(0);
        var history = (await world.Owner.GetAsync($"{Path}/history")).Expect(HttpStatusCode.OK);
        history.Text("entries.0.lines.0.text").Should().StartWith($"verified the sending domain {world.Domain}");
    }

    [Fact]
    public async Task A_domain_that_was_never_verified_becomes_verified_silently_when_its_records_pass()
    {
        await using var world = await StartAsync(verified: false);
        world.Zone.Publish(world.Domain);

        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));

        var state = await world.StateAsync();
        state.Status.Should().Be(SendingDomainStatus.Verified);
        state.VerifiedAt.Should().NotBeNull();
        state.LastCheckedAt.Should().NotBeNull();
        state.VerificationLostAt.Should().BeNull();
        await world.ExpectNothingToldAsync();
        (await world.Host.Store.ScheduledTimesAsync(world.ConfirmKey(Monday), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_domain_that_was_never_verified_and_fails_is_saved_after_each_look_and_never_raises()
    {
        await using var world = await StartAsync(verified: false);

        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday));
        var first = await world.StateAsync();
        await world.LookAsync(RecheckSendingDomain.Daily(world.OrganizationId, Monday.AddDays(1)));
        var second = await world.StateAsync();

        first.Status.Should().Be(SendingDomainStatus.Pending);
        first.LastCheckedAt.Should().NotBeNull("a domain still being set up has each look recorded");
        first.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.Missing);
        second.LastCheckedAt.Should().BeAfter(first.LastCheckedAt!.Value);
        second.VerificationLostAt.Should().BeNull();
        await world.ExpectNothingToldAsync();
        (await world.Host.Store.ScheduledTimesAsync(world.ConfirmKey(Monday), Ct)).Should().BeEmpty("only a verified domain gets a second look");
    }

    [Fact]
    public async Task A_manual_check_that_passes_clears_the_lost_mark_and_the_owners_notification()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var domain = NewDomain();
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        await PublishInApiAsync(domain);
        (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");

        // The state the daily check leaves behind, made directly because the running stack has no way to unpublish a sandbox domain.
        var domainId = await fixture.OwnerScalarAsync<Guid>("SELECT id FROM sending_domains WHERE organization_id = @org", ("org", owner.OrganizationId));
        await fixture.OwnerSqlAsync(
            "UPDATE sending_domains SET status = 'Pending', verified_at = NULL, verification_lost_at = now() WHERE organization_id = @org",
            ("org", owner.OrganizationId));
        var key = domainId.ToString("N");
        await fixture.RaiseAsync(owner.OrganizationId, key, domain);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        (await owner.WaitForItemsAsync(1)).At("unreadCount").GetInt32().Should().Be(1);

        var checkedDomain = (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK);

        checkedDomain.Text("status").Should().Be("Verified");
        (await fixture.OwnerScalarAsync<bool>("SELECT verification_lost_at IS NOT NULL FROM sending_domains WHERE organization_id = @org", ("org", owner.OrganizationId)))
            .Should().BeFalse("a pass clears the mark");
        await owner.WaitForItemsAsync(0);
    }

    [Fact]
    public async Task A_manual_check_that_still_fails_keeps_the_lost_mark()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync(Path, Set(NewDomain()))).Expect(HttpStatusCode.OK);
        await fixture.OwnerSqlAsync(
            "UPDATE sending_domains SET verification_lost_at = now() WHERE organization_id = @org", ("org", owner.OrganizationId));

        (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Pending");

        (await fixture.OwnerScalarAsync<bool>("SELECT verification_lost_at IS NOT NULL FROM sending_domains WHERE organization_id = @org", ("org", owner.OrganizationId)))
            .Should().BeTrue("only the provider's yes clears it");
    }

    [Fact]
    public async Task The_daily_fan_out_asks_for_one_look_per_organization_with_real_dns_and_nothing_in_the_sandbox()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var clock = new FixedTime(new DateTimeOffset(2026, 11, 9, 5, 47, 0, TimeSpan.Zero));
        await using var db = await fixture.CreateDbContextAsync(new TenantContext(), Ct);

        var zone = new SandboxDnsZone();
        using var sendGrid = new SendGridSendingDomainProvider(Options.Create(new EmailOptions { SendGrid = new SendGridOptions { ApiKey = "SG.fictional-key" } }), new HttpClientHandler());
        SendingDomainService Service(EmailOptions options) => new(
            Options.Create(options), sendGrid, new SandboxSendingDomainProvider(zone), new DnsRecordChecker(), new SandboxDnsRecordChecker(zone), clock);
        var sandbox = Service(new EmailOptions { Sandbox = new EmailSandboxOptions { Enabled = true } });
        var real = Service(new EmailOptions { SendGrid = new SendGridOptions { ApiKey = "SG.fictional-key" } });
        var inSandbox = await RecheckSendingDomainsHandler.Handle(new RecheckSendingDomains(), db, sandbox, clock, Ct);
        var withSendGrid = await RecheckSendingDomainsHandler.Handle(new RecheckSendingDomains(), db, real, clock, Ct);

        inSandbox.Should().BeEmpty("the sandbox's DNS lives in the API's memory, which the worker cannot see");
        withSendGrid.OfType<RecheckSendingDomain>().Should().Contain(RecheckSendingDomain.Daily(organization.Id, new DateOnly(2026, 11, 9)));
        withSendGrid.OfType<RecheckSendingDomain>().Should().OnlyContain(m => !m.Confirming);
        RecheckSendingDomain.Daily(organization.Id, new DateOnly(2026, 11, 9)).Key.Should().Be($"{organization.Id:N}:20261109:daily");
        RecheckSendingDomain.Confirm(organization.Id, new DateOnly(2026, 11, 9)).Key.Should().Be($"{organization.Id:N}:20261109:confirm");
    }

    private static string NewDomain() => $"acme-{Guid.NewGuid():N}.example.com";

    private static JsonObject Set(string domain) => new() { ["domain"] = domain };

    private Task PublishInApiAsync(string domain) => SandboxDnsAsync("publish", domain);

    // The API's sandbox DNS: "publish" puts a domain's records in, "unpublish" takes them out.
    private async Task SandboxDnsAsync(string action, string domain)
    {
        using var client = fixture.AnonymousClient();
        using var response = await client.PostAsync(
            $"/v1/dev/sending-domains/{action}", new StringContent(Set(domain).ToJsonString(), System.Text.Encoding.UTF8, "application/json"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // An organization with two Owners and an Admin, its sending domain, and an in-process worker host that checks it.
    private async Task<RecheckWorld> StartAsync(bool verified)
    {
        var owner = await fixture.SignedInWithOrganizationAsync();
        var second = await fixture.InviteAndJoinAsync(owner, "Owner");
        var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var domain = NewDomain();
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        if (verified)
        {
            await PublishInApiAsync(domain);
            (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");
        }

        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        var mail = new CapturingEmailSender();
        var host = await MessagingHost.StartHandlerAsync(
            fixture,
            settings,
            [],
            Ct,
            configuration: new Dictionary<string, string?>
            {
                ["ScreenHire:Email:Sandbox:Enabled"] = "true",
                ["ScreenHire:Domains:AppHost"] = "app.example.test",
                ["ScreenHire:Domains:Scheme"] = "https",
                ["ScreenHire:SendingDomainRecheck:ConfirmDelay"] = "01:00:00",
            },
            configureServices: services =>
            {
                services.AddSingleton<IEmailSender>(mail);
                // The worker records history for the settings people edit; this host would not without its interceptor.
                var history = new ChangeHistoryInterceptor(TimeProvider.System, new EmailTemplates());
                services.ConfigureDbContext<ScreenHireDbContext>(options => options.AddInterceptors(history));
            },
            environmentName: "Development");

        var domainId = await fixture.OwnerScalarAsync<Guid>("SELECT id FROM sending_domains WHERE organization_id = @org", ("org", owner.OrganizationId));
        return new RecheckWorld(fixture, owner, second, admin, domain, domainId, host, mail);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
