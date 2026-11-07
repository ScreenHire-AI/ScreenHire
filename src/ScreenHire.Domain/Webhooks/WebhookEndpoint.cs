using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Webhooks;

/// <summary>
/// One URL an organization's integration receives events at (docs/architecture/workflow-engine.md §5). Each endpoint
/// has its own signing secret, derived from the platform key and the version in its <see cref="WebhookEndpointSecret"/>
/// row, so the database never holds the secret. That row is separate so change history can track this one
/// (docs/architecture/change-history.md, "Secrets"). Its URL can carry a token, which history records as a host alone.
/// </summary>
public sealed class WebhookEndpoint : ITenantOwned
{
    public const int MaxUrlLength = 2048;

    public const int MaxDescriptionLength = 200;

    public const int MaxDisabledReasonLength = 500;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    /// <summary>The <see cref="WebhookEventTypes"/> this endpoint receives.</summary>
    public List<string> Events { get; set; } = [];

    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Deliveries in a row that failed every attempt. A success resets it; reaching the configured limit disables the
    /// endpoint. The worker updates it in one statement, so parallel deliveries never lose a count.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Why the endpoint stopped receiving, when the worker disabled it. Null when a person did, or it is enabled.</summary>
    public string? DisabledReason { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Turns the endpoint on or off by hand. Turning it on clears an automatic disable and its failure count.</summary>
    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        if (enabled == Enabled)
        {
            return;
        }

        Enabled = enabled;
        ConsecutiveFailures = 0;
        DisabledReason = null;
        DisabledAt = enabled ? null : now;
        UpdatedAt = now;
    }

    public static WebhookEndpoint Create(Guid organizationId, string url, string? description, IEnumerable<string> events, DateTimeOffset now) =>
        new()
        {
            OrganizationId = organizationId,
            Url = url,
            Description = description,
            Events = [.. events.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            CreatedAt = now,
            UpdatedAt = now,
        };
}

/// <summary>
/// The inputs of an endpoint's signing secret, in a row of its own so change history, which reads only what it tracks,
/// never sees them (ADR-0016). The secret itself is derived from the platform key, the endpoint id and
/// <see cref="Version"/>, and is never stored. The key is the endpoint's id: one row each, made with the endpoint.
/// </summary>
public sealed class WebhookEndpointSecret : ITenantOwned
{
    public Guid OrganizationId { get; init; }

    public Guid EndpointId { get; init; }

    /// <summary>Which secret signs deliveries: version 1 at creation, one more on each rotation.</summary>
    public int Version { get; private set; } = 1;

    public required DateTimeOffset RotatedAt { get; set; }

    /// <summary>A new secret signs every delivery from now on, retries of earlier events included. The old one stops at once.</summary>
    public void Rotate(DateTimeOffset now)
    {
        Version++;
        RotatedAt = now;
    }

    public static WebhookEndpointSecret For(WebhookEndpoint endpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new() { OrganizationId = endpoint.OrganizationId, EndpointId = endpoint.Id, RotatedAt = now };
    }
}

/// <summary>The events an endpoint can subscribe to, by their name on the wire.</summary>
public static class WebhookEventTypes
{
    public const string ApplicationSubmitted = "application.submitted";

    public const string ApplicationMoved = "application.moved";

    public const string VerificationAssessed = "verification.assessed";

    public const string AutomationFailed = "automation.failed";

    public const string JobPublished = "job.published";

    public const string JobClosed = "job.closed";

    public static IReadOnlyList<string> All { get; } =
        [ApplicationSubmitted, ApplicationMoved, VerificationAssessed, AutomationFailed, JobPublished, JobClosed];

    public static bool IsKnown(string type) => All.Contains(type, StringComparer.Ordinal);
}
