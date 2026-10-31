using FluentValidation;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's talent pool (docs/architecture/talent-pool.md): the candidates the team kept in mind, whose
/// permission for future roles still holds. <c>permissionMonths</c> is the organization's period now.
/// </summary>
public sealed record TalentPoolList(int PermissionMonths, IReadOnlyList<TalentPoolMember> People);

/// <summary>One candidate in the talent pool.</summary>
/// <remarks>
/// <c>applicationId</c>, <c>jobId</c>, <c>jobTitle</c>: the application they were kept in mind from, which keeps
/// everything about them (answers, files, scorecards, notes, Verification results and Activity). <c>note</c>: why the team
/// kept them, from that application's Kept in mind entry; null when nobody wrote one. <c>tags</c>: that application's
/// tags. <c>keptByUserId</c>: who kept them. <c>permissionEndsAt</c>: when their permission runs out and they leave the
/// pool. <c>lastInvitedJobId</c>, <c>lastInvitedJobTitle</c>, <c>lastInvitedAt</c>: their last invitation to apply.
/// <c>appliedJobIds</c>: every job the candidate has an application for, as the reader sees them, so an invitation or an
/// add skips them. No Verification output: keeping and inviting never read it (ADR-0004).
/// </remarks>
public sealed record TalentPoolMember(
    Guid CandidateId,
    string CandidateName,
    string Email,
    Guid ApplicationId,
    Guid JobId,
    string JobTitle,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset KeptAt,
    Guid? KeptByUserId,
    DateTimeOffset PermissionEndsAt,
    Guid? LastInvitedJobId,
    string? LastInvitedJobTitle,
    DateTimeOffset? LastInvitedAt,
    IReadOnlyList<Guid> AppliedJobIds);

/// <summary>
/// Keeps the candidates of these applications in mind: each leaves its job without a rejection and joins the talent pool.
/// <c>note</c> says why, once for all of them. Only an open application whose candidate gave permission for future roles
/// on its form can be kept.
/// </summary>
public sealed record KeepInMindRequest(IReadOnlyList<Guid> ApplicationIds, string? Note = null);

/// <summary>
/// What happened: <c>kept</c> joined the pool; <c>notAllowed</c> have no permission for future roles (they never gave it,
/// it ran out or they took it back); <c>alreadyEnded</c> were hired, rejected, withdrawn or kept in mind already.
/// </summary>
public sealed record KeptInMind(int Kept, int NotAllowed, int AlreadyEnded);

/// <summary>
/// Takes the candidate out of the talent pool. <c>candidateAsked</c>: they asked to be removed, so their permission for
/// future roles is withdrawn too, and they cannot be kept again until they give it on a new application.
/// </summary>
public sealed record RemoveFromTalentPoolRequest(bool CandidateAsked = false);

/// <summary>Invites these candidates from the talent pool to apply for <c>jobId</c>, an open job.</summary>
public sealed record InviteToApplyRequest(IReadOnlyList<Guid> CandidateIds, Guid JobId);

/// <summary>The invitation as <c>candidateId</c> would receive it, for <c>jobId</c>.</summary>
public sealed record InvitationPreviewRequest(Guid CandidateId, Guid JobId);

/// <summary>
/// How many invitations were queued, and how many candidates were skipped: no longer in the pool, or already applied to
/// the job. Each sent invitation appears in Messages and in the Activity of the application they were kept from.
/// </summary>
public sealed record InvitationsQueued(int Queued, int Skipped);

/// <summary>Adds these candidates from the talent pool to <c>jobId</c>, which must not be closed.</summary>
public sealed record AddFromTalentPoolRequest(IReadOnlyList<Guid> CandidateIds, Guid JobId);

/// <summary>
/// The new applications, one per candidate added, and how many were skipped because they had already applied to the
/// job. Each new application carries the candidate's permission for future roles from the one they were kept from.
/// </summary>
public sealed record AddedFromTalentPool(IReadOnlyList<Guid> ApplicationIds, int Skipped);

/// <summary>
/// The page a candidate opens from the link in an invitation to stop being kept. <c>kept</c>: the organization still
/// keeps their details for future roles; false once they stopped it, or it ran out.
/// </summary>
public sealed record PublicTalentPoolStatus(PublicOrganization Organization, bool Kept);

internal sealed class KeepInMindRequestValidator : AbstractValidator<KeepInMindRequest>
{
    /// <summary>A selection a person makes on one screen, as for email.</summary>
    public const int MaxApplications = 200;

    public KeepInMindRequestValidator()
    {
        RuleFor(r => r.ApplicationIds).NotEmpty().WithMessage("Choose at least one candidate.")
            .Must(ids => ids is null || ids.Count <= MaxApplications).WithMessage($"Keep at most {MaxApplications} candidates in mind at once.");
        RuleFor(r => r.Note).MaximumLength(JobApplication.MaxNoteLength);
    }
}

internal sealed class InviteToApplyRequestValidator : AbstractValidator<InviteToApplyRequest>
{
    public InviteToApplyRequestValidator()
    {
        RuleFor(r => r.CandidateIds).NotEmpty().WithMessage("Choose at least one candidate.")
            .Must(ids => ids is null || ids.Count <= KeepInMindRequestValidator.MaxApplications)
            .WithMessage($"Invite at most {KeepInMindRequestValidator.MaxApplications} candidates at once.");
        RuleFor(r => r.JobId).NotEmpty().WithMessage("Choose a job.");
    }
}

internal sealed class AddFromTalentPoolRequestValidator : AbstractValidator<AddFromTalentPoolRequest>
{
    public AddFromTalentPoolRequestValidator()
    {
        RuleFor(r => r.CandidateIds).NotEmpty().WithMessage("Choose at least one candidate.")
            .Must(ids => ids is null || ids.Count <= KeepInMindRequestValidator.MaxApplications)
            .WithMessage($"Add at most {KeepInMindRequestValidator.MaxApplications} candidates at once.");
        RuleFor(r => r.JobId).NotEmpty().WithMessage("Choose a job.");
    }
}
