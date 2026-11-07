using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Candidates;

/// <summary>
/// A person who applied to one of the organization's jobs. One candidate per email address within an organization, so
/// a second application reuses the record. Candidate-supplied data only (CLAUDE.md non-negotiable 5).
/// </summary>
/// <remarks>
/// The name is two parts, as the apply form asks for it: <see cref="FirstName"/> and an optional <see cref="LastName"/>.
/// Candidates from before the split keep their whole name as the first name, with no last name: nobody's name is split
/// by guesswork. <see cref="FullName"/> is derived from the two and stored, so everything that reads it keeps working.
/// </remarks>
public sealed class Candidate : ITenantOwned
{
    /// <summary>The most a first or a last name may hold from a form or the API.</summary>
    public const int MaxNamePartLength = 100;

    /// <summary>
    /// The most <see cref="FirstName"/> holds: a name from before the split, kept whole, or the API's single
    /// <c>fullName</c>.
    /// </summary>
    public const int MaxFirstNameLength = 200;

    /// <summary>The most <see cref="FullName"/> holds: a first name, a space and a last name.</summary>
    public const int MaxFullNameLength = MaxFirstNameLength + 1 + MaxNamePartLength;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public string FirstName { get; private set; } = "";

    /// <summary>Null when the candidate gave none, and for every candidate from before the name was split.</summary>
    public string? LastName { get; private set; }

    /// <summary>The first name, then the last name when there is one. Derived; set only through <see cref="Create"/>.</summary>
    public string FullName { get; private set; } = "";

    public required string Email { get; init; }

    /// <summary>The lookup key: <see cref="Email"/> trimmed and lower-cased.</summary>
    public string NormalizedEmail { get; private init; } = "";

    public string? Phone { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the careers site last asked to email this candidate a link for a data request. The cooldown reads it, so a
    /// stranger typing the address again and again cannot flood the inbox. The decision to send is an atomic
    /// <c>ExecuteUpdate</c> on this column (two requests at once must not both send), so <see cref="DataRequestLinkDue"/>
    /// is for display and tests, not for deciding.
    /// </summary>
    public DateTimeOffset? DataRequestLinkSentAt { get; private set; }

    /// <summary>Whether no link went to this candidate within <paramref name="cooldown"/> of <paramref name="now"/>.</summary>
    public bool DataRequestLinkDue(DateTimeOffset now, TimeSpan cooldown) =>
        DataRequestLinkSentAt is not { } sent || now - sent >= cooldown;

    /// <summary>Records that a link went at <paramref name="now"/>, on a candidate this context holds (seeding and tests; the real claim is atomic in SQL).</summary>
    public void MarkDataRequestLinkSent(DateTimeOffset now) => DataRequestLinkSentAt = now;

    /// <summary>
    /// When the careers site last asked to email this candidate a link to their page (docs/architecture/candidate-portal.md).
    /// The cooldown reads it, as <see cref="DataRequestLinkSentAt"/> does for data requests, and it is its own column so a
    /// data request link never holds back a portal link. The decision to send is an atomic <c>ExecuteUpdate</c> on it.
    /// </summary>
    public DateTimeOffset? PortalLinkSentAt { get; private set; }

    /// <summary>Records that a portal link went at <paramref name="now"/>, on a candidate this context holds (seeding and tests; the real claim is atomic in SQL).</summary>
    public void MarkPortalLinkSent(DateTimeOffset now) => PortalLinkSentAt = now;

    /// <summary>
    /// Whether the email provider reported this address as undeliverable or as a spam complaint. ScreenHire stops
    /// emailing a flagged address and says so on the candidate. Per organization, like the candidate. It is a
    /// delivery fact only: it never feeds a verification level, a score or a rejection (ADR-0004).
    /// </summary>
    public CandidateEmailIssue EmailIssue { get; private set; } = CandidateEmailIssue.None;

    public DateTimeOffset? EmailIssueAt { get; private set; }

    /// <summary>The provider's short reason for a bounce ("550 mailbox not found"). Never message content.</summary>
    public string? EmailIssueReason { get; private set; }

    /// <summary>The longest provider reason kept.</summary>
    public const int MaxEmailIssueReasonLength = 300;

    /// <summary>False while <see cref="EmailIssue"/> is set: nothing is sent to the address.</summary>
    public bool CanReceiveEmail => EmailIssue == CandidateEmailIssue.None;

    /// <summary>
    /// Flags the address. A complaint is never downgraded to a bounce, and a repeat report changes nothing. Returns true
    /// when the flag changed.
    /// </summary>
    public bool FlagEmail(CandidateEmailIssue issue, string? reason, DateTimeOffset now)
    {
        if (issue == CandidateEmailIssue.None)
        {
            throw new ArgumentException("Use ClearEmailIssue to remove a flag.", nameof(issue));
        }

        if (EmailIssue == issue || EmailIssue == CandidateEmailIssue.SpamComplaint)
        {
            return false;
        }

        EmailIssue = issue;
        EmailIssueAt = now;
        EmailIssueReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim()[..Math.Min(reason.Trim().Length, MaxEmailIssueReasonLength)];
        return true;
    }

    /// <summary>Removes the flag, because the candidate wrote back from the address or a person says it works. Returns true when there was one.</summary>
    public bool ClearEmailIssue()
    {
        if (EmailIssue == CandidateEmailIssue.None)
        {
            return false;
        }

        EmailIssue = CandidateEmailIssue.None;
        EmailIssueAt = null;
        EmailIssueReason = null;
        return true;
    }

    public static Candidate Create(Guid organizationId, string firstName, string? lastName, string email, string? phone, DateTimeOffset now)
    {
        var first = firstName.Trim();
        var last = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
        return new()
        {
            OrganizationId = organizationId,
            FirstName = first,
            LastName = last,
            FullName = JoinName(first, last),
            Email = email.Trim(),
            NormalizedEmail = Normalize(email),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedAt = now,
        };
    }

    /// <summary>"Casey Morgan", or "Casey" with no last name.</summary>
    public static string JoinName(string firstName, string? lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {lastName}";

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Why ScreenHire no longer emails a candidate's address (reported by the email provider).</summary>
public enum CandidateEmailIssue
{
    None,

    /// <summary>The address does not accept mail: a hard bounce, or a send the provider dropped because it bounced before.</summary>
    Bounced,

    /// <summary>The candidate marked an email as spam. Writing again would be unwelcome as well as undeliverable.</summary>
    SpamComplaint,
}
