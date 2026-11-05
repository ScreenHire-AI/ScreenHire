using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Privacy;

/// <summary>
/// A candidate's request for a copy of their data, or for it to be erased (docs/architecture/data-requests.md). The row
/// holds no name and no address: the team's list joins to the candidate while the candidate exists, and the foreign key
/// sets <see cref="CandidateId"/> to null when they are erased, so the row outlives the erasure with no personal data.
/// </summary>
/// <remarks>
/// The domain methods take the time as a parameter (no <c>TimeProvider</c> in this project) and change only this row.
/// Nothing here decides anything for a person: a request waits until a team member sends the copy, erases or declines.
/// </remarks>
public sealed class DataRequest : ITenantOwned
{
    /// <summary>The longest reason a team member may give when declining; it is emailed to the candidate.</summary>
    public const int MaxDeclineReasonLength = 500;

    /// <summary>The longest export blob name kept.</summary>
    public const int MaxBlobNameLength = 300;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The person who asked. Null once they have been erased.</summary>
    public Guid? CandidateId { get; private set; }

    public DataRequestKind Kind { get; init; }

    public DataRequestStatus Status { get; private set; } = DataRequestStatus.Open;

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>The team's target for an answer: <see cref="ReceivedAt"/> plus the configured number of days. A target on the dashboard, not a promise to the candidate.</summary>
    public DateTimeOffset DueAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>The member who completed or declined it; null when the system closed it (an erasure, or the retention schedule).</summary>
    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>Why the team declined, as emailed to the candidate. Cleared when the candidate is erased.</summary>
    public string? DeclineReason { get; private set; }

    /// <summary>The blob holding the copy of the candidate's data, once it was built. Cleared when it expires or the candidate is erased.</summary>
    public string? ExportBlobName { get; private set; }

    public DateTimeOffset? ExportExpiresAt { get; private set; }

    /// <summary>When the email with the download link went. Set once, so a redelivered message sends nothing twice.</summary>
    public DateTimeOffset? CopySentAt { get; private set; }

    /// <summary>A new request waiting for the team. <paramref name="dueIn"/> is the configured answer target.</summary>
    public static DataRequest Open(Guid organizationId, Guid candidateId, DataRequestKind kind, DateTimeOffset now, TimeSpan dueIn)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a request kind.");
        }

        if (dueIn < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueIn), dueIn, "The answer target cannot be in the past.");
        }

        return new DataRequest
        {
            OrganizationId = organizationId,
            CandidateId = candidateId,
            Kind = kind,
            Status = DataRequestStatus.Open,
            ReceivedAt = now,
            DueAt = now + dueIn,
        };
    }

    /// <summary>Whether the team's target has passed while the request still waits.</summary>
    public bool IsOverdue(DateTimeOffset now) => Status == DataRequestStatus.Open && now > DueAt;

    /// <summary>
    /// The team turned the request down, with <paramref name="reason"/> (trimmed, 1 to <see cref="MaxDeclineReasonLength"/>
    /// characters) for the candidate. Only an open request can be declined.
    /// </summary>
    public void Decline(string reason, Guid userId, DateTimeOffset now)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(reason);
        var trimmed = reason.Trim();
        if (trimmed.Length is 0 or > MaxDeclineReasonLength)
        {
            throw new ArgumentException($"A reason is 1 to {MaxDeclineReasonLength} characters.", nameof(reason));
        }

        Status = DataRequestStatus.Declined;
        DeclineReason = trimmed;
        ResolvedAt = now;
        ResolvedByUserId = userId;
    }

    /// <summary>The request was answered: the copy was sent, or the candidate was erased. Only an open request can be completed.</summary>
    public void Complete(Guid? userId, DateTimeOffset now)
    {
        EnsureOpen();
        Status = DataRequestStatus.Completed;
        ResolvedAt = now;
        ResolvedByUserId = userId;
    }

    /// <summary>
    /// The copy was built and its download link emailed: records where it is and until when it can be downloaded, and
    /// completes the request. A repeat (a redelivered message) changes nothing and returns false.
    /// </summary>
    public bool CopySent(string blobName, DateTimeOffset expiresAt, Guid? userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        if (Kind != DataRequestKind.Copy)
        {
            throw new InvalidOperationException("Only a request for a copy has a copy to send.");
        }

        if (CopySentAt is not null)
        {
            return false;
        }

        if (Status == DataRequestStatus.Open)
        {
            Complete(userId, now);
        }

        ExportBlobName = blobName;
        ExportExpiresAt = expiresAt;
        CopySentAt = now;
        return true;
    }

    /// <summary>The copy's blob is gone (it expired, or its candidate was erased): forgets where it was.</summary>
    public void ClearExport()
    {
        ExportBlobName = null;
        ExportExpiresAt = null;
    }

    /// <summary>
    /// The candidate was erased. Forgets the copy and any decline reason, which may name them, and closes the request when
    /// it was still open: erasing answers it. <paramref name="userId"/> is the member who erased, null for the retention
    /// schedule. Returns the blob that held the copy, if any, so the caller can have it deleted.
    /// </summary>
    public string? ForgetCandidate(Guid? userId, DateTimeOffset now)
    {
        var blob = ExportBlobName;
        ClearExport();
        DeclineReason = null;
        if (Status == DataRequestStatus.Open)
        {
            Complete(userId, now);
        }

        return blob;
    }

    private void EnsureOpen()
    {
        if (Status != DataRequestStatus.Open)
        {
            throw new InvalidOperationException($"The request is already {Status}.");
        }
    }
}

public enum DataRequestKind
{
    /// <summary>The candidate asked for a copy of the data held about them.</summary>
    Copy,

    /// <summary>The candidate asked for their data to be erased.</summary>
    Erasure,
}

public enum DataRequestStatus
{
    /// <summary>Waiting for the team.</summary>
    Open,

    /// <summary>Answered: the copy was sent, or the candidate was erased.</summary>
    Completed,

    /// <summary>The team turned it down and told the candidate why.</summary>
    Declined,
}
