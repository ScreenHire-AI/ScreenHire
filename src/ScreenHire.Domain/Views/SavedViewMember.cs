using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Views;

/// <summary>
/// A shared view one person added to their own sidebar (docs/architecture/saved-views.md). Sharing a view only offers it:
/// it is in a teammate's sidebar when this row exists. The owner needs no row, since a view is always in its owner's
/// sidebar. The row goes when the view is made private, when the person leaves the organization, and with the view when
/// it is purged. Whether the person may still see the view is checked when the list is read (<see cref="ViewPolicy.MaySee"/>).
/// </summary>
public sealed class SavedViewMember : ITenantOwned
{
    public Guid OrganizationId { get; init; }

    public Guid ViewId { get; init; }

    /// <summary>The person who added it. A user id, as for <see cref="SavedFilterView.OwnerUserId"/>.</summary>
    public Guid UserId { get; init; }

    /// <summary>Where it sits among the views the person added, first at the top.</summary>
    public int Position { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public static SavedViewMember Create(Guid organizationId, Guid viewId, Guid userId, int position, DateTimeOffset now) =>
        new() { OrganizationId = organizationId, ViewId = viewId, UserId = userId, Position = position, AddedAt = now };
}
