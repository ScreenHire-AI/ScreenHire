using System.Globalization;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.History;

/// <summary>
/// A library form's changes in words. The form row and its version rows share one history, as a hiring template's do:
/// created, archived, a version started or published, and the draft's questions.
/// </summary>
public static class FormTemplateChanges
{
    /// <summary>
    /// The form row. <paramref name="copiedFromName"/> is the form it was copied from, if any. Its name, <c>UpdatedAt</c>
    /// and <c>LatestPublishedVersion</c> follow the versions, so they never make a line of their own.
    /// </summary>
    public static IReadOnlyList<ChangeLine> Form(FormTemplate? before, FormTemplate? after, string? copiedFromName = null)
    {
        if (after is null)
        {
            return before is null ? [] : [ChangeLine.Plain("deleted the form")];
        }

        if (before is null)
        {
            return [copiedFromName is null ? ChangeLine.Plain("created the form") : ChangeLine.Of("created the form from ", copiedFromName)];
        }

        return Archive.Line(before.ArchivedAt, after.ArchivedAt, "form") is { } archive ? [archive] : [];
    }

    /// <summary>
    /// A version row. A version added with its form says nothing: "created the form" covers it. A later version says it
    /// started, with what differs from <paramref name="previous"/>, the newest version before it.
    /// </summary>
    public static IReadOnlyList<ChangeLine> Version(
        FormTemplateVersion? before, FormTemplateVersion? after, FormContent? previous = null, bool addedWithForm = false)
    {
        if (after is null)
        {
            return before is null ? [] : [ChangeLine.Of("discarded the draft of version ", Number(before.Version))];
        }

        if (before is null)
        {
            if (addedWithForm)
            {
                return [];
            }

            List<ChangeLine> started = [ChangeLine.Of("started version ", Number(after.Version))];
            if (previous is not null)
            {
                started.AddRange(DefinitionChanges.Form(previous, after.Content));
            }

            return started;
        }

        var lines = DefinitionChanges.Form(before.Content, after.Content).ToList();
        if (before.Status == TemplateVersionStatus.Draft && after.Status == TemplateVersionStatus.Published)
        {
            lines.Add(ChangeLine.Of("published version ", Number(after.Version)));
        }

        return lines;
    }

    private static string Number(int version) => version.ToString(CultureInfo.InvariantCulture);
}
