using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.SendingDomains;

/// <summary>What one look at a domain found: each record's state, and whether the provider confirmed the domain. Nothing is saved.</summary>
public sealed record SendingDomainProbe(IReadOnlyList<SendingDomainRecord> Records, bool Verified, string? ProviderMessage);

public enum SendingDomainMode
{
    /// <summary>No provider here: the Sending domain page says so and nothing is set up.</summary>
    Off,

    /// <summary>The local sandbox: made-up records that verify once a developer publishes the domain.</summary>
    Sandbox,

    /// <summary>SendGrid's Domain Authentication API and real DNS.</summary>
    SendGrid,
}

/// <summary>
/// The organization's sending domain, as this host is configured (<see cref="Mode"/>). Always registered, so an endpoint
/// that takes it resolves, and <see cref="IsAvailable"/> false hides the feature, as <c>SlackAccess</c> does for Slack.
/// Each method saves its own changes: replacing a domain removes the old row before it adds the new one, because the
/// organization may have only one.
/// </summary>
/// <remarks>
/// Both providers are constructor parameters, not looked up from an <c>IServiceProvider</c>: Wolverine refuses a
/// handler whose dependency locates services at run time, and the sandbox must stay out of a real deployment's way.
/// </remarks>
public sealed class SendingDomainService(
    IOptions<EmailOptions> options,
    SendGridSendingDomainProvider sendGrid,
    SandboxSendingDomainProvider sandbox,
    DnsRecordChecker dns,
    SandboxDnsRecordChecker sandboxDns,
    TimeProvider time)
{
    public SendingDomainMode Mode =>
        options.Value.SendGrid.Enabled ? SendingDomainMode.SendGrid
        : options.Value.Sandbox.Enabled ? SendingDomainMode.Sandbox
        : SendingDomainMode.Off;

    public bool IsAvailable => Mode != SendingDomainMode.Off;

    private ISendingDomainProvider Provider => Mode switch
    {
        SendingDomainMode.SendGrid => sendGrid,
        SendingDomainMode.Sandbox => sandbox,
        _ => throw new InvalidOperationException("No email provider is set up here (ScreenHire:Email)."),
    };

    private IDnsRecordChecker Checker => Mode == SendingDomainMode.SendGrid ? dns : sandboxDns;

    /// <summary>
    /// Sets <paramref name="domain"/> (already normalized) up with the provider and stores it with its records, replacing
    /// the organization's current one. The same domain again returns the stored row unchanged.
    /// </summary>
    public async Task<SendingDomain> SetAsync(
        ScreenHireDbContext db, Guid organizationId, string domain, Uri inboundUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var current = await db.SendingDomains.SingleOrDefaultAsync(cancellationToken);
        if (current?.Domain == domain)
        {
            return current;
        }

        // One transaction, so a replacement that cannot go through leaves the organization's current domain as it was. The new
        // row goes in before the provider is asked: a domain another organization already holds fails on its unique index
        // here (row-level security hides that organization's row, so nothing could check it earlier), with nothing created
        // at the provider. The old domain leaves the provider only once the new one is committed.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (current is not null)
        {
            db.SendingDomains.Remove(current);
            await db.SaveChangesAsync(cancellationToken);
        }

        var created = new SendingDomain { OrganizationId = organizationId, Domain = domain, CreatedAt = time.GetUtcNow() };
        db.SendingDomains.Add(created);
        await db.SaveChangesAsync(cancellationToken);

        var provisioned = await Provider.CreateAsync(new SendingDomainRequest(domain, created.ReplyHost, inboundUrl), cancellationToken);
        created.Provisioned(provisioned.ProviderDomainId, provisioned.Records);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (current?.ProviderDomainId is { } previousId)
        {
            try
            {
                await Provider.DeleteAsync(previousId, current.ReplyHost, cancellationToken);
            }
            catch (SendingDomainProviderException)
            {
                // The organization no longer uses it; a leftover at the provider is harmless and the owner can remove it there.
            }
        }

        return created;
    }

    /// <summary>
    /// Looks every record up in DNS and, when all are in place, asks the provider to confirm the domain, and saves nothing.
    /// The daily re-check probes first, so one failed lookup changes nothing until a second probe agrees.
    /// </summary>
    public async Task<SendingDomainProbe> ProbeAsync(SendingDomain domain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var checkedRecords = new List<SendingDomainRecord>();
        foreach (var record in domain.Records)
        {
            checkedRecords.Add(record with { State = await Checker.CheckAsync(record, cancellationToken) });
        }

        var allInPlace = checkedRecords.All(r => r.State == SendingDomainRecordState.Matches);
        var validation = allInPlace && domain.ProviderDomainId is { } id
            ? await Provider.ValidateAsync(id, cancellationToken)
            : new ProviderValidation(false, null);
        return new SendingDomainProbe(checkedRecords, validation.Valid, validation.Message);
    }

    /// <summary>Records what a probe found on the domain and saves it, as Check records does.</summary>
    public async Task<SendingDomain> RecordAsync(ScreenHireDbContext db, SendingDomain domain, SendingDomainProbe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(probe);
        domain.RecordCheck(probe.Records, probe.Verified, probe.ProviderMessage, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return domain;
    }

    /// <summary>
    /// Looks every record up in DNS and, when all are in place, asks the provider to confirm the domain. Only the
    /// provider's yes makes the domain verified, so mail never goes out as the company before the provider can sign it.
    /// </summary>
    public async Task<SendingDomain> CheckAsync(ScreenHireDbContext db, SendingDomain domain, CancellationToken cancellationToken) =>
        await RecordAsync(db, domain, await ProbeAsync(domain, cancellationToken), cancellationToken);

    /// <summary>Removes the domain from the provider and from the organization. Mail goes back to ScreenHire's address.</summary>
    public async Task RemoveAsync(ScreenHireDbContext db, SendingDomain domain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.ProviderDomainId is { } id)
        {
            await Provider.DeleteAsync(id, domain.ReplyHost, cancellationToken);
        }

        db.SendingDomains.Remove(domain);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public static class SendingDomainRegistration
{
    /// <summary>The sending-domain services. The mode is read per call, from <see cref="EmailOptions"/>.</summary>
    public static IServiceCollection AddScreenHireSendingDomains(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScreenHireEmailOptions();
        services.TryAddSingleton<SandboxDnsZone>();
        services.TryAddSingleton<SendGridSendingDomainProvider>();
        services.TryAddSingleton<SandboxSendingDomainProvider>();
        services.TryAddSingleton<DnsRecordChecker>();
        services.TryAddSingleton<SandboxDnsRecordChecker>();
        services.TryAddScoped<SendingDomainService>();
        services.AddOptions<SendingDomainRecheckOptions>().BindConfiguration(SendingDomainRecheckOptions.Section);
        return services;
    }
}
