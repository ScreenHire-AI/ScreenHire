using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Notifications;

/// <summary>
/// One thing that needs one person's attention, shown in the bell in the dashboard's top bar (docs/architecture/notifications.md).
/// A row belongs to one person: dismissing it hides it for them only. It stores facts, not prose: <see cref="Kind"/> and
/// <see cref="Payload"/> are turned into a plain line by the dashboard, so the wording can change without a migration.
/// <see cref="Key"/> names the thing the notification is about (the sending domain's id), so raising it twice for one person is
/// one row and clearing it finds every person's copy.
/// </summary>
/// <remarks>
/// Row-level security and the tenant query filter are the organization's, because the worker raises and clears rows for people
/// it does not act as. The per-person boundary is in code: any read for a signed-in person must narrow to their
/// <see cref="UserId"/>, as <c>NotificationEndpoints</c> does, or it shows a teammate's alerts.
/// </remarks>
public sealed class Notification : ITenantOwned
{
    public const int MaxKindLength = 60;
    public const int MaxKeyLength = 200;
    public const int MaxLinkLength = 300;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The person it is for: a user id, because a membership id changes when someone leaves and rejoins.</summary>
    public Guid UserId { get; init; }

    /// <summary>What kind of notification it is, one of the constants in <see cref="NotificationKinds"/>.</summary>
    public string Kind { get; init; } = "";

    /// <summary>What it is about, stable for that thing. Unique per person and kind.</summary>
    public string Key { get; init; } = "";

    /// <summary>The facts the line is made from (the sending domain's name). Never personal data about a candidate.</summary>
    public Dictionary<string, string> Payload { get; init; } = [];

    /// <summary>The dashboard page that fixes it, as a path.</summary>
    public string Link { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset? DismissedAt { get; private set; }

    public bool IsUnread => ReadAt is null && DismissedAt is null;

    /// <summary>
    /// Whether <paramref name="link"/> is a path inside the dashboard: it starts with one slash. An address, a scheme or
    /// <c>//host</c> would send a person off the site from a link they trust.
    /// </summary>
    public static bool IsDashboardPath(string? link) =>
        link is { Length: > 0 and <= MaxLinkLength } && link[0] == '/' && (link.Length == 1 || (link[1] != '/' && link[1] != '\\'))
        && !link.Any(char.IsControl);

    public static Notification Create(
        Guid organizationId, Guid userId, string kind, string key, IReadOnlyDictionary<string, string>? payload, string link, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (kind.Length > MaxKindLength || key.Length > MaxKeyLength)
        {
            throw new ArgumentException("The kind or the key is too long.");
        }

        if (!IsDashboardPath(link))
        {
            throw new ArgumentException("The link must be a path inside the dashboard, such as /settings/team.", nameof(link));
        }

        return new Notification
        {
            OrganizationId = organizationId,
            UserId = userId,
            Kind = kind,
            Key = key,
            Payload = payload is null ? [] : new Dictionary<string, string>(payload),
            Link = link,
            CreatedAt = now,
        };
    }

    /// <summary>Marks it read. Returns whether it changed; reading twice keeps the first time.</summary>
    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null)
        {
            return false;
        }

        ReadAt = now;
        return true;
    }

    /// <summary>Hides it for this person. Dismissing counts as reading it. Returns whether it changed.</summary>
    public bool Dismiss(DateTimeOffset now)
    {
        if (DismissedAt is not null)
        {
            return false;
        }

        DismissedAt = now;
        ReadAt ??= now;
        return true;
    }
}

/// <summary>
/// The kinds of notification. A new kind is a constant here and a line in the dashboard's <c>notification-lines.ts</c>; a
/// kind the dashboard does not know shows a generic line with its link.
/// </summary>
public static class NotificationKinds
{
    /// <summary>The sending domain stopped verifying. Key: the domain's id. Payload: <c>domain</c>.</summary>
    public const string SendingDomainFailing = "sending-domain-failing";

    /// <summary>
    /// The free trial ends within <c>TrialReminders.NoticeDays</c> days. Key: <see cref="TrialKey"/>. Payload: <c>endsAt</c> (ISO 8601) and
    /// <c>plan</c>. The dashboard works out the days left from <c>endsAt</c>, so the one row stays right as the days pass.
    /// </summary>
    public const string TrialEnding = "trial-ending";

    /// <summary>The free trial ended and no plan has started. Key: <see cref="TrialKey"/>. Payload: <c>endsAt</c> and <c>plan</c>.</summary>
    public const string TrialEnded = "trial-ended";

    /// <summary>The key of both trial kinds: an organization has one trial.</summary>
    public const string TrialKey = "trial";

    /// <summary>
    /// Kinds the daily retention purge keeps. They say something that stays true until a person acts, and the code that raises
    /// them clears them when it stops being true: a trial that ended with no plan is still the case a month later.
    /// </summary>
    public static IReadOnlyList<string> KeptUntilCleared { get; } = [TrialEnding, TrialEnded];

    /// <summary>The month's AI use passed 50%, 80% or 100% of the budget. Key: <c>yyyy-MM:budget-N</c>. Payload: <c>percent</c>.</summary>
    public const string AiUsageBudget = "ai-usage-budget";

    /// <summary>The month's AI use reached the cap and the AI checks paused. Key: <c>yyyy-MM:cap</c>.</summary>
    public const string AiUsageCap = "ai-usage-cap";

    /// <summary>A day's AI use was well above the recent average. Key: <c>yyyy-MM-dd:spike</c>. Payload: <c>day</c>.</summary>
    public const string AiUsageSpike = "ai-usage-spike";
}
