using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// The raw network and session data the careers form's submission arrived with: the candidate's address, browser,
/// salted device hash, when they opened the form and any automation markers. Recorded only for careers-site
/// applications, after the notice (CLAUDE.md non-negotiable 4); a recruiter's own address is never recorded for a
/// candidate. Short-lived: the retention job deletes the row at <see cref="ExpiresAt"/>, and the signals derived from it
/// keep only coarse facts (network operator, country), never the address.
/// </summary>
public sealed class SubmissionObservation : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The candidate's IP address as the API saw it (behind trusted proxies only), or null if unknown.</summary>
    public string? IpAddress { get; init; }

    /// <summary>The candidate's browser as the careers server passed it on.</summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// The device fingerprint, keyed-hashed with a per-organization salt before it is stored, so it cannot be matched
    /// across employers or reversed. Null when the browser sent none or no key is configured.
    /// </summary>
    public string? DeviceHash { get; init; }

    /// <summary>When the careers server first showed the candidate the form. Set by the server, but it round-trips through the browser.</summary>
    public DateTimeOffset? FormStartedAt { get; init; }

    /// <summary>Automation markers the page reported, such as <c>webdriver</c>. Empty when the page reported none.</summary>
    public List<string> AutomationMarkers { get; init; } = [];

    public DateTimeOffset ObservedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public const int MaxUserAgentLength = 512;

    public const int MaxMarkers = 10;

    public const int MaxMarkerLength = 40;
}
