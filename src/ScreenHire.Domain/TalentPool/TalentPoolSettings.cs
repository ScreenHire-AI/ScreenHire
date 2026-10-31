using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.TalentPool;

/// <summary>
/// An organization's talent pool settings: how long a candidate's permission to keep their details for future roles
/// lasts. Tenant-scoped, unlike the public <c>organizations</c> row. No row means the default, 12 months.
/// </summary>
/// <remarks>
/// The 12-month default and the 6 to 24 month range are a product choice awaiting a legal review (docs/status.md,
/// Owner actions). A permission ends at the shorter of the period when it was given and the period now
/// (<c>JobApplication.FutureRolesUntil</c>), so a longer setting never extends one already given.
/// </remarks>
public sealed class TalentPoolSettings : ITenantOwned
{
    public const int DefaultMonths = 12;

    public const int MinMonths = 6;

    public const int MaxMonths = 24;

    /// <summary>The organization; one row per organization.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>How long a candidate's permission lasts from when they gave it, in months.</summary>
    public int PermissionMonths { get; set; } = DefaultMonths;

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The months <paramref name="settings"/> sets, or the default when the organization has no row.</summary>
    public static int MonthsOf(TalentPoolSettings? settings) => settings?.PermissionMonths ?? DefaultMonths;

    /// <summary>Whether <paramref name="months"/> is a period an organization may set.</summary>
    public static bool IsAllowed(int months) => months is >= MinMonths and <= MaxMonths;
}
