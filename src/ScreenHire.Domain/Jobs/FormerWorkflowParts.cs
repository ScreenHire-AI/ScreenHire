using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Jobs;

/// <summary>
/// The stages and apply-form questions a job's workflow no longer has, kept so what applications recorded under them still
/// reads in words: a timeline entry names a stage by id, and an answer names its question by id. A workflow change that
/// drops a stage or a question adds it here (<see cref="Job.ReviseWorkflow"/>); a current stage or question with the same
/// id wins over a former one wherever names are looked up.
/// </summary>
/// <remarks>
/// <c>stages</c>: Each stage's id, name and kind as it last was. <c>questions</c>: Each question as the apply form last asked
/// it, with its options, so a choice's label still reads.
/// </remarks>
public sealed record FormerWorkflowParts(IReadOnlyList<FormerStage> Stages, IReadOnlyList<Question> Questions)
{
    public static FormerWorkflowParts None { get; } = new([], []);

    /// <summary>A stage's name: <paramref name="current"/>'s, else a former stage's, else null.</summary>
    public string? StageName(HiringWorkflow current, string? stageId)
    {
        ArgumentNullException.ThrowIfNull(current);
        return stageId is null ? null : current.FindStage(stageId)?.Name ?? Stages.FirstOrDefault(s => s.Id == stageId)?.Name;
    }

    /// <summary>These parts, plus what <paramref name="before"/> had that <paramref name="after"/> drops; a part dropped again replaces its older copy.</summary>
    public FormerWorkflowParts Keep(HiringWorkflow before, HiringWorkflow after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var keptStages = after.Stages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var droppedStages = before.Stages.Where(s => !keptStages.Contains(s.Id)).Select(s => new FormerStage(s.Id, s.Name, s.Kind)).ToList();
        var keptQuestions = after.Questionnaire.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var droppedQuestions = before.Questionnaire.Questions.Where(q => !keptQuestions.Contains(q.Id)).ToList();
        if (droppedStages.Count == 0 && droppedQuestions.Count == 0)
        {
            return this;
        }

        var newStageIds = droppedStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var newQuestionIds = droppedQuestions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        return new(
            [.. Stages.Where(s => !newStageIds.Contains(s.Id)), .. droppedStages],
            [.. Questions.Where(q => !newQuestionIds.Contains(q.Id)), .. droppedQuestions]);
    }
}

/// <summary>A stage a job's workflow had: its id, its name and its kind as it last was.</summary>
public sealed record FormerStage(string Id, string Name, StageKind Kind);
