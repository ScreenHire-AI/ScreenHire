using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Application.Portal;

/// <summary>How an application ended, in the words a candidate sees. The employer's own stage names never reach the page.</summary>
public enum FunnelEnd
{
    /// <summary>Still open.</summary>
    None,

    Hired,

    /// <summary>Rejected, or kept by the team for future roles. A saved candidate reads exactly as a rejected one and the page says nothing about a list.</summary>
    NotMovingForward,

    Withdrawn,
}

/// <summary>
/// Where an application stands on the four fixed steps every candidate sees, whatever the employer's workflow is
/// (docs/architecture/candidate-portal.md). <see cref="Current"/> is the index into <see cref="Steps"/>, or null once the
/// application has ended and <see cref="Ended"/> says how.
/// </summary>
/// <remarks>
/// Only the stage's kind is read. The page never learns a stage's name, how many stages the job has, a rating, a
/// scorecard, a note or anything about verification (CLAUDE.md non-negotiables 1 to 4): "In review" is what a screening, an
/// assessment, a verification step and a stage the employer made up all look like.
/// </remarks>
public sealed record CandidateFunnel(int? Current, FunnelEnd Ended)
{
    /// <summary>The four steps, in order.</summary>
    public static readonly IReadOnlyList<string> Steps = ["Applied", "In review", "Interview", "Decision"];

    /// <summary>
    /// The funnel for <paramref name="application"/> in <paramref name="stage"/>, the job's stage with the application's
    /// current id (null when the employer removed it since).
    /// </summary>
    public static CandidateFunnel For(JobApplication application, WorkflowStage? stage)
    {
        ArgumentNullException.ThrowIfNull(application);

        // The stage decides first: a Withdrawn application that was once kept in mind is still Withdrawn.
        switch (stage?.Kind)
        {
            case StageKind.Hired:
                return new(null, FunnelEnd.Hired);
            case StageKind.Withdrawn:
                return new(null, FunnelEnd.Withdrawn);
            case StageKind.Rejected:
                return new(null, FunnelEnd.NotMovingForward);
        }

        if (application.KeptInMindAt is not null)
        {
            return new(null, FunnelEnd.NotMovingForward);
        }

        return new(
            stage?.Kind switch
            {
                StageKind.Applied => 0,
                StageKind.Interview => 2,
                StageKind.Offer => 3,
                _ => 1, // Screening, Assessment, Verification, Custom, and a stage that no longer exists
            },
            FunnelEnd.None);
    }
}
