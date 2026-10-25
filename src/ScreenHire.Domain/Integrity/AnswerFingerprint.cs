using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// A 64-bit SimHash of one free-text answer, so later applications to the same organization can find near-identical
/// answers without reading every answer (<c>CNT_NEAR_DUPLICATE_ANSWER</c>, integrity-engine.md §10.10). The hash is a
/// locality-sensitive fingerprint of word sequences, not the text; a match is confirmed against the text itself.
/// Never compared across organizations.
/// </summary>
public sealed class AnswerFingerprint : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The candidate, so a candidate's own answers to two jobs never match each other.</summary>
    public Guid CandidateId { get; init; }

    public required string QuestionId { get; init; }

    public long SimHash { get; init; }

    public int WordCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
