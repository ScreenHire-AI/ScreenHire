using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Contracts;

/// <summary>Who made a change (design system T6): a member, an organization API key, or ScreenHire itself (an automation, a built-in).</summary>
public enum ChangeActorKind
{
    Member,
    ApiKey,
    ScreenHire,
}

/// <summary>When a tracked object last changed and who changed it, the last fact in its header.</summary>
/// <remarks>
/// <c>at</c>: null only for a built-in in ScreenHire's wording. <c>by</c>: null when nobody was recorded, for an object
/// created or changed before its history was. <c>userId</c> names a member, by id, so the screen shows their current
/// name; <c>apiKeyId</c> names an API key. <c>created</c>: nothing has changed the object since it was created, so the
/// header says Created, not Updated.
/// </remarks>
public sealed record LastUpdate(DateTimeOffset? At, ChangeActorKind? By, Guid? UserId = null, Guid? ApiKeyId = null, bool Created = false)
{
    /// <summary>A ScreenHire built-in nobody in the organization has changed.</summary>
    public static LastUpdate ByScreenHire(DateTimeOffset? at) => new(at, ChangeActorKind.ScreenHire);

    /// <summary>
    /// The newest recorded change. With none, the object's creation when nothing changed it since
    /// (<paramref name="updatedAt"/> null or the creation time itself), else its last update; either with nobody named.
    /// </summary>
    public static LastUpdate From(ChangeStamp? stamp, DateTimeOffset createdAt, DateTimeOffset? updatedAt = null) =>
        stamp is not null ? Of(stamp)
        : updatedAt is { } updated && updated != createdAt ? new(updated, null)
        : new(createdAt, null, Created: true);

    /// <summary>
    /// For something people only ever change, never create (a settings row, a built-in's own wording): the newest
    /// recorded change, or <paramref name="changedAt"/> with nobody named.
    /// </summary>
    public static LastUpdate FromChange(ChangeStamp? stamp, DateTimeOffset? changedAt) => stamp is not null ? Of(stamp) : new(changedAt, null);

    private static LastUpdate Of(ChangeStamp stamp) =>
        new(stamp.At, HistoryMappings.Actor(stamp.UserId, stamp.ApiKeyId), stamp.UserId, stamp.ApiKeyId, stamp.Created);
}

/// <summary>
/// One page of an object's history, newest first. <c>nextBefore</c> is the id of this page's last entry, to pass as
/// <c>before</c> for the next page, or null after the last.
/// </summary>
public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, Guid? NextBefore);

/// <summary>Everything one save changed in the object: who, when, and a line per change.</summary>
public sealed record HistoryEntry(Guid Id, DateTimeOffset At, ChangeActorKind By, Guid? UserId, Guid? ApiKeyId, IReadOnlyList<HistoryLine> Lines);

/// <summary>
/// One change in words, such as "made Notes on the exercise optional". <c>text</c> is the sentence; <c>parts</c> are its
/// runs, with the name of the thing that changed marked <c>strong</c>. <c>details</c> are its values before and after.
/// </summary>
public sealed record HistoryLine(string Text, IReadOnlyList<HistoryTextPart> Parts, IReadOnlyList<HistoryDetail> Details);

public sealed record HistoryTextPart(string Text, bool Strong);

/// <summary>
/// A value before and after. Equal values did not change and show for context; a null <c>before</c> is new and a null
/// <c>after</c> was removed. An <c>Instant</c> is ISO 8601, for the browser to format.
/// </summary>
public sealed record HistoryDetail(string Label, string? Before, string? After, ChangeValueKind Kind);

internal static class HistoryMappings
{
    /// <summary>How a line names a member whose account no longer exists, as the dashboard names them.</summary>
    public const string FormerTeammate = "a former teammate";

    public static ChangeActorKind Actor(Guid? userId, Guid? apiKeyId) =>
        userId is not null ? ChangeActorKind.Member : apiKeyId is not null ? ChangeActorKind.ApiKey : ChangeActorKind.ScreenHire;

    /// <summary>The members the records' lines name, by id: the history stores ids, and names them when it is read.</summary>
    public static IReadOnlySet<Guid> People(IEnumerable<ChangeRecord> records) =>
        records.SelectMany(r => r.Lines).SelectMany(l => l.Parts).Select(p => p.UserId).OfType<Guid>().ToHashSet();

    /// <param name="record">The record.</param>
    /// <param name="names">The current name of each member its lines name (<see cref="People"/>); one missing is a former teammate.</param>
    public static HistoryEntry ToEntry(this ChangeRecord record, IReadOnlyDictionary<Guid, string> names) =>
        new(record.Id, record.OccurredAt, Actor(record.ActorUserId, record.ActorApiKeyId), record.ActorUserId, record.ActorApiKeyId,
            [.. record.Lines.Select(l => l.ToLine(names))]);

    private static HistoryLine ToLine(this ChangeLine line, IReadOnlyDictionary<Guid, string> names)
    {
        var parts = line.Parts
            .Select(p => new HistoryTextPart(p.UserId is { } person ? names.GetValueOrDefault(person, FormerTeammate) : p.Text, p.Strong))
            .ToList();
        return new(string.Concat(parts.Select(p => p.Text)), parts, [.. (line.Details ?? []).Select(d => new HistoryDetail(d.Label, d.Before, d.After, d.Kind))]);
    }
}
