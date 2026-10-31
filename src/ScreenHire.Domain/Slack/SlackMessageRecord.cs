using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Slack;

/// <summary>
/// That ScreenHire posted, or gave up on, one Slack message: the handlers' effect check, so a message redelivered after
/// the deduplication window still posts once. No text: the message's words live only in Slack. Kept for 30 days.
/// </summary>
public sealed class SlackMessageRecord : ITenantOwned
{
    public const int MaxKeyLength = 200;

    public const int MaxErrorLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The message's idempotency key, from stable ids. Unique per organization.</summary>
    public required string IdempotencyKey { get; init; }

    public SlackMessageOutcome Outcome { get; init; }

    /// <summary>Slack's error code when it refused the message for good, such as <c>not_in_channel</c>.</summary>
    public string? ErrorCode { get; init; }

    public DateTimeOffset At { get; init; }
}

public enum SlackMessageOutcome
{
    Sent,

    /// <summary>Slack refused it for a reason a retry cannot fix; the problem shows on the page.</summary>
    Refused,
}

/// <summary>
/// A message the local sandbox workspace "received": the stand-in for Slack's own storage, as
/// <c>SandboxListing</c> is the sandbox board's. Development and tests only.
/// </summary>
public sealed class SlackSandboxMessage : ITenantOwned
{
    public const int MaxConversationLength = 60;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The channel id, or the direct message's id (<c>D</c> and the person's Slack id).</summary>
    public required string Conversation { get; init; }

    public required string Text { get; init; }

    public DateTimeOffset PostedAt { get; init; }
}
