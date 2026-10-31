using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Verification;

/// <summary>
/// The raw network and session data the careers form's submission arrived with: the candidate's address, browser,
/// salted device hash, how long each page of the form was open and any automation markers. Recorded only for careers-site
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

    /// <summary>
    /// When the careers server first showed the candidate the form. No longer recorded: submit velocity reads
    /// <see cref="PageTimes"/>. Rows from before the change keep it until the retention job deletes them.
    /// </summary>
    public DateTimeOffset? FormStartedAt { get; init; }

    /// <summary>
    /// The active time on each page of the form in the sitting that sent it: from the careers server showing a page to
    /// the candidate posting it, added up per page and capped per visit. Set by the server, but it round-trips through the
    /// browser, so it is a hint. Empty with an accommodation, or when the careers server sent none.
    /// </summary>
    public List<PageTime> PageTimes { get; init; } = [];

    /// <summary>The candidate saved a draft of this application and sent it from that draft (apply-flow.md §7).</summary>
    public bool ResumedFromDraft { get; init; }

    /// <summary>The questions whose answer was the one the draft saved: answered in an earlier sitting, not timed again.</summary>
    public List<string> CarriedOverQuestionIds { get; init; } = [];

    /// <summary>Automation markers the page reported, such as <c>webdriver</c>. Empty when the page reported none.</summary>
    public List<string> AutomationMarkers { get; init; } = [];

    public DateTimeOffset ObservedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public const int MaxUserAgentLength = 512;

    public const int MaxMarkers = 10;

    public const int MaxMarkerLength = 40;

    /// <summary>The most pages a form has: its details, 50 questions a page each and its review.</summary>
    public const int MaxPages = 60;

    /// <summary>The most one visit to a page counts: a page left open longer is not more active.</summary>
    public const int MaxSecondsPerPage = 1800;

    public const int MaxPageLength = 20;
}

/// <summary>The active seconds on one page of the careers form, such as <c>page-2</c>.</summary>
public sealed record PageTime(string Page, int Seconds);
