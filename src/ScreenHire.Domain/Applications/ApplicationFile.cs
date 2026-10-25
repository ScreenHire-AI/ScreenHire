using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// A file a candidate uploaded with an application, such as their resume: the answer to a file question. The bytes
/// live in blob storage under <see cref="BlobName"/>; this row is what the application knows about them.
/// </summary>
public sealed class ApplicationFile : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The file question this file answers (<see cref="ApplicationForm.ResumeQuestionId"/> for the resume).</summary>
    public required string QuestionId { get; init; }

    /// <summary>The upload id the candidate's answer carries.</summary>
    public Guid UploadId { get; init; }

    /// <summary>Where the bytes are, within the applicant uploads container.</summary>
    public required string BlobName { get; init; }

    /// <summary>The file's name as the candidate's device had it, made safe for storage and display.</summary>
    public required string FileName { get; init; }

    /// <summary>The media type for the file's extension, whose content was checked against it.</summary>
    public required string ContentType { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the content: the key for duplicate-file checks within the organization.</summary>
    public required string Sha256 { get; init; }

    public MalwareScanStatus ScanStatus { get; private set; } = MalwareScanStatus.Pending;

    public DateTimeOffset? ScannedAt { get; private set; }

    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>
    /// Records a scan's result. A file is scanned once: a second result for a scanned file changes nothing and returns
    /// false, so a redelivered scan message is harmless.
    /// </summary>
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

public enum MalwareScanStatus
{
    /// <summary>Uploaded; no scan result yet.</summary>
    Pending,

    Clean,

    /// <summary>The scanner found malware. Reviewers are not offered the file.</summary>
    Infected,

    /// <summary>No scanner is configured, so the file was not scanned.</summary>
    NotScanned,
}
