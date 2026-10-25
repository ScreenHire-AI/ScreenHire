using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.History;

/// <summary>
/// A library scorecard's changes in words: created, renamed, its questions changed. Questions are matched by their ids,
/// as a job's copy of a scorecard is (<see cref="DefinitionChanges"/>).
/// </summary>
public static class ScorecardTemplateChanges
{
    /// <param name="before">The row before the save; null when the save added it.</param>
    /// <param name="after">The row after the save; null when the save deleted it.</param>
    /// <param name="copiedFrom">The name of the scorecard a new one was copied from, if any.</param>
    public static IReadOnlyList<ChangeLine> Describe(ScorecardTemplate? before, ScorecardTemplate? after, string? copiedFrom = null)
    {
        if (after is null)
        {
            return before is null ? [] : [ChangeLine.Plain("deleted the scorecard")];
        }

        if (before is null)
        {
            return copiedFrom is null ? [ChangeLine.Plain("created the scorecard")] : [ChangeLine.Of("created the scorecard from ", copiedFrom)];
        }

        var lines = new List<ChangeLine>();
        if (before.Name != after.Name)
        {
            lines.Add(ChangeLine.Of($"renamed the scorecard from {before.Name} to ", after.Name));
        }

        if (DefinitionChanges.ScorecardQuestionDetails(before.Questions, after.Questions) is { Count: > 0 } details)
        {
            lines.Add(ChangeLine.Plain("changed the questions", details));
        }

        if (Archive.Line(before.ArchivedAt, after.ArchivedAt, "scorecard") is { } archive)
        {
            lines.Add(archive);
        }

        return lines;
    }
}
