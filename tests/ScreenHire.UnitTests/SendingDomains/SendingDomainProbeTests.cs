using Microsoft.Extensions.Options;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.SendingDomains;

/// <summary>
/// A probe is one look at a domain's DNS and the provider's answer, and saves nothing: the daily re-check probes first, so one
/// failed lookup changes nothing until a second probe agrees. Recording what a probe found is a separate step.
/// </summary>
public sealed class SendingDomainProbeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 5, 47, 0, TimeSpan.Zero);

    private readonly SandboxDnsZone zone = new();
    private readonly SendGridSendingDomainProvider sendGrid;
    private readonly SendingDomainService service;

    public SendingDomainProbeTests()
    {
        var options = Options.Create(new EmailOptions { Sandbox = new EmailSandboxOptions { Enabled = true } });
        sendGrid = new SendGridSendingDomainProvider(options, new NoNetworkHandler());
        service = new SendingDomainService(
            options,
            sendGrid,
            new SandboxSendingDomainProvider(zone),
            new DnsRecordChecker(),
            new SandboxDnsRecordChecker(zone),
            new FixedTime(Now));
    }

    public void Dispose() => sendGrid.Dispose();

    private async Task<SendingDomain> ProvisionedAsync(string name)
    {
        var domain = new SendingDomain { Domain = name, CreatedAt = Now.AddDays(-30) };
        var provisioned = await new SandboxSendingDomainProvider(zone).CreateAsync(
            new SendingDomainRequest(name, domain.ReplyHost, new Uri("https://api.example.test/inbound")), TestContext.Current.CancellationToken);
        domain.Provisioned(provisioned.ProviderDomainId, provisioned.Records);
        return domain;
    }

    [Fact]
    public async Task A_probe_of_a_published_domain_finds_it_verified_and_changes_nothing_on_the_domain()
    {
        var domain = await ProvisionedAsync("probe-one.example.com");
        zone.Publish(domain.Domain);

        var probe = await service.ProbeAsync(domain, TestContext.Current.CancellationToken);

        probe.Verified.Should().BeTrue();
        probe.Records.Should().HaveCount(4).And.OnlyContain(r => r.State == SendingDomainRecordState.Matches);
        domain.Status.Should().Be(SendingDomainStatus.Pending, "a probe saves nothing");
        domain.LastCheckedAt.Should().BeNull();
        domain.VerifiedAt.Should().BeNull();
        domain.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.NotChecked);
    }

    [Fact]
    public async Task A_probe_of_a_domain_that_is_not_published_finds_every_record_missing_and_the_provider_saying_no()
    {
        var domain = await ProvisionedAsync("probe-two.example.com");

        var probe = await service.ProbeAsync(domain, TestContext.Current.CancellationToken);

        probe.Verified.Should().BeFalse();
        probe.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.Missing);
        domain.LastCheckedAt.Should().BeNull();
        domain.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.NotChecked);
    }

    [Fact]
    public async Task Recording_a_probe_on_the_domain_is_what_changes_it()
    {
        var domain = await ProvisionedAsync("probe-three.example.com");
        zone.Publish(domain.Domain);
        var probe = await service.ProbeAsync(domain, TestContext.Current.CancellationToken);
        domain.Status.Should().Be(SendingDomainStatus.Pending);

        domain.RecordCheck(probe.Records, probe.Verified, probe.ProviderMessage, Now);

        domain.Status.Should().Be(SendingDomainStatus.Verified);
        domain.LastCheckedAt.Should().Be(Now);
        domain.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.Matches);
    }

    [Fact]
    public async Task A_probe_does_not_clear_the_mark_that_the_owners_were_told()
    {
        var domain = await ProvisionedAsync("probe-four.example.com");
        domain.MarkVerificationLost(Now.AddDays(-1));
        zone.Publish(domain.Domain);

        await service.ProbeAsync(domain, TestContext.Current.CancellationToken);

        domain.VerificationLostAt.Should().Be(Now.AddDays(-1));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // The sandbox never calls SendGrid; a request here would be a bug in the service's choice of provider.
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The sandbox probe must not call SendGrid.");
    }
}
