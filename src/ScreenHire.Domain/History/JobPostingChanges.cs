using ScreenHire.Domain.Distribution;

namespace ScreenHire.Domain.History;

/// <summary>
/// A job's posting to one board, in words, for the job's history: only the text that board has of its own (its title and
/// description). A posting changes state all day as boards answer; none of that makes a line, so a worker's save of a
/// posting records nothing.
/// </summary>
public static class JobPostingChanges
{
    /// <param name="before">The posting before the save; null when the save added it.</param>
    /// <param name="after">The posting after the save; null when the save deleted it.</param>
    public static IReadOnlyList<ChangeLine> Describe(JobPosting? before, JobPosting? after)
    {
        if (after is null)
        {
            return [];
        }

        var board = after.BoardLabel ?? after.BoardId;
        var lines = new List<ChangeLine>();
        if (before?.TitleOverride != after.TitleOverride)
        {
            var details = new ChangeDetail[] { new("Title", before?.TitleOverride, after.TitleOverride) };
            lines.Add(after.TitleOverride is null
                ? ChangeLine.Of("went back to the job's title for ", board, details: details)
                : ChangeLine.Of("customized the title for ", board, details: details));
        }

        if (before?.DescriptionOverride != after.DescriptionOverride)
        {
            // Bold, a heading or a list changes the HTML but not the words, and before and after would read the same.
            var was = before?.DescriptionOverride is { } html1 ? RichTextToPlain.Convert(html1) : null;
            var now = after.DescriptionOverride is { } html2 ? RichTextToPlain.Convert(html2) : null;
            if (was == now && now is not null)
            {
                lines.Add(ChangeLine.Of("changed the formatting of the description for ", board));
            }
            else
            {
                var details = new ChangeDetail[] { new("Description", was, now, ChangeValueKind.Paragraphs) };
                lines.Add(now is null
                    ? ChangeLine.Of("went back to the job's description for ", board, details: details)
                    : ChangeLine.Of("customized the description for ", board, details: details));
            }
        }

        return lines;
    }
}
