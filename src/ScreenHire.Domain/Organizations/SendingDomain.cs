using System.Text.RegularExpressions;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// The company's own domain that its candidate email is sent from (docs/architecture/email.md, ADR-0021). One per
/// organization. The provider returns the DNS records the domain needs; ScreenHire checks each one itself to say which
/// is missing, and the domain becomes <see cref="SendingDomainStatus.Verified"/>, and mail switches to
/// <see cref="FromAddress"/>, only when the provider confirms it too. Until then mail keeps ScreenHire's address, so
/// nothing goes out unauthenticated in the company's name.
/// </summary>
public sealed partial class SendingDomain : ITenantOwned
{
    /// <summary>The longest a domain name may be (RFC 1035).</summary>
    public const int MaxLength = 253;

    /// <summary>The local part of the address mail comes from.</summary>
    public const string FromLocalPart = "hiring";

    /// <summary>The label replies arrive under. A subdomain, because an MX record on the domain itself would take all of the company's mail.</summary>
    public const string ReplyLabel = "reply";

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The domain, lower-case, without a scheme, path or trailing dot.</summary>
    public required string Domain { get; init; }

    /// <summary>The provider's id for the domain, used to validate and remove it. Null until the provider has set the domain up.</summary>
    public string? ProviderDomainId { get; private set; }

    public SendingDomainStatus Status { get; private set; } = SendingDomainStatus.Pending;

    public List<SendingDomainRecord> Records { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastCheckedAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>What the provider said when it last refused to verify the domain, in its own short words. Null when it did not.</summary>
    public string? ProviderMessage { get; private set; }

    /// <summary>The address mail comes from once the domain is verified.</summary>
    public string FromAddress => $"{FromLocalPart}@{Domain}";

    /// <summary>The host replies arrive on: <c>reply.</c> then the domain.</summary>
    public string ReplyHost => $"{ReplyLabel}.{Domain}";

    public bool IsVerified => Status == SendingDomainStatus.Verified;

    /// <summary>Records that the provider set the domain up: its id and the DNS records the owner adds.</summary>
    public void Provisioned(string providerDomainId, IEnumerable<SendingDomainRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerDomainId);
        ProviderDomainId = providerDomainId;
        SetRecords(records);
    }

    public void SetRecords(IEnumerable<SendingDomainRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        Records = [.. records];
    }

    /// <summary>Records what the DNS check found, and whether the provider confirmed the domain.</summary>
    public void RecordCheck(IEnumerable<SendingDomainRecord> checkedRecords, bool providerConfirmed, string? providerMessage, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checkedRecords);
        Records = [.. checkedRecords];
        LastCheckedAt = now;
        ProviderMessage = providerConfirmed ? null : providerMessage;
        if (providerConfirmed && Status != SendingDomainStatus.Verified)
        {
            Status = SendingDomainStatus.Verified;
            VerifiedAt = now;
        }
        else if (!providerConfirmed)
        {
            Status = SendingDomainStatus.Pending;
            VerifiedAt = null;
        }
    }

    /// <summary>
    /// The domain in its stored form, or false when <paramref name="input"/> is not a plain domain name a person owns:
    /// at least two labels, letters, digits and hyphens, no address, scheme, path, wildcard or IP address.
    /// </summary>
    public static bool TryNormalize(string? input, out string domain)
    {
        domain = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = input.Trim().TrimEnd('.').ToLowerInvariant();
        if (candidate.Length > MaxLength - ReplyLabel.Length - 1 || !DomainName().IsMatch(candidate))
        {
            return false;
        }

        // 1.2.3.4 has digit-only labels and a digit-only last label: no real top-level domain is all digits.
        if (candidate.Split('.')[^1].All(char.IsAsciiDigit))
        {
            return false;
        }

        domain = candidate;
        return true;
    }

    [GeneratedRegex(@"^(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))+$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainName();
}

public enum SendingDomainStatus
{
    /// <summary>Added; the records are not all in place, or the provider has not confirmed them.</summary>
    Pending,

    /// <summary>The provider confirmed the domain: mail comes from it.</summary>
    Verified,
}

/// <summary>Why a record is on the list.</summary>
public enum SendingDomainRecordPurpose
{
    /// <summary>A DKIM key, so the mail is signed as the company's domain.</summary>
    Dkim,

    /// <summary>The return path (bounce) host. It carries the SPF record on its own subdomain, so the company's own SPF is untouched.</summary>
    ReturnPath,

    /// <summary>The MX record of the reply subdomain, so candidates' answers reach ScreenHire.</summary>
    Replies,
}

/// <summary>What the last DNS lookup found for a record.</summary>
public enum SendingDomainRecordState
{
    NotChecked,

    /// <summary>The record is there with the value the provider needs.</summary>
    Matches,

    /// <summary>The name has a record of this type with another value.</summary>
    Differs,

    /// <summary>No record of this type answered.</summary>
    Missing,
}

/// <summary>One DNS record the owner adds, as the provider returned it, and what the last check found.</summary>
public sealed record SendingDomainRecord(
    SendingDomainRecordPurpose Purpose,
    string Type,
    string Host,
    string Value,
    SendingDomainRecordState State = SendingDomainRecordState.NotChecked,
    int? Priority = null);
