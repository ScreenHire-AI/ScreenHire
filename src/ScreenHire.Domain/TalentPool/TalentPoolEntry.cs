using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.TalentPool;

/// <summary>
/// A candidate in the organization's talent pool: the team kept them in mind from one application, which they left
/// without a rejection (docs/architecture/talent-pool.md). One entry per candidate. It belongs to that application, so a
/// member on Only jobs they're on sees it only through a job they are on (ADR-0015), and it goes with the application or
/// the candidate when either is deleted.
/// </summary>
/// <remarks>
/// The entry holds no candidate data of its own. Why the team kept them is the note on the <see cref="KeptEventId"/>
/// timeline entry; their tags are the application's; when their permission runs out comes from the application's
/// consent. Leaving the pool deletes the entry and records why on the application's Activity.
/// </remarks>
public sealed class TalentPoolEntry : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid CandidateId { get; init; }

    /// <summary>The application the team kept the candidate in mind from.</summary>
    public Guid ApplicationId { get; init; }

    /// <summary>The application's job, for listing the pool by where people were kept from.</summary>
    public Guid JobId { get; init; }

    /// <summary>The <see cref="ApplicationEventType.KeptInMind"/> entry, which holds the note saying why.</summary>
    public Guid KeptEventId { get; init; }

    public DateTimeOffset KeptAt { get; init; }

    /// <summary>The member who kept them in mind.</summary>
    public Guid? KeptByUserId { get; init; }

    /// <summary>The job the candidate was last invited to apply for, if any.</summary>
    public Guid? LastInvitedJobId { get; private set; }

    public DateTimeOffset? LastInvitedAt { get; private set; }

    /// <summary>The entry for <paramref name="application"/>, kept in mind by <paramref name="kept"/>.</summary>
    public static TalentPoolEntry For(JobApplication application, ApplicationEvent kept)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(kept);
        if (kept.ApplicationId != application.Id || kept.Type != ApplicationEventType.KeptInMind || application.KeptInMindAt is null)
        {
            throw new InvalidOperationException("The entry needs the application's own Kept in mind entry.");
        }

        return new TalentPoolEntry
        {
            OrganizationId = application.OrganizationId,
            CandidateId = application.CandidateId,
            ApplicationId = application.Id,
            JobId = application.JobId,
            KeptEventId = kept.Id,
            KeptAt = kept.OccurredAt,
            KeptByUserId = kept.ActorUserId,
        };
    }

    /// <summary>Records an invitation to apply for <paramref name="jobId"/>. An older one than the last changes nothing.</summary>
    public void Invited(Guid jobId, DateTimeOffset at)
    {
        if (LastInvitedAt is { } last && last > at)
        {
            return;
        }

        LastInvitedJobId = jobId;
        LastInvitedAt = at;
    }
}
