namespace ScreenHire.Domain.Hiring;

/// <summary>
/// Where a job's candidates go when it switches to another template: the stage of the new workflow each current stage
/// is preselected to (workflow-engine.md section 7, Tailoring a job). A person can change every choice before the switch.
/// </summary>
public static class StageMapping
{
    /// <summary>
    /// The stage of <paramref name="to"/> for <paramref name="stage"/>, one of <paramref name="from"/>'s stages: by kind
    /// first, then by name. Among the stages of the same kind, the one with the same name wins, else the one at the same
    /// place among that kind (the second interview goes to the second interview, or the last one there is). With no stage
    /// of that kind, a stage with the same name, else the first stage.
    /// </summary>
    public static WorkflowStage Suggest(HiringWorkflow from, WorkflowStage stage, HiringWorkflow to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(to);
        if (to.Stages.Count == 0)
        {
            throw new ArgumentException("The new workflow has no stages.", nameof(to));
        }

        var sameKind = to.Stages.Where(s => s.Kind == stage.Kind).ToList();
        if (sameKind.Count > 0)
        {
            if (sameKind.FirstOrDefault(s => SameName(s, stage)) is { } named)
            {
                return named;
            }

            var place = from.Stages.Where(s => s.Kind == stage.Kind).TakeWhile(s => s.Id != stage.Id).Count();
            return sameKind[Math.Min(place, sameKind.Count - 1)];
        }

        return to.Stages.FirstOrDefault(s => SameName(s, stage)) ?? to.Stages[0];
    }

    private static bool SameName(WorkflowStage left, WorkflowStage right) =>
        string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase);
}
