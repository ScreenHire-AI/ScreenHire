using System.Globalization;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.History;

/// <summary>
/// A hiring template's changes in words. The template row and its version rows share one history: the template's
/// name and description, starting and publishing versions, and the draft's definition.
/// </summary>
public static class HiringTemplateChanges
{
    /// <summary>
    /// The template row. <paramref name="copiedFromName"/> is the template it was copied from, if any. Its
    /// <c>UpdatedAt</c> and <c>LatestPublishedVersion</c> follow the versions, so they never make a line of their own.
    /// </summary>
    public static IReadOnlyList<ChangeLine> Template(HiringTemplate? before, HiringTemplate? after, string? copiedFromName = null)
    {
        if (after is null)
        {
            return [ChangeLine.Plain("deleted the template")];
        }

        if (before is null)
        {
            return [copiedFromName is null ? ChangeLine.Plain("created the template") : ChangeLine.Of("created the template from ", copiedFromName)];
        }

        var lines = new List<ChangeLine>();
        if (before.Name != after.Name)
        {
            lines.Add(ChangeLine.Of($"renamed the template from {before.Name} to ", after.Name));
        }

        if (before.Description != after.Description)
        {
            lines.Add(ChangeLine.Plain("changed the description", [new("Description", before.Description, after.Description, ChangeValueKind.Paragraphs)]));
        }

        if (Archive.Line(before.ArchivedAt, after.ArchivedAt, "template") is { } archive)
        {
            lines.Add(archive);
        }

        return lines;
    }

    /// <summary>
    /// A version row. A version added with its template says nothing: "created the template" covers it. A later version
    /// says it started, with what differs from <paramref name="previous"/>, the newest version before it.
    /// </summary>
    public static IReadOnlyList<ChangeLine> Version(
        HiringTemplateVersion? before, HiringTemplateVersion? after, HiringTemplateDefinition? previous = null, bool addedWithTemplate = false)
    {
        if (after is null)
        {
            return before is null ? [] : [ChangeLine.Of("discarded the draft of version ", Number(before.Version))];
        }

        if (before is null)
        {
            if (addedWithTemplate)
            {
                return [];
            }

            List<ChangeLine> started = [ChangeLine.Of("started version ", Number(after.Version))];
            if (previous is not null)
            {
                started.AddRange(DefinitionChanges.Describe(previous, after.Definition));
            }

            return started;
        }

        var lines = DefinitionChanges.Describe(before.Definition, after.Definition).ToList();
        if (before.Status == TemplateVersionStatus.Draft && after.Status == TemplateVersionStatus.Published)
        {
            lines.Add(ChangeLine.Of("published version ", Number(after.Version)));
        }

        return lines;
    }

    private static string Number(int version) => version.ToString(CultureInfo.InvariantCulture);
}
