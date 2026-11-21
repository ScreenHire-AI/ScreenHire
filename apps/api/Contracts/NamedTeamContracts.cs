using FluentValidation;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A named team in Settings, Team, Teams: its name and its people who are still members, each with their role, as a job's
/// hiring team lists them. Adding it to a job copies these people.
/// </summary>
public sealed record TeamDetail(Guid Id, string Name, IReadOnlyList<HiringTeamMember> Members, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

/// <summary>Makes a named team, or renames one and sets its people to <c>memberIds</c>, the organization's member ids.</summary>
public sealed record TeamRequest(string? Name, IReadOnlyList<Guid>? MemberIds);

/// <summary>
/// The team a new job from a template is likely to want: the one the newest job from the same template, or with the same
/// department, started with, among the jobs the reader sees. Null when there is none.
/// </summary>
public sealed record TeamSuggestion(Guid? TeamId);

internal sealed class TeamRequestValidator : AbstractValidator<TeamRequest>
{
    public TeamRequestValidator()
    {
        RuleFor(r => r.Name).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Give the team a name.");
        RuleFor(r => r.Name).Must(name => name is null || name.Trim().Length <= NamedTeam.MaxNameLength)
            .WithMessage($"Keep the name to {NamedTeam.MaxNameLength} characters or fewer.");
        RuleFor(r => r.Name).Must(name => name is null || !name.Contains('\n', StringComparison.Ordinal)).WithMessage("Keep the name on one line.");
        RuleFor(r => r.MemberIds).NotNull().WithMessage("Say who is on the team.");
        RuleFor(r => r.MemberIds).Must(ids => ids is null || ids.Count <= HiringTeamRequestValidator.MaxMembers)
            .WithMessage($"At most {HiringTeamRequestValidator.MaxMembers} people.");
    }
}
