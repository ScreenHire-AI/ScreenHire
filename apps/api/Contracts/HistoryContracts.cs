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
/// <c>at</c>: null only for a built-in email template in ScreenHire's wording. <c>by</c>: null for an object last
/// changed before its history was recorded. <c>userId</c> names a member, by id, so the screen shows their current name;
/// <c>apiKeyId</c> names an API key.
/// </remarks>
public sealed record LastUpdate(DateTimeOffset? At, ChangeActorKind? By, Guid? UserId = null, Guid? ApiKeyId = null)
{
    /// <summary>A ScreenHire built-in nobody in the organization has changed.</summary>
    public static LastUpdate ByScreenHire(DateTimeOffset? at) => new(at, ChangeActorKind.ScreenHire);

    /// <summary>The newest recorded change, or <paramref name="fallback"/> with nobody named when none is recorded.</summary>
    public static LastUpdate From(ChangeStamp? stamp, DateTimeOffset? fallback) =>
        stamp is null ? new(fallback, null) : new(stamp.At, HistoryMappings.Actor(stamp.UserId, stamp.ApiKeyId), stamp.UserId, stamp.ApiKeyId);
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
    public static ChangeActorKind Actor(Guid? userId, Guid? apiKeyId) =>
        userId is not null ? ChangeActorKind.Member : apiKeyId is not null ? ChangeActorKind.ApiKey : ChangeActorKind.ScreenHire;

    public static HistoryEntry ToEntry(this ChangeRecord record) =>
        new(record.Id, record.OccurredAt, Actor(record.ActorUserId, record.ActorApiKeyId), record.ActorUserId, record.ActorApiKeyId,
            [.. record.Lines.Select(l => new HistoryLine(
                l.Sentence(),
                [.. l.Parts.Select(p => new HistoryTextPart(p.Text, p.Strong))],
                [.. (l.Details ?? []).Select(d => new HistoryDetail(d.Label, d.Before, d.After, d.Kind))]))]);
}
