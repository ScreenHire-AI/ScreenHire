namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A named group of the organization's people, such as Engineering or Kitchen, kept in Settings, Team, Teams. Adding a team
/// to a job copies its people into the job's hiring team (ADR-0015); the job keeps that copy, so a later change here
/// changes no job. A team by itself gives nobody access to anything.
/// </summary>
public sealed class NamedTeam : ITenantOwned
{
    /// <summary>Long enough for a converted hiring template's name, at most 120 characters, plus " team" and a number.</summary>
    public const int MaxNameLength = 140;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public string Name { get; private set; } = "";

    /// <summary><see cref="Name"/> trimmed and upper-cased: names are unique in an organization, ignoring case.</summary>
    public string NormalizedName { get; private set; } = "";

    /// <summary>
    /// The people on the team, as organization member ids (<see cref="OrganizationMember.Id"/>), as a job's hiring team
    /// holds them. Someone who leaves the organization stays in the list until the next save and is never copied to a job.
    /// </summary>
    public List<Guid> MemberIds { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>A new team. The caller has checked the name and that every id is a current member.</summary>
    public static NamedTeam Create(Guid organizationId, string name, IEnumerable<Guid> memberIds, DateTimeOffset now)
    {
        var team = new NamedTeam { OrganizationId = organizationId, CreatedAt = now };
        team.Name = name.Trim();
        team.NormalizedName = Normalize(name);
        team.MemberIds = [.. memberIds.Distinct()];
        return team;
    }

    /// <summary>Renames the team and replaces its people. The caller has checked the name and that every id is a current member.</summary>
    public void Update(string name, IEnumerable<Guid> memberIds, DateTimeOffset now)
    {
        var trimmed = name.Trim();
        var people = memberIds.Distinct().ToList();
        if (Name == trimmed && people.SequenceEqual(MemberIds))
        {
            return;
        }

        Name = trimmed;
        NormalizedName = Normalize(trimmed);
        MemberIds = people;
        UpdatedAt = now;
    }

    /// <summary>The form names are compared in: trimmed and upper-cased.</summary>
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
