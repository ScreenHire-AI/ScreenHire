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
    public static readonly IReadOnlyList<string> Steps = ["Applied", "In review", "Interview", "Offer"];

    /// <summary>
    /// The funnel for <paramref name="application"/> in <paramref name="stage"/>, the job's stage with the application's
    /// current id (null when the employer removed it since), in a workflow whose stages are <paramref name="stages"/>, in
    /// pipeline order.
    /// </summary>
    public static CandidateFunnel For(JobApplication application, WorkflowStage? stage, IReadOnlyList<WorkflowStage> stages)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(stages);

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

        return new(StepOf(stage, stages), FunnelEnd.None);
    }

    // Applied, Interview and Offer stages are their own steps. A decision is a move, not a place: the team moves an application
    // from its last interview to an offer or ends it. So any other open stage takes the step of the last Interview or Offer
    // stage before it in the pipeline: a take-home between two interviews stays Interview, a holding stage after the last
    // interview stays Interview until the team decides, and a check after the offer stays Offer. With neither before it, or
    // for a stage the employer has since removed, it is In review. The step never goes back unless the team moves it back.
    private static int StepOf(WorkflowStage? stage, IReadOnlyList<WorkflowStage> stages)
    {
        switch (stage?.Kind)
        {
            case null:
                return 1;
            case StageKind.Applied:
                return 0;
            case StageKind.Interview:
                return 2;
            case StageKind.Offer:
                return 3;
        }

        var step = 1;
        foreach (var earlier in stages.TakeWhile(s => s.Id != stage.Id))
        {
            step = earlier.Kind switch
            {
                StageKind.Interview => Math.Max(step, 2),
                StageKind.Offer => 3,
                _ => step,
            };
        }

        return step;
    }
}
