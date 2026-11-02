using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// A candidate's reply to an email ScreenHire sent them, as it arrived. It answers one send (<see cref="SendId"/>, an
/// <see cref="ApplicationEmail"/> id the reply address named), and the <see cref="ApplicationEventType.EmailReceived"/>
/// entry that records it has this row's id (<see cref="ApplicationEventData.EmailId"/>). Text only: the HTML part is
/// sanitized to the editor's elements before it is stored, and attachments are separate rows
/// (<see cref="InboundEmailAttachment"/>) whose bytes sit in blob storage after a malware scan.
/// </summary>
public sealed class InboundEmail : IApplicationScoped
{
    /// <summary>The most an inbound subject keeps (one header line).</summary>
    public const int MaxSubjectLength = ApplicationEmail.MaxSubjectLength;

    /// <summary>The most characters of a reply's body kept, per part. Longer replies are cut and say so.</summary>
    public const int MaxBodyLength = 100_000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The email this answers: the send id the reply address carried.</summary>
    public Guid SendId { get; init; }

    /// <summary>The message's own <c>Message-ID</c> header, when it had one: a redelivered post finds it and stores nothing twice.</summary>
    public string? MessageId { get; init; }

    public required string FromAddress { get; init; }

    public string? FromName { get; init; }

    public required string Subject { get; init; }

    public required string TextBody { get; init; }

    public required string HtmlBody { get; init; }

    /// <summary>An out-of-office or other automatic answer (<c>Auto-Submitted</c>): kept, but it notifies nobody.</summary>
    public bool AutoReply { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>When the person who sent the original was told. Null until then; set once, so a redelivered event emails nobody twice.</summary>
    public DateTimeOffset? NotifiedAt { get; private set; }

    /// <summary>True the first time it is called: the caller sends the notification only then.</summary>
    public bool MarkNotified(DateTimeOffset now)
    {
        if (NotifiedAt is not null)
        {
            return false;
        }

        NotifiedAt = now;
        return true;
    }
}

/// <summary>
/// A file attached to a candidate's reply. The bytes live in the email attachments container under
/// <see cref="BlobName"/>; the file is offered to reviewers only after <see cref="ScanStatus"/> says it is not infected,
/// as an applicant's upload is.
/// </summary>
public sealed class InboundEmailAttachment : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid InboundEmailId { get; init; }

    public required string BlobName { get; init; }

    /// <summary>The name the candidate's mail program gave it, made safe for storage and display.</summary>
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the content.</summary>
    public required string Sha256 { get; init; }

    public MalwareScanStatus ScanStatus { get; private set; } = MalwareScanStatus.Pending;

    public DateTimeOffset? ScannedAt { get; private set; }

    /// <summary>Records a scan's result once; a second result changes nothing and returns false.</summary>
    public bool RecordScan(MalwareScanStatus status, DateTimeOffset now)
    {
        if (status == MalwareScanStatus.Pending)
        {
            throw new ArgumentException("A scan result cannot be Pending.", nameof(status));
        }

        if (ScanStatus != MalwareScanStatus.Pending)
        {
            return false;
        }

        ScanStatus = status;
        ScannedAt = now;
        return true;
    }
}
