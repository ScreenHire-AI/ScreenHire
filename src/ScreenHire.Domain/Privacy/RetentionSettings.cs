using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Privacy;

/// <summary>
/// An organization's record retention: after how many months an ended application is deleted. Tenant-scoped; no row, or a
/// null value, means off, which is the default. Changes are tracked in the settings history (docs/architecture/change-history.md).
/// The results of a run are not stored here: the "Last run" line reads the newest <c>RetentionApplied</c> entry of the team log.
/// </summary>
public sealed class RetentionSettings : ITenantOwned
{
    public const int MinMonths = 1;

    public const int MaxMonths = 120;

    /// <summary>The organization; one row per organization.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>Months an application stays after it ended, or null for never deleting.</summary>
    public int? EndedApplicationsMonths { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether <paramref name="months"/> is a value a person may set.</summary>
    public static bool IsValidMonths(int months) => months is >= MinMonths and <= MaxMonths;

    /// <summary>Sets the period, or turns retention off with null. Refuses a value outside <see cref="MinMonths"/> to <see cref="MaxMonths"/>.</summary>
    public void Set(int? months, DateTimeOffset now)
    {
        if (months is { } value && !IsValidMonths(value))
        {
            throw new ArgumentOutOfRangeException(nameof(months), months, $"Retention is {MinMonths} to {MaxMonths} months.");
        }

        EndedApplicationsMonths = months;
        UpdatedAt = now;
    }

    /// <summary>The instant before which an application must have ended to be deleted at <paramref name="now"/>, or null when retention is off.</summary>
    public DateTimeOffset? Cutoff(DateTimeOffset now) => CutoffFor(EndedApplicationsMonths, now);

    /// <summary>As <see cref="Cutoff"/>, for a period that is not saved yet (the dry-run preview).</summary>
    public static DateTimeOffset? CutoffFor(int? months, DateTimeOffset now) =>
        months is { } value && IsValidMonths(value) ? now.AddMonths(-value) : null;
}
