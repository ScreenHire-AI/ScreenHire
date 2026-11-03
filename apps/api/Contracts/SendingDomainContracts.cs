using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's sending domain (Settings, Sending domain). <c>available</c> is false when this deployment has no email
/// provider: the page says so and offers nothing. <c>sandbox</c> says the records are made up for a local run. <c>domain</c>
/// is null until one is added. <c>lastUpdated</c> is the newest change to it (added, verified, no longer verified), null with
/// no domain; its History is <c>GET /v1/organization/sending-domain/history</c>.
/// </summary>
public sealed record OrganizationSendingDomain(bool Available, bool Sandbox, SendingDomainDetail? Domain, LastUpdate? LastUpdated = null);

/// <summary>
/// A sending domain and what is left to do. <c>status</c> is Verified only once the email provider confirms the records,
/// and only then does mail come from <c>fromAddress</c>; until then it keeps ScreenHire's address.
/// </summary>
/// <remarks>
/// <c>replyHost</c>: The subdomain candidates' replies arrive on. <c>providerMessage</c>: The provider's short reason when
/// it did not confirm a domain whose records are all in place. <c>lastCheckedAt</c>: When the records were last looked up.
/// </remarks>
public sealed record SendingDomainDetail(
    string Domain,
    SendingDomainStatus Status,
    string FromAddress,
    string ReplyHost,
    IReadOnlyList<SendingDomainRecordEntry> Records,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? VerifiedAt,
    string? ProviderMessage);

/// <summary>One DNS record to add at the domain's DNS host, why it is needed, and what the last check found.</summary>
/// <remarks><c>priority</c>: For an MX record, its preference number.</remarks>
public sealed record SendingDomainRecordEntry(
    SendingDomainRecordPurpose Purpose,
    string Type,
    string Host,
    string Value,
    int? Priority,
    SendingDomainRecordState State,
    string Reason);

/// <summary>The domain to send from, such as <c>example.com</c>. Replaces the organization's current one.</summary>
public sealed record SetSendingDomainRequest(string Domain);

internal static class SendingDomainMappings
{
    public static SendingDomainDetail ToDetail(this SendingDomain domain) =>
        new(
            domain.Domain, domain.Status, domain.FromAddress, domain.ReplyHost,
            [.. domain.Records.Select(r => new SendingDomainRecordEntry(r.Purpose, r.Type, r.Host, r.Value, r.Priority, r.State, ReasonFor(r.Purpose)))],
            domain.CreatedAt, domain.LastCheckedAt, domain.VerifiedAt, domain.ProviderMessage);

    /// <summary>Why the owner is asked to add a record, in the words the page shows.</summary>
    public static string ReasonFor(SendingDomainRecordPurpose purpose) => purpose switch
    {
        SendingDomainRecordPurpose.Dkim => "Signs your mail as your domain, so it passes DMARC.",
        SendingDomainRecordPurpose.ReturnPath => "Sets the return path for bounces. It also carries the SPF record on its own subdomain, so your existing SPF record stays as it is.",
        _ => "Delivers candidates' replies to ScreenHire. It is on the reply subdomain only, so the rest of your mail is not affected.",
    };
}
