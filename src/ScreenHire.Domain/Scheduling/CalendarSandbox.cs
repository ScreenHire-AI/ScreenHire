using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Scheduling;

/// <summary>
/// A block of time a member is busy in the local sandbox calendar: the stand-in for what a provider's free/busy
/// answers. Development and tests only (the sandbox is refused elsewhere), added through the API's sandbox route.
/// </summary>
public sealed class CalendarSandboxBusy : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset StartsAt { get; init; }

    public DateTimeOffset EndsAt { get; init; }
}

/// <summary>
/// An event the sandbox calendar "received" for a member: the stand-in for the provider's own storage, as
/// <c>SlackSandboxMessage</c> is Slack's. It counts as busy while it is not <see cref="Deleted"/>.
/// </summary>
public sealed class CalendarSandboxEvent : ITenantOwned
{
    public const int MaxEventIdLength = 200;

    public const int MaxTextLength = 4000;

    public const int MaxUrlLength = 500;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid UserId { get; init; }

    /// <summary>The event's id in this calendar: the caller's idempotency id, so creating twice finds the first.</summary>
    public required string ProviderEventId { get; init; }

    public required string Summary { get; set; }

    public string? Description { get; set; }

    public string? MeetingUrl { get; init; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Cancelled: kept so a repeated cancel is a no-op, and not busy.</summary>
    public bool Deleted { get; set; }
}
