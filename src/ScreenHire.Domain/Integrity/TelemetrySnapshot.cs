using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// The form telemetry one page view of the careers apply form sent: session aggregates only (CLAUDE.md non-negotiable 4),
/// never raw keystrokes, key values, timings per key or field content. The page sends a cumulative snapshot each time it
/// is hidden, so a page view keeps only its latest (<see cref="Sequence"/>). A telemetry session is one candidate's way
/// through the form; it is bound to the application when the application is sent.
/// </summary>
/// <remarks>
/// Unbound snapshots (an application never sent) expire within a day; bound ones live as long as the other raw
/// integrity data (<c>ScreenHire:Integrity:Retention:RawNetworkDays</c>). An application with the assistive-technology
/// switch on keeps none: submission deletes its session.
/// </remarks>
public sealed class TelemetrySnapshot : IJobScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The telemetry session, from the token the API issued with the application form.</summary>
    public Guid SessionId { get; init; }

    /// <summary>One render of one form step, chosen by the page.</summary>
    public Guid PageViewId { get; init; }

    /// <summary>The page's count of snapshots sent so far. A lower or equal one never replaces a stored one.</summary>
    public int Sequence { get; set; }

    /// <summary>The application the session ended in, once it was sent; null until then.</summary>
    public Guid? ApplicationId { get; set; }

    public List<FieldTelemetry> Fields { get; set; } = [];

    public PageTelemetry Page { get; set; } = new(0, []);

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The most fields one page view may report: every question a job can ask, plus the fixed fields.</summary>
    public const int MaxFields = 110;

    public const int MaxFieldIdLength = 40;
}

/// <summary>Aggregates for one form field, keyed by the form's field id (a question id, <c>firstName</c>, ...).</summary>
/// <remarks>
/// <c>pasteCount</c>, <c>pastedCharacters</c>, <c>typedCharacters</c>: counts, not content. <c>focusCount</c> and
/// <c>blurCount</c>: times the field gained and lost focus. <c>activeMilliseconds</c>: total time focused.
/// <c>autofilled</c>: the browser or a password manager filled it. There is deliberately no typing-rhythm field: timing
/// between keys is keystroke dynamics, a behavioural biometric, and v1 has no biometrics (CLAUDE.md non-negotiable 3).
/// Adding one needs its own legal review (integrity-engine.md §10.2).
/// </remarks>
public sealed record FieldTelemetry(
    string FieldId,
    int PasteCount,
    int PastedCharacters,
    int TypedCharacters,
    int FocusCount,
    int BlurCount,
    int ActiveMilliseconds,
    bool Autofilled);

/// <summary>
/// Page-level aggregates: how often the page was hidden (the candidate switched to another tab or app; which one is never
/// known) and automation markers such as <c>webdriver</c>.
/// </summary>
public sealed record PageTelemetry(int VisibilityChanges, IReadOnlyList<string> AutomationMarkers);
