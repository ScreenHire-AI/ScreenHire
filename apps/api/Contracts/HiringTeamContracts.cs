using FluentValidation;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A person on a job's hiring team, or on a hiring template's default one (ADR-0015). <c>memberId</c> is their
/// membership's id, which the team stores. <c>seesAllJobs</c> says whether they see every job anyway; a member on Only
/// jobs they're on sees this job through this list.
/// </summary>
public sealed record HiringTeamMember(Guid MemberId, Guid UserId, string Email, string DisplayName, string RoleName, bool SeesAllJobs);

/// <summary>
/// Sets a hiring team to <c>memberIds</c>, the organization's member ids. For a job, <c>copyFromJobId</c> also brings
/// over another job's team, which the caller must be able to see.
/// </summary>
public sealed record HiringTeamRequest(IReadOnlyList<Guid>? MemberIds, Guid? CopyFromJobId = null);

internal sealed class HiringTeamRequestValidator : AbstractValidator<HiringTeamRequest>
{
    /// <summary>More than any team needs, and few enough to check in one query.</summary>
    public const int MaxMembers = 200;

    public HiringTeamRequestValidator()
    {
        RuleFor(r => r.MemberIds).NotNull().WithMessage("Say who is on the team.");
        RuleFor(r => r.MemberIds).Must(ids => ids is null || ids.Count <= MaxMembers).WithMessage($"At most {MaxMembers} people.");
    }
}
