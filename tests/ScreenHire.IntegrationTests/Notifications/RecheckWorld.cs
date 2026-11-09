using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>
/// One organization with two Owners and an Admin, its sending domain, and the in-process worker host that re-checks it. The host's
/// DNS zone is its own and starts empty. A message is sent to the host the way the worker would receive it, and the test waits for
/// it to be handled before it looks at anything.
/// </summary>
internal sealed class RecheckWorld(
    AppHostFixture fixture, Tenant owner, Tenant secondOwner, Tenant admin, string domain, Guid domainId, MessagingHost host, CapturingEmailSender mail)
    : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Tenant Owner => owner;

    public Tenant SecondOwner => secondOwner;

    public Tenant Admin => admin;

    public string Domain => domain;

    public MessagingHost Host => host;

    public CapturingEmailSender Mail => mail;

    public Guid OrganizationId => owner.OrganizationId;

    /// <summary>What a notification about this domain is keyed by: its id.</summary>
    public string DomainKey => domainId.ToString("N");

    /// <summary>The host's DNS: a domain is "in DNS" for the host only once it is published here.</summary>
    public SandboxDnsZone Zone => host.Services.GetRequiredService<SandboxDnsZone>();

    public string ConfirmKey(DateOnly day) => RecheckSendingDomain.Confirm(OrganizationId, day).Key;

    /// <summary>Sends one look to the host and waits until it was handled.</summary>
    public async Task LookAsync(RecheckSendingDomain message)
    {
        await host.Services.GetRequiredService<IMessageBus>().SendAsync(message);
        await WaitHandledAsync(message.Key, "RecheckSendingDomain");
    }

    /// <summary>
    /// Releases the second look the daily one scheduled, by moving its due time to now, and waits until it was handled. It is the
    /// scheduled message itself that runs, so what it carries is what the daily look chose to send.
    /// </summary>
    public async Task ReleaseConfirmAsync(DateOnly day)
    {
        (await host.Store.ReleaseScheduledAsync(ConfirmKey(day), Ct)).Should().Be(1, "the daily look scheduled exactly one second look");
        await WaitHandledAsync(ConfirmKey(day), "RecheckSendingDomain");
    }

    public async Task WaitHandledAsync(string key, string messageType, int count = 1) =>
        await WolverineStore.WaitUntilAsync(
            async () => await host.Store.CountHandledAsync(key, Ct, messageType) >= count,
            $"the host to handle {count} {messageType} message(s) for {key}", Ct);

    /// <summary>The domain as the database holds it.</summary>
    public async Task<SendingDomain> StateAsync()
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, OrganizationId);
        return await db.SendingDomains.AsNoTracking().SingleAsync(Ct);
    }

    public async Task<string> EmailOfAsync(Tenant member) =>
        (await fixture.OwnerScalarAsync<string>("SELECT email FROM users WHERE id = @id", ("id", member.UserId)))!;

    /// <summary>No email, no notification row and no <c>RaiseNotification</c> was made, so a look that was meant to tell nobody told nobody.</summary>
    public async Task ExpectNothingToldAsync()
    {
        Mail.Sent.Should().BeEmpty();
        (await fixture.OwnerScalarAsync<long>("SELECT count(*) FROM notifications WHERE organization_id = @org", ("org", OrganizationId))).Should().Be(0);
        (await host.Store.CountIncomingAsync(OrganizationId.ToString(), Ct, "RaiseNotification")).Should().Be(0);
        (await host.Store.CountIncomingAsync(OrganizationId.ToString(), Ct, "EmailSendingDomainFailing")).Should().Be(0);
    }

    /// <summary>Waits until the host has sent the Owners' emails, one message per Owner.</summary>
    public async Task WaitEmailedAsync(int owners) => await WaitHandledAsync(DomainKey, "EmailSendingDomainFailing", owners);

    public async ValueTask DisposeAsync()
    {
        await host.DisposeAsync();
        admin.Dispose();
        secondOwner.Dispose();
        owner.Dispose();
    }
}

/// <summary>An email sender that records what it was asked to send and sends nothing.</summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
