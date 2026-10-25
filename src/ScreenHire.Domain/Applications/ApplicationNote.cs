using System.Text.RegularExpressions;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// A note from the hiring team on an application, with its latest text only. The timeline records that it was added,
/// edited and deleted (<see cref="ApplicationEventType.NoteAdded"/>, <see cref="ApplicationEventType.NoteEdited"/>,
/// <see cref="ApplicationEventType.NoteDeleted"/>), by note id and never with its text, because the timeline is
/// append-only and deleting a note must erase its words. Earlier text is never kept, so deleting this row erases the
/// note, and only who deleted it and when remain.
/// </summary>
public sealed class ApplicationNote : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The person who wrote it, or null when an API key did (<see cref="AuthorApiKeyId"/>).</summary>
    public Guid? AuthorUserId { get; init; }

    /// <summary>The organization API key that wrote it; null when a person did.</summary>
    public Guid? AuthorApiKeyId { get; init; }

    /// <summary>The note as its author last saved it. A mention is <c>@[userId]</c> (<see cref="NoteMentions"/>).</summary>
    public string Text { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the text last changed; null for a note never edited.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>The people the text mentions now, one row each.</summary>
    public List<ApplicationNoteMention> Mentions { get; init; } = [];

    internal static ApplicationNote Create(JobApplication application, string text, DateTimeOffset now, Guid? authorUserId, Guid? authorApiKeyId)
    {
        var note = new ApplicationNote
        {
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            AuthorUserId = authorUserId,
            AuthorApiKeyId = authorApiKeyId,
            Text = text,
            CreatedAt = now,
        };
        note.SyncMentions(now);
        return note;
    }

    /// <summary>
    /// Replaces the text. Null when <paramref name="text"/> is what the note says already; otherwise the mentions the
    /// new text adds, the only people an edit notifies. Mentions the new text drops are removed.
    /// </summary>
    internal IReadOnlyList<ApplicationNoteMention>? Edit(string text, DateTimeOffset now)
    {
        if (text == Text)
        {
            return null;
        }

        Text = text;
        EditedAt = now;
        return SyncMentions(now);
    }

    private List<ApplicationNoteMention> SyncMentions(DateTimeOffset now)
    {
        var mentioned = NoteMentions.Parse(Text);
        Mentions.RemoveAll(m => !mentioned.Contains(m.UserId));
        var added = mentioned
            .Where(userId => Mentions.All(m => m.UserId != userId))
            .Select(userId => new ApplicationNoteMention { OrganizationId = OrganizationId, NoteId = Id, UserId = userId, CreatedAt = now })
            .ToList();
        Mentions.AddRange(added);
        return added;
    }
}

/// <summary>
/// One person a note mentions. It is also the mention email's state: <see cref="EmailedAt"/> is set once the email has
/// gone, so a redelivered message sends nothing twice. Removing the mention, or the note, removes the row.
/// </summary>
public sealed class ApplicationNoteMention : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid NoteId { get; init; }

    /// <summary>The member mentioned, by user id like every other person a row names.</summary>
    public Guid UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? EmailedAt { get; private set; }

    public void MarkEmailed(DateTimeOffset now) => EmailedAt ??= now;
}

/// <summary>
/// Mentions in a note's text. A mention is stored as <c>@[userId]</c>, the member's user id, so a teammate who is
/// renamed shows their current name, and the dashboard turns it into "@Robin Hale".
/// </summary>
public static partial class NoteMentions
{
    /// <summary>The stored form of a mention of <paramref name="userId"/>.</summary>
    public static string Token(Guid userId) => $"@[{userId}]";

    /// <summary>The user ids <paramref name="text"/> mentions, each once, in the order they first appear.</summary>
    public static IReadOnlyList<Guid> Parse(string text) =>
        [.. MentionPattern().Matches(text).Select(m => Guid.Parse(m.Groups[1].Value)).Distinct()];

    [GeneratedRegex(@"@\[([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\]")]
    private static partial Regex MentionPattern();
}

/// <summary>
/// Who may change a note (design system section 21): only its author edits it; the author, or a role with Delete
/// anyone's notes (Owners and Admins among the built-in roles), deletes it. An API key is the author of the notes it
/// wrote. The API checks this on every edit and delete.
/// </summary>
public static class NotePolicy
{
    public static bool IsAuthor(ApplicationNote note, Guid? userId, Guid? apiKeyId)
    {
        ArgumentNullException.ThrowIfNull(note);
        return (note.AuthorUserId is { } author && author == userId) || (note.AuthorApiKeyId is { } key && key == apiKeyId);
    }

    public static bool MayEdit(ApplicationNote note, Guid? userId, Guid? apiKeyId) => IsAuthor(note, userId, apiKeyId);

    /// <summary>
    /// <paramref name="permissions"/> are what the person's role allows in the note's organization; null for an API key,
    /// which has no role (ADR-0014).
    /// </summary>
    public static bool MayDelete(ApplicationNote note, Guid? userId, Guid? apiKeyId, IReadOnlySet<Permission>? permissions) =>
        IsAuthor(note, userId, apiKeyId) || (userId is not null && permissions?.Contains(Permission.DeleteAnyNote) == true);
}
