using ScreenHire.Domain.Notifications;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A member's own preferences in one organization, from Your account (docs/architecture/notifications.md and
/// sign-in-and-tenancy.md, "Your account"): their time zone, language and date format, two working habits, and their
/// notification choices by email and Slack. Preferences about the device (theme, reduced motion) stay in the browser;
/// toasts stay on the membership (<see cref="NotificationSettings"/>). One row per membership, made on the first save:
/// a member without one has every default.
/// </summary>
public sealed class MemberPreferences : ITenantOwned
{
    public const int MaxTimeZoneLength = 64;
    public const int MaxLanguageLength = 35;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid MemberId { get; init; }

    /// <summary>The person, by user id, for the digest's lookups by person.</summary>
    public Guid UserId { get; init; }

    /// <summary>An IANA time zone ("Europe/London"); null until the dashboard or the person sets one, read as UTC.</summary>
    public string? TimeZone { get; private set; }

    /// <summary>The language and region dates and numbers are written in ("en-GB"); null follows the browser.</summary>
    public string? Language { get; private set; }

    public DateFormat DateFormat { get; private set; } = DateFormat.Browser;

    /// <summary>Single-key shortcuts; null until saved here, while the dashboard keeps the browser's earlier choice.</summary>
    public bool? ShortcutsEnabled { get; private set; }

    /// <summary>Whether a move that sends emails or creates tasks asks first; null as for <see cref="ShortcutsEnabled"/>.</summary>
    public bool? ConfirmMoves { get; private set; }

    public NoticeScope Scope { get; private set; } = NoticeScope.MyJobs;

    public NoticeChoices Notices { get; private set; } = NoticeChoices.Default;

    /// <summary>The person's local date of the last daily digest sent, so a day gets at most one.</summary>
    public DateOnly? LastDigestOn { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static MemberPreferences Create(Guid organizationId, Guid memberId, Guid userId, DateTimeOffset now) =>
        new() { OrganizationId = organizationId, MemberId = memberId, UserId = userId, UpdatedAt = now };

    public void SetProfile(string? timeZone, string? language, DateFormat dateFormat, DateTimeOffset now)
    {
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone;
        Language = string.IsNullOrWhiteSpace(language) ? null : language;
        DateFormat = dateFormat;
        UpdatedAt = now;
    }

    /// <summary>Sets the time zone only when none is set: the dashboard fills it from the browser once.</summary>
    public bool FillTimeZone(string timeZone, DateTimeOffset now)
    {
        if (TimeZone is not null)
        {
            return false;
        }

        TimeZone = timeZone;
        UpdatedAt = now;
        return true;
    }

    public void SetHabits(bool shortcutsEnabled, bool confirmMoves, DateTimeOffset now)
    {
        ShortcutsEnabled = shortcutsEnabled;
        ConfirmMoves = confirmMoves;
        UpdatedAt = now;
    }

    public void SetNotices(NoticeScope scope, NoticeChoices notices, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notices);
        Scope = scope;
        Notices = notices;
        UpdatedAt = now;
    }

    public void MarkDigestSent(DateOnly localDate) => LastDigestOn = localDate;
}

/// <summary>How the dashboard writes a date: the browser's way for the language, or one fixed order.</summary>
public enum DateFormat
{
    Browser,
    DayMonthYear,
    MonthDayYear,
    Iso,
}
