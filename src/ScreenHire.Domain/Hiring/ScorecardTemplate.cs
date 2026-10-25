using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Hiring;

/// <summary>
/// One of an organization's own scorecards in its library, next to the built-ins (<see cref="BuiltInScorecards"/>).
/// A hiring template's stage names it by <see cref="Key"/>; a job copies its questions when it takes the workflow, so an
/// edit here reaches jobs created afterwards and never a job that has its copy (workflow-engine.md section 8).
/// </summary>
public sealed class ScorecardTemplate : ITenantOwned
{
    public const int MaxNameLength = 80;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Name { get; set; }

    /// <summary>The questions, in order. Every scorecard then asks for an overall recommendation and a note.</summary>
    public required IReadOnlyList<ScorecardQuestion> Questions { get; set; }

    /// <summary>The library scorecard this one was copied from, a built-in's id or another of the organization's own.</summary>
    public string? CopiedFrom { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The id stages name it by: its id in the "D" format.</summary>
    public string Key => Id.ToString("D");

    public static ScorecardTemplate Create(
        Guid organizationId, string name, IReadOnlyList<ScorecardQuestion> questions, DateTimeOffset now, string? copiedFrom = null) =>
        new() { OrganizationId = organizationId, Name = name, Questions = questions, CopiedFrom = copiedFrom, CreatedAt = now, UpdatedAt = now };

    public void Update(string name, IReadOnlyList<ScorecardQuestion> questions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(questions);
        Name = name;
        Questions = questions;
        UpdatedAt = now;
    }

    /// <summary>A job's copy of this scorecard.</summary>
    public Scorecard ToScorecard() => new(Name, Questions);
}
