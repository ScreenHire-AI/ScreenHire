using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.History;

/// <summary>
/// An email template's changes in words. A built-in template with no row sends ScreenHire's wording, so its first edit
/// (the row's insert) compares against that wording, and deleting the row restores it.
/// </summary>
public static class EmailTemplateChanges
{
    /// <param name="before">The row before the save; null when the save added it.</param>
    /// <param name="after">The row after the save; null when the save deleted it.</param>
    /// <param name="original">ScreenHire's wording of a built-in template, its subject and message; null for the organization's own.</param>
    public static IReadOnlyList<ChangeLine> Describe(EmailTemplate? before, EmailTemplate? after, (string Subject, string Body)? original = null)
    {
        var builtIn = (after ?? before)?.BuiltInId is not null;
        if (after is null)
        {
            if (before is null)
            {
                return [];
            }

            return builtIn && original is { } wording
                ? [ChangeLine.Plain("restored the original wording", Wording(before.Subject, before.Body, wording.Subject, wording.Body))]
                : [ChangeLine.Plain("deleted the template")];
        }

        if (before is null && !builtIn)
        {
            return [ChangeLine.Plain("created the template")];
        }

        var lines = new List<ChangeLine>();
        var (subject, body) = before is not null ? (before.Subject, before.Body) : original ?? (after.Subject, after.Body);
        if (before is not null && before.Name != after.Name && after.Name is { } name)
        {
            lines.Add(ChangeLine.Of($"renamed the template from {before.Name} to ", name));
        }

        if (subject != after.Subject)
        {
            lines.Add(ChangeLine.Plain("changed the subject", [new("Subject", subject, after.Subject)]));
        }

        if (body != after.Body)
        {
            // The message is rich text: bold, a heading or a list changes the HTML but not the words.
            var (was, now) = (RichTextToPlain.Convert(body), RichTextToPlain.Convert(after.Body));
            lines.Add(was == now
                ? ChangeLine.Plain("changed the formatting of the message")
                : ChangeLine.Plain("changed the message", [new("Message", was, now, ChangeValueKind.Paragraphs)]));
        }

        if (Archive.Line(before?.ArchivedAt, after.ArchivedAt, "template") is { } archive)
        {
            lines.Add(archive);
        }

        return lines;
    }

    private static List<ChangeDetail> Wording(string subjectBefore, string bodyBefore, string subjectAfter, string bodyAfter) =>
    [
        .. new[]
        {
            new ChangeDetail("Subject", subjectBefore, subjectAfter),
            new ChangeDetail("Message", RichTextToPlain.Convert(bodyBefore), RichTextToPlain.Convert(bodyAfter), ChangeValueKind.Paragraphs),
        }.Where(d => d.Before != d.After),
    ];
}
