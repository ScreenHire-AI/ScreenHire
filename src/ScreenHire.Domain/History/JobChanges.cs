using System.Globalization;
using ScreenHire.Domain.Jobs;
using static ScreenHire.Domain.History.ChangeLabels;

namespace ScreenHire.Domain.History;

/// <summary>
/// A job's changes in words: its posting content, its status and its own copy of the workflow. Bookkeeping that follows
/// from those (<c>UpdatedAt</c>, <c>WorkflowVersion</c>, <c>PublishedVersion</c>, the row version) makes no line.
/// </summary>
public static class JobChanges
{
    /// <param name="before">The job before the save; null when the save created it.</param>
    /// <param name="after">The job after the save; null when the save deleted it.</param>
    /// <param name="templateName">The name of the template the job's workflow comes from, when it has one.</param>
    public static IReadOnlyList<ChangeLine> Describe(Job? before, Job? after, string? templateName = null)
    {
        if (after is null)
        {
            return before is null ? [] : [ChangeLine.Plain("deleted the job")];
        }

        if (before is null)
        {
            return [templateName is null ? ChangeLine.Plain("created the job") : ChangeLine.Of("created the job from ", templateName)];
        }

        var lines = new List<ChangeLine>();
        if (before.Title != after.Title)
        {
            lines.Add(ChangeLine.Of($"renamed the job from {before.Title} to ", after.Title));
        }

        if (before.Slug != after.Slug)
        {
            lines.Add(ChangeLine.Of($"changed the address from {before.Slug} to ", after.Slug));
        }

        if (before.Description != after.Description)
        {
            // Bold, a heading or a list changes the HTML but not the words, and before and after would read the same.
            var (was, now) = (RichTextToPlain.Convert(before.Description), RichTextToPlain.Convert(after.Description));
            lines.Add(was == now
                ? ChangeLine.Plain("changed the formatting of the description")
                : ChangeLine.Plain("changed the description", [new("Description", was, now, ChangeValueKind.Paragraphs)]));
        }

        switch (before.Department, after.Department)
        {
            case (null, { } set):
                lines.Add(ChangeLine.Of("set the department to ", set));
                break;
            case ({ }, null):
                lines.Add(ChangeLine.Plain("cleared the department"));
                break;
            case ({ } from, { } to) when from != to:
                lines.Add(ChangeLine.Of($"changed the department from {from} to ", to));
                break;
        }

        if (before.WorkplaceType != after.WorkplaceType)
        {
            lines.Add(ChangeLine.Of($"changed the workplace from {Workplace(before.WorkplaceType)} to ", Workplace(after.WorkplaceType)));
        }

        if (before.EmploymentType != after.EmploymentType)
        {
            lines.Add(ChangeLine.Of($"changed the employment type from {Employment(before.EmploymentType)} to ", Employment(after.EmploymentType)));
        }

        if (!before.Locations.SequenceEqual(after.Locations) || !before.ApplicantLocationRequirements.SequenceEqual(after.ApplicantLocationRequirements))
        {
            lines.Add(ChangeLine.Plain("changed the location", [new("Location", Location(before), Location(after))]));
        }

        if (before.Salary != after.Salary)
        {
            lines.Add(ChangeLine.Plain("changed the salary", [new("Salary", Salary(before.Salary), Salary(after.Salary))]));
        }

        if (before.ValidThrough != after.ValidThrough)
        {
            lines.Add(ChangeLine.Plain("changed when the posting expires", [new("Expires", Instant(before.ValidThrough), Instant(after.ValidThrough), ChangeValueKind.Instant)]));
        }

        if (before.SourceTemplateVersion != after.SourceTemplateVersion && after.SourceTemplateVersion is { } version && templateName is not null)
        {
            lines.Add(ChangeLine.Of($"took version {version.ToString(CultureInfo.InvariantCulture)} of ", templateName));
        }

        lines.AddRange(DefinitionChanges.Describe(before.Workflow, after.Workflow));

        if (before.Status != after.Status)
        {
            lines.Add(after.Status switch
            {
                JobStatus.Open => ChangeLine.Plain("published the job"),
                JobStatus.Closed => ChangeLine.Plain("closed the job"),
                _ => ChangeLine.Plain("made the job a draft"),
            });
        }
        else if (after.Status == JobStatus.Open && after.PublishedVersion > before.PublishedVersion)
        {
            lines.Add(ChangeLine.Plain("published the changes"));
        }

        return lines;
    }

    private static string? Location(Job job)
    {
        var text = JobLocation.Describe(job.WorkplaceType, job.Locations, job.ApplicantLocationRequirements);
        return text.Length == 0 ? null : text;
    }

    private static string? Instant(DateTimeOffset? at) => at?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
