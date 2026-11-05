using FluentValidation;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// Who asked, while they are still in the organization's records: <c>id</c>, <c>name</c> and <c>email</c>. A request from
/// someone since erased has no candidate (<c>null</c>), and nothing about them is kept.
/// </summary>
public sealed record DataRequestCandidate(Guid Id, string Name, string Email);

/// <summary>
/// One candidate's request for a copy of their data or for it to be erased. <c>dueAt</c> is the team's target for an answer
/// and <c>overdue</c> says it has passed while the request is still open; neither is a promise to the candidate.
/// <c>copySentAt</c> is when the download link for a copy went out. <c>declineReason</c> is what the team told the candidate;
/// it is cleared if the candidate is erased.
/// </summary>
public sealed record DataRequestEntry(
    Guid Id,
    DataRequestKind Kind,
    DataRequestStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DueAt,
    bool Overdue,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? CopySentAt,
    string? DeclineReason,
    DataRequestCandidate? Candidate);

/// <summary>
/// The organization's data requests, earliest due date first: every open one, and those answered in the last 90 days.
/// <c>openCount</c> is how many are open.
/// </summary>
public sealed record DataRequestList(IReadOnlyList<DataRequestEntry> Items, int OpenCount);

/// <summary>
/// Erases the candidate behind an erasure request. <c>confirmation</c> is the candidate's full name as the person typed it,
/// compared ignoring case and surrounding spaces.
/// </summary>
public sealed record EraseDataRequestRequest(string Confirmation);

/// <summary>Declines a request. <c>reason</c> (1 to 500 characters) is emailed to the candidate, so it is written for them.</summary>
public sealed record DeclineDataRequestRequest(string Reason);

internal sealed class DeclineDataRequestRequestValidator : AbstractValidator<DeclineDataRequestRequest>
{
    public DeclineDataRequestRequestValidator() =>
        RuleFor(r => r.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Say why, so the candidate can read it.")
            .Must(reason => reason is null || reason.Trim().Length <= DataRequest.MaxDeclineReasonLength)
            .WithMessage($"A reason is at most {DataRequest.MaxDeclineReasonLength} characters.");
}

/// <summary>
/// The organization's record retention. <c>months</c> is how long after it ended an application stays before it is deleted
/// (1 to 120), or <c>null</c> for never, the default. <c>lastUpdated</c> is the last change ("by ScreenHire" while it is
/// the default); its history is <c>/v1/organization/data-retention/history</c>. <c>lastRun</c> is the newest run that deleted
/// something, or <c>null</c> when none has.
/// </summary>
public sealed record OrganizationRetention(int? Months, LastUpdate LastUpdated, RetentionLastRun? LastRun);

/// <summary>When a retention run last deleted applications, and how many (the latest batch, when a run took several).</summary>
public sealed record RetentionLastRun(DateTimeOffset At, int ApplicationsRemoved);

/// <summary>Sets the period (1 to 120 months), or turns retention off with <c>null</c>.</summary>
public sealed record UpdateRetentionRequest(int? Months);

internal sealed class UpdateRetentionRequestValidator : AbstractValidator<UpdateRetentionRequest>
{
    public UpdateRetentionRequestValidator() =>
        RuleFor(r => r.Months)
            .InclusiveBetween(RetentionSettings.MinMonths, RetentionSettings.MaxMonths)
            .When(r => r.Months is not null)
            .WithMessage($"Choose from {RetentionSettings.MinMonths} to {RetentionSettings.MaxMonths} months, or turn deleting off.");
}

/// <summary>
/// What saving a period would delete right now: <c>applications</c> that ended before it, belonging to <c>candidates</c>
/// different people. Saved candidates are never counted.
/// </summary>
public sealed record RetentionPreview(int Applications, int Candidates);
